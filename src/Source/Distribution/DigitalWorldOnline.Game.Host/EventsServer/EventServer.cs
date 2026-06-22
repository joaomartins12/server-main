using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Config.Events;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
        private readonly IServiceScopeFactory _scopeFactory; // ✅ substitui IServiceProvider
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
            IServiceScopeFactory scopeFactory, // ✅ injeta o ScopeFactory em vez do ServiceProvider
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
            _scopeFactory = scopeFactory; // ✅ inicializa o ScopeFactory
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;

            Maps = new List<GameMap>();
            Events = configs.Events;
        }

        /// <summary>
        /// Atualiza o estado de um mob de evento no banco de dados (assíncrono e seguro).
        /// </summary>
        private async Task SaveMobToDatabaseAsync(MobConfigModel mob)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

            var mobDto = await dbContext.MobConfig
                .SingleOrDefaultAsync(m => m.Id == mob.Id);

            if (mobDto == null)
            {
                _logger.Error($"❌ BOSS {mob.Name},{mob.Id} não existe no banco. Não foi possível atualizar MobConfig.");
                return;
            }

            mobDto.DeathTime = mob.DeathTime;
            mobDto.ResurrectionTime = mob.ResurrectionTime;

            try
            {
                await dbContext.SaveChangesAsync(); // ✅ async + liberta conexão
                _logger.Information($"✅ BOSS {mob.Name},{mob.Id} atualizado com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.Error($"⚠️ Erro ao atualizar BOSS {mob.Name} (Id: {mob.Id}): {ex.Message}");
            }
        }

        /// <summary>
        /// Método público para atualizar mobs de evento — podes chamar quando o boss morre ou renasce.
        /// </summary>
        public async Task UpdateEventMobAsync(MobConfigModel mob)
        {
            await SaveMobToDatabaseAsync(mob);
        }

        /// <summary>
        /// Inicializa os mapas e eventos configurados.
        /// </summary>
        private void AddContent()
        {
            Events?.ForEach(eventConfig =>
            {
                eventConfig.EventMaps.ForEach(eventMap =>
                {
                    Maps.Add(new GameMap(eventMap.Map.MapId, AddMobs(), AddDrops()));
                });
            });

            Maps = new List<GameMap>
            {
                new GameMap(9001, AddMobs(), AddDrops()),
                new GameMap(9002, AddBoss(), new List<Drop>())
            };
        }

        private List<EventMobConfigModel> AddMobs() => new();
        private List<Drop> AddDrops() => new();
        private List<EventMobConfigModel> AddBoss() => new(); // Exemplo: bosses de evento
    }
}
