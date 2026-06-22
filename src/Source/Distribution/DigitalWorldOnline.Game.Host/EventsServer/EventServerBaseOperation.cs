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
using Newtonsoft.Json;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace DigitalWorldOnline.GameHost.EventsServer
{
    public sealed partial class EventServer
    {
        private DateTime _lastMapsSearch = DateTime.Now;
        private DateTime _lastMobsSearch = DateTime.Now;
        private DateTime _lastConsignedShopsSearch = DateTime.Now;
        private byte _loadChannel = 0;

        // Background sync task for maps and objects
        private Task? _backgroundSyncTask;

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
                Maps.Remove(mapToClose);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Search for new maps to instance.
        /// </summary>
        public async Task SearchNewMaps(CancellationToken cancellationToken)
        {
            await ForceInitializeMap(1,0);

            if (DateTime.Now > _lastMapsSearch)
            {
                var mapsToLoad =
                    _mapper.Map<List<GameMap>>(await _sender.Send(new GameMapsConfigQuery(MapTypeEnum.Event),
                        cancellationToken));

                foreach (var newMap in mapsToLoad)
                {
                    if (!Maps.Any(x => x.Id == newMap.Id && x.Channel == _loadChannel))
                    {
                        newMap.Channel = _loadChannel;
                        _logger.Information($"Initializing new {newMap.Type} map {newMap.Id} Ch {_loadChannel} - {newMap.Name} ...");
                        Maps.Add(newMap);
                    }
                }

                _lastMapsSearch = DateTime.Now.AddSeconds(10);
            }
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
                        if (newMap.Type == MapTypeEnum.Event)
                        {
                            newMap.Channel = client.Tamer.Channel;
                            _logger.Information($"Initializing new {newMap.Type} map {newMap.Id} Ch {client.Tamer.Channel} - {newMap.Name} ...");
                            Maps.Add(newMap);
                        }
                    }
                }
            }

            _lastMapsSearch = DateTime.Now.AddSeconds(10);
        }

        public async Task LoadAllMaps(CancellationToken cancellationToken)
        {
            if (DateTime.Now > _lastMapsSearch)
            {
                //_mapper.Map<List<MapAssetModel>>(await _sender.Send(new MapAssetsQuery()));
                var mapsToLoad = _mapper.Map<List<GameMap>>(await _sender.Send(new GameMapConfigsQuery()));

                _logger.Information($"Initializing a maps Default...");

                foreach (var newMap in mapsToLoad)
                {
                    if (!Maps.Any(x => x.Id == newMap.Id && x.Type == MapTypeEnum.Event))
                    {
                        if (newMap.Type == MapTypeEnum.Event)
                        {
                            newMap.Channel = 0;
                            Maps.Add(newMap);
                        }
                    }
                }

                _logger.Information($"Defaults maps has been initialized!");

                _lastMapsSearch = DateTime.Now.AddSeconds(10);
            }
        }

        /// <summary>
        /// Gets the maps objects.
        /// </summary>
        public async Task GetMapObjects(CancellationToken cancellationToken)
        {
            await GetMapConsignedShops(cancellationToken);
            await GetMapMobs(cancellationToken);
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
                    // Fetch mob configurations for the map
                    var mapMobs = _mapper.Map<IList<MobConfigModel>>(
                        await _sender.Send(new MapMobConfigsQuery(map.Id), cancellationToken)
                    );

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
            if (DateTime.Now > _lastConsignedShopsSearch)
            {
                // Take a snapshot of initialized maps
                var initializedMaps = Maps.Where(x => x.Initialized).ToList();
                foreach (var map in initializedMaps)
                {
                    if (map.Operating)
                        continue;

                    var consignedShops =
                        _mapper.Map<List<ConsignedShop>>(await _sender.Send(new ConsignedShopsQuery((int)map.Id),
                            cancellationToken));

                    map.UpdateConsignedShops(consignedShops);
                }

                _lastConsignedShopsSearch = DateTime.Now.AddSeconds(15);
            }
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

                    await Task.Delay(500, cancellationToken);
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

                if (map.Initialized == false) map.Initialize();
                map.ManageHandlersEvent();

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                var tasks = new List<Task>
                        {
                            Task.Run(() => TamerOperation(map)),
                            Task.Run(() => MonsterOperation(map)),
                            Task.Run(() => DropsOperation(map)),
                           
                        };
                await Task.WhenAll(tasks);


                stopwatch.Stop();
                var totalTime = stopwatch.Elapsed.TotalMilliseconds;

                if (totalTime >= 1000)
                    _logger.Information($"RunMap ({map.MapId}): {totalTime}.");

                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error at map running: {ex.Message} {ex.StackTrace}.");
            }
        }
        public async Task ForceInitializeMap(int mapId,byte channel)
        {
            var existingMap = Maps.FirstOrDefault(x => x.MapId == mapId && x.Channel == channel);

            if (existingMap != null)
            {
                return;
            }

            var mapsToLoad = _mapper.Map<List<GameMap>>(await _sender.Send(new GameMapConfigsQuery()));
            var mapToInitialize = mapsToLoad.FirstOrDefault(x => x.MapId == mapId);

            if (mapToInitialize != null)
            {
                mapToInitialize.Channel = channel;             

                mapToInitialize.Mobs.Clear();
                mapToInitialize.Tamers.Clear();

                Maps.Add(mapToInitialize);

                _logger.Information($"EventMap {mapToInitialize.Id} Channel {channel} has been initialized successfully.");
            }
            else
            {
                _logger.Warning($"Map {mapId} could not be found in the configuration.");
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

                client.Tamer.MobsInView.Clear();
                map.AddClient(client);
                client.Tamer.Revive();
            }
            else
            {
                //var map = Maps.FirstOrDefault(x => x.Initialized && x.MapId == client.Tamer.Location.MapId);
                var map = Maps.FirstOrDefault(x =>
                    x.Initialized && x.MapId == client.Tamer.Location.MapId && x.Channel == client.Tamer.Channel);

                client.SetLoading();

                if (map != null)
                {
                    client.Tamer.MobsInView.Clear();
                    map.AddClient(client);
                    client.Tamer.Revive();
                }
                else
                {
                    await Task.Run(async () =>
                    {
                        var stopWatch = Stopwatch.StartNew();
                        var timeLimit = 15000;

                        while (map == null)
                        {
                            await Task.Delay(2500);

                            map = Maps.FirstOrDefault(x => x.Initialized && x.MapId == client.Tamer.Location.MapId &&
                                                           x.Channel == client.Tamer.Channel);

                            _loadChannel = client.Tamer.Channel;
                            _logger.Warning($"Waiting Normal Map {client.Tamer.Location.MapId} CH {_loadChannel} initialization.");

                            if (map == null)
                                await SearchNewMaps(client);

                            if (stopWatch.ElapsedMilliseconds >= timeLimit)
                            {
                                _logger.Warning($"The map instance {client.Tamer.Location.MapId} CH {_loadChannel} has not been started, aborting process...");
                                //stopWatch.Stop();
                                break;
                            }
                        }

                        if (map == null)
                        {
                            _loadChannel = client.Tamer.Channel;
                            client.Disconnect();
                        }
                        else
                        {
                            client.Tamer.MobsInView.Clear();
                            map.AddClient(client);
                            client.Tamer.Revive();
                        }
                    });
                }
            }
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

        public void BroadcastForTargetTamers(List<long> targetTamers,byte[] packet)
        {
            Maps
                .Where(x => x.Clients.Any(gameClient => targetTamers.Contains(gameClient.TamerId)))
                .ToList()
                .ForEach(map => map.BroadcastForTargetTamers(targetTamers,packet));
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
        public bool IMobsAttacking(short mapId,long tamerId)
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

        public void AddSummonMobs(short mapId, SummonMobModel summon, long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            map?.AddMob(summon);
        }
        public void AddSummonMobs(int mapId,SummonMobModel summon)
        {
            var targetMap = Maps.FirstOrDefault(map => map.MapId == mapId);
            if (targetMap != null)
            {
                targetMap.AddMob(summon); 
            }
        }

        public void AddSummonMobs(List<int> mapIds,SummonMobModel summon)
        {
            foreach (var map in Maps.Where(m => mapIds.Contains((int)m.Id)))
            {
                map.AddMob(summon);
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
        public IMob GetNearestIMobToTarget(short mapId,int handler,int range,long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return null;

            var originMob = targetMap.IMobs.FirstOrDefault(x => x.GeneralHandler == handler);

            if (originMob == null)
                return null;

            var originX = originMob.CurrentLocation.X;
            var originY = originMob.CurrentLocation.Y;

            return GetNearestIMob(targetMap.IMobs.Where(x => x.Alive).ToList(),originX,originY,range);
        }

        public static IMob GetNearestIMob(List<IMob> mobs,int originX,int originY,int range)
        {
            IMob nearestMob = null;
            double minDistance = double.MaxValue;

            foreach (var mob in mobs)
            {
                var mobX = mob.CurrentLocation.X;
                var mobY = mob.CurrentLocation.Y;
                var distance = CalculateDistance(originX,originY,mobX,mobY);

                if (distance <= range && distance < minDistance)
                {
                    minDistance = distance;
                    nearestMob = mob;
                }
            }

            return nearestMob;
        }
        public IMob? GetIMobByHandler(short mapId,int handler,long tamerId)
        {
            var map = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));
            if (map == null)
                return null;

            return map.IMobs.FirstOrDefault(x => x.GeneralHandler == handler);
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
        public List<IMob> GetIMobsNearbyPartner(Location location,int range,long tamerId)
        {
            var targetMap = Maps.FirstOrDefault(x => x.Clients.Exists(gameClient => gameClient.TamerId == tamerId));

            if (targetMap == null)
                return default;

            var originX = location.X;
            var originY = location.Y;

            return GetTargetIMobs(targetMap.IMobs.Where(x => x.Alive).ToList(),originX,originY,range)
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
        public List<IMob> GetIMobsNearbyTargetMob(short mapId,int handler,int range,long tamerId)
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

            targetMobs.AddRange(GetTargetIMobs(targetMap.IMobs.Where(x => x.Alive).ToList(),originX,originY,range));

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
        public static List<IMob> GetTargetIMobs(List<IMob> mobs,int originX,int originY,int range)
        {
            var targetMobs = new List<IMob>();

            foreach (var mob in mobs)
            {
                var mobX = mob.CurrentLocation.X;
                var mobY = mob.CurrentLocation.Y;

                var distance = CalculateDistance(originX,originY,mobX,mobY);

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

        // Background loop that periodically syncs maps and map objects from the database.
        private async Task SyncMapsAndObjectsLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await SearchNewMaps(cancellationToken);
                        await GetMapObjects(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "[EVENT SYNC LOOP] Error while syncing maps or map objects");
                    }

                    try { await Task.Delay(1000, cancellationToken); } catch (OperationCanceledException) { break; }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[EVENT SYNC LOOP] Fatal error in background sync loop");
            }
        }
    }
}