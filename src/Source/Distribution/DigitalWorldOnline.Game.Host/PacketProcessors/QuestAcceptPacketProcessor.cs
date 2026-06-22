using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.GameHost.EventsServer;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;
using DigitalWorldOnline.Game.Managers;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class QuestAcceptPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.QuestAccept;

        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly PartyManager _partyManager;
        private readonly MapServer _mapServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly IConfiguration _configuration;

        // Aqui definimos o mapeamento das quests para teleportes
        private static readonly Dictionary<int, (int MapId, int SpawnPoint)> QuestTeleports = new()
        {
            { 4506, (250, 2) },
            { 4511, (254, 2) },
            { 4514, (250, 3) },
            { 4522, (251, 1) },
            { 4526, (254, 3) },
            { 4527, (254, 4) },
            { 4528, (254, 3) },
            { 4529, (251, 2) },
            { 4533, (250, 4) },
            { 4534, (250, 5) },
            { 4540, (250, 6) },
            { 4549, (250, 7) },
            { 4555, (250, 8) },
            { 4558, (254, 5) },
            { 4564, (254, 6) },
            { 4566, (250, 9) },
            { 4570, (251, 3) },
            { 4575, (255, 2) },
            { 4578, (250, 10) },
            { 4579, (251, 4) },
            { 4583, (251, 5) },
            { 4585, (254, 7) },
            { 4589, (250, 4) },
            { 4590, (250, 3) },
            { 4592, (255, 3) },
            { 4595, (250, 11) },
            { 4597, (3, 1) },
            { 4599, (251, 6) },
            { 4604, (255, 4) },
            { 4606, (250, 11) },
            { 4609, (254, 8) },
            { 4619, (251, 7) },
            { 4624, (251, 7) },
            { 4631, (251, 1) },
            { 4661, (3, 2) },
            { 4662, (254, 8) },
            { 4672, (1801, 1) },
            { 4683, (1800, 2) },
            { 4703, (1805, 0) },
            { 4730, (1802, 2) },
            { 4788, (265, 1) },
            { 4796, (263, 0) },
            { 4822, (263, 0) },
            { 4834, (267, 0) },
            // Adicione outras quests aqui se quiser
            // { 4507, (310, 1) },
            // { 4508, (200, 5) },
        };

        public QuestAcceptPacketProcessor(AssetsLoader assets, ILogger logger, ISender sender, PartyManager partyManager, MapServer mapServer, EventServer eventServer,
            PvpServer pvpServer, IConfiguration configuration, DungeonsServer dungeonServer)
        {
            _assets = assets;
            _logger = logger;
            _sender = sender;
            _partyManager = partyManager;
            _mapServer = mapServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _configuration = configuration;
            _dungeonServer = dungeonServer;
        }

        private async Task MovePlayerToMap(GameClient client, int mapId, int waypointIndex)
        {
            var currentMapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            switch (currentMapConfig.Type)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonServer.RemoveClient(client);
                    break;
                case MapTypeEnum.Event:
                    _eventServer.RemoveClient(client);
                    break;
                case MapTypeEnum.Pvp:
                    _pvpServer.RemoveClient(client);
                    break;
                case MapTypeEnum.Default:
                    _mapServer.RemoveClient(client);
                    break;
            }

            var waypoints = await _sender.Send(new MapRegionListAssetsByMapIdQuery(mapId));
            if (waypoints == null || !waypoints.Regions.Any())
            {
                _logger.Error($"Waypoints não encontrados para o mapa {mapId}.");
                client.Send(new SystemMessagePacket($"Waypoints não encontrados para o mapa {mapId}."));
                return;
            }

            var destination = waypoints.Regions.ElementAtOrDefault(waypointIndex);
            if (destination == null)
            {
                _logger.Error($"Waypoint inválido: {waypointIndex} para o mapa {mapId}.");
                client.Send(new SystemMessagePacket($"Waypoint inválido: {waypointIndex} para o mapa {mapId}."));
                return;
            }

            client.Tamer.NewLocation(mapId, destination.X, destination.Y);
            await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));

            client.Tamer.Partner.NewLocation(mapId, destination.X, destination.Y);
            await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));

            client.Tamer.UpdateState(CharacterStateEnum.Loading);
            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

            client.SetGameQuit(false);

            client.Send(new MapSwapPacket(
                _configuration["GameServer:PublicAddress"],
                _configuration["GameServer:Port"],
                mapId, destination.X, destination.Y).Serialize());
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var questId = packet.ReadShort();

            _logger.Debug($"Quest id: {questId}");
            client.blockAchievement = false;

            if (client.Tamer.Progress.AcceptQuest(questId))
            {
                var questInfo = _assets.Quest.FirstOrDefault(x => x.QuestId == questId);

                if (questInfo == null)
                {
                    _logger.Error($"Unknown quest id {questId}.");
                    client.Send(new SystemMessagePacket($"Unknown quest id {questId}."));
                    client.Tamer.Progress.RemoveQuest(questId);
                    return;
                }

                // NOVO: checagem no dicionário
                if (QuestTeleports.TryGetValue(questId, out var teleportInfo))
                {
                    await MovePlayerToMap(client, teleportInfo.MapId, teleportInfo.SpawnPoint);
                }

                foreach (var questSupply in questInfo.QuestSupplies)
                {
                    var item = new ItemModel();
                    item.SetItemId(questSupply.ItemId);
                    item.SetAmount(questSupply.Amount);
                    item.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId));

                    if (item.ItemInfo == null)
                    {
                        _logger.Error($"Item information not found for item {item.ItemId}.");
                        client.Send(new SystemMessagePacket($"Item information not found for item {item.ItemId}."));
                        client.Tamer.Progress.RemoveQuest(questId);
                        return;
                    }

                    var itemClone = (ItemModel)item.Clone();

                    if (!client.Tamer.Inventory.AddItem(itemClone))
                    {
                        client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
                        client.Tamer.Progress.RemoveQuest(questId);
                        return;
                    }
                }

                _logger.Debug($"Tamer {client.TamerId}:{client.Tamer.Name} accepted quest {questId}.");

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new AddCharacterProgressCommand(client.Tamer.Progress));
            }
        }
    }
}
