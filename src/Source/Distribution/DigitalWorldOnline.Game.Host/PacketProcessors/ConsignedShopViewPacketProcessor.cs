using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.GameServer;
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
            if (client == null || !client.IsConnected)
                return;

            var packet = new GamePacketReader(packetData);
            packet.Skip(4);
            var handler = packet.ReadInt();

            _logger.Information("[SHOP DEBUG] View request received from {Tamer} (Handler={Handler})", client.Tamer?.Name ?? "Unknown", handler);

            // 🔹 1. Tentativa única de handshake (para validar comunicação)
            try
            {
                var handshakeValue = (short)(client.Handshake ^ 32321);
                var timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                client.Send(new ConnectionPacket(handshakeValue, timestamp));
                _logger.Debug("[SHOP DEBUG] Handshake OK ({Handshake}) sent to {Tamer}", handshakeValue, client.Tamer?.Name ?? "Unknown");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[SHOP DEBUG] Handshake failed for {Tamer}. Continuing anyway...", client.Tamer?.Name ?? "Unknown");
            }

            // 🔹 2. Busca a loja pelo handler
            var consignedShop = _mapper.Map<ConsignedShop>(
                await _sender.Send(new ConsignedShopByHandlerQuery(handler)));

            if (consignedShop == null)
            {
                _logger.Warning("[SHOP DEBUG] Shop {Handler} not found. Sending empty shop.", handler);
                client.Send(new ConsignedShopItemsViewPacket());
                return;
            }

            // 🔹 3. Busca o dono da loja com os itens já carregados
            var shopOwner = _mapper.Map<CharacterModel>(
                await _sender.Send(new CharacterAndItemsByIdQuery(consignedShop.CharacterId)));

            if (shopOwner == null || shopOwner.ConsignedShopItems == null || shopOwner.ConsignedShopItems.Count == 0)
            {
                _logger.Warning("[SHOP DEBUG] Shop {Handler} has no items loaded. Sending empty shop to {Tamer}.", handler, client.Tamer?.Name ?? "Unknown");
                client.Send(new ConsignedShopItemsViewPacket());
                return;
            }

            // 🔹 4. Garante que os itens têm info carregada
            foreach (var item in shopOwner.ConsignedShopItems.Items)
            {
                if (item.ItemId <= 0)
                    continue;

                var info = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId);
                if (info == null)
                {
                    _logger.Warning("[SHOP DEBUG] ItemId {ItemId} missing in assets. Handler={Handler}", item.ItemId, handler);
                    continue;
                }

                item.SetItemInfo(info);
            }

            // 🔹 5. Cria o packet com os dados já carregados
            var viewPacket = new ConsignedShopItemsViewPacket(consignedShop, shopOwner.ConsignedShopItems, shopOwner.Name);

            try
            {
                _logger.Information("[SHOP DEBUG] Sending ConsignedShopItemsViewPacket (Handler={Handler}) to {Tamer}", handler, client.Tamer.Name);
                client.Send(viewPacket);

                // reenvio leve para garantir estabilidade de UI
                await Task.Delay(300);
                if (client.IsConnected)
                {
                    client.Send(viewPacket);
                    _logger.Debug("[SHOP DEBUG] Resent shop packet for {Handler} to {Tamer}", handler, client.Tamer.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[SHOP DEBUG] Error sending shop view for handler {Handler}", handler);
                try
                {
                    client.Send(new ConsignedShopItemsViewPacket());
                }
                catch (Exception e2)
                {
                    _logger.Error(e2, "[SHOP DEBUG] Failed to send empty fallback shop to {Tamer}", client.Tamer?.Name ?? "Unknown");
                }
            }
        }
    }
}
