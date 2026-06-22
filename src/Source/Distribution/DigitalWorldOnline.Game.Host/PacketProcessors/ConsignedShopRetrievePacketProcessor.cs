using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Delete;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.Commons.Writers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedShopRetrievePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedShopRetrieve;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly EventServer _eventServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly ISender _sender;

        public ConsignedShopRetrievePacketProcessor(
            AssetsLoader assets,
            MapServer mapServer,
            EventServer eventServer,
            DungeonsServer dungeonsServer,
            PvpServer pvpServer,
            ILogger logger,
            IMapper mapper,
            ISender sender)
        {
            _assets = assets;
            _mapServer = mapServer;
            _eventServer = eventServer;
            _dungeonsServer = dungeonsServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _mapper = mapper;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var shop = _mapper.Map<ConsignedShop>(await _sender.Send(new ConsignedShopByTamerIdQuery(client.TamerId)));

            if (shop != null)
            {
                var seller = _mapper.Map<CharacterModel>(await _sender.Send(new CharacterAndItemsByIdQuery(shop.CharacterId)));
                List<ItemModel> equippedItems = seller.ConsignedShopItems?.EquippedItems?.ToList() ?? new List<ItemModel>();
                var items = equippedItems.Clone();

                items.ForEach(item =>
                {
                    item.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId));
                });

                // Verifica quantos slots estão disponíveis no armazém
                var warehouse = client.Tamer.ConsignedWarehouse;
                var availableSlots = warehouse.TotalEmptySlots;
                var requiredSlots = items.Count(item => !warehouse.Items.Any(wItem => wItem.ItemId == item.ItemId && wItem.Amount < wItem.ItemInfo?.Overlap));

                if (requiredSlots > availableSlots)
                {
                    var errorMessage = new PacketWriter();
                    errorMessage.WriteString("Seu armazém não tem espaço suficiente para recolher todos os itens da loja.");
                    client.Send(errorMessage);
                    _logger.Warning($"Falha ao recolher loja de {client.Tamer.Name}: itens excedem o espaço do armazém.");

                    await _mapServer.CallDiscord(
                        $"**Erro ao recolher loja consignada**\n" +
                        $"**Player**: {client.Tamer.Name}\n" +
                        $"**Motivo**: Espaço insuficiente no armazém.\n" +
                        $"**Itens na loja**: {items.Count} | **Espaço disponível**: {availableSlots}",
                        client,
                        "ff6600",
                        "CONSIGNED SHOP ERROR",
                        "1374552269764431923"
                    );

                    return;
                }

                // Se tiver espaço, segue o processo normal
                client.Tamer.ConsignedShopItems.RemoveOrReduceItems(items.Clone());
                await _sender.Send(new DeleteConsignedShopCommand(shop.GeneralHandler));
                warehouse.AddItems(items.Clone());

                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
                switch (mapConfig?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(shop).Serialize());
                        break;
                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(shop).Serialize());
                        break;
                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(shop).Serialize());
                        break;
                    default:
                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(shop).Serialize());
                        break;
                }

                await _sender.Send(new UpdateItemsCommand(client.Tamer.ConsignedShopItems));
                await _sender.Send(new UpdateItemsCommand(warehouse));

                // Log no Discord
                var itemDetails = string.Join("\n", items.Select(item => $"- {item.ItemInfo?.Name ?? "Desconhecido"} x{item.Amount}"));
                await _mapServer.CallDiscord(
                    $"**Lojinha Fechada**\n" +
                    $"**Player**: {client.Tamer.Name} fechou sua loja consignada.\n" +
                    $"**Itens devolvidos**:\n{itemDetails}",
                    client,
                    "ff0000", // Vermelho
                    "CONSIGNED SHOP CLOSE",
                    "1374552269764431923"
                );
            }

            _logger.Debug($"Sending consigned shop close packet...");
            client.Send(new ConsignedShopClosePacket());
        }

    }
}
