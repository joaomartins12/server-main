using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.DTOs.Config;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Config.Events;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Models.Map.Dungeons;
using DigitalWorldOnline.Commons.Models.Mechanics;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.MapServer;
using MediatR;
using Newtonsoft.Json;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace DigitalWorldOnline.GameHost
{
    public sealed partial class DungeonsServer
    {
        private DateTime _lastMapsSearch = DateTime.Now;
        private DateTime _lastMobsSearch = DateTime.Now;
        private DateTime _lastConsignedShopsSearch = DateTime.Now;

        //TODO: externalizar
        private readonly int _startToSee = 18000;
        private readonly int _stopSeeing = 18001;

        /// <summary>  
        /// Cleans unused running maps.  
        /// </summary>  
        public Task CleanMaps()
        {
            try
            {
                var mapsToRemove = new List<GameMap>();
                mapsToRemove.AddRange(Maps.Where(x => x.CloseMap));

                foreach (var map in mapsToRemove)
                {
                    _logger.Warning($"Removing inactive instance for {map.Type} map {map.Id} - {map.Name}...");
                    Maps.Remove(map);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in CleanMaps: {ex.Message} {ex.StackTrace}");
            }

            return Task.CompletedTask;
        }

        public Task CleanMap(int DungeonId)
        {
            try
            {
                var mapToClose = Maps.FirstOrDefault(x => x.DungeonId == DungeonId);

                if (mapToClose != null)
                {
                    _logger.Warning($"Removing inactive instance for {mapToClose.Type} mapID: {mapToClose.MapId} - {mapToClose.Name}");
                    Maps.Remove(mapToClose);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in CleanMap: {ex.Message} {ex.StackTrace}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Search for new maps to instance.
        /// </summary>
        public async Task SearchNewMaps(CancellationToken cancellationToken)
        {
            try
            {
                if (DateTime.Now > _lastMapsSearch)
                {
                    var mapsToLoad =
                        _mapper.Map<List<GameMap>>(await _sender.Send(new GameMapsConfigQuery(MapTypeEnum.Dungeon),
                            cancellationToken));

                    var party = _partyManager.Parties;
                    foreach (var newMap in mapsToLoad)
                    {
                        foreach (var partymap in party)
                        {
                            // Verifica se o índice LeaderId está dentro do intervalo de Members
                            if (partymap.Members.Count > 0 &&
                                partymap.LeaderId >= 0 &&
                                partymap.LeaderId < partymap.Members.Count &&
                                Maps.All(x => x.Id == partymap.Id) &&
                                newMap.MapId == partymap.Members.ElementAt((byte)partymap.LeaderId).Value.Location.MapId)
                            {
                                _logger.Debug(
                                    $"Initializing new instance for {newMap.Type} map {newMap.Id} - {newMap.Name}...");

                                int[] RoyalBaseMaps = { 1701, 1702, 1703 };
                                if (Array.Exists(RoyalBaseMaps, element => element == newMap.MapId))
                                {
                                    var royalBaseMap = new RoyalBaseMap((short)newMap.MapId, newMap.Mobs);
                                    newMap.IsRoyalBaseUpdate(true);
                                    newMap.setRoyalBaseMap(royalBaseMap);
                                }
                                else
                                {
                                    newMap.IsRoyalBaseUpdate();
                                    newMap.setRoyalBaseMap(null);
                                }

                                Maps.Add(newMap);
                            }
                        }
                    }

                    _lastMapsSearch = DateTime.Now.AddSeconds(5);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in SearchNewMaps: {ex.Message} {ex.StackTrace}");
            }
        }

        public async Task SearchNewMaps(bool isParty, GameClient client)
        {
            try
            {
                if (client?.Tamer?.Location == null)
                {
                    _logger.Warning("[Dungeon] Invalid client or tamer location.");
                    return;
                }

                var mapsToLoad = _mapper.Map<List<GameMap>>(
                    await _sender.Send(new GameMapsConfigQuery(MapTypeEnum.Dungeon)));

                if (mapsToLoad == null || !mapsToLoad.Any())
                {
                    _logger.Warning("[Dungeon] No maps to load.");
                    return;
                }

                if (isParty)
                {
                    var party = _partyManager.FindParty(client.TamerId);

                    _logger.Information("[Dungeon] Party locating...");
                    if (party == null)
                    {
                        _logger.Warning("[Dungeon] Party not found.");
                        return;
                    }

                    _logger.Information("[Dungeon] Party located.");

                    foreach (var newMap in mapsToLoad)
                    {
                        if (!Maps.Exists(x => x.DungeonId == party.Id) &&
                            newMap.MapId == client.Tamer.Location.MapId)
                        {
                            AddDungeonInstance(newMap, party.Id, client, isParty);
                        }
                    }
                }
                else
                {
                    _logger.Information("[Dungeon] Adding for solo character.");

                    foreach (var newMap in mapsToLoad)
                    {
                        if (!Maps.Exists(x => x.DungeonId == client.TamerId) &&
                            newMap.MapId == client.Tamer.Location.MapId)
                        {
                            AddDungeonInstance(newMap, (int)client.TamerId, client, isParty);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in SearchNewMaps: {ex.Message} {ex.StackTrace}");
            }
        }

        private void AddDungeonInstance(GameMap newMap, int dungeonId, GameClient client, bool isParty)
        {
            try
            {
                var newDungeon = (GameMap)newMap.Clone();

                // Remover mobs do Coliseu
                newDungeon.Mobs.RemoveAll(x => x.Coliseum && x.Round > 0);

                // Remover mobs fora do dia da semana
                if (newMap.MapId == 2001 || newMap.MapId == 2002)
                {
                    var today = (DungeonDayOfWeekEnum)DateTime.Now.DayOfWeek;
                    newDungeon.Mobs.RemoveAll(x => x.WeekDay != today);
                }

                newDungeon.SetId(dungeonId);

                int[] royalBaseMaps = { 1701, 1702, 1703 };
                if (royalBaseMaps.Contains(newDungeon.MapId))
                {
                    var royalBaseMap = new RoyalBaseMap((short)newDungeon.MapId, newDungeon.Mobs);
                    newDungeon.IsRoyalBaseUpdate(true);
                    newDungeon.setRoyalBaseMap(royalBaseMap);
                }
                else
                {
                    newDungeon.IsRoyalBaseUpdate();
                    newDungeon.setRoyalBaseMap(null);
                }

                string type = isParty ? "Party" : "Tamer";
                _logger.Warning($"[Dungeon] Adding {newMap.Name} for {type} of {client.Tamer.Name}...");
                Maps.Add(newDungeon);
                _logger.Warning($"[Dungeon] Added {newMap.Name} for {type} of {client.Tamer.Name}...");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error adding dungeon instance: {ex.Message} {ex.StackTrace}");
            }
        }


        /// <summary>
        /// Gets the maps objects.
        /// </summary>
        public async Task GetMapObjects(CancellationToken cancellationToken)
        {
            try
            {
                await GetMapMobs(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in GetMapObjects: {ex.Message} {ex.StackTrace}");
                throw; // Re-throw the exception to ensure it propagates if necessary
            }
        }

        public async Task GetMapObjects()
        {
            try
            {
                await GetMapMobs();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in GetMapObjects: {ex.Message} {ex.StackTrace}");
                throw; // Re-throw the exception to ensure it propagates if necessary  
            }
        }

        /// <summary>  
        /// Gets the map latest mobs.  
        /// </summary>  
        /// <returns>The mobs collection</returns>  
        private async Task GetMapMobs(CancellationToken cancellationToken)
        {
            try
            {
                if (DateTime.Now > _lastMobsSearch)
                {
                    // Take a snapshot of initialized maps  
                    var initializedMaps = Maps.Where(x => x.Initialized).ToList();

                    foreach (var map in initializedMaps)
                    {
                        // Fetch mob configurations for the map  
                        var mapMobs = _mapper.Map<IList<MobConfigModel>>(
                            await _sender.Send(new MapMobConfigsQuery(map.Id), cancellationToken)
                        );

                        if (mapMobs != null)
                        {
                            // Remove mobs that are Coliseum and have Round > 0  
                            mapMobs = mapMobs
                                .Where(x => !x.Coliseum || x.Round == 0)
                                .ToList();
                        }

                        if (map.RequestMobsUpdate(mapMobs))
                            map.UpdateMobsList();
                    }

                    // Update the mob search timestamp  
                    _lastMobsSearch = DateTime.Now.AddSeconds(30);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in GetMapMobs: {ex.Message} {ex.StackTrace}");
            }
        }


        private async Task GetMapMobs()
        {
            try
            {
                // Take a snapshot of initialized maps  
                var initializedMaps = Maps.Where(x => x.Initialized).ToList();

                foreach (var map in initializedMaps)
                {
                    var mapMobs = _mapper.Map<IList<MobConfigModel>>(await _sender.Send(new MapMobConfigsQuery(map.Id)));

                    if (mapMobs != null)
                    {
                        // Filtra todos os mobs válidos diretamente (sem precisar remover depois)  
                        mapMobs = mapMobs
                            .Where(x => !x.Coliseum || x.Round == 0)
                            .ToList();
                    }

                    if (map.RequestMobsUpdate(mapMobs))
                        map.UpdateMobsList();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in GetMapMobs: {ex.Message} {ex.StackTrace}");
            }
        }


        /// <summary>  
        /// The default hosted service "starting" method.  
        /// </summary>  
        /// <param name="cancellationToken">Control token for the operation</param>  
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await CleanMaps();
                    await SearchNewMaps(cancellationToken);
                    await GetMapObjects(cancellationToken);

                    var tasks = new List<Task>();

                    Maps.ForEach(map => { tasks.Add(RunMap(map)); });

                    await Task.WhenAll(tasks);

                    await Task.Delay(700, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Unexpected map exception in StartAsync: {ex.Message} {ex.StackTrace}");
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

                // Unify operations into a single task to ensure synchronization and exception handling
                await Task.Run(() =>
                {
                    TamerOperation(map);
                    MonsterOperation(map);
                    DropsOperation(map);
                });

                stopwatch.Stop();
                var totalTime = stopwatch.Elapsed.TotalMilliseconds;

                var delayTime = (int)Math.Max(500 - totalTime, 100);
                await Task.Delay(delayTime);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error at map running for MapId: {map.MapId}: {ex.Message} {ex.StackTrace}.");
                map.MarkForClose();

                // Restart the dungeon system in case of critical failure
                // await RestartSystem(CancellationToken.None);
            }
        }
        /// <summary>
        /// Adds a new gameclient to the target map.
        /// </summary>
        /// <param name="client">The game client to be added.</param>
        private GameMap FindExistingDungeonMap(GameClient client, GameParty party)
        {
            try
            {
                // Use a single LINQ query to simplify and optimize the search logic
                return Maps.FirstOrDefault(x =>
                    x.Initialized &&
                    x.MapId == client.Tamer.Location.MapId &&
                    (party != null
                        ? (x.DungeonId == party.LeaderId || x.DungeonId == party.Id)
                        : x.DungeonId == client.Tamer.Id));
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in FindExistingDungeonMap: {ex.Message} {ex.StackTrace}");
                throw; // Re-throw the exception to ensure it propagates if necessary
            }
        }

        private async Task JoinMap(GameClient client, GameMap map)
        {
            try
            {
                _logger.Debug($"[JoinMap] Starting JoinMap for client TamerId: {client.TamerId}, MapId: {map.MapId}");

                client.SetLoading();
                _logger.Debug($"[JoinMap] SetLoading called for client TamerId: {client.TamerId}");

                client.Tamer.MobsInView.Clear();
                _logger.Debug($"[JoinMap] Cleared MobsInView for client TamerId: {client.TamerId}");

                await map.AddClientDG(client);
                _logger.Debug($"[JoinMap] AddClientDG called for client TamerId: {client.TamerId}, MapId: {map.MapId}");

                client.Tamer.Revive();
                _logger.Debug($"[JoinMap] Revive called for client TamerId: {client.TamerId}");

                _logger.Information($"[JoinMap] Successfully joined client TamerId: {client.TamerId} to MapId: {map.MapId}");
            }

            catch (Exception ex)
            {
                _logger.Error($"[JoinMap] Error while joining client TamerId: {client.TamerId} to MapId: {map.MapId}: {ex.Message} {ex.StackTrace}");
                if (client.Tamer.Guild != null)
                {
                    // Existing code
                    await _sender.Send(new UpdateGuildMemberCommand(client.Tamer.Guild.Id, client.TamerId));
                }

                // Handle the error gracefully to avoid affecting the entire system
                try
                {
                    client.Disconnect();
                    _logger.Warning($"[JoinMap] Disconnected client TamerId: {client.TamerId} due to an error.");
                }
                catch (Exception disconnectEx)
                {
                    _logger.Error($"[JoinMap] Failed to disconnect client TamerId: {client.TamerId}: {disconnectEx.Message} {disconnectEx.StackTrace}");
                }
            }
            finally
            {
                _logger.Debug($"[JoinMap] Finished execution for client TamerId: {client.TamerId}, MapId: {map.MapId}");
            }
        }

        private async Task<GameMap> WaitForMapInitialization(GameClient client, GameParty party, MapConfigDTO mapConfig)
        {
            var timeout = TimeSpan.FromSeconds(30);
            var stopwatch = Stopwatch.StartNew();
            GameMap map = null;

            while (stopwatch.Elapsed < timeout)
            {
                try
                {
                    map = FindExistingDungeonMap(client, party);

                    if (map != null && map.Initialized)
                    {
                        _logger.Information($"[Dungeon] Instância iniciada para {mapConfig.Name} ({client.Tamer.Location.MapId}). Total de instâncias ativas: {Maps.Count}");
                        return map;
                    }

                    _logger.Warning($"[Dungeon] {mapConfig.Name}({client.Tamer.Location.MapId}) instanciando para {client.Tamer.Name}...");
                    await Task.Delay(1000);

                    // Tenta criar novamente caso nada tenha sido iniciado  
                    await SearchNewMaps(party != null, client);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Dungeon] Erro ao tentar inicializar o mapa {mapConfig.Name} para {client.Tamer.Name}: {ex.Message} {ex.StackTrace}");
                }
            }

            _logger.Warning($"[Dungeon] Timeout ao instanciar dungeon {mapConfig.Name} para {client.Tamer.Name}");

            // Reinicia o sistema caso o mapa não seja inicializado no tempo limite  

            return null;
        }

        public async Task AddClient(GameClient client)
        {
            try
            {
                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
                if (mapConfig == null)
                {
                    _logger.Warning($"[Dungeon] MapConfig is null for MapId: {client.Tamer.Location.MapId}. Restarting system...");
                    return;
                }

                var isDungeon = mapConfig.Type == MapTypeEnum.Dungeon;

                if (client.Tamer.TargetTamerIdTP > 0)
                {
                    var map = Maps.FirstOrDefault(x =>
                        x.Clients.Exists(gameClient => gameClient.TamerId == client.Tamer.TargetTamerIdTP));

                    if (map != null)
                        await JoinMap(client, map);

                    return;
                }

                if (!isDungeon)
                    return;

                var party = _partyManager.FindParty(client.TamerId);
                bool isInParty = party != null;

                GameMap selectedMap = FindExistingDungeonMap(client, party);

                if (selectedMap != null)
                {
                    await JoinMap(client, selectedMap);
                    return;
                }

                // Remove mapas órfãos  
                if (isInParty)
                    Maps.RemoveAll(x => x.DungeonId == party!.LeaderId || x.DungeonId == party.Id);

                // Tenta criar um novo mapa  
                await SearchNewMaps(isInParty, client);

                selectedMap = await WaitForMapInitialization(client, party, mapConfig);

                if (selectedMap != null)
                    await JoinMap(client, selectedMap);
                else
                {
                    _logger.Warning($"[Dungeon] Failed to initialize map for Tamer: {client.Tamer.Name}. Restarting system...");
                    client.Disconnect();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Dungeon] Unexpected error in AddClient: {ex.Message} {ex.StackTrace}. Restarting system...");
                client.Disconnect();
            }
            finally
            {
                _logger.Debug($"[Dungeon] Finished execution of AddClient for TamerId: {client.TamerId}");
            }
        }


        /// <summary>
        /// Removes the gameclient from the target map.
        /// </summary>
        /// <param name="client">The gameclient to be removed.</param>
        public void RemoveClient(GameClient client)
        {
            try
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

                var party = _partyManager.FindParty(client.TamerId);

                if (party != null)
                {
                    if (map?.Clients.Count == 0)
                    {
                        _logger.Warning($"[Dungeon] Party dungeon {map.Name} fechada (party {party.Id}) — nenhum jogador ativo.");
                        map.MarkForClose();
                        CleanMap(party.Id);
                        CleanMap((int)party.LeaderId);
                    }
                }
                else
                {
                    if (map?.Clients.Count == 0)
                    {
                        _logger.Warning($"[Dungeon] Solo dungeon {map?.Name} fechada (tamer {client.TamerId}).");
                        map?.MarkForClose();
                        CleanMap((int)client.TamerId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in RemoveClient: {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForChannel(byte channel, byte[] packet)
        {
            try
            {
                var maps = Maps.Where(x => x.Channel == channel).ToList();
                maps?.ForEach(map => { map.BroadcastForMap(packet); });
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForChannel: {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastGlobal(byte[] packet)
        {
            try
            {
                var maps = Maps.Where(x => x.Clients.Any()).ToList();
                maps?.ForEach(map => { map.BroadcastForMap(packet); });
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastGlobal: {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForMap(short mapId, byte[] packet, long tamerId)
        {
            try
            {
                var map = Maps.FirstOrDefault(x => x.MapId == mapId && x.Clients.Exists(y => y.TamerId == tamerId));
                map?.BroadcastForMap(packet);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForMap (with tamerId): {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForUniqueTamer(long tamerId, byte[] packet)
        {
            try
            {
                var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));
                map?.BroadcastForUniqueTamer(tamerId, packet);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForUniqueTamer: {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForMapAllChannels(short mapId, byte[] packet)
        {
            try
            {
                var maps = Maps.Where(x => x.Clients.Exists(gameClient => gameClient.Tamer.Location.MapId == mapId)).SelectMany(map => map.Clients);
                maps.ToList().ForEach(client => { client.Send(packet); });
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForMapAllChannels: {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForSelectedMaps(byte[] packet, List<int> mapIds)
        {
            try
            {
                var maps = Maps.Where(map => map.Clients.Any() && mapIds.Contains(map.MapId)).ToList();
                maps?.ForEach(map => { map.BroadcastForMap(packet); });
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForSelectedMaps: {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForMap(short mapId, byte[] packet)
        {
            try
            {
                var map = Maps.FirstOrDefault(x => x.MapId == mapId);
                map?.BroadcastForMap(packet);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForMap: {ex.Message} {ex.StackTrace}");
            }
        }

        public GameClient? FindClientByTamerId(long tamerId)
        {
            try
            {
                return Maps.SelectMany(map => map.Clients).FirstOrDefault(client => client.TamerId == tamerId);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in FindClientByTamerId: {ex.Message} {ex.StackTrace}");
                return null;
            }
        }

        public GameClient? FindClientByTamerName(string tamerName)
        {
            try
            {
                return Maps.SelectMany(map => map.Clients).FirstOrDefault(client => client.Tamer.Name == tamerName);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in FindClientByTamerName: {ex.Message} {ex.StackTrace}");
                return null;
            }
        }

        public GameClient? FindClientByTamerHandle(int handle)
        {
            try
            {
                return Maps.SelectMany(map => map.Clients).FirstOrDefault(client => client.Tamer?.GeneralHandler == handle);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in FindClientByTamerHandle: {ex.Message} {ex.StackTrace}");
                return null;
            }
        }

        public GameClient? FindClientByTamerHandleAndChannel(int handle, long TamerId)
        {
            try
            {
                return Maps.Where(x => x.Clients.Exists(gameClient => gameClient.TamerId == TamerId))
                    .SelectMany(map => map.Clients)
                    .FirstOrDefault(client => client.Tamer?.GeneralHandler == handle);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in FindClientByTamerHandleAndChannel: {ex.Message} {ex.StackTrace}");
                return null;
            }
        }

        public void BroadcastForTargetTamers(List<long> targetTamers, byte[] packet)
        {
            try
            {
                Maps
                    .Where(x => x.Clients.Any(gameClient => targetTamers.Contains(gameClient.TamerId)))
                    .ToList()
                    .ForEach(map => map.BroadcastForTargetTamers(targetTamers, packet));
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForTargetTamers (list): {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForTargetTamers(long sourceId, byte[] packet)
        {
            try
            {
                var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == sourceId));
                map?.BroadcastForTargetTamers(map.TamersView[sourceId], packet);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForTargetTamers (sourceId): {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForTamerViewsAndSelf(long sourceId, byte[] packet)
        {
            try
            {
                var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == sourceId));
                map?.BroadcastForTamerViewsAndSelf(sourceId, packet);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForTamerViewsAndSelf (sourceId): {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForTamerViewsAndSelf(GameClient client, byte[] packet)
        {
            try
            {
                var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient =>
                    gameClient.TamerId == client.TamerId && gameClient.Tamer.Channel == client.Tamer.Channel));
                map?.BroadcastForTamerViewsAndSelf(client.TamerId, packet);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForTamerViewsAndSelf (client): {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForTamerViews(long sourceId, byte[] packet)
        {
            try
            {
                var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == sourceId));
                map?.BroadcastForTamerViewOnly(sourceId, packet);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForTamerViews (sourceId): {ex.Message} {ex.StackTrace}");
            }
        }

        public void BroadcastForTamerViews(GameClient client, byte[] packet)
        {
            try
            {
                var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient =>
                    gameClient.TamerId == client.TamerId && gameClient.Tamer.Channel == client.Tamer.Channel));
                map?.BroadcastForTamerViewOnly(client.TamerId, packet);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in BroadcastForTamerViews (client): {ex.Message} {ex.StackTrace}");
            }
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

        public void AddSummonMobs(short mapId, SummonMobModel summon, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(x => x.TamerId == tamerId));

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
                return new List<MobConfigModel>();

            var originMob = targetMap.Mobs.FirstOrDefault(x => x.GeneralHandler == handler);

            if (originMob == null)
                return new List<MobConfigModel>();

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

        // --------------------------------------------------------------------------------------------------------------

        public List<SummonMobModel> GetMobsNearbyPartner(Location location, int range, bool Summon, long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return new List<SummonMobModel>();

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
                return new List<SummonMobModel>();

            var originMob = targetMap.SummonMobs.FirstOrDefault(x => x.GeneralHandler == handler);

            if (originMob == null)
                return new List<SummonMobModel>();

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

        private static double CalculateDistance(int x1, int y1, int x2, int y2)
        {
            var deltaX = x2 - x1;
            var deltaY = y2 - y1;

            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        // ----------------------------------------------------------------------------

        public bool EnemiesAttacking(short mapId, long partnerId, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            return map?.PlayersAttacking(partnerId) ?? false;
        }

        public async Task CallDiscord(string message, GameClient tamer, string coloured, string local, string Channel = "1307382358084944075", bool custom = false)
        {
        }

        public async Task CallDiscordWarnings(string title, string message, string coloured, string dischannel, string role, long digimonid)
        {
        }
    }
}
public class UpdateGuildMemberCommand : IRequest
{
    public long GuildId { get; private set; }
    public long MemberId { get; private set; }

    public UpdateGuildMemberCommand(long guildId, long memberId)
    {
        GuildId = guildId;
        MemberId = memberId;
    }
}
