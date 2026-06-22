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
                mapsToRemove.AddRange(Maps.Where(x => x.CloseMap && x.Clients.Count == 0));

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
        // 2) Pesquisa em background — clona o template e fixa a chave = party.Id
        public async Task SearchNewMaps(CancellationToken cancellationToken)
        {
            try
            {
                if (DateTime.Now > _lastMapsSearch)
                {
                    var mapsToLoad =
                        _mapper.Map<List<GameMap>>(await _sender.Send(new GameMapsConfigQuery(MapTypeEnum.Dungeon),
                            cancellationToken));

                    var parties = _partyManager.Parties;

                    foreach (var party in parties)
                    {
                        if (party.Members.Count == 0 ||
                            party.LeaderId < 0 ||
                            party.LeaderId >= party.Members.Count)
                            continue;

                        var leaderLoc = party.Members.ElementAt((int)party.LeaderId).Value.Location;
                        if (leaderLoc == null)
                            continue;

                        foreach (var template in mapsToLoad)
                        {
                            if (template.MapId != leaderLoc.MapId)
                                continue;

                            // Se já existir instância da party para este MapId, não cria outra
                            if (Maps.Any(x => x.DungeonId == party.Id && x.MapId == template.MapId))
                                continue;

                            _logger.Debug($"Initializing new instance for {template.Type} map {template.Id} - {template.Name}...");

                            // CLONE sempre o template antes de adicionar
                            var instance = (GameMap)template.Clone();

                            // Royal Base
                            int[] royalBaseMaps = { 1701, 1702, 1703 };
                            if (Array.Exists(royalBaseMaps, m => m == instance.MapId))
                            {
                                var rb = new RoyalBaseMap((short)instance.MapId, instance.Mobs);
                                instance.IsRoyalBaseUpdate(true);
                                instance.setRoyalBaseMap(rb);
                            }
                            else
                            {
                                instance.IsRoyalBaseUpdate();
                                instance.setRoyalBaseMap(null);
                            }

                            // chave única = PartyId
                            instance.SetId((int)party.Id);

                            lock (Maps)
                            {
                                if (!Maps.Any(x => x.DungeonId == party.Id && x.MapId == instance.MapId))
                                {
                                    Maps.Add(instance);
                                    _logger.Information($"[Dungeon] Instance created (background): PartyId={party.Id}, MapId={instance.MapId}, Name={instance.Name}");
                                }
                            }
                        }
                    }

                    _lastMapsSearch = DateTime.Now.AddSeconds(600);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in SearchNewMaps: {ex.Message} {ex.StackTrace}");
            }
        }

        // 3) Pesquisa síncrona por entrada (party/solo) — consistente com a chave
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

                var entranceMapId = client.Tamer.Location.MapId;

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

                    foreach (var template in mapsToLoad)
                    {
                        if (template.MapId != entranceMapId)
                            continue;

                        // se já existe a instância da party, não cria outra
                        if (Maps.Exists(x => x.DungeonId == party.Id && x.MapId == entranceMapId))
                            return;

                        lock (Maps)
                        {
                            if (!Maps.Exists(x => x.DungeonId == party.Id))
                                AddDungeonInstance(template, (int)party.Id, client, isParty);
                        }

                        return;
                    }
                }
                else
                {
                    _logger.Information("[Dungeon] Adding for solo character.");

                    var soloKey = (int)client.TamerId;

                    foreach (var template in mapsToLoad)
                    {
                        if (template.MapId != entranceMapId)
                            continue;

                        if (Maps.Exists(x => x.DungeonId == soloKey && x.MapId == entranceMapId))
                            return;

                        lock (Maps)
                        {
                            if (!Maps.Exists(x => x.DungeonId == soloKey))
                                AddDungeonInstance(template, soloKey, client, isParty);
                        }

                        return;
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
                // evita duplicações por corrida
                lock (Maps)
                {
                    if (Maps.Any(x => x.DungeonId == dungeonId || x.Id == dungeonId))
                    {
                        _logger.Information($"[Dungeon] Já existe instância com dungeonId={dungeonId}. Ignorando criação.");
                        return;
                    }
                }

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

                lock (Maps)
                {
                    if (!Maps.Any(x => x.DungeonId == dungeonId || x.Id == dungeonId))
                    {
                        Maps.Add(newDungeon);
                        _logger.Warning($"[Dungeon] Added {newMap.Name} for {type} of {client.Tamer.Name}...");
                    }
                    else
                    {
                        _logger.Information($"[Dungeon] Instância já existe (race) para dungeonId={dungeonId}. Não adicionada.");
                    }
                }
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
                    try
                    {
                        TamerOperation(map);
                        MonsterOperation(map);
                        DropsOperation(map);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.Warning($"[Dungeon] Non-critical map loop error in MapId={map.MapId}: {innerEx.Message}");
                        // Não fecha o mapa — apenas loga
                    }
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
        // 1) Usa apenas party.Id como chave de instância (nunca LeaderId)
        private GameMap FindExistingDungeonMap(GameClient client, GameParty party)
        {
            try
            {
                return Maps.FirstOrDefault(x =>
                    x.Initialized &&
                    x.MapId == client.Tamer.Location.MapId &&
                    (party != null
                        ? (x.DungeonId == party.Id)
                        : x.DungeonId == client.Tamer.Id));
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in FindExistingDungeonMap: {ex.Message} {ex.StackTrace}");
                throw;
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

                // ✅ NOVO (mínimo): garante que o cliente não está listado em outra instância
                foreach (var m in Maps.ToList())
                {
                    if (m == null || m == map) continue;

                    if (m.Clients.RemoveAll(c => c != null && c.TamerId == client.TamerId) > 0)
                        _logger.Information($"[JoinMap] Detach {client.TamerId} de Inst={m.Id} (MapId={m.MapId})");

                    m.TamersView?.Remove(client.TamerId);
                    if (m.TamersView != null)
                    {
                        foreach (var kv in m.TamersView)
                            kv.Value?.Remove(client.TamerId);
                    }
                }

                await map.AddClientDG(client);
                _logger.Debug($"[JoinMap] AddClientDG called for client TamerId: {client.TamerId}, MapId: {map.MapId}");
                _lastClientJoin[client.TamerId] = DateTime.Now;
                client.Tamer.Revive();
                _logger.Debug($"[JoinMap] Revive called for client TamerId: {client.TamerId}");

                _logger.Information($"[JoinMap] Successfully joined client TamerId: {client.TamerId} to MapId: {map.MapId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[JoinMap] Error while joining client TamerId: {client.TamerId} to MapId: {map.MapId}: {ex.Message} {ex.StackTrace}");
                if (client.Tamer.Guild != null)
                {
                    await _sender.Send(new UpdateGuildMemberCommand(client.Tamer.Guild.Id, client.TamerId));
                }

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
            _logger.Information($"[JoinMap] MapId={map.MapId} | ClientsActive={map.Clients.Count} | Tamers: {string.Join(", ", map.Clients.Select(c => c.Tamer.Name))}");

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

        // 4) AddClient — remove “órfãos” só por PartyId (nada de LeaderId)
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

                // Remove mapas órfãos APENAS com a chave certa (PartyId)
                if (isInParty)
                    Maps.RemoveAll(x => x.DungeonId == party!.Id);

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
        // 5) RemoveClient — limpa só a instância com a chave real (PartyId)
        private readonly Dictionary<long, DateTime> _lastClientJoin = new();

        public void RemoveClient(GameClient client)
        {
            try
            {
                // Ignora se o jogador acabou de entrar há menos de 5 segundos
                if (_lastClientJoin.TryGetValue(client.TamerId, out var lastJoin) &&
                    (DateTime.Now - lastJoin).TotalSeconds < 5)
                {
                    _logger.Warning($"[Dungeon] Ignorando RemoveClient para {client.Tamer.Name} — acabou de entrar na dungeon ({(DateTime.Now - lastJoin).TotalMilliseconds}ms atrás).");
                    return;
                }

                var map = Maps.FirstOrDefault(x => x.Clients.Exists(gc => gc.TamerId == client.TamerId));
                if (map == null)
                {
                    _logger.Warning($"[Dungeon] RemoveClient chamado mas o mapa não foi encontrado para TamerId={client.TamerId}");
                    return;
                }

                map.RemoveClient(client);

                var party = _partyManager.FindParty(client.TamerId);
                Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    lock (Maps)
                    {
                        if (map.Clients.Count == 0)
                        {
                            _logger.Warning($"[Dungeon] Nenhum jogador ativo após 5s em {map.Name}. Fechando instância.");
                            map.MarkForClose();
                            CleanMap(party != null ? (int)party.Id : (int)client.TamerId);
                        }
                        else
                        {
                            _logger.Information($"[Dungeon] Ainda existem jogadores na instância {map.Name}, mantendo aberta.");
                        }
                    }
                });
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
                if (map == null)
                    return;

                if (map.TamersView != null &&
                    map.TamersView.TryGetValue(sourceId, out var viewers) &&
                    viewers != null &&
                    viewers.Count > 0)
                {
                    // 🔹 Broadcast normal (viewers válidos)
                    map.BroadcastForTargetTamers(viewers, packet);
                }
                else
                {
                    // 🔹 Envia o packet para todos os jogadores do mesmo mapa, exceto o próprio
                    var allTamers = map.Clients
                        .Where(c => c.TamerId != sourceId)
                        .Select(c => c.TamerId)
                        .ToList();

                    if (allTamers.Count > 0)
                    {
                        map.BroadcastForTargetTamers(allTamers, packet);
                    }
                }
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
                var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == client.TamerId));
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
