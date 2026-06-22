using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class TradeConfirmationPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.TradeConfirmation;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public TradeConfirmationPacketProcessor(MapServer mapServer, DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer, ILogger logger, ISender sender)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            GameClient? targetClient;

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            switch (mapConfig!.Type)
            {
                case MapTypeEnum.Dungeon:
                    targetClient = _dungeonServer.FindClientByTamerHandleAndChannel(client.Tamer.TargetTradeGeneralHandle, client.TamerId);
                    break;

                case MapTypeEnum.Event:
                    targetClient = _eventServer.FindClientByTamerHandleAndChannel(client.Tamer.TargetTradeGeneralHandle, client.TamerId);
                    break;

                case MapTypeEnum.Pvp:
                    targetClient = _pvpServer.FindClientByTamerHandleAndChannel(client.Tamer.TargetTradeGeneralHandle, client.TamerId);
                    break;

                default:
                    targetClient = _mapServer.FindClientByTamerHandleAndChannel(client.Tamer.TargetTradeGeneralHandle, client.TamerId);
                    break;
            }

            // Enviar confirmação de trade
            client.Send(new TradeConfirmationPacket(client.Tamer.GeneralHandler));
            targetClient.Send(new TradeConfirmationPacket(client.Tamer.GeneralHandler));
            client.Tamer.SetTradeConfirm(true);

            if (client.Tamer.TradeConfirm && targetClient.Tamer.TradeConfirm)
            {
                // Verifica se ambos os jogadores possuem espaço suficiente para os itens
                if (client.Tamer.Inventory.TotalEmptySlots < targetClient.Tamer.TradeInventory.Count)
                {
                    InvalidTrade(client, targetClient);
                    return;
                }
                else if (targetClient.Tamer.Inventory.TotalEmptySlots < client.Tamer.TradeInventory.Count)
                {
                    InvalidTrade(client, targetClient);
                    return;
                }

                var firstTamerItems = client.Tamer.TradeInventory.EquippedItems
    .Select(x => $"{(x.ItemInfo?.Name ?? "Unknown Item")} (ID: {x.ItemId}) x{x.Amount}");

                var secondTamerItems = targetClient.Tamer.TradeInventory.EquippedItems
                    .Select(x => $"{(x.ItemInfo?.Name ?? "Unknown Item")} (ID: {x.ItemId}) x{x.Amount}");


                var firstTamerBits = client.Tamer.TradeInventory.Bits;
                var secondTamerBits = targetClient.Tamer.TradeInventory.Bits;

                #region ITEM TRADE

                // Remove os itens trocados do inventário de ambos os jogadores
                if (client.Tamer.TradeInventory.Count > 0)
                    client.Tamer.Inventory.RemoveOrReduceItems(client.Tamer.TradeInventory.EquippedItems.Clone());

                if (targetClient.Tamer.TradeInventory.Count > 0)
                    targetClient.Tamer.Inventory.RemoveOrReduceItems(targetClient.Tamer.TradeInventory.EquippedItems.Clone());

                // Adiciona os itens trocados ao inventário dos respectivos jogadores
                if (targetClient.Tamer.TradeInventory.Count > 0)
                    client.Tamer.Inventory.AddItems(targetClient.Tamer.TradeInventory.EquippedItems.Clone());

                if (client.Tamer.TradeInventory.Count > 0)
                    targetClient.Tamer.Inventory.AddItems(client.Tamer.TradeInventory.EquippedItems.Clone());

                #endregion

                #region BITS TRADE

                // Troca os bits entre os jogadores
                if (client.Tamer.TradeInventory.Bits >= 1)
                {
                    client.Tamer.Inventory.RemoveBits(client.Tamer.TradeInventory.Bits);
                    targetClient.Tamer.Inventory.AddBits(client.Tamer.TradeInventory.Bits);
                }

                if (targetClient.Tamer.TradeInventory.Bits >= 1)
                {
                    targetClient.Tamer.Inventory.RemoveBits(targetClient.Tamer.TradeInventory.Bits);
                    client.Tamer.Inventory.AddBits(targetClient.Tamer.TradeInventory.Bits);
                }

                #endregion

                // Limpa a trade após a troca
                targetClient.Tamer.ClearTrade();
                client.Tamer.ClearTrade();

                // Envia as confirmações finais de trade
                client.Send(new TradeFinalConfirmationPacket(client.Tamer.GeneralHandler));
                targetClient.Send(new TradeFinalConfirmationPacket(client.Tamer.GeneralHandler));

                // Atualiza o inventário dos jogadores
                await _sender.Send(new UpdateItemsCommand(targetClient.Tamer.Inventory));
                await _sender.Send(new UpdateItemListBitsCommand(targetClient.Tamer.Inventory));

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory));

                // Carrega o inventário atualizado dos jogadores
                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
                targetClient.Send(new LoadInventoryPacket(targetClient.Tamer.Inventory, InventoryTypeEnum.Inventory));

                //  _logger.Information($"Trade Finalizada: {client.Tamer.Name} trocou os seguintes itens com {targetClient.Tamer.Name}:\n" +
                //  $"[Jogador 1: {client.Tamer.Name}] Itens: {string.Join(", ", firstTamerItems)} | Bits: {firstTamerBits}\n" +
                //  $"[Jogador 2: {targetClient.Tamer.Name}] Itens: {string.Join(", ", secondTamerItems)} | Bits: {secondTamerBits}");

                // Enviar log para o Discord
                await _mapServer.CallDiscord(
                 $"**Trade Finalizada**\n" +
                 $"**Jogador 1**: {client.Tamer.Name} trocou os seguintes itens com {targetClient.Tamer.Name}:\n\n" +
                 $"**Itens de {client.Tamer.Name}**: {string.Join("\n ", firstTamerItems)} | **Bits**: {firstTamerBits}\n" +
                 $"**Itens de {targetClient.Tamer.Name}**: {string.Join("\n\n ", secondTamerItems)} \n **Bits**: {secondTamerBits}",
                 client,
                 "00ff00", // Cor de sucesso (verde)
                 "TRADE",  // Título do log
                 "1374552737617809428" // ID do canal Discord (substitua pelo canal correto)
                 );
            }
        }

        private static void InvalidTrade(GameClient client, GameClient? targetClient)
        {
            // Cancela a trade e notifica os jogadores
            client.Send(new TradeCancelPacket(targetClient.Tamer.GeneralHandler));
            client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
            targetClient.Send(new TradeCancelPacket(targetClient.Tamer.GeneralHandler));

            targetClient.Tamer.ClearTrade();
            client.Tamer.ClearTrade();
        }
    }
}
