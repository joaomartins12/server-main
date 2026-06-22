using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Writers;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ArchiveAcademyErrorPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ArchiveAcademyError;

        private readonly ILogger _logger;

        public ArchiveAcademyErrorPacketProcessor(ILogger logger)
        {
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packets = new GamePacketReader(packetData);


            var errorCode = 0;

            var packet = new PacketWriter();

            packet.Type(3228);
            packet.WriteInt(errorCode);
            
            client.Send(packet.Serialize());

            //client.Send(new ArchiveAcademyIniciarPacket());
        }
    }
}

