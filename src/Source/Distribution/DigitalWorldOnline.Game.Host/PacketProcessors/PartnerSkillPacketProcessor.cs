using Microsoft.Extensions.Configuration;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;
using static DigitalWorldOnline.Commons.Packets.GameServer.AddBuffPacket;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Models;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.CompilerServices;
using static Quartz.Logging.OperationName;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public partial class PartnerSkillPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.PartnerSkill;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly DigimonSkillManager _digimonSkillManager;


        public PartnerSkillPacketProcessor(AssetsLoader assets, MapServer mapServer, DungeonsServer dungeonServer, EventServer eventServer, PvpServer pvpServer,
            ILogger logger, ISender sender, DigimonSkillManager digimonSkillManager)
        {
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
            _digimonSkillManager = digimonSkillManager;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var skillSlot = packet.ReadByte();
            var attackerHandler = packet.ReadInt();
            var targetHandler = packet.ReadInt();

            if (client.Partner == null) await Task.CompletedTask;

            Func<short, long, bool> broadcastMobs = client.DungeonMap
                ? (id, data) => _dungeonServer.IMobsAttacking(id, data)
                : client.EventMap
                    ? (id, data) => _eventServer.IMobsAttacking(id, data)
                    : (id, data) => _mapServer.IMobsAttacking(id, data);

            var skill = _assets.DigimonSkillInfo.FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            Action<long, byte[]> broadcastAction = client.DungeonMap
                ? (id, data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id, data)
                : client.EventMap
                    ? (id, data) => _eventServer.BroadcastForTamerViewsAndSelf(id, data)
                    : (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data);

            if (skill == null || skill.SkillInfo == null)
            {
                await Task.CompletedTask;
            }

            if (client.Tamer.Partner.NextSkillTimeDict.TryGetValue(skillSlot, out DateTime nextSkillTime) && DateTime.UtcNow < nextSkillTime)
            {
                return;
            }

            SkillTypeEnum skillType;

            var areaOfEffect = skill.SkillInfo.AreaOfEffect;
            var range = skill.SkillInfo.Range;
            var targetType = skill.SkillInfo.Target;

            Func<short, int, int, long, IMob> getMobHandler = client.DungeonMap
                ? _dungeonServer.GetNearestIMobToTarget
                : client.EventMap ? _eventServer.GetNearestIMobToTarget
                : _mapServer.GetNearestIMobToTarget;

            Func<short, int, int, long, List<IMob>> getNearbyTargetMob = client.DungeonMap
                ? _dungeonServer.GetIMobsNearbyTargetMob
                : client.EventMap
                    ? _eventServer.GetIMobsNearbyTargetMob
                    : _mapServer.GetIMobsNearbyTargetMob;

            Func<Location, int, long, List<IMob>> getNearbyPartnerMob = client.DungeonMap
                ? _dungeonServer.GetIMobsNearbyPartner
                : client.EventMap
                    ? _eventServer.GetIMobsNearbyPartner
                    : _mapServer.GetIMobsNearbyPartner;

            if (range < 500)
            {
                range = 900;
            }

            var targetMobs = new List<IMob>();

            if (areaOfEffect > 0)
            {
                skillType = SkillTypeEnum.TargetArea;

                var targets = new List<IMob>();

                if (targetType == 17)
                {
                    targets = getNearbyPartnerMob(client.Partner.Location, areaOfEffect, client.TamerId);
                }
                else if (targetType == 18)
                {
                    targets = getNearbyTargetMob(client.Partner.Location.MapId, targetHandler, areaOfEffect, client.TamerId);
                }

                targetMobs.AddRange(targets);
            }
            else if (areaOfEffect == 0 && targetType == 80)
            {
                skillType = SkillTypeEnum.Implosion;

                var targets = new List<IMob>();

                targets = getNearbyTargetMob(client.Partner.Location.MapId, targetHandler, range, client.TamerId);

                targetMobs.AddRange(targets);
            }
            else
            {
                skillType = SkillTypeEnum.Single;

                var mob = getMobHandler(client.Tamer.Location.MapId, targetHandler, range, client.TamerId);

                if (mob == null)
                    await Task.CompletedTask;

                targetMobs.Add(mob);
            }

            if (targetMobs.Any())
            {
                if (skillType == SkillTypeEnum.Single && !targetMobs.First().Alive)
                    await Task.CompletedTask;

                client.Partner.ReceiveDamage(skill.SkillInfo.HPUsage);
                client.Partner.UseDs(skill.SkillInfo.DSUsage);

                var castingTime = (int)Math.Round(skill.SkillInfo.CastingTime);

                if (skillSlot == 0) castingTime = 2000;
                if (skillSlot == 1) castingTime = 3000;
                if (skillSlot == 2) castingTime = 5000;
                if (skillSlot == 3) castingTime = 8000;

                client.Partner.SetEndCasting(castingTime);

                if (!client.Tamer.InBattle)
                {
                    client.Tamer.SetHidden(false);
                    broadcastAction(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                    client.Tamer.StartBattleWithSkill(targetMobs, skillType);
                }
                else
                {
                    client.Tamer.SetHidden(false);
                    client.Tamer.UpdateTargetWithSkill(targetMobs, skillType);
                }

                if (skillType != SkillTypeEnum.Single)
                {
                    var totalSkillDamage = _digimonSkillManager.SkillDamage(client, skill, skillSlot);

                    targetMobs.ForEach(targetMob =>
                    {
                        var finalDmg = client.Tamer.GodMode ? targetMob.CurrentHP : totalSkillDamage;

                        if (finalDmg <= 0) finalDmg = client.Tamer.Partner.AT;
                        if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                        if (!targetMob.InBattle)
                        {
                            broadcastAction(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                            targetMob.StartBattle(client.Tamer);
                        }
                        else
                        {
                            targetMob.AddTarget(client.Tamer);
                        }

                        var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                        if (newHp <= 0)
                        {
                            targetMob?.Die();
                        }
                    });

                    broadcastAction(client.TamerId, new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize());
                    broadcastAction(client.TamerId, new AreaSkillPacket(attackerHandler, client.Partner.HpRate, targetMobs, skillSlot, totalSkillDamage).Serialize());
                }
                else
                {
                    var targetMob = targetMobs.First();

                    if (!targetMob.InBattle)
                    {
                        broadcastAction(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                        targetMob.StartBattle(client.Tamer);
                    }
                    else
                    {
                        targetMob.AddTarget(client.Tamer);
                    }

                    var finalDmg = client.Tamer.GodMode ? targetMob.CurrentHP : _digimonSkillManager.SkillDamage(client, skill, skillSlot);

                    if (finalDmg <= 0) finalDmg = client.Tamer.Partner.AT;
                    if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                    var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                    if (newHp > 0)
                    {
                        broadcastAction(client.TamerId, new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize());

                        broadcastAction(client.TamerId, new SkillHitPacket(attackerHandler, targetMob.GeneralHandler, skillSlot, finalDmg, targetMob.CurrentHpRate).Serialize());
                        client.Tamer.Partner.NextSkillTime = DateTime.UtcNow.AddMilliseconds(castingTime);
                    }
                    else
                    {
                        broadcastAction(client.TamerId, new KillOnSkillPacket(
                                attackerHandler, targetMob.GeneralHandler, skillSlot, finalDmg).Serialize());

                        targetMob?.Die();
                        client.Tamer.Partner.NextSkillTime = DateTime.UtcNow.AddMilliseconds(skill.SkillInfo.Cooldown);
                    }

                    if (!client.Tamer.Partner.NextSkillTimeDict.ContainsKey(skillSlot))
                    {
                        client.Tamer.Partner.NextSkillTimeDict[skillSlot] = DateTime.UtcNow.AddMilliseconds(skill.SkillInfo.Cooldown);
                    }
                    else
                    {
                        client.Tamer.Partner.NextSkillTimeDict[skillSlot] = DateTime.UtcNow.AddMilliseconds(skill.SkillInfo.Cooldown);
                    }
                }

                if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId) && client.Tamer.InBattle)
                {
                    client.Tamer.StopIBattle();

                    _ = Task.Run(async () =>
                    {
                        // Tentativas de reenviar o "SetCombatOff" para garantir entrega
                        const int maxAttempts = 10;
                        const int delayBetweenAttempts = 200; // ms

                        for (int i = 0; i < maxAttempts; i++)
                        {
                            try
                            {
                                broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                                _logger.Debug($"[PartnerSkill] Sent SetCombatOffPacket attempt {i + 1}/{maxAttempts} for CharacterId={client.Tamer.Id}");
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning($"[PartnerSkill] Failed to send SetCombatOffPacket attempt {i + 1}: {ex.Message}");
                            }

                            await Task.Delay(delayBetweenAttempts);
                        }

                        _logger.Debug($"[PartnerSkill] Finished SetCombatOff resend loop for CharacterId={client.Tamer.Id}");
                    });
                }
                else
                {
                    // ✅ NOVO: se o combate continua, reativar autoattack
                    if (client.Tamer.InBattle && client.Partner != null)
                    {
                        client.Partner.StartAutoAttack();
                        _logger.Debug($"[PartnerSkill] AutoAttack retomado após skill. CharacterId={client.Tamer.Id}");
                    }
                }
                var evolution = client.Tamer.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Tamer.Partner.CurrentType);

                if (evolution != null && skill.SkillInfo.Cooldown / 1000 >= 20)
                {
                    evolution.Skills[skillSlot].SetCooldown(skill.SkillInfo.Cooldown / 1000);
                    await _sender.Send(new UpdateEvolutionCommand(evolution));
                }
            }
            await Task.CompletedTask;
        }
    }
}