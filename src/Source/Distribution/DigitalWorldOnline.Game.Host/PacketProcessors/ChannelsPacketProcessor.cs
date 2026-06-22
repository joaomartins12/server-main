using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using MediatR;
using Microsoft.IdentityModel.Tokens;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ChannelsPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.Channels;

        private readonly ISender _sender;

        public ChannelsPacketProcessor(ISender sender)
        {
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var channels = new Dictionary<byte, byte>();

            if (!client.DungeonMap)
            {
                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
                if (mapConfig == null || mapConfig.Channels <= 0)
                {
                    // Se não existem canais → mostra aviso
                    client.Send(new SystemMessagePacket("No channels available in this map.").Serialize());
                    return;
                }

                // Apenas channel 0 válido
                channels[0] = 0;
            }
            else
            {
                // Dungeon não tem canais
                client.Send(new SystemMessagePacket("No channels available in dungeon maps.").Serialize());
                return;
            }

            if (!channels.IsNullOrEmpty())
            {
                client.Send(new AvailableChannelsPacket(channels).Serialize());
            }
        }
    }
}
