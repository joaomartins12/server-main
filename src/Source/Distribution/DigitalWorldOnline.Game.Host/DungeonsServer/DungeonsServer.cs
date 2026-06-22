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
    public sealed partial class DungeonsServer
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

        public List<GameMap> Maps { get; set; }

        public DungeonsServer(
            PartyManager partyManager,
            AssetsLoader assets,
            ConfigsLoader configs,
            StatusManager statusManager,
            ExpManager expManager,
            DropManager dropManager,
            ILogger logger,
            ISender sender,
            IMapper mapper,
            IServiceScopeFactory scopeFactory, // ✅ injeta ScopeFactory
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
            _scopeFactory = scopeFactory; // ✅ inicializa corretamente
            _eventManager = eventManager;

            Maps = new List<GameMap>();
        }
    }
}
