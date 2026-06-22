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
    public class SealClosePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.CloseSeal;

        private const int NormalCloserId = 131002;
        private const int BoundCloserId = 131003;
        private const int EventCloserId = 131005;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public SealClosePacketProcessor(
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

            var sequentialSealId = packet.ReadShort();
            var amount = packet.ReadShort();

            var requestCloser = 1;

            if (amount >= 50) requestCloser = (int)Math.Round((decimal)(amount / 50));

            var targetSeal = client.Tamer.SealList.FindSeal(sequentialSealId);

            if (targetSeal != null)
            {
                // Verifica se o jogador possui selos suficientes
                if (targetSeal.Amount < amount)
                {
                    client.Send(new SystemMessagePacket("Quantidade de selos insuficiente."));
                    return;
                }

                targetSeal.DecreaseAmount(amount);

                var availableClosers = _assets.ItemInfo.Where(x => x.Type == 192 && x.Section == 19201);

                var closersList = new List<ItemModel>();
                foreach (var availableCloser in availableClosers)
                {
                    var inventoryCloser = client.Tamer.Inventory.FindItemsById(availableCloser.ItemId);
                    if (inventoryCloser != null) closersList.AddRange(inventoryCloser);
                }

                closersList = closersList.OrderBy(x => x.Slot).ToList();

                var needCloser = requestCloser;

                foreach (var closer in closersList)
                {
                    if (closer.Amount >= needCloser)
                    {
                        closer.ReduceAmount(needCloser);
                        needCloser = 0;
                    }
                    else
                    {
                        needCloser -= closer.Amount;
                        closer.SetAmount();
                    }

                    if (needCloser == 0)
                        break;
                }

                if (needCloser > 0)
                {
                    _logger.Error($"Invalid closers amount for tamer {client.TamerId}.");
                    client.Send(new SystemMessagePacket($"Invalid closers amount, reload your character."));
                    client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
                    return;
                }

                // Corrige: Gera um novo objeto para cada selo, para evitar stack incorreto
                for (int i = 0; i < amount; i++)
                {
                    var sealItem = new ItemModel(1, targetSeal.SealId);
                    sealItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == sealItem.ItemId));
                    client.Tamer.Inventory.AddItem(sealItem);
                }

                client.Partner?.SetSealStatus(_assets.SealInfo);
                client.Send(new UpdateStatusPacket(client.Tamer));

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateCharacterSealsCommand(client.Tamer.SealList));
            }
        }
    }
}