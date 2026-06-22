using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Serilog;

using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Enums.Character;

namespace DigitalWorldOnline.Game.Managers
{
    public class ConsignedShopManager
    {
        private readonly MapServer _mapServer;
        private readonly EventServer _eventServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;
        private readonly IMapper _mapper;
        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;

        private static readonly SemaphoreSlim _syncLock = new(1, 1);
        private readonly Dictionary<int, DateTime> _lastMapSync = new();

        // In-memory cache for consigned shops per map
        private readonly ConcurrentDictionary<int, (List<ConsignedShop> Shops, DateTime Fetched)> _consignedShopsCache = new();
        // Increased TTL to reduce DB load (was5s)
        private readonly TimeSpan _consignedShopsCacheTtl = TimeSpan.FromSeconds(20);

        // Coalescing: track in-flight loads so concurrent requests for same mapId reuse same DB load task
        private readonly ConcurrentDictionary<int, Task<List<ConsignedShop>>> _ongoingLoads = new();

        // Background bounded queue for processing shop syncs
        private readonly ConcurrentQueue<GameClient> _shopSyncQueue = new();
        private readonly SemaphoreSlim _queueSignal = new(0, int.MaxValue);
        private readonly SemaphoreSlim _concurrencySemaphore = new(10, 10); // max10 concurrent syncs
        private Task? _shopProcessingTask;
        private readonly object _processingTaskLock = new();

        public ConsignedShopManager(
            MapServer mapServer,
            EventServer eventServer,
            DungeonsServer dungeonsServer,
            PvpServer pvpServer,
            ISender sender,
            IMapper mapper,
            AssetsLoader assets,
            ILogger logger)
        {
            _mapServer = mapServer;
            _eventServer = eventServer;
            _dungeonsServer = dungeonsServer;
            _pvpServer = pvpServer;
            _sender = sender;
            _mapper = mapper;
            _assets = assets;
            _logger = logger;
        }

        /// <summary>
        /// Enqueue a shop sync request to be processed by a bounded background worker.
        /// </summary>
        public void EnqueueShopSync(GameClient client)
        {
            if (client == null) return;

            _shopSyncQueue.Enqueue(client);
            _queueSignal.Release();

            // Ensure processing task is running
            lock (_processingTaskLock)
            {
                if (_shopProcessingTask == null || _shopProcessingTask.IsCompleted)
                {
                    _shopProcessingTask = Task.Run(() => ProcessShopQueueLoop());
                }
            }
        }

