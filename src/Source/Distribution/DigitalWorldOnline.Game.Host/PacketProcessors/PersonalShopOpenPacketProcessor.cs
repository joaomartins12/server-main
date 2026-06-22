using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class PersonalShopOpenPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.TamerShopList;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly ISender _sender;

        public PersonalShopOpenPacketProcessor(
            MapServer mapServer,
            AssetsLoader assets,
            ILogger logger,
            IMapper mapper,
            ISender sender)
        {
            _mapServer = mapServer;
            _assets = assets;
            _logger = logger;
            _mapper = mapper;
            _sender = sender;
        }
        public async Task Process(GameClient client,byte[] packetData)
        {
            try
            {
                var packet = new GamePacketReader(packetData);

                packet.Skip(4);
                var handler = packet.ReadInt();

                _logger.Debug($"Searching personal shop with handler {handler}...");

                //1) If the target tamer is online and has a personal shop, use its in-memory data directly (fast path)
                var personalShop = _mapServer.FindClientByTamerHandle(handler);
                if (personalShop != null)
                {
                    _logger.Debug($"Fast-path: Found online personal shop owner {personalShop.Tamer.Name} - sending their shop items.");

                    var tShop = personalShop.Tamer.TamerShop;

                    // Populate item infos quickly from assets lookup
                    var itemInfoLookup = _assets.ItemInfo.ToDictionary(x => x.ItemId, x => x);
                    if (tShop != null)
                    {
                        foreach (var item in tShop.Items)
                        {
                            if (itemInfoLookup.TryGetValue(item.ItemId, out var itemInfo))
                                item.SetItemInfo(itemInfo);
                        }

                        // Ensure empties are fixed and persist asynchronously without blocking response
                        tShop.CheckEmptyItems();
                        _ = _sender.Send(new UpdateItemsCommand(tShop));
                    }

                    client.Send(new PersonalShopItemsViewPacket(personalShop.Tamer.TamerShop, personalShop.Tamer.ShopName));
                    return;
                }

                //2) Try to find consigned shop in map cache (cheaper than DB)
                var mapContaining = _mapServer.Maps.FirstOrDefault(m => m.Clients.Exists(c => c.TamerId == client.TamerId));
                ConsignedShop? cachedShop = null;
                if (mapContaining != null)
                {
                    // map may have ConsignedShops list updated by ConsignedShopManager
                    cachedShop = mapContaining.ConsignedShops.FirstOrDefault(s => s.GeneralHandler == (uint)handler || s.GeneralHandler == (uint)handler);
                    if (cachedShop != null)
                    {
                        _logger.Debug($"Cache-path: Found consigned shop in map cache for handler {handler}.");
                    }
                }

                //3) If cached shop found, try to find seller client in maps (online) to use in-memory items
                if (cachedShop != null)
                {
                    var sellerClient = _mapServer.FindClientByTamerId(cachedShop.CharacterId);
                    ItemListModel consignedItems = new ItemListModel(ItemListEnum.ConsignedShop);
                    string ownerName = string.Empty;

                    if (sellerClient != null && sellerClient.IsConnected)
                    {
                        ownerName = sellerClient.Tamer.Name;
                        consignedItems = sellerClient.Tamer.ConsignedShopItems ?? new ItemListModel(ItemListEnum.ConsignedShop);
                    }
                    else
                    {
                        // seller offline: fall back to DB to fetch seller items and info
                        var sellerDto = await _sender.Send(new CharacterAndItemsByIdQuery(cachedShop.CharacterId));
                        if (sellerDto != null)
                        {
                            var seller = _mapper.Map<CharacterModel>(sellerDto);
                            ownerName = seller.Name;
                            consignedItems = seller.ConsignedShopItems ?? new ItemListModel(ItemListEnum.ConsignedShop);
                        }
                    }

                    // Populate item info and persist empty-check asynchronously
                    var itemInfoLookup = _assets.ItemInfo.ToDictionary(x => x.ItemId, x => x);
                    foreach (var item in consignedItems.Items)
                    {
                        if (itemInfoLookup.TryGetValue(item.ItemId, out var info))
                            item.SetItemInfo(info);
                    }

                    consignedItems.CheckEmptyItems();
                    _ = _sender.Send(new UpdateItemsCommand(consignedItems));

                    client.Send(new ConsignedShopItemsViewPacket(cachedShop, consignedItems, ownerName, false));
                    return;
                }

                //4) Fallback: fetch consigned shop DTO from DB via sender (slower path)
                var consignedShopData = await _sender.Send(new ConsignedShopByHandlerQuery(handler));
                if (consignedShopData == null)
                {
                    _logger.Warning($"Consigned shop not found for handler {handler}. Sending empty view.");
                    client.Send(new ConsignedShopItemsViewPacket());
                    return;
                }

                var sellerData = await _sender.Send(new CharacterAndItemsByIdQuery(consignedShopData.CharacterId));
                if (sellerData == null)
                {
                    _logger.Warning($"Seller not found for shop {consignedShopData.CharacterId}. Sending empty view.");
                    client.Send(new ConsignedShopItemsViewPacket());
                    return;
                }

                var consignedShop = _mapper.Map<ConsignedShop>(consignedShopData);
                var sellerModel = _mapper.Map<CharacterModel>(sellerData);

                var itemLookup = _assets.ItemInfo.ToDictionary(x => x.ItemId, x => x);
                var consignedItemsFromDb = sellerModel.ConsignedShopItems ?? new ItemListModel(ItemListEnum.ConsignedShop);

                foreach (var item in consignedItemsFromDb.Items)
                {
                    if (itemLookup.TryGetValue(item.ItemId, out var itinfo))
                        item.SetItemInfo(itinfo);
                }

                consignedItemsFromDb.CheckEmptyItems();
                await _sender.Send(new UpdateItemsCommand(consignedItemsFromDb));

                client.Send(new ConsignedShopItemsViewPacket(consignedShop, consignedItemsFromDb, sellerModel.Name, sellerModel.Name == client.Tamer.Name));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing PersonalShopOpen packet. Sending empty response to avoid client freeze.");
                client.Send(new ConsignedShopItemsViewPacket());
            }
        }

    }
}