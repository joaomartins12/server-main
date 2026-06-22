using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class SealOpenPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.OpenSeal;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public SealOpenPacketProcessor(
            AssetsLoader assets,
            ISender sender,
            ILogger logger)
        {
            _assets = assets;
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var sealItem = client.Tamer.Inventory.FindItemBySlot(packet.ReadShort());
            var sealId = sealItem.ItemId;
            var totalSeals = sealItem.Amount;

            // Calcula quantos openers são necessários (1 a cada 50 selos)
            var requiredOpeners = (int)Math.Ceiling(totalSeals / 50.0);

            // Busca todos os openers disponíveis no inventário
            var availableOpeners = _assets.ItemInfo.Where(x => x.Type == 191 && x.Section == 19101);
            var openersList = new List<ItemModel>();
            foreach (var openerInfo in availableOpeners)
            {
                var inventoryOpeners = client.Tamer.Inventory.FindItemsById(openerInfo.ItemId);
                if (inventoryOpeners != null) openersList.AddRange(inventoryOpeners);
            }
            openersList = openersList.OrderBy(x => x.Slot).ToList();

            var needOpeners = requiredOpeners;
            foreach (var opener in openersList)
            {
                if (opener.Amount >= needOpeners)
                {
                    opener.ReduceAmount(needOpeners);
                    needOpeners = 0;
                }
                else
                {
                    needOpeners -= opener.Amount;
                    opener.SetAmount();
                }
                if (needOpeners == 0)
                    break;
            }

            if (needOpeners > 0)
            {
                _logger.Error($"Invalid openers amount for tamer {client.TamerId}.");
                client.Send(new SystemMessagePacket($"Quantidade de openers insuficiente, recarregue seu personagem."));
                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
                return;
            }

            var sealInfo = _assets.SealInfo.FirstOrDefault(x => x.SealId == sealId);
            if (sealInfo != null)
            {
                client.Tamer.SealList.AddOrUpdateSeal(sealId, (short)totalSeals, sealInfo.SequentialId);
                client.Partner?.SetSealStatus(_assets.SealInfo);

                client.Send(new UpdateStatusPacket(client.Tamer));

                sealItem.SetAmount(0);
                client.Tamer.Inventory.CheckEmptyItems();

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateCharacterSealsCommand(client.Tamer.SealList));

                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
            }
            else
            {
                _logger.Error($"Invalid seal asset for seal id {sealId} on tamer {client.TamerId} open seal.");
                client.Send(new SystemMessagePacket($"Invalid seal id {sealId}."));
                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
            }
        }
    }
}