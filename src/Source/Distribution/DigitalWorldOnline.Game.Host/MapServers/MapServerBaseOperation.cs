using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Config.Events;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Game.Managers;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace DigitalWorldOnline.GameHost
{
    public sealed partial class MapServer
    {
        private DateTime _lastMapsSearch = DateTime.Now;
        private DateTime _lastMobsSearch = DateTime.Now;
        private DateTime _lastConsignedShopsSearch = DateTime.Now;
        private byte _loadChannel = 0;

        // Background sync task for maps and objects
        private Task? _backgroundSyncTask;

        // Cache for map templates to avoid DB calls on client connect
        private readonly object _cacheLock = new object();
        private List<GameMap> _cachedMapTemplates = new List<GameMap>();

        //TODO: externalizar
        private readonly int _startToSee = 18000;
        private readonly int _stopSeeing = 18001;

        /// <summary>
        /// Cleans unused running maps.
        /// </summary>
        public Task CleanMaps()
        {
            var mapsToRemove = new List<GameMap>();
            mapsToRemove.AddRange(Maps.Where(x => x.CloseMap));

            foreach (var map in mapsToRemove)
            {
                Maps.Remove(map);
            }

            return Task.CompletedTask;
        }

        public Task CleanMap(int ChannelId)
        {
            var mapToClose = Maps.FirstOrDefault(x => x.Channel == ChannelId);

            if (mapToClose != null)
            {
                _logger.Information($"Removing inactive map for {mapToClose.Type} mapID: {mapToClose.MapId} - {mapToClose.Name}");
                Maps.Remove(mapToClose);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Search for new maps to instance.
        /// </summary>
        public async Task SearchNewMaps(CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            if (DateTime.Now > _lastMapsSearch)
            {
                var mapsToLoad =
                    _mapper.Map<List<GameMap>>(await _sender.Send(new GameMapsConfigQuery(MapTypeEnum.Default), cancellationToken));

                // Update cache of templates
                lock (_cacheLock)
                {
                    _cachedMapTemplates = mapsToLoad.Select(m => _mapper.Map<GameMap>(m)).ToList();
                }

                foreach (var newMap in mapsToLoad)
                {
                    if (!Maps.Any(x => x.Id == newMap.Id && x.Channel == _loadChannel))
                    {
                        newMap.Channel = _loadChannel;
                        Maps.Add(newMap);
                    }
                }

                _lastMapsSearch = DateTime.Now.AddSeconds(5);
            }
            _logger.Information($"[MAP SEARCH] Maps search completed in {stopwatch.ElapsedMilliseconds}ms");
        }

        public async Task SearchNewMaps(GameClient client)
        {
            var mapsToLoad = _mapper.Map<List<GameMap>>(await _sender.Send(new GameMapConfigsQuery()));

            foreach (var newMap in mapsToLoad)
            {
                if (newMap.MapId == client.Tamer.Location.MapId)
                {
                    if (!Maps.Any(x => x.MapId == client.Tamer.Location.MapId && x.Channel == client.Tamer.Channel))
                    {
                        if (newMap.Type == MapTypeEnum.Default)
                        {
                            //newMap.Channel = client.Tamer.Channel;
                            newMap.Channel = _loadChannel;
                            //_logger.Information($"Initializing new {newMap.Type} map {newMap.MapId} Ch {client.Tamer.Channel} - {newMap.Name} ...");
                            Maps.Add(newMap);
                        }
                    }
                }
            }

            // Also update cache
            lock (_cacheLock)
            {
                _cachedMapTemplates = mapsToLoad.Select(m => _mapper.Map<GameMap>(m)).ToList();
            }

            _lastMapsSearch = DateTime.Now.AddSeconds(5);
        }

        public async Task LoadAllMaps(CancellationToken cancellationToken)
        {
            if (DateTime.Now > _lastMapsSearch)
            {
                //_mapper.Map<List<MapAssetModel>>(await _sender.Send(new MapAssetsQuery()));
                var mapsToLoad = _mapper.Map<List<GameMap>>(await _sender.Send(new GameMapConfigsQuery()));


                foreach (var newMap in mapsToLoad)
                {
                    if (!Maps.Any(x => x.Id == newMap.Id && x.Type == MapTypeEnum.Default))
                    {
                        if (newMap.Type == MapTypeEnum.Default)
                        {
                            newMap.Channel = 0;
                            Maps.Add(newMap);
                        }
                    }
                }


                _lastMapsSearch = DateTime.Now.AddSeconds(10);
            }
        }

        /// <summary>
        /// Gets the maps objects.
        /// </summary>
        public async Task GetMapObjects(CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            await GetMapConsignedShops(cancellationToken);
            _logger.Information($"[MAP OBJECTS] Consigned shops synced in {stopwatch.ElapsedMilliseconds}ms");
            stopwatch.Restart();
            await GetMapMobs(cancellationToken);
            _logger.Information($"[MAP OBJECTS] Mobs synced in {stopwatch.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Gets the map latest mobs.
        /// </summary>
        /// <returns>The mobs collection</returns>
        private async Task GetMapMobs(CancellationToken cancellationToken)
        {
            if (DateTime.Now > _lastMobsSearch)
            {
                // Take a snapshot of initialized maps
                var initializedMaps = Maps.Where(x => x.Initialized).ToList();

                foreach (var map in initializedMaps)
                {
                    // Fetch mob configurations for the map using MobManager to benefit from cache/coalescing
                    var mapMobs = await _mobManager.GetMobsForMapAsync(map.Id, cancellationToken);

                    // Check if an update is necessary and apply it
                    if (map.RequestMobsUpdate(mapMobs))
                        map.UpdateMobsList();
                }

                // Update the mob search timestamp
                _lastMobsSearch = DateTime.Now.AddSeconds(30);
            }
        }

        /// <summary>
        /// Gets the consigned shops latest list.
        /// </summary>
        /// <returns>The consigned shops collection</returns>
        private async Task GetMapConsignedShops(CancellationToken cancellationToken)
        {
            // Delegated entirely to ConsignedShopManager via _shopManager.SyncPlayerShopsAsync in map loop.
            // Previous DB polling removed to reduce duplicate ConsignedShopsQuery load.
            await Task.CompletedTask;
        }

        /// <summary>
        /// The default hosted service "starting" method.
        /// </summary>
        /// <param name="cancellationToken">Control token for the operation</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Start background sync loop to avoid blocking the main map loop on DB calls
            if (_backgroundSyncTask == null || _backgroundSyncTask.IsCompleted)
            {
                _backgroundSyncTask = Task.Run(() => SyncMapsAndObjectsLoop(cancellationToken), cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await CleanMaps();
                    // SearchNewMaps and GetMapObjects are executed in background by SyncMapsAndObjectsLoop

                    var tasks = new List<Task>();

                    Maps.ForEach(map => { tasks.Add(RunMap(map)); });

                    await Task.WhenAll(tasks);

                    await Task.Delay(100, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Unexpected map exception: {ex.Message} {ex.StackTrace}");
                    await Task.Delay(3000, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Runs the target map operations.
        /// </summary>
        /// <param name="map">the target map</param>
        private async Task RunMap(GameMap map)
        {
            try
            {
                map.Initialize();
                map.ManageHandlers();

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                // Operações do mapa
                var tamerStopwatch = new Stopwatch();
                tamerStopwatch.Start();
                await Task.Run(() => TamerOperation(map));
                tamerStopwatch.Stop();

                var monsterStopwatch = new Stopwatch();
                monsterStopwatch.Start();
                await Task.Run(() => MonsterOperation(map));
                monsterStopwatch.Stop();

                var dropsStopwatch = new Stopwatch();
                dropsStopwatch.Start();
                await Task.Run(() => DropsOperation(map));
                dropsStopwatch.Stop();

                // 🔹 Adicionamos aqui a sincronização das lojas
                var shopStopwatch = new Stopwatch();
                shopStopwatch.Start();

                if (map.Clients.Any())
                {
                    foreach (var client in map.Clients)
                    {
                        if (client?.Tamer != null && client.IsConnected)
                        {
                            try
                            {
                                await _shopManager.SyncPlayerShopsAsync(client);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "[SHOP MANAGER] Erro ao sincronizar lojas para {Tamer}", client.Tamer?.Name ?? "Unknown");
                            }
                        }
                    }
                }

                shopStopwatch.Stop();

                stopwatch.Stop();
                var totalTime = stopwatch.Elapsed.TotalMilliseconds;
                var delayTime = (int)Math.Max(500 - totalTime, 100);

                _logger.Debug("[MAP LOOP] Mapa {MapId} processado em {Time}ms (Shops={ShopTime}ms, Tamer={TamerTime}ms, Mobs={MobTime}ms, Drops={DropTime}ms)",
                    map.MapId,
                    Math.Round(totalTime),
                    Math.Round(shopStopwatch.Elapsed.TotalMilliseconds),
                    Math.Round(tamerStopwatch.Elapsed.TotalMilliseconds),
                    Math.Round(monsterStopwatch.Elapsed.TotalMilliseconds),
                    Math.Round(dropsStopwatch.Elapsed.TotalMilliseconds));

                await Task.Delay(delayTime);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error at map running for MapId: {map.MapId}: {ex.Message} {ex.StackTrace}.");
            }
        }

        /// <summary>
        /// Adds a new gameclient to the target map.
        /// </summary>
        /// <param name="client">The game client to be added.</param>
        public async Task AddClient(GameClient client)
        {
            if (client.Tamer.TargetTamerIdTP > 0)
            {
                var map = Maps.FirstOrDefault(x =>
                    x.Clients.Exists(gameClient => gameClient.TamerId == client.Tamer.TargetTamerIdTP));

                client.SetLoading();

                if (map != null)
                    AddClientToMap(client, map);
            }
            else
            {
                var map = Maps.FirstOrDefault(x =>
                    x.Initialized && x.MapId == client.Tamer.Location.MapId && x.Channel == client.Tamer.Channel);

                client.SetLoading();

                if (map != null)
                {
                    AddClientToMap(client, map);
                }
                else
                {
                    var stopWatch = Stopwatch.StartNew();
                    var timeLimit = 150000; // 15 segundos
                    var delayInterval = 2500; // 2,5 segundos

                    while (map == null && stopWatch.ElapsedMilliseconds < timeLimit)
                    {
                        await Task.Delay(delayInterval);

                        map = Maps.FirstOrDefault(x =>
                            x.Initialized && x.MapId == client.Tamer.Location.MapId && x.Channel == client.Tamer.Channel);

                        if (map == null)
                        {
                            _loadChannel = client.Tamer.Channel;
                            _logger.Warning($"Waiting map {client.Tamer.Location.MapId} CH {_loadChannel} initialization...");
                            await SearchNewMaps(client);
                        }
                    }

                    if (map == null)
                    {
                        _logger.Warning($"The map instance {client.Tamer.Location.MapId} CH {_loadChannel} has not been started, aborting process...");
                        client.Disconnect();
                    }
                    else
                    {
                        AddClientToMap(client, map);
                    }
                }
            }
        }

        private void AddClientToMap(GameClient client, GameMap map)
        {
            client.Tamer.MobsInView.Clear();
            map.AddClient(client);
            client.Tamer.Revive();
        }

        /// <summary>
        /// Removes the gameclient from the target map.
        /// </summary>
        /// <param name="client">The gameclient to be removed.</param>
        public void RemoveClient(GameClient client)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == client.TamerId));

            map?.BroadcastForTargetTamers(client.TamerId,
                new LocalMapSwapPacket(
                    client.Tamer.GeneralHandler,
                    client.Tamer.Partner.GeneralHandler,
                    client.Tamer.Location.X,
                    client.Tamer.Location.Y,
                    client.Tamer.Partner.Location.X,
                    client.Tamer.Partner.Location.Y
                ).Serialize()
            );

            map?.RemoveClient(client);
        }

        public void BroadcastForChannel(byte channel, byte[] packet)
        {
            var maps = Maps.Where(x => x.Channel == channel).ToList();

            maps?.ForEach(map => { map.BroadcastForMap(packet); });
        }

        public void BroadcastGlobal(byte[] packet)
        {
            var maps = Maps.Where(x => x.Clients.Any()).ToList();

            maps?.ForEach(map => { map.BroadcastForMap(packet); });
        }

        public void BroadcastForSelectedMaps(byte[] packet, List<int> mapIds)
        {
            var maps = Maps.Where(map => map.Clients.Any() && mapIds.Contains(map.MapId)).ToList();

            maps?.ForEach(map => { map.BroadcastForMap(packet); });
        }

        public void BroadcastForMap(short mapId, byte[] packet)
        {
            var map = Maps.FirstOrDefault(x => x.MapId == mapId);

            map?.BroadcastForMap(packet);
        }

        public void BroadcastForMapAllChannels(short mapId, byte[] packet)
        {
            var maps = Maps.Where(x => x.Clients.Exists(gameClient => gameClient.Tamer.Location.MapId == mapId))
                .SelectMany(map => map.Clients);
            maps.ToList().ForEach(client => { client.Send(packet); });
        }

        public void BroadcastForUniqueTamer(long tamerId, byte[] packet)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            map?.BroadcastForUniqueTamer(tamerId, packet);
        }

        public GameClient? FindClientByTamerId(long tamerId)
        {
            return Maps.SelectMany(map => map.Clients).FirstOrDefault(client => client.TamerId == tamerId);
        }

        public GameClient? FindClientByTamerName(string tamerName)
        {
            return Maps.SelectMany(map => map.Clients).FirstOrDefault(client => client.Tamer.Name == tamerName);
        }

        public GameClient? FindClientByTamerHandle(int handle)
        {
            return Maps.SelectMany(map => map.Clients).FirstOrDefault(client => client.Tamer?.GeneralHandler == handle);
        }

        public GameClient? FindClientByTamerHandleAndChannel(int handle, long TamerId)
        {
            return Maps.Where(x => x.Clients.Exists(gameClient => gameClient.TamerId == TamerId))
                .SelectMany(map => map.Clients)
                .FirstOrDefault(client => client.Tamer?.GeneralHandler == handle);
        }

        public void BroadcastForTargetTamers(List<long> targetTamers, byte[] packet)
        {
            Maps
                .Where(x => x.Clients.Any(gameClient => targetTamers.Contains(gameClient.TamerId)))
                .ToList()
                .ForEach(map => map.BroadcastForTargetTamers(targetTamers, packet));
        }

        public void BroadcastForTargetTamers(long sourceId, byte[] packet)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == sourceId));

            map?.BroadcastForTargetTamers(map.TamersView[sourceId], packet);
        }

        public void BroadcastForTamerViewsAndSelf(long sourceId, byte[] packet)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == sourceId));

            map?.BroadcastForTamerViewsAndSelf(sourceId, packet);
        }

        public void BroadcastForTamerViewsAndSelf(GameClient client, byte[] packet)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient =>
                gameClient.TamerId == client.TamerId && gameClient.Tamer.Channel == client.Tamer.Channel));

            map?.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
        }

        public void BroadcastForTamerViews(GameClient client, byte[] packet)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient =>
                gameClient.TamerId == client.TamerId && gameClient.Tamer.Channel == client.Tamer.Channel));

            map?.BroadcastForTamerViewOnly(client.TamerId, packet);
        }

        public void AddMapDrop(Drop drop, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            map?.DropsToAdd.Add(drop);
        }

        public void RemoveDrop(Drop drop, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            map?.RemoveMapDrop(drop);
        }

        public Drop? GetDrop(short mapId, int dropHandler, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            return map?.GetDrop(dropHandler);
        }

        //Mobs
        public bool MobsAttacking(short mapId, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            return map?.MobsAttacking(tamerId) ?? false;
        }
        public bool IMobsAttacking(short mapId, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            return map?.IMobsAttacking(tamerId) ?? false;
        }
        public bool MobsAttacking(short mapId, long tamerId, bool Summon)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            return map?.MobsAttacking(tamerId) ?? false;
        }

        public List<CharacterModel> GetNearbyTamers(short mapId, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            return map?.NearbyTamers(tamerId);
        }

        // ----------------------------------------------------------------------------

        public void AddSummonMob(short mapId, SummonMobModel summon, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            map?.AddMobSumon(summon);
        }

        public void AddSummonMobs(short mapId, SummonMobModel summon)
        {
            var map = Maps.FirstOrDefault(x => x.MapId == mapId);

            map?.AddMob(summon);
        }
        public void AddSummonMobs(SummonMobModel summon)
        {
            foreach (var map in Maps)
            {
                map.AddMob(summon);  // Add the summon to every map
            }
        }
        public void AddMobs(short mapId, MobConfigModel mob, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            map?.AddMob(mob);
        }

        // ----------------------------------------------------------------------------

        public MobConfigModel? GetMobByHandler(short mapId, int handler, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (map == null)
                return null;

            return map.Mobs.FirstOrDefault(x => x.GeneralHandler == handler);
        }

        public SummonMobModel? GetMobByHandler(short mapId, int handler, bool summon, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (map == null)
                return null;

            return map.SummonMobs.FirstOrDefault(x => x.GeneralHandler == handler);
        }

        public DigimonModel? GetEnemyByHandler(short mapId, int handler, long tamerId)
        {
            return Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId))?
                .ConnectedTamers.Select(x => x.Partner).FirstOrDefault(x => x.GeneralHandler == handler);
        }

        // ----------------------------------------------------------------------------

        public List<MobConfigModel> GetMobsNearbyPartner(Location location, int range, long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return default;

            var originX = location.X;
            var originY = location.Y;

            return GetTargetMobs(targetMap.Mobs.Where(x => x.Alive).ToList(), originX, originY, range)
                .DistinctBy(x => x.Id).ToList();
        }

        public List<MobConfigModel> GetMobsNearbyPartnerByHandler(Location location, int handler, int range,
            long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (map == null)
                return null;

            var targetMob = map.Mobs.FirstOrDefault(x => x.GeneralHandler == handler);

            if (targetMob == null)
                return default;

            var originX = targetMob.CurrentLocation.X;
            var originY = targetMob.CurrentLocation.Y;

            var areaMobs = new List<MobConfigModel>();

            areaMobs.Add(targetMob);

            areaMobs.AddRange(GetTargetMobs(map.Mobs.Where(x => x.Alive).ToList(), originX, originY, range / 5));

            return areaMobs.DistinctBy(x => x.Id).ToList();
        }

        public List<MobConfigModel> GetMobsNearbyTargetMob(short mapId, int handler, int range, long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return default;

            var originMob = targetMap.Mobs.FirstOrDefault(x => x.GeneralHandler == handler);

            if (originMob == null)
                return default;

            var originX = originMob.CurrentLocation.X;
            var originY = originMob.CurrentLocation.Y;

            var targetMobs = new List<MobConfigModel>();
            targetMobs.Add(originMob);

            targetMobs.AddRange(GetTargetMobs(targetMap.Mobs.Where(x => x.Alive).ToList(), originX, originY, range));

            return targetMobs.DistinctBy(x => x.Id).ToList();
        }

        public static List<MobConfigModel> GetTargetMobs(List<MobConfigModel> mobs, int originX, int originY, int range)
        {
            var targetMobs = new List<MobConfigModel>();

            foreach (var mob in mobs)
            {
                var mobX = mob.CurrentLocation.X;
                var mobY = mob.CurrentLocation.Y;

                var distance = CalculateDistance(originX, originY, mobX, mobY);

                if (distance <= range)
                {
                    targetMobs.Add(mob);
                }
            }

            return targetMobs;
        }

        // ----------------------------------------------------------------------------

        public List<SummonMobModel> GetMobsNearbyPartner(Location location, int range, bool Summon, long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return default;

            var originX = location.X;
            var originY = location.Y;

            return GetTargetMobs(targetMap.SummonMobs.Where(x => x.Alive).ToList(), originX, originY, range)
                .DistinctBy(x => x.Id).ToList();
        }

        public List<SummonMobModel> GetMobsNearbyTargetMob(short mapId, int handler, int range, bool Summon,
            long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return default;

            var originMob = targetMap.SummonMobs.FirstOrDefault(x => x.GeneralHandler == handler);

            if (originMob == null)
                return default;

            var originX = originMob.CurrentLocation.X;
            var originY = originMob.CurrentLocation.Y;

            var targetMobs = new List<SummonMobModel>();
            targetMobs.Add(originMob);

            targetMobs.AddRange(GetTargetMobs(targetMap.SummonMobs.Where(x => x.Alive).ToList(), originX, originY,
                range));

            return targetMobs.DistinctBy(x => x.Id).ToList();
        }
        public IMob GetNearestIMobToTarget(short mapId, int handler, int range, long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return null;

            var originMob = targetMap.IMobs.FirstOrDefault(x => x.GeneralHandler == handler);

            if (originMob == null)
                return null;

            var originX = originMob.CurrentLocation.X;
            var originY = originMob.CurrentLocation.Y;

            return GetNearestIMob(targetMap.IMobs.Where(x => x.Alive).ToList(), originX, originY, range);
        }

        public static IMob GetNearestIMob(List<IMob> mobs, int originX, int originY, int range)
        {
            IMob nearestMob = null;
            double minDistance = double.MaxValue;

            foreach (var mob in mobs)
            {
                var mobX = mob.CurrentLocation.X;
                var mobY = mob.CurrentLocation.Y;
                var distance = CalculateDistance(originX, originY, mobX, mobY);

                if (distance <= range && distance < minDistance)
                {
                    minDistance = distance;
                    nearestMob = mob;
                }
            }

            return nearestMob;
        }
        public static List<SummonMobModel> GetTargetMobs(List<SummonMobModel> mobs, int originX, int originY, int range)
        {
            var targetMobs = new List<SummonMobModel>();

            foreach (var mob in mobs)
            {
                var mobX = mob.CurrentLocation.X;
                var mobY = mob.CurrentLocation.Y;

                var distance = CalculateDistance(originX, originY, mobX, mobY);

                if (distance <= range)
                {
                    targetMobs.Add(mob);
                }
            }

            return targetMobs;
        }
        public List<IMob> GetIMobsNearbyPartner(Location location, int range, long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return default;

            var originX = location.X;
            var originY = location.Y;

            return GetTargetIMobs(targetMap.IMobs.Where(x => x.Alive).ToList(), originX, originY, range)
                .DistinctBy(x => x.Id).ToList();
        }

        public List<IMob> GetIMobsNearbyTargetMob(short mapId, int handler, int range, long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return default;

            var originMob = targetMap.IMobs.FirstOrDefault(x => x.GeneralHandler == handler);

            if (originMob == null)
                return default;

            var originX = originMob.CurrentLocation.X;
            var originY = originMob.CurrentLocation.Y;

            var targetMobs = new List<IMob>();
            targetMobs.Add(originMob);

            targetMobs.AddRange(GetTargetIMobs(targetMap.IMobs.Where(x => x.Alive).ToList(), originX, originY, range));

            return targetMobs.DistinctBy(x => x.Id).ToList();
        }
        public IMob? GetIMobByHandler(short mapId, int handler, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            return map?.IMobs.FirstOrDefault(x => x.GeneralHandler == handler);
        }
        public static List<IMob> GetTargetIMobs(List<IMob> mobs, int originX, int originY, int range)
        {
            var targetMobs = new List<IMob>();

            foreach (var mob in mobs)
            {
                var mobX = mob.CurrentLocation.X;
                var mobY = mob.CurrentLocation.Y;

                var distance = CalculateDistance(originX, originY, mobX, mobY);

                if (distance <= range)
                {
                    targetMobs.Add(mob);
                }
            }

            return targetMobs;
        }

        // ----------------------------------------------------------------------------

        public bool EnemiesAttacking(short mapId, long partnerId, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            return map?.PlayersAttacking(partnerId) ?? false;
        }

        public async Task CallDiscord(string message, GameClient tamer, string coloured, string local, string Channel = "1374551248061202632", bool custom = false)
        {
            return;

            // TODO: discord in the future in needed
            var payload = new
            {
                message = message,
                tamer = new
                {
                    AccountId = tamer.AccountId,
                    TamerChannel = tamer.Tamer.Channel,
                    TamerName = tamer.Tamer.Name
                },
                coloured = coloured,
                local = local,
                channel = Channel,
                custom = custom,
                type = 2
            };

            var json_data = JsonConvert.SerializeObject(payload);

            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("http://admin.mundodigitaluniverse.space/discord.php"),
                    Content = new StringContent(json_data, Encoding.UTF8, "application/json")
                };

                var response = await client.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();
            }
        }

        public async Task CallDiscordWarnings(string title, string message, string coloured, string dischannel, string role, long digimonid)
        {
            var payload = new
            {
                title = title,
                message = message,
                coloured = coloured,
                dischannel = dischannel,
                role = role,
                digimonid = digimonid,
                type = 1
            };

            var json_data = JsonConvert.SerializeObject(payload);

            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("http://admin.mundodigitaluniverse.space/discord.php"),
                    Content = new StringContent(json_data, Encoding.UTF8, "application/json")
                };

                var response = await client.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();
            }
        }

        // ----------------------------------------------------------------------------

        private static double CalculateDistance(int x1, int y1, int x2, int y2)
        {
            var deltaX = x2 - x1;
            var deltaY = y2 - y1;

            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        // ----------------------------------------------------------------------------

        // Background loop that periodically syncs maps and map objects from the database.
        private async Task SyncMapsAndObjectsLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // These methods already check internal timers to avoid frequent DB calls.
                        await SearchNewMaps(cancellationToken);
                        await GetMapObjects(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // cancellation requested, break out cleanly
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "[SYNC LOOP] Error while syncing maps or map objects");
                    }

                    // Wait a bit before next sync to avoid tight loop; the called methods also control frequency
                    try { await Task.Delay(1000, cancellationToken); } catch (OperationCanceledException) { break; }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[SYNC LOOP] Fatal error in background sync loop");
            }
        }
    }
}