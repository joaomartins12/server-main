using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class PartnerStopPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.PartnerStop;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public PartnerStopPacketProcessor(
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            ILogger logger,
            ISender sender)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            if (client?.Partner == null)
                return;

            var partner = client.Partner;
            var attackerHandler = partner.GeneralHandler;

            // Para apenas o auto attack (não mexe em skills)
            partner.StopAutoAttack();

            // Se não há mobs mais em aggro, sai do combate
            Func<short, long, bool> broadcastMobs = client.DungeonMap
                ? _dungeonServer.IMobsAttacking
                : _mapServer.IMobsAttacking;

            if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId))
            {
                client.Tamer.StopBattle(true);

                // broadcast apenas em dungeon/map
                if (client.DungeonMap)
                {
                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new SetCombatOffPacket(attackerHandler).Serialize());
                }
                else
                {
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new SetCombatOffPacket(attackerHandler).Serialize());
                }
            }

            _logger.Information($"[Stop] Partner {partner.Id} parou auto-attack para {client.Tamer?.Name ?? "Unknown"}.");
            await Task.CompletedTask;
        }
    }
}
