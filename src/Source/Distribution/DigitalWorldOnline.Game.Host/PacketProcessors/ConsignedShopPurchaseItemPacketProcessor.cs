using System.Collections.Concurrent;
using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Delete;
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
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedShopPurchaseItemPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedShopPurchaseItem;

        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly ISender _sender;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;

        public ConsignedShopPurchaseItemPacketProcessor(AssetsLoader assets, ILogger logger, IMapper mapper, ISender sender, MapServer mapServer, DungeonsServer dungeonServer)
        {
            _assets = assets;
            _logger = logger;
            _mapper = mapper;
            _sender = sender;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
        }

        private static readonly ConcurrentDictionary<int, object> ShopLocks = new();

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var shopHandler = packet.ReadInt();
            var shopSlot = packet.ReadInt();
            var shopSlotInDatabase = shopSlot - 1;
            var boughtItemId = packet.ReadInt();
            var boughtAmount = packet.ReadInt();
            packet.Skip(60);
            var boughtUnitPrice = packet.ReadInt64();

            // 🔒 Garantir que uma única thread processe uma compra por loja
            var shopLock = ShopLocks.GetOrAdd(shopHandler, _ => new object());

            // Usar um método separado para evitar o uso de 'await' dentro de 'lock'
            await ProcessWithLockAsync(client, shopHandler, shopSlot, shopSlotInDatabase, boughtItemId, boughtAmount, boughtUnitPrice, shopLock);
        }

        private async Task ProcessWithLockAsync(GameClient client, int shopHandler, int shopSlot, int shopSlotInDatabase, int boughtItemId, int boughtAmount, long boughtUnitPrice, object shopLock)
        {
            // Usar 'Task.Run' para executar o código dentro do 'lock' em um contexto síncrono
            await Task.Run(() =>
            {
                lock (shopLock)
                {
                    ProcessInternal(client, shopHandler, shopSlot, shopSlotInDatabase, boughtItemId, boughtAmount, boughtUnitPrice).GetAwaiter().GetResult();
                }
            });
        }

        private async Task ProcessInternal(GameClient client, int shopHandler, int shopSlot, int shopSlotInDatabase, int boughtItemId, int boughtAmount, long boughtUnitPrice)
        {
            var shop = _mapper.Map<ConsignedShop>(await _sender.Send(new ConsignedShopByHandlerQuery(shopHandler)));

            if (shop == null)
            {
                client.Send(new UnloadConsignedShopPacket(shopHandler));
                return;
            }

            var refreshedSeller = _mapper.Map<CharacterModel>(await _sender.Send(new CharacterAndItemsByIdQuery(shop.CharacterId)));

            if (refreshedSeller == null)
            {
                await _sender.Send(new DeleteConsignedShopCommand(shopHandler));
                client.Send(new UnloadConsignedShopPacket(shopHandler));
                return;
            }

            // 🛑 Verifica se a loja está sendo fechada no momento da compra
            if (refreshedSeller.ConsignedShopItems == null || refreshedSeller.ConsignedShopItems.Count == 0)
            {
                client.Send(new NoticeMessagePacket("The shop is being closed. Transaction canceled."));
                client.SetGameQuit(true);
                client.Disconnect();

                await _mapServer.CallDiscord(
                    $"[ConsignedShop] {client.Tamer.Name} (AccountId: {client.Tamer.AccountId}) foi desconectado tentando comprar na loja fechada (ShopHandler: {shopHandler}, ShopSlot: {shopSlot}).",
                    client,
                    "e06666",
                    "DISCONNECTED",
                    "1374551861683683338"
                );
                return;
            }

            if (refreshedSeller.Name == client.Tamer.Name)
            {
                client.Send(new NoticeMessagePacket($"You cannot buy from the store itself!"));
                return;
            }

            var currentItem = refreshedSeller.ConsignedShopItems.Items.FirstOrDefault(x => x.Slot == shopSlotInDatabase);

            if (currentItem == null || currentItem.Amount < boughtAmount)
            {
                client.Send(new NoticeMessagePacket("The item is no longer available or amount exceeds available stock."));
                client.Send(new ConsignedShopBoughtItemPacket(TamerShopActionEnum.NoPartFound, shopSlot, boughtAmount).Serialize());
                return;
            }

            var totalValue = currentItem.TamerShopSellPrice * boughtAmount;

            if (client.Tamer.Inventory.Bits < totalValue)
            {
                client.SetGameQuit(true);
                client.Disconnect();
                return;
            }

            client.Tamer.Inventory.RemoveBits(totalValue);
            await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory));

            var newItem = new ItemModel(currentItem.ItemId, boughtAmount);
            var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == currentItem.ItemId);
            newItem.SetItemInfo(itemInfo);

            client.Tamer.Inventory.AddItems(((ItemModel)newItem.Clone()).GetList());
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

            var sellerClient = client.Server.FindByTamerId(shop.CharacterId);

            if (sellerClient is { IsConnected: true })
            {
                var itemName = itemInfo?.Name ?? "item";
                sellerClient.Send(new NoticeMessagePacket($"You sold {boughtAmount}x {itemName} for {client.Tamer.Name}!"));

                sellerClient.Tamer.ConsignedWarehouse.AddBits(totalValue);
                await _sender.Send(new UpdateItemListBitsCommand(sellerClient.Tamer.ConsignedWarehouse));

                refreshedSeller.ConsignedShopItems.RemoveOrReduceItems(((ItemModel)newItem.Clone()).GetList(), true);
                await _sender.Send(new UpdateItemsCommand(refreshedSeller.ConsignedShopItems));
            }
            else
            {
                refreshedSeller.ConsignedWarehouse.AddBits(totalValue);
                refreshedSeller.ConsignedShopItems.RemoveOrReduceItems(((ItemModel)newItem.Clone()).GetList(), true);

                await _sender.Send(new UpdateItemListBitsCommand(refreshedSeller.ConsignedWarehouse));
                await _sender.Send(new UpdateItemsCommand(refreshedSeller.ConsignedShopItems));
            }

            if (refreshedSeller.ConsignedShopItems.Count == 0)
            {
                await _sender.Send(new DeleteConsignedShopCommand(shopHandler));
                await _sender.Send(new UpdateItemsCommand(refreshedSeller.ConsignedShopItems));

                _mapServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id, new UnloadConsignedShopPacket(shopHandler).Serialize());
                _mapServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id, new ConsignedShopClosePacket().Serialize());
            }

            client.Send(new ConsignedShopBoughtItemPacket(TamerShopActionEnum.TamerShopWindow, shopSlot, boughtAmount));

            var itemNameForDiscord = itemInfo?.Name ?? "Unknown Item";
            await _mapServer.CallDiscord(
                $"**Compra Concluída**\n" +
                $"{client.Tamer.Name} comprou {boughtAmount}x {itemNameForDiscord} de {refreshedSeller.Name}.\n" +
                $"**Total Bits**: {totalValue}\n" +
                $"**Vendedor**: {refreshedSeller.Name}",
                client,
                "00ff00",
                "PURCHASE",
                "1374552173966528622"
            );

            // Libera o lock ao fim da transação
            ShopLocks.TryRemove(shopHandler, out _);
        }
    }
}
