using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ItemRemovePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ItemRemove;

        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly MapServer _mapServer;  // Assuming you need the MapServer for Discord logging

        public ItemRemovePacketProcessor(
            ISender sender,
            MapServer mapServer,  // Pass MapServer into the constructor
            ILogger logger)
        {
            _sender = sender;
            _logger = logger;
            _mapServer = mapServer;  // Initialize _mapServer
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var slot = packet.ReadShort();
            var posx = packet.ReadInt();
            var posy = packet.ReadInt();
            var amount = packet.ReadShort();

            _logger.Verbose($"Processing ItemRemove for Tamer {client.TamerId}. Slot: {slot}, Amount: {amount}, Position: ({posx}, {posy}).");

            var targetItem = client.Tamer.Inventory.FindItemBySlot(slot);

            if (targetItem?.ItemId > 0)
            {
                var itemName = targetItem.ItemInfo?.Name ?? "Unknown Item";  // Obtém o nome do item, caso exista


                // Verificação do tipo de item (não alterada)
                //if (targetItem.ItemInfo?.ItemBoundType == 0)
                //{
                //    var temp = (ItemModel)targetItem.Clone();
                //
                //    if (client.Tamer.Inventory.RemoveOrReduceItems(targetItem.GetList()))
                //    {
                //        var drop = _dropManager.CreateItemDrop(
                //            client.TamerId,
                //            client.Tamer.GeneralHandler,
                //            temp.ItemId,
                //            temp.Amount,
                //            temp.Amount,
                //            client.Tamer.Location.MapId,
                //            client.Tamer.Location.X,
                //            client.Tamer.Location.Y,
                //            true
                //        );
                //
                //        _mapServer.AddMapDrop(drop);
                //
                //        _logger.Verbose($"Tamer {client.TamerId} throw away {targetItem.ItemId} x{targetItem.Amount} at {client.Tamer.Location.MapId}.");
                //    }
                //}
                //else
                //{

                client.Tamer.Inventory.RemoveOrReduceItem(targetItem, amount, slot);

                await _sender.Send(new UpdateItemCommand(targetItem));
                await _sender.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));




                // Adicionando o log do Discord
                await _mapServer.CallDiscord(
                    $"**Item Removido**\n" +
                    $"{client.Tamer.Name} removeu {amount}x {itemName} (ID: {targetItem.ItemId}) de seu inventário.\n" +
                    $"**Slot**: {slot}\n" +
                    $"**Posição**: ({posx}, {posy})",
                    client,
                    "ff9900", // Cor de aviso (laranja)
                    "ITEM REMOVAL",  // Título do log
                    "1374551337160806540" // ID do canal Discord (substitua pelo canal correto)
                );
            }
            else
            {

                // Adicionando o log de erro no Discord
                //   await _mapServer.CallDiscord(
                //    $"**Item Não Encontrado**\n" +
                //    $"{client.Tamer.Name} tentou remover um item (ID: {slot}) que não existe no inventário.\n" +
                //    $"**Slot**: {slot}",
                //   client,
                //  "ff0000", // Cor de erro (vermelho)
                //  "ITEM REMOVAL ERROR",  // Título do log
                //  "1374526354510446693" // ID do canal Discord (substitua pelo canal correto)
                //  );
            }
        }
    }
}


