using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Utils;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class AccountWarehouseItemRetrievePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.RetrivieAccountWarehouseItem;

        private readonly ILogger _logger;
        private readonly ISender _sender;

        public AccountWarehouseItemRetrievePacketProcessor(ILogger logger, ISender sender)
        {
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            var itemSlot = packet.ReadShort();

            if (client.Tamer.AccountCashWarehouse == null)
            {
                _logger.Error($"[Warehouse] AccountCashWarehouse is NULL for Tamer {client.Tamer?.Name ?? "Unknown"}!");
                return;
            }

            var targetItem = client.Tamer.AccountCashWarehouse.FindItemBySlot(itemSlot);

            if (targetItem == null)
            {
                _logger.Warning($"[Warehouse] Item not found in slot {itemSlot} for Tamer {client.Tamer.Name}");
                return;
            }

            // Clona para transferir para o inventário
            var newItem = (ItemModel)targetItem.Clone();
            newItem.SetItemId(targetItem.ItemId);
            newItem.SetAmount(targetItem.Amount);
            newItem.SetItemInfo(targetItem.ItemInfo);

            if (newItem.IsTemporary)
                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

            if (client.Tamer.Inventory.AddItemGiftStorage(newItem))
            {
                // Remove do warehouse
                client.Tamer.AccountCashWarehouse.RemoveItem(targetItem, itemSlot);
                client.Tamer.AccountCashWarehouse.Sort();

                // 🔹 Usa o item original no pacote (alguns clients crasham se for o clone)
                client.Send(new AccountWarehouseItemRetrievePacket(targetItem, itemSlot));

                // 🔹 Recarrega warehouse e inventário
                client.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));
                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));

                // 🔹 Atualiza DB
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountCashWarehouse));

                _logger.Information($"[Warehouse] Item {targetItem.ItemId} x{targetItem.Amount} retirado do warehouse (slot {itemSlot}) para Tamer {client.Tamer.Name}");
            }
            else
            {
                _logger.Warning($"[Warehouse] Falha ao transferir item {targetItem.ItemId} para inventário: sem slots livres (Tamer {client.Tamer.Name})");
            }
        }
    }
}
