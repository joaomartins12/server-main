using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Config.Events;
using AutoMapper;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Application
{
    public class ConfigsLoader
    {
        private readonly ISender _sender;
        private readonly IMapper _mapper;
        private readonly ILogger _logger;
        private bool? _loading;

        public bool Loading => _loading == null || _loading.Value;

        public List<CloneConfigModel> Clones { get; private set; }
        public List<HatchConfigModel> Hatchs { get; private set; }
        public List<FruitConfigModel> Fruits { get; private set; }
        public List<EventConfigModel> Events { get; private set; }
        public List<GlobalDropsConfigModel> GlobalDrops { get; private set; }

        public ConfigsLoader(ISender sender, IMapper mapper, ILogger logger)
        {
            _sender = sender;
            _mapper = mapper;
            _logger = logger;
        }

        public ConfigsLoader Load()
        {
            Task.Run(LoadConfigs);

            return this;
        }

        private async Task LoadConfigs()
        {
            if (_loading != null)
                return;

            _loading = true;

            try
            {
                Clones = _mapper.Map<List<CloneConfigModel>>(await _sender.Send(new CloneConfigsQuery()));
            }
            catch (Exception ex)
            {
                _logger.Error("Error on Loading Config.Clone: {ex}", ex.Message);
            }

            try
            {
                GlobalDrops = _mapper.Map<List<GlobalDropsConfigModel>>(await _sender.Send(new GlobalDropsConfigQuery()));
            }
            catch (Exception ex)
            {
                _logger.Error("Error on Loading Config.GlobalDrops: {ex}", ex.Message);
            }

            try
            {
                Hatchs = _mapper.Map<List<HatchConfigModel>>(await _sender.Send(new HatchConfigsQuery()));
            }
            catch (Exception ex)
            {
                _logger.Error("Error on Loading Config.Hatch: {ex}", ex.Message);
            }

            try
            {
                Fruits = _mapper.Map<List<FruitConfigModel>>(await _sender.Send(new FruitConfigsQuery()));
            }
            catch (Exception ex)
            {
                _logger.Error("Error on Loading Config.Fruit: {ex}", ex.Message);
            }

            try
            {
                Events = _mapper.Map<List<EventConfigModel>>(await _sender.Send(new EventsConfigQuery()));
            }
            catch (Exception ex)
            {
                _logger.Error("Error on Loading Config.Events: {ex}", ex.Message);
            }

            _loading = false;
        }
    }
}
