using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.GameHost;
using MediatR;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ChangeTamerModelProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.TamerChangeModel;


        private readonly MapServer _mapServer;
        private readonly IConfiguration _configuration;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        private const string GameServerAddress = "GameServer:Address";
        private const string GamerServerPublic = "GameServer:PublicAddress";
        private const string GameServerPort = "GameServer:Port";

        public ChangeTamerModelProcessor(
            MapServer mapServer,
            IConfiguration configuration,
            ISender sender,
            ILogger logger)
        {
            _configuration = configuration;
            _mapServer = mapServer;
            _sender = sender;
            _logger = logger;
        }
        public async Task Process(GameClient client, byte[] packetData)
        {

            var packet = new GamePacketReader(packetData);

            int newModel = packet.ReadInt();
            short itemSlot = packet.ReadShort();

            var inventoryItem = client.Tamer.Inventory.FindItemBySlot(itemSlot);

            if (inventoryItem != null)
            {
                client.Tamer.Inventory.RemoveOrReduceItem(inventoryItem, 1, itemSlot);

                client.Tamer.RemovePartnerPassiveBuff();
                client.Tamer.SetPartnerPassiveBuff((CharacterModelEnum)newModel);

                var ActiveSkill = client.Tamer.ActiveSkill.Where(x => x.Type == Commons.Enums.ClientEnums.TamerSkillTypeEnum.Normal && x.SkillId > 0).ToList();

                if (ActiveSkill.Count > 0)
                {
                    foreach (var skill in ActiveSkill)
                    {
                        var activeSkill = client.Tamer.ActiveSkill.FirstOrDefault(x => x.Id == skill.Id);
                        activeSkill.SetTamerSkill(0, 0, Commons.Enums.ClientEnums.TamerSkillTypeEnum.Normal);

                        await _sender.Send(new UpdateTamerSkillCooldownByIdCommand(activeSkill));
                    }
                }

                await _sender.Send(new ChangeTamerModelByIdCommand(client.Tamer.Id, (CharacterModelEnum)newModel));
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));

                client.Send(new ChangeTamerModelPacket(newModel, itemSlot));

                _mapServer.RemoveClient(client);

                // Definindo o mapa e waypoint fixos
                int mapaId = 3;
                int waypoint = 1;

                // Supondo que existe um método para obter X e Y a partir do waypoint
                // Substitua por sua lógica real de obtenção de X e Y do waypoint
                (int x, int y) = GetWaypointLocation(mapaId, waypoint);

                client.Tamer.NewLocation(mapaId, x, y);
                await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));

                client.Tamer.Partner.NewLocation(mapaId, x, y);
                await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));

                client.Tamer.UpdateState(CharacterStateEnum.Loading);
                await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

                client.SetGameQuit(false);

                client.Send(new MapSwapPacket(
                    _configuration[GamerServerPublic],
                    _configuration[GameServerPort],
                    mapaId,
                    x,
                    y)
                    .Serialize());
            }
        }

        // Adicione este método utilitário na mesma classe
        private (int x, int y) GetWaypointLocation(int mapId, int waypoint)
        {
            // Exemplo de lógica fixa para waypoint 1 do mapa 3
            if (mapId == 3 && waypoint == 1)
            {
                // Substitua pelos valores reais de X e Y do waypoint 1 do mapa 3
                return (20234, 41309); // Exemplo: X=100, Y=200
            }
            // Adicione outros waypoints se necessário
            return (0, 0);
        }
    }
}