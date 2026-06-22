using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Delete;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ConsignedShopViewPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsignedShopView;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly EventServer _eventServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;
        private readonly IMapper _mapper;
        private readonly ILogger _logger;

        public ConsignedShopViewPacketProcessor(
            MapServer mapServer,
            EventServer eventServer,
            DungeonsServer dungeonsServer,
            PvpServer pvpServer,
            AssetsLoader assets,
            IMapper mapper,
            ISender sender,
            ILogger logger)
        {
            _mapServer = mapServer;
            _eventServer = eventServer;
            _dungeonsServer = dungeonsServer;
            _pvpServer = pvpServer;
            _assets = assets;
            _mapper = mapper;
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            _logger.Information("Pacote de Visualização da Loja Consignada recebido");

            packet.Skip(4);
            var handler = packet.ReadInt();

            _logger.Information("Buscando loja consignada com o identificador {Handler}", handler);

            var consignedShop =
                _mapper.Map<ConsignedShop>(await _sender.Send(new ConsignedShopByHandlerQuery(handler)));
            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
            if (consignedShop == null)
            {
                _logger.Warning("Loja consignada não encontrada com o identificador {Handler}", handler);
                client.Send(new ConsignedShopItemsViewPacket());

                // descarregamento
                switch (mapConfig?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(handler).Serialize());
                        break;
                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(handler).Serialize());
                        break;
                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(handler).Serialize());
                        break;
                    default:
                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(handler).Serialize());
                        break;
                }

                return;
            }

            var shopOwner =
                _mapper.Map<CharacterModel>(
                    await _sender.Send(new CharacterAndItemsByIdQuery(consignedShop.CharacterId)));

            if (shopOwner == null || shopOwner.ConsignedShopItems.Count == 0)
            {
                _logger.Warning("Shop {Handler} vazia ou inválida, eliminando...", handler);
                await _sender.Send(new DeleteConsignedShopCommand(handler));
                client.Send(new ConsignedShopItemsViewPacket());

                // descarregamento
                switch (mapConfig?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(handler).Serialize());
                        break;
                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(handler).Serialize());
                        break;
                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(handler).Serialize());
                        break;
                    default:
                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UnloadConsignedShopPacket(handler).Serialize());
                        break;
                }

                return;
            }

            foreach (var item in shopOwner.ConsignedShopItems.Items)
            {
                item.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId));

                if (item.ItemId > 0 && item.ItemInfo == null)
                {
                    item.SetItemId();
                    shopOwner.ConsignedShopItems.CheckEmptyItems();
                    _logger.Information("Atualizando a lista de itens da loja consignada {Handler}", handler);
                    await _sender.Send(new UpdateItemsCommand(shopOwner.ConsignedShopItems));
                }
            }

            var viewPacket = new ConsignedShopItemsViewPacket(consignedShop, shopOwner.ConsignedShopItems, shopOwner.Name);

            _logger.Information("Enviando pacote de visualização inicial da loja consignada {Handler} para {Tamer}",
                handler, client.Tamer.Name);
            client.Send(viewPacket);

            // 🔄 ciclo de reenvio
            _ = Task.Run(async () =>
            {
                for (int i = 1; i <= 50; i++)
                {
                    await Task.Delay(300);
                    client.Send(viewPacket);
                    _logger.Information("[SHOP DEBUG] Reenvio {ReenvioCount}s da shop {Handler} para {Tamer}",
                        i, handler, client.Tamer.Name);
                }
            });
        }
    }
}
