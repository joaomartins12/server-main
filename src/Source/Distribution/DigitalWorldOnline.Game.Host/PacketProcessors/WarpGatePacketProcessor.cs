using Microsoft.Extensions.Configuration;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.GameHost.EventsServer;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.Game.Managers;
using MediatR;
using Serilog;
using DigitalWorldOnline.Commons.Enums.ClientEnums;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class WarpGatePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.WarpGate;

        private const string GamerServerPublic = "GameServer:PublicAddress";
        private const string GameServerPort = "GameServer:Port";

        private readonly PartyManager _partyManager;
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        public WarpGatePacketProcessor(
            PartyManager partyManager,
            AssetsLoader assets,
            MapServer mapServer,
            DungeonsServer dungeonServer,
            EventServer eventServer,
            PvpServer pvpServer,
            ISender sender,
            ILogger logger,
            IConfiguration configuration)
        {
            _partyManager = partyManager;
            _configuration = configuration;
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            var portalId = packet.ReadInt();

            var portal = _assets.Portal.FirstOrDefault(x => x.Id == portalId);
            var portalRequest = _assets.Npcs.FirstOrDefault(x => x.NpcId == portal?.NpcId);

            if (portal == null)
            {
                client.Send(new SystemMessagePacket($"Portal {portalId} not found."));
                _logger.Error($"Portal id {portalId} not found.");

                var mapId = client.Tamer.Location.MapId;
                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(mapId));
                var waypoints = await _sender.Send(new MapRegionListAssetsByMapIdQuery(mapId));

                if (mapConfig == null || waypoints == null || !waypoints.Regions.Any())
                {
                    client.Send(new SystemMessagePacket($"Map information not found for {mapId}"));
                    _logger.Error($"Map information not found for {mapId}");
                    return;
                }

                if (client.DungeonMap)
                    _dungeonServer.RemoveClient(client);
                else
                    _mapServer.RemoveClient(client);

                var destination = waypoints.Regions.First();

                client.Tamer.NewLocation(mapId, destination.X, destination.Y);
                await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));

                client.Tamer.Partner.NewLocation(mapId, destination.X, destination.Y);
                await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));

                client.Tamer.UpdateState(CharacterStateEnum.Loading);
                await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

                client.Send(new MapSwapPacket(
                    _configuration[GamerServerPublic],
                    _configuration[GameServerPort],
                    mapId, destination.X, destination.Y));
            }
            else
            {
                // requisitos
                if (portalRequest?.Portals != null)
                {
                    var portalRequestInfo = portalRequest.Portals;
                    var request = portalRequestInfo.SelectMany(x => x.PortalsAsset).ToList();
                    var removeInfo = request[portal.PortalIndex];

                    for (int i = 0; i < 3; i++)
                    {
                        switch (removeInfo.npcPortalsAsset[i].Type)
                        {
                            case NpcResourceTypeEnum.Money:
                                client.Tamer.Inventory.RemoveBits(removeInfo.npcPortalsAsset[i].ItemId);
                                await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory));
                                break;

                            case NpcResourceTypeEnum.Item:
                                var targetItem = client.Tamer.Inventory.FindItemById(removeInfo.npcPortalsAsset[i].ItemId);
                                if (targetItem != null)
                                {
                                    client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);
                                    await _sender.Send(new UpdateItemCommand(targetItem));
                                }
                                break;
                        }
                    }
                }

                // 🔹 Warp local (mesmo mapa → sem Loading/MapSwap)
                if (portal.IsLocal || portal.DestinationMapId == client.Tamer.Location.MapId)
                {
                    client.Tamer.NewLocation(portal.DestinationMapId, portal.DestinationX, portal.DestinationY);
                    await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));

                    client.Tamer.Partner.NewLocation(portal.DestinationMapId, portal.DestinationX, portal.DestinationY);
                    await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));

                    // Apenas troca de posição dentro do mesmo mapa
                    client.Send(new LocalMapSwapPacket(
                        client.Tamer.GeneralHandler,
                        client.Tamer.Partner.GeneralHandler,
                        portal.DestinationX, portal.DestinationY,
                        portal.DestinationX, portal.DestinationY
                    ));

                    // 🔹 Força atualização da visibilidade dos tamers
                    var gameMap = _mapServer.Maps.FirstOrDefault(m => m.MapId == client.Tamer.Location.MapId);
                    if (gameMap != null)
                    {
                        _mapServer.ForceUpdateTamers(gameMap);
                    }

                    // 🔹 Não envia Loading aqui
                    return;
                }

                // warp normal
                if (client.DungeonMap)
                    _dungeonServer.RemoveClient(client);
                else
                    _mapServer.RemoveClient(client);

                client.Tamer.NewLocation(portal.DestinationMapId, portal.DestinationX, portal.DestinationY);
                await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));

                client.Tamer.Partner.NewLocation(portal.DestinationMapId, portal.DestinationX, portal.DestinationY);
                await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));

                client.Tamer.UpdateState(CharacterStateEnum.Loading);
                await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

                client.SetGameQuit(false);

                client.Send(new MapSwapPacket(
                    _configuration[GamerServerPublic],
                    _configuration[GameServerPort],
                    client.Tamer.Location.MapId,
                    client.Tamer.Location.X,
                    client.Tamer.Location.Y));

                var party = _partyManager.FindParty(client.TamerId);
                if (party != null)
                {
                    party.UpdateMember(party[client.TamerId], client.Tamer);

                    _mapServer.BroadcastForTargetTamers(
                        party.GetMembersIdList(),
                        new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());

                    _dungeonServer.BroadcastForTargetTamers(
                        party.GetMembersIdList(),
                        new PartyMemberWarpGatePacket(party[client.TamerId], client.Tamer).Serialize());
                }
            }
        }
    }
}
