using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using Serilog;

namespace DigitalWorldOnline.Game
{
    public sealed partial class GamePacketProcessor : IProcessor, IDisposable
    {
        private readonly IEnumerable<IGamePacketProcessor> _packetProcessors;
        private readonly AssetsLoader _assets;
        private readonly ConfigsLoader _configs;
        private readonly ILogger _logger;

        public GamePacketProcessor(
            IEnumerable<IGamePacketProcessor> packetProcessors,
            AssetsLoader assets,
            ConfigsLoader configs,
            ILogger logger)
        {
            _packetProcessors = packetProcessors;
            _assets = assets;
            _configs = configs;
            _logger = logger;
        }

        public async Task ProcessPacketAsync(GameClient client, byte[] data)
        {
            while (_assets.Loading || _configs.Loading)
                await Task.Delay(1000);

            var packet = new GamePacketReader(data);

            switch (packet.Enum)
            {
                case GameServerPacketEnum.Unknown:
                    break;

                // PartnerStop é processado como qualquer outro
                case GameServerPacketEnum.PartnerStop:
                    DispatchPacket(client, packet.Enum, data);
                    break;

                // Attack e Skill sempre processam na hora
                case GameServerPacketEnum.PartnerAttack:
                case GameServerPacketEnum.PartnerSkill:
                    DispatchPacket(client, packet.Enum, data);
                    break;

                default:
                    DispatchPacket(client, packet.Enum, data);
                    break;
            }
        }

        private void DispatchPacket(GameClient client, GameServerPacketEnum type, byte[] data)
        {
            var processor = _packetProcessors.FirstOrDefault(x => x.Type == type);

            if (processor != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await processor.Process(client, data);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Erro ao processar packet {type} para {client.Tamer?.Name ?? "Unknown"}.");
                    }
                });
            }
            else
            {
                _logger.Error($"No processor for packet {type} to player {client.Tamer?.Name ?? "Unknown"}.");
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
