using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class PartnerAttackPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.PartnerAttack;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly AttackManager _attackManager;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public PartnerAttackPacketProcessor(
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            ILogger logger,
            ISender sender,
            AttackManager attackManager)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _logger = logger;
            _sender = sender;
            _attackManager = attackManager;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            var attackerHandler = packet.ReadInt();
            var targetHandler = packet.ReadInt();

            // Broadcast helpers
            Action<long, byte[]> broadcastAction = client.DungeonMap
                ? _dungeonServer.BroadcastForTamerViewsAndSelf
                : client.EventMap ? _eventServer.BroadcastForTamerViewsAndSelf
                : _mapServer.BroadcastForTamerViewsAndSelf;

            Func<short, long, bool> broadcastMobs = client.DungeonMap
                ? _dungeonServer.IMobsAttacking
                : client.EventMap ? _eventServer.IMobsAttacking
                : _mapServer.IMobsAttacking;

            var targetMob = client.DungeonMap
                ? _dungeonServer.GetIMobByHandler(client.Tamer.Location.MapId, targetHandler, client.Tamer.Id)
                : client.EventMap
                    ? _eventServer.GetIMobByHandler(client.Tamer.Location.MapId, targetHandler, client.Tamer.Id)
                    : _mapServer.GetIMobByHandler(client.Tamer.Location.MapId, targetHandler, client.Tamer.Id);

            // Se não houver alvo ou partner
            if (targetMob == null || client.Partner == null)
            {
                if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId))
                {
                    client.Tamer.StopBattle(true);
                    broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                }
                return;
            }

            client.Partner.StartAutoAttack();

            if (!targetMob.Alive)
            {
                // Alvo já morto
                if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId))
                {
                    client.Tamer.StopBattle(true);
                    broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                }
                return;
            }

            if (client.Partner.IsAttacking)
            {
                // já atacando — só atualiza o target se for outro mob
                if (client.Tamer.TargetMob?.GeneralHandler != targetMob.GeneralHandler)
                {
                    client.Tamer.SetHidden(false);
                    client.Tamer.UpdateTarget(targetMob);
                    client.Partner.StartAutoAttack();
                }
                return;
            }

            // Preparar ataque
            client.Partner.SetEndAttacking();

            if (!client.Tamer.InBattle)
            {
                client.Tamer.SetHidden(false);
                broadcastAction(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                client.Tamer.StartBattle(targetMob);
            }
            else
            {
                client.Tamer.SetHidden(false);
                client.Tamer.UpdateTarget(targetMob);
            }

            if (!targetMob.InBattle)
            {
                broadcastAction(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                targetMob.StartBattle(client.Tamer);
            }
            else targetMob.AddTarget(client.Tamer);

            client.Tamer.Partner.StartAutoAttack();

            // Acerto ou miss
            if (!client.Tamer.GodMode && client.Tamer.CanMissHit())
            {
                broadcastAction(client.TamerId, new MissHitPacket(attackerHandler, targetHandler).Serialize());
            }
            else
            {
                // Dano
                var critBonusMultiplier = 0.00;
                var blocked = false;

                var finalDmg = client.Tamer.GodMode
                    ? targetMob.CurrentHP
                    : AttackManager.CalculateDamage(client, out critBonusMultiplier, out blocked);

                if (finalDmg <= 0) finalDmg = 1;
                if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);
                var hitType = blocked ? 2 : critBonusMultiplier > 0 ? 1 : 0;

                if (newHp > 0)
                {
                    broadcastAction(client.TamerId,
                        new HitPacket(attackerHandler, targetHandler, finalDmg, targetMob.HPValue, newHp, hitType).Serialize());
                }
                else
                {
                    client.Partner.SetEndAttacking();
                    broadcastAction(client.TamerId,
                        new KillOnHitPacket(attackerHandler, targetHandler, finalDmg, hitType).Serialize());

                    targetMob.Die();

                    if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId))
                    {
                        client.Tamer.StopBattle(true);
                        broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                    }
                }
            }

            // Próximo hit (AS)
            client.Tamer.Partner.NextHitTime = DateTime.UtcNow.AddMilliseconds(client.Partner.AS);
        }
    }
}