        private async Task ProcessShopQueueLoop()
        {
            try
            {
                while (true)
                {
                    await _queueSignal.WaitAsync();

                    if (_shopSyncQueue.TryDequeue(out var client))
                    {
                        // Limit concurrency
                        await _concurrencySemaphore.WaitAsync();

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await SyncPlayerShopsInternal(client).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "[SHOP MANAGER] Error processing queued shop sync for {Tamer}", client?.Tamer?.Name ?? "Unknown");
                            }
                            finally
                            {
                                _concurrencySemaphore.Release();
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[SHOP MANAGER] Shop processing loop terminated unexpectedly");
            }
        }

        /// <summary>
        /// Sincroniza as lojas consignadas do mapa do jogador.
        /// Kept public for direct calls; uses cache and logs DB timings.
        /// </summary>
        public async Task SyncPlayerShopsAsync(GameClient client)
        {
            if (client == null || client.Tamer == null || !client.IsConnected)
                return;

            // If background processing is preferred, enqueue instead
            EnqueueShopSync(client);
        }

        // Internal method that actually performs the synchronization using cache and DB timing logs
        private async Task SyncPlayerShopsInternal(GameClient client)
        {
            if (client == null || client.Tamer == null || !client.IsConnected)
                return;

            if (client.Tamer.State == CharacterStateEnum.Loading)
            {
                _logger.Debug("[SHOP MANAGER] Aguardando fim do loading para {Tamer}", client.Tamer.Name);
                return;
            }

            var mapId = client.Tamer.Location.MapId;
            var now = DateTime.UtcNow;

            try
            {
                await _syncLock.WaitAsync();

                if (_lastMapSync.TryGetValue(mapId, out var lastSync) && (now - lastSync).TotalSeconds < 1)
                    return;

                _lastMapSync[mapId] = now;
            }
            finally
            {
                _syncLock.Release();
            }

            List<ConsignedShop>? allShops = null;

            // Try cache
            if (_consignedShopsCache.TryGetValue(mapId, out var cacheEntry) && (DateTime.UtcNow - cacheEntry.Fetched) < _consignedShopsCacheTtl)
            {
                allShops = cacheEntry.Shops;
                _logger.Debug("[SHOP MANAGER] Using cached consigned shops for map {MapId}", mapId);
            }

            if (allShops == null)
            {
                try
                {
                    // Coalesce concurrent loads: only one DB call per mapId will run concurrently
                    var loadTask = _ongoingLoads.GetOrAdd(mapId, _ => LoadAndCacheShopsAsync(mapId));

                    var shops = await loadTask.ConfigureAwait(false);
                    allShops = shops;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[SHOP MANAGER] Error fetching consigned shops for map {MapId}", mapId);
                    return;
                }
            }

            // Update the map's consigned shops if map exists
            var map = _mapServer.Maps.FirstOrDefault(x => x.MapId == mapId && x.Channel == client.Tamer.Channel);
            if (map != null)
            {
                map.UpdateConsignedShops(allShops);

                foreach (var tamer in map.ConnectedTamers)
                {
                    // send shops to each connected tamer using packet
                    var targetClient = map.Clients.FirstOrDefault(c => c.TamerId == tamer.Id);
                    if (targetClient != null && targetClient.IsConnected)
                    {
                        SendConsignedShopsToClient(targetClient, allShops);
                    }
                }
            }
        }

        // Load shops from DB (via sender), update cache, and ensure the in-flight tracker is cleaned up
        private async Task<List<ConsignedShop>> LoadAndCacheShopsAsync(int mapId)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var shopsDto = await _sender.Send(new ConsignedShopsQuery(mapId)).ConfigureAwait(false);
                sw.Stop();
                _logger.Information("[DBCALL] ConsignedShopsQuery mapId={MapId} took {Elapsed}ms", mapId, sw.ElapsedMilliseconds);

                var allShops = _mapper.Map<List<ConsignedShop>>(shopsDto) ?? new List<ConsignedShop>();

                // update cache
                _consignedShopsCache[mapId] = (allShops, DateTime.UtcNow);

                return allShops;
            }
            finally
            {
                // remove the in-flight task so subsequent calls can retry or start a fresh load
                _ongoingLoads.TryRemove(mapId, out _);
            }
        }

        private void SendConsignedShopsToClient(GameClient client, List<ConsignedShop> shops)
        {
            try
            {
                if (client == null || !client.IsConnected) return;

                foreach (var shop in shops.Where(s => s.Channel == client.Tamer.Channel))
                {
                    var packet = new LoadConsignedShopPacket(shop).Serialize();
                    client.Send(packet);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[SHOP MANAGER] Failed sending consigned shops to {Tamer}", client?.Tamer?.Name ?? "Unknown");
            }
        }

        /// <summary>
        /// Descarrega as lojas que saíram do mapa ou foram removidas.
        /// Also invalidates cache entries for the affected map(s) so next request refreshes.
        /// </summary>
        public void UnloadRemovedShops(GameClient client, List<int> removedHandlers)
        {
            if (client == null || !client.IsConnected || removedHandlers == null || removedHandlers.Count == 0)
                return;

            foreach (var handler in removedHandlers)
            {
                try
                {
                    var unloadPacket = new UnloadConsignedShopPacket(handler).Serialize();
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, unloadPacket);
                    _logger.Information("[SHOP MANAGER] Shop {Handler} descarregada para {Tamer}",
                        handler, client.Tamer.Name);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[SHOP MANAGER] Falha ao descarregar shop {Handler} para {Tamer}",
                        handler, client.Tamer.Name);
                }
            }

            // Invalidate cache for this client's map so next sync reloads fresh data
            var mapId = client.Tamer?.Location.MapId ?? -1;
            if (mapId >= 0)
            {
                InvalidateMapCache(mapId);
            }
        }

        /// <summary>
        /// Invalidate cached shops for a map (call when a shop changes/added/removed server-side)
        /// </summary>
        public void InvalidateMapCache(int mapId)
        {
            _consignedShopsCache.TryRemove(mapId, out _);
        }
    }
}
