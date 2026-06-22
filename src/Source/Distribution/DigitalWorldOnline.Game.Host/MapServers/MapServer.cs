using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DigitalWorldOnline.GameHost
{
    public sealed partial class MapServer
    {
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
        private readonly EventManager _eventManager;

        // ✅ Novo Manager
        private readonly ConsignedShopManager _shopManager;
        private readonly MobManager _mobManager; // new

        public List<GameMap> Maps { get; set; }

        public IEnumerable<GameClient> GetAllClients()
        {
            return Maps.SelectMany(map => map.Clients);
        }

        public MapServer(
            PartyManager partyManager,
            AssetsLoader assets,
            ConfigsLoader configs,
            StatusManager statusManager,
            ExpManager expManager,
            DropManager dropManager,
            ILogger logger,
            ISender sender,
            IMapper mapper,
            IServiceScopeFactory scopeFactory, // ✅ injeta o ScopeFactory em vez de ServiceProvider
            EventManager eventManager)
        {
            _partyManager = partyManager;
            _statusManager = statusManager;
            _expManager = expManager;
            _dropManager = dropManager;
            _assets = assets.Load();
            _configs = configs.Load();
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
            _scopeFactory = scopeFactory; // ✅ inicializa aqui
            _eventManager = eventManager;

            // ✅ Inicializa o Shop Manager
            _shopManager = new ConsignedShopManager(
                this,               // referência do MapServer
                null,               // eventos do mapa
                null,               // dungeons (podes preencher depois)
                null,               // pvp (podes preencher depois)
                _sender,
                _mapper,
                _assets,
                _logger
            );

            // ✅ Initialize MobManager
            _mobManager = new MobManager(_sender, _mapper, _logger, TimeSpan.FromSeconds(5));

            Maps = new List<GameMap>();
        }

        /// <summary>
        /// Salva dados de mobs no banco de dados de forma assíncrona.
        /// </summary>
        private async Task SaveMobToDatabaseAsync(MobConfigModel mob)
        {
            // ✅ cria um escopo temporário seguro (fecha ao sair do using)
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

            var mobDto = await dbContext.MobConfig
                .SingleOrDefaultAsync(m => m.Id == mob.Id);

            if (mobDto == null)
            {
                _logger.Error($"❌ BOSS {mob.Name},{mob.Id} não existe no banco. Impossível atualizar MobConfig.");
                return;
            }

            mobDto.DeathTime = mob.DeathTime;
            mobDto.ResurrectionTime = mob.ResurrectionTime;

            try
            {
                await dbContext.SaveChangesAsync(); // ✅ async + liberta conexão
                _logger.Information($"✅ BOSS {mob.Name} (Id: {mob.Id}) atualizado com sucesso no banco.");
            }
            catch (Exception ex)
            {
                _logger.Error($"⚠️ Erro ao atualizar BOSS {mob.Name} (Id: {mob.Id}): {ex.Message}");
            }
        }

        /// <summary>
        /// Exemplo de chamada correta (podes adaptar para onde quiseres chamar este método).
        /// </summary>
        public async Task UpdateBossRespawnAsync(MobConfigModel mob)
        {
            await SaveMobToDatabaseAsync(mob);
        }
    }
}
