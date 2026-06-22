using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchInsertEggPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchInsertEgg;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public HatchInsertEggPacketProcessor(AssetsLoader assets, ISender sender, ILogger logger)
        {
            _assets = assets;
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var vipEnabled = packet.ReadByte();
            var itemSlot = packet.ReadInt();

            var inventoryItem = client.Tamer.Inventory.FindItemBySlot(itemSlot);
            var incubator = client.Tamer.Incubator;

            await incubator.Lock.WaitAsync(); // <- trava aqui
            try
            {
                if (incubator.NotDevelopedEgg)
                {
                    var newItem = new ItemModel();
                    newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == incubator.EggId));
                    newItem.SetItemId(incubator.EggId);
                    newItem.SetAmount(1);

                    var cloneItem = (ItemModel)newItem.Clone();

                    if (client.Tamer.Inventory.AddItem(cloneItem))
                    {
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                    }
                    else
                    {
                        _logger.Warning($"Inventory full for incubator recovery of item {incubator.EggId}.");
                        client.Send(new SystemMessagePacket($"Inventory full for incubator recovery of item {incubator.EggId}."));
                        return;
                    }
                }

                incubator.InsertEgg(inventoryItem.ItemId);
                client.Tamer.Inventory.RemoveOrReduceItem(inventoryItem, 1, itemSlot);

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateIncubatorCommand(incubator));
            }
            finally
            {
                incubator.Lock.Release(); // <- libera aqui
            }
        }

    }
}
