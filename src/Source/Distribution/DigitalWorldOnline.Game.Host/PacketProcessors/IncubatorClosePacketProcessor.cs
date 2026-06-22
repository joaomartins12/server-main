using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Entities;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class IncubatorClosePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.IncubatorClose;

        private readonly ILogger _logger;

        public IncubatorClosePacketProcessor(ILogger logger)
        {
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            // Simply log the close request - no hatching state checks
            _logger.Verbose($"Character {client.TamerId} closed incubator menu");

            // No additional processing needed since this is just a UI close notification
            await Task.CompletedTask;
        }
    }
}