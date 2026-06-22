using DigitalWorldOnline.Application.Separar.Commands.Delete;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class PartnerDeletePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.PartnerDelete;

        private readonly MapServer _mapServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public PartnerDeletePacketProcessor(MapServer mapServer, ILogger logger, ISender sender)
        {
            _mapServer = mapServer;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var slot = packet.ReadByte();
            var validation = packet.ReadString();

            var digimonId = client.Tamer.Digimons.First(x => x.Slot == slot).Id;

            var result = client.PartnerDeleteValidation(validation);

            if (result > 0)
            {
                var digimon = client.Tamer.Digimons.FirstOrDefault(x => x.Slot == slot);
                client.Tamer.RemoveDigimon(slot);
                await _mapServer.CallDiscord($"DELETOU O DIGIMON ID {digimonId}:{digimon.Name}/{digimon.BaseInfo.Name}!", client, "e06666", "DELETE", "1374552438069002240");

                client.Send(new PartnerDeletePacket(slot));

                await _sender.Send(new DeleteDigimonCommand(digimonId));

            }
            else
            {
                client.Send(new PartnerDeletePacket(result));
            }
        }
    }
}