using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
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
            var packet = new GamePacketReader(packetData);

            packet.Skip(4);
            var handler = packet.ReadInt();
            var personalShop = _mapServer.FindClientByTamerHandle(handler);

            _logger.Debug($"Searching personal shop with handler {handler}...");

            var consignedShopData = await _sender.Send(new ConsignedShopByHandlerQuery(handler));
            if (consignedShopData == null)
            {
                _logger.Error($"Consigned shop not found for handler {handler}.");
                return;
            }

            var sellerData = await _sender.Send(new CharacterAndItemsByIdQuery(consignedShopData.CharacterId));
            if (sellerData == null)
            {
                _logger.Error($"Seller not found for shop {consignedShopData.CharacterId}.");
                return;
            }

            var consignedShop = _mapper.Map<ConsignedShop>(consignedShopData);
            var seller = _mapper.Map<CharacterModel>(sellerData);

            var itemInfoLookup = _assets.ItemInfo.ToDictionary(x => x.ItemId,x => x);

            if (personalShop != null)
            {
                _logger.Debug($"Found Tamer {personalShop.Tamer.Name} with shop {personalShop.Tamer.ShopName} - {handler}.");

                foreach (var item in personalShop.Tamer.TamerShop.Items)
                {
                    if (itemInfoLookup.TryGetValue(item.ItemId,out var itemInfo))
                    {
                        item.SetItemInfo(itemInfo);
                    }

                    if (item.ItemId > 0 && item.ItemInfo == null)
                    {
                        item.SetItemId();
                        personalShop.Tamer.TamerShop.CheckEmptyItems();

                        _logger.Debug($"Updating consigned shop item list...");
                        await _sender.Send(new UpdateItemsCommand(personalShop.Tamer.TamerShop));
                    }
                }

                _logger.Debug($"Sending consigned shop item list view packet...");
                client.Send(new PersonalShopItemsViewPacket(personalShop.Tamer.TamerShop,personalShop.Tamer.ShopName));
                return;
            }

            _logger.Debug($"Sending consigned shop items for {seller.Name}...");

            foreach (var item in seller.ConsignedShopItems.EquippedItems)
            {
                if (itemInfoLookup.TryGetValue(item.ItemId,out var itemInfo))
                {
                    item.SetItemInfo(itemInfo);
                }

                if (item.ItemId > 0 && item.ItemInfo == null)
                {

                    item.SetItemId();
                    personalShop.Tamer.TamerShop.CheckEmptyItems();

                    _logger.Debug($"Updating equipped consigned shop items...");
                    await _sender.Send(new UpdateItemsCommand(seller.ConsignedShopItems));
                }
            }

            if (seller.Name == client.Tamer.Name)
            {
                client.Send(new ConsignedShopItemsViewPacket(consignedShop,seller.ConsignedShopItems,seller.Name,true));
            }
            else
            {
                client.Send(new ConsignedShopItemsViewPacket(consignedShop,seller.ConsignedShopItems,seller.Name));
            }
        }

    }
}