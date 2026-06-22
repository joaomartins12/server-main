using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedShopOpenPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedShopOpen;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly EventServer _eventServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public ConsignedShopOpenPacketProcessor(
            MapServer mapServer,
            EventServer eventServer,
            DungeonsServer dungeonsServer,
            PvpServer pvpServer,
            AssetsLoader assets,
            ILogger logger,
            ISender sender)
        {
            _mapServer = mapServer;
            _eventServer = eventServer;
            _dungeonsServer = dungeonsServer;
            _pvpServer = pvpServer;
            _assets = assets;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var posX = packet.ReadInt();
            var posY = packet.ReadInt();
            packet.Skip(4);
            var shopName = packet.ReadString();
            packet.Skip(9);
            var sellQuantity = packet.ReadInt();

            List<ItemModel> sellList = new(sellQuantity);

            client.Tamer.ConsignedShopItems.Clear();

            for (int i = 0; i < sellQuantity; i++)
            {
                var itemId = packet.ReadInt();
                var itemAmount = packet.ReadInt();

                var sellItem = new ItemModel(itemId, itemAmount);

                packet.Skip(64);

                var price = packet.ReadInt64();
                sellItem.SetSellPrice(price);

                packet.Skip(8);
                sellList.Add(sellItem);

                // Console.WriteLine($"Item Index: {i} | Price: {price}\n");
            }

            foreach (var item in sellList)
            {
                item.SetItemInfo(_assets.ItemInfo.First(x => x.ItemId == item.ItemId));

                var itemsCount = sellList.Count(x =>
                    x.ItemId == item.ItemId && x.TamerShopSellPrice != item.TamerShopSellPrice);

                if (itemsCount > 0)
                {
                    client.Send(new DisconnectUserPacket("You cant add 2 items of same id with different price!")
                        .Serialize());
                    return;
                }

                var HasQuanty = client.Tamer.Inventory.CountItensById(item.ItemId);

                // Verifica se o item é "bound" e realiza a desconexão
                if (item.Amount > HasQuanty || item.ItemInfo.BoundType == 2)
                {
                    // Cancela o trade e desconecta o jogador
                    client.Disconnect();

                    // Envia a notificação para o Discord
                    await _mapServer.CallDiscord(
                        $"Tentativa de adicionar item pra venda não-transferível (Bound) por {client.Tamer.Name}. Jogador desconectado.",
                        client,
                        "e06666", // Cor do log (vermelho)
                        "DISCONNECTED", // Tipo do log
                        "1374551861683683338" // ID do canal do Discord (substitua pelo seu canal)
                    );

                    return;
                }
            }

            client.Tamer.ConsignedShopItems.AddItems(sellList.Clone(), true);
            await _sender.Send(new UpdateItemsCommand(client.Tamer.ConsignedShopItems));

            client.Tamer.Inventory.RemoveOrReduceItems(sellList.Clone());
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

            var newShop = ConsignedShop.Create(client.TamerId, shopName, posX, posY, client.Tamer.Location.MapId,
                client.Tamer.Channel, client.Tamer.ShopItemId);

            var Id = await _sender.Send(new CreateConsignedShopCommand(newShop));

            newShop.SetId(Id.Id);
            newShop.SetGeneralHandler(Id.GeneralHandler);

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            // Logando a criação da loja e os itens no Discord
            var itemDetails = string.Join("\n", sellList.Select(item =>
                $"**{_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId)?.Name ?? "Item"}** x{item.Amount} por {item.TamerShopSellPrice} Bits"));

            await _mapServer.CallDiscord(
                $"**Lojinha Aberta**\n" +
                $"**Player**: {client.Tamer.Name} abriu uma loja consignada.\n" +
                $"**Shop Name**: {shopName}\n" +
                $"**Items a venda**:\n{itemDetails}",
                client,
                "00ff00", // Cor de sucesso (verde)
                "CONSIGNED SHOP OPEN",  // Título do log
                "1374552102784860171" // ID do canal Discord (substitua pelo canal correto)
            );

            switch (mapConfig?.Type)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new LoadConsignedShopPacket(newShop).Serialize());
                    break;

                case MapTypeEnum.Event:
                    _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new LoadConsignedShopPacket(newShop).Serialize());
                    break;

                case MapTypeEnum.Pvp:
                    _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new LoadConsignedShopPacket(newShop).Serialize());
                    break;

                default:
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new LoadConsignedShopPacket(newShop).Serialize());
                    break;
            }

            client.Tamer.UpdateShopItemId(0);
            client.Send(new PersonalShopPacket(TamerShopActionEnum.CloseWindow, client.Tamer.ShopItemId));
            client.Tamer.RestorePreviousCondition();

            switch (mapConfig?.Type)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new SyncConditionPacket(client.Tamer.GeneralHandler, client.Tamer.CurrentCondition)
                            .Serialize());
                    break;

                case MapTypeEnum.Event:
                    _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new SyncConditionPacket(client.Tamer.GeneralHandler, client.Tamer.CurrentCondition)
                            .Serialize());
                    break;

                case MapTypeEnum.Pvp:
                    _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new SyncConditionPacket(client.Tamer.GeneralHandler, client.Tamer.CurrentCondition)
                            .Serialize());
                    break;

                default:
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new SyncConditionPacket(client.Tamer.GeneralHandler, client.Tamer.CurrentCondition)
                            .Serialize());
                    break;
            }
        }
    }
}
