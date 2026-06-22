using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class LoadAccountCashWarehousePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.LoadAccountWarehouse;

        private readonly ILogger _logger;

        public LoadAccountCashWarehousePacketProcessor(ILogger logger)
        {
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            try
            {
                var accountWarehouse = client.Tamer?.AccountCashWarehouse;

                if (accountWarehouse == null)
                {
                    _logger.Warning(
                        $"[LoadAccountCashWarehouse] AccountCashWarehouse is null for AccountId={client.AccountId}, TamerId={client.TamerId}"
                    );
                    client.Send(new SystemMessagePacket("Cash Warehouse not available."));
                    return;
                }

                client.Send(new LoadAccountWarehousePacket(accountWarehouse));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[LoadAccountCashWarehouse] Unexpected error for AccountId={client.AccountId}, TamerId={client.TamerId}");
                client.Send(new SystemMessagePacket("Error loading Cash Warehouse."));
            }
        }
    }
}
