using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using MediatR;
using Serilog;
using DigitalWorldOnline.Application;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class CashShopReloadBalancePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.CashShopReloadBalance;

        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly AssetsLoader _assets;

        public CashShopReloadBalancePacketProcessor(
            ILogger logger,
            AssetsLoader assets,
            ISender sender)
        {
            _logger = logger;
            _assets = assets;
            _sender = sender;
        }

        public Task Process(GameClient client, byte[] packetData)
        {
            // Avoid creating a GamePacketReader when not needed (reduces allocations)
            _logger.Debug("Sending account cash coins packet for character {TamerId}...", client.TamerId);

            client.Send(new CashShopCoinsPacket(client.Premium, client.Silk));

            return Task.CompletedTask;
        }
    }
}

