using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class TradeAddItemPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.TradeAddItem;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public TradeAddItemPacketProcessor(MapServer mapServer, DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer, ILogger logger, ISender sender)
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
            var packet = new GamePacketReader(packetData);

            var inventorySlot = packet.ReadShort();
            var amount = packet.ReadShort();
            var slotAtual = client.Tamer.TradeInventory.EquippedItems.Count;

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

            var item = client.Tamer.Inventory.FindItemBySlot(inventorySlot);

            // Verifica se o item existe
            if (item == null)
            {
                client.Send(new ChatMessagePacket("Invalid item slot.", ChatTypeEnum.Notice, "System"));
                return;
            }

            // Verifica se o item é bound
            if (item.ItemInfo.BoundType == 2)
            {
                targetClient?.Tamer.ClearTrade();
                targetClient?.Send(new TradeInventoryUnlockPacket(client.Tamer.TargetTradeGeneralHandle));
                targetClient?.Send(new TradeCancelPacket(client.Tamer.GeneralHandler));

                await _mapServer.CallDiscord($"Tentativa de comércio com item bound ({item.ItemInfo.Name}) de {client.Tamer.Name}. Jogador desconectado.", client, "e06666", "DISCONNECTED", "1374551861683683338");

                client.Disconnect();
                return;
            }

            // Verifica se a quantidade no slot é válida
            if (item.Amount < amount)
            {
                // Sistema de banimento
                var banProcessor = SingletonResolver.GetService<BanForCheating>();
                var banMessage = banProcessor.SimpleBan(client.AccountId, client.Tamer.Name,
                    AccountBlockEnum.Short, "Cheating", client, "Voce tentou adicionar adicionar itens que não tinha via cheat.");

                // Log no Discord
                await _mapServer.CallDiscord(
                    $"[TRADE CHEAT ATTEMPT] {client.Tamer.Name} tentou adicionar {amount}x {item.ItemInfo.Name} ao trade, mas só possui {item.Amount} no slot. Banido por tentativa de cheat.",
                    client,
                    "ff0000",
                    "Trade Usando Cheat Engine",
                    "1374551861683683338"
                );

                // Mensagem no chat para todos do mapa
                var chatPacket = new NoticeMessagePacket(banMessage).Serialize();
                client.SendToAll(chatPacket);

                client.Disconnect();
                return;
            }


            // Verifica se já adicionou esse item (anti-duplicação)
            if (client.Tamer.TradeInventory.EquippedItems.Any(i => i.ItemId == item.ItemId))
            {
                _logger.Warning($"[WARNING] {client.Tamer.Name} attempted to add duplicate item {item.ItemInfo.Name} in trade.");
                return;
            }

            // Verifica se há slot vazio
            var emptySlot = client.Tamer.TradeInventory.GetEmptySlot;
            if (emptySlot == -1)
            {
                client.Send(new ChatMessagePacket("No empty slot available in trade inventory.", ChatTypeEnum.Notice, "System"));
                return;
            }

            // Clona item com a quantidade desejada
            var newItem = (ItemModel)item.Clone();
            newItem.Amount = amount;
            client.Tamer.TradeInventory.AddItemTrade(newItem);

            // Envia pacote de adição para ambos
            client.Send(new TradeAddItemPacket(client.Tamer.GeneralHandler, newItem.ToArray(), (byte)emptySlot, inventorySlot));
            targetClient?.Send(new TradeAddItemPacket(client.Tamer.GeneralHandler, newItem.ToArray(), (byte)emptySlot, inventorySlot));

            // Desbloqueia inventário
            targetClient?.Send(new TradeInventoryUnlockPacket(client.Tamer.TargetTradeGeneralHandle));
            client.Send(new TradeInventoryUnlockPacket(client.Tamer.TargetTradeGeneralHandle));
        }


    }
}

