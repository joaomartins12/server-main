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
    public class GiftStorageItemRetrievePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.GiftStorageItemRetrieve;

        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly AssetsLoader _assets;

        public GiftStorageItemRetrievePacketProcessor(ILogger logger, ISender sender, AssetsLoader assets)
        {
            _logger = logger;
            _sender = sender;
            _assets = assets;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var withdrawType = packet.ReadShort();
            var itemSlot = packet.ReadShort();

            if (withdrawType == 1)
            {
                var targetItem = client.Tamer.GiftWarehouse.GiftFindItemBySlot(itemSlot);

                if (targetItem != null)
                {
                    var newItem = new ItemModel();

                    newItem.SetItemId(targetItem.ItemId);
                    newItem.SetAmount(targetItem.Amount);
                    newItem.SetItemInfo(targetItem.ItemInfo);

                    if (newItem.IsTemporary)
                        newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                    if (client.Tamer.Inventory.AddItem(newItem))
                    {
                        client.Tamer.GiftWarehouse.RemoveItem(targetItem, (short)itemSlot);
                        client.Tamer.GiftWarehouse.UpdateGiftSlot();

                        client.Send(new LoadGiftStoragePacket(client.Tamer.GiftWarehouse));
                        client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.GiftWarehouse));
                    }
                    else
                    {
                        _logger.Warning($"Failed to add item !! Tamer {client.Tamer.Name} dont have free slots");
                    }

                }

            }
            else
            {
                var giftStorage = client.Tamer.GiftWarehouse;
                var Items = client.Tamer.GiftWarehouse.Items.Where(x => x.ItemId > 0).ToList();

                foreach (var targetItem in Items)
                {
                    if (targetItem != null)
                    {

                        var itemId = targetItem.ItemId;

                        var newItem = new ItemModel();
                        newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == targetItem.ItemId));

                        newItem.ItemId = targetItem.ItemId;
                        newItem.Amount = targetItem.Amount;

                        if (newItem.IsTemporary)
                            newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                        var itemClone = (ItemModel)newItem.Clone();

                        if (client.Tamer.Inventory.AddItem(newItem))
                        {
                            client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));

                            client.Tamer.GiftWarehouse.RemoveItem(targetItem, (short)targetItem.Slot);
                        }
                        else
                        {
                            _logger.Warning($"Failed to add item !! Tamer {client.Tamer.Name} dont have free slots");
                        }
                    }
                }

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateItemsCommand(client.Tamer.GiftWarehouse));
                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
                client.Send(new LoadGiftStoragePacket(giftStorage));

            }
        }
    }
}