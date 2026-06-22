using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Config.Events;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DigitalWorldOnline.GameHost.EventsServer
{
    public sealed partial class EventServer
    {
        private readonly EventManager _eventManager;
        private readonly DungeonsServer _dungeonServer;
        private readonly MapServer _mapServer;
        private readonly PartyManager _partyManager;
        private readonly StatusManager _statusManager;
        private readonly ExpManager _expManager;
        private readonly DropManager _dropManager;
        private readonly AssetsLoader _assets;
        private readonly ConfigsLoader _configs;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;
        private readonly IServiceProvider _serviceProvider;
        public List<EventConfigModel> Events { get; set; }

        public List<GameMap> Maps { get; set; }

        public EventServer(
            EventManager eventManager,
            PartyManager partyManager,
            AssetsLoader assets,
            ConfigsLoader configs,
            StatusManager statusManager,
            ExpManager expManager,
            DropManager dropManager,
            ILogger logger,
            ISender sender,
            IMapper mapper,
            IServiceProvider serviceProvider,
            MapServer mapServer,
            DungeonsServer dungeonServer)
        {
            _eventManager = eventManager;
            _partyManager = partyManager;
            _statusManager = statusManager;
            _expManager = expManager;
            _dropManager = dropManager;
            _assets = assets.Load();
            _configs = configs.Load();
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
            _serviceProvider = serviceProvider;

            Maps = new List<GameMap>();
            Events = configs.Events;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
        }

        private void SaveMobToDatabase(MobConfigModel mob)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                var mobDto = dbContext.MobConfig.SingleOrDefault(m => m.Id == mob.Id);

                if (mobDto == null)
                {
                    _logger.Error($"BOSS {mob.Name},{mob.Id} Does not exist in the database Unable to call MobConfig.");
                    return;
                }

                mobDto.DeathTime = mob.DeathTime;
                mobDto.ResurrectionTime = mob.ResurrectionTime;

                try
                {
                    dbContext.SaveChanges();
                    _logger.Information($"BOSS {mob.Name},{mob.Id} Update seuccess.");
                }
                catch (Exception ex)
                {
                    _logger.Error($"BOSS time Update error： {mob.Name} (Id: {mob.Id}): {ex.Message}");
                }
            }
        }

        private void AddContent()
        {
            Events?.ForEach(eventConfig =>
            {
                eventConfig.EventMaps.ForEach(eventMap =>
                {
                    Maps.Add(new GameMap(eventMap.Map.MapId, AddMobs(), AddDrops()));
                });
            });
           Maps = new List<GameMap>()
            {
                new GameMap(9001, AddMobs(), AddDrops()),
                new GameMap(9002, AddBoss(), new List<Drop>())
            };
        }

        private List<EventMobConfigModel> AddMobs()
        {
            return new List<EventMobConfigModel>();
        }

        private List<Drop> AddDrops()
        {
            return new List<Drop>();
        }

        private List<EventMobConfigModel> AddBoss()
        {
            //11940
            //15213
            return new List<EventMobConfigModel>();
        }
    }
}