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
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

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

        // anti-spam + casting locks
        private readonly ConcurrentDictionary<long, DateTime> _lastSkillTs = new();
        private const int PartnerSkillCooldownMs = 250;     // evita spam
        private const int PartnerCastingTimeoutMs = 1500;   // fail-safe para limpar estado

        public PartnerSkillPacketProcessor(
            AssetsLoader assets,
            MapServer mapServer,
            DungeonsServer dungeonServer,
            EventServer eventServer,
            PvpServer pvpServer,
            ILogger logger,
            ISender sender,
            DigimonSkillManager digimonSkillManager)
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
            try
            {
                var packet = new GamePacketReader(packetData);

                var skillSlot = packet.ReadByte();
                var attackerHandler = packet.ReadInt();
                var targetHandler = packet.ReadInt();

                if (client?.Partner == null)
                {
                    _logger.Information("[PartnerSkill] Client {Client} não tem Partner válido.", client?.TamerId);
                    return;
                }

                // Anti-spam
                if (_lastSkillTs.TryGetValue(client.TamerId, out var last) &&
                    (DateTime.UtcNow - last).TotalMilliseconds < PartnerSkillCooldownMs)
                {
                    _logger.Information("[PartnerSkill] Bloqueado por spam: Client={Client} SkillSlot={Slot}", client.TamerId, skillSlot);
                    return;
                }
                _lastSkillTs[client.TamerId] = DateTime.UtcNow;

                // Delegates para map/event/dungeon
                Func<short, long, bool> broadcastMobs = client.DungeonMap
                    ? _dungeonServer.IMobsAttacking
                    : client.EventMap ? _eventServer.IMobsAttacking
                    : _mapServer.IMobsAttacking;

                Action<long, byte[]> broadcastAction = client.DungeonMap
                    ? _dungeonServer.BroadcastForTamerViewsAndSelf
                    : client.EventMap ? _eventServer.BroadcastForTamerViewsAndSelf
                    : _mapServer.BroadcastForTamerViewsAndSelf;

                Func<short, int, int, long, IMob> getMobHandler = client.DungeonMap
                    ? _dungeonServer.GetNearestIMobToTarget
                    : client.EventMap ? _eventServer.GetNearestIMobToTarget
                    : _mapServer.GetNearestIMobToTarget;

                Func<short, int, int, long, List<IMob>> getNearbyTargetMob = client.DungeonMap
                    ? _dungeonServer.GetIMobsNearbyTargetMob
                    : client.EventMap ? _eventServer.GetIMobsNearbyTargetMob
                    : _mapServer.GetIMobsNearbyTargetMob;

                Func<Location, int, long, List<IMob>> getNearbyPartnerMob = client.DungeonMap
                    ? _dungeonServer.GetIMobsNearbyPartner
                    : client.EventMap ? _eventServer.GetIMobsNearbyPartner
                    : _mapServer.GetIMobsNearbyPartner;

                var skill = _assets.DigimonSkillInfo.FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
                if (skill?.SkillInfo == null)
                {
                    _logger.Information("[PartnerSkill] Skill inválida ou SkillInfo null. Client={Client} Slot={Slot}", client.TamerId, skillSlot);
                    return;
                }

                // Cooldown check
                if (client.Tamer.Partner.NextSkillTimeDict.TryGetValue(skillSlot, out DateTime nextSkillTime) &&
                    DateTime.UtcNow < nextSkillTime)
                {
                    _logger.Information("[PartnerSkill] Skill em cooldown. Client={Client} Slot={Slot}", client.TamerId, skillSlot);
                    return;
                }

                // Range mínimo
                var range = skill.SkillInfo.Range < 500 ? 900 : skill.SkillInfo.Range;
                var areaOfEffect = skill.SkillInfo.AreaOfEffect;
                var targetType = skill.SkillInfo.Target;

                var targetMobs = new List<IMob>();
                SkillTypeEnum skillType;

                if (areaOfEffect > 0)
                {
                    skillType = SkillTypeEnum.TargetArea;
                    if (targetType == 17)
                        targetMobs.AddRange(getNearbyPartnerMob(client.Partner.Location, areaOfEffect, client.TamerId));
                    else if (targetType == 18)
                        targetMobs.AddRange(getNearbyTargetMob(client.Partner.Location.MapId, targetHandler, areaOfEffect, client.TamerId));
                }
                else if (targetType == 80)
                {
                    skillType = SkillTypeEnum.Implosion;
                    targetMobs.AddRange(getNearbyTargetMob(client.Partner.Location.MapId, targetHandler, range, client.TamerId));
                }
                else
                {
                    skillType = SkillTypeEnum.Single;
                    var mob = getMobHandler(client.Tamer.Location.MapId, targetHandler, range, client.TamerId);
                    if (mob == null)
                    {
                        _logger.Information("[PartnerSkill] Mob target não encontrado. Client={Client} Slot={Slot} TargetHandler={Target}", client.TamerId, skillSlot, targetHandler);
                        return;
                    }
                    targetMobs.Add(mob);
                }

                if (!targetMobs.Any()) return;
                if (skillType == SkillTypeEnum.Single && !targetMobs.First().Alive)
                {
                    _logger.Information("[PartnerSkill] Alvo já morto. Client={Client} Slot={Slot}", client.TamerId, skillSlot);
                    return;
                }

                // Consome recursos
                client.Partner.ReceiveDamage(skill.SkillInfo.HPUsage);
                client.Partner.UseDs(skill.SkillInfo.DSUsage);

                // Casting
                var castingTime = (int)Math.Round(skill.SkillInfo.CastingTime);
                if (skillSlot is >= 0 and <= 3) castingTime = 10;
                client.Partner.SetEndCasting(castingTime);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(PartnerCastingTimeoutMs);
                    try { client.Partner.SetEndCasting(0); }
                    catch (Exception exDelay) { _logger.Information(exDelay, "[PartnerSkill] Erro ao limpar casting via timeout. Client={Client}", client.TamerId); }
                });

                // Inicia combate
                client.Tamer.SetHidden(false);
                if (!client.Tamer.InBattle)
                {
                    broadcastAction(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                    client.Tamer.StartBattleWithSkill(targetMobs, skillType);
                }
                else
                {
                    client.Tamer.UpdateTargetWithSkill(targetMobs, skillType);
                }

                // Função utilitária para calcular dano final
                int GetFinalDamage(IMob mob, int baseDamage)
                {
                    var dmg = client.Tamer.GodMode ? mob.CurrentHP : baseDamage;
                    if (dmg <= 0) dmg = client.Tamer.Partner.AT;
                    if (dmg > mob.CurrentHP) dmg = mob.CurrentHP;
                    return dmg;
                }

                // Aplicar skill
                if (skillType != SkillTypeEnum.Single)
                {
                    var totalSkillDamage = _digimonSkillManager.SkillDamage(client, skill, skillSlot);

                    foreach (var mob in targetMobs)
                    {
                        var finalDmg = GetFinalDamage(mob, totalSkillDamage);

                        if (!mob.InBattle)
                        {
                            broadcastAction(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                            mob.StartBattle(client.Tamer);
                        }
                        else mob.AddTarget(client.Tamer);

                        if (mob.ReceiveDamage(finalDmg, client.TamerId) <= 0) mob.Die();
                    }

                    broadcastAction(client.TamerId, new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize());
                    broadcastAction(client.TamerId, new AreaSkillPacket(attackerHandler, client.Partner.HpRate, targetMobs, skillSlot, totalSkillDamage).Serialize());
                }
                else
                {
                    var mob = targetMobs.First();
                    if (!mob.InBattle)
                    {
                        broadcastAction(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                        mob.StartBattle(client.Tamer);
                    }
                    else mob.AddTarget(client.Tamer);

                    var finalDmg = GetFinalDamage(mob, _digimonSkillManager.SkillDamage(client, skill, skillSlot));
                    var newHp = mob.ReceiveDamage(finalDmg, client.TamerId);

                    if (newHp > 0)
                    {
                        broadcastAction(client.TamerId, new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize());
                        broadcastAction(client.TamerId, new SkillHitPacket(attackerHandler, mob.GeneralHandler, skillSlot, finalDmg, mob.CurrentHpRate).Serialize());
                        client.Tamer.Partner.NextSkillTime = DateTime.UtcNow.AddMilliseconds(castingTime);
                    }
                    else
                    {
                        broadcastAction(client.TamerId, new KillOnSkillPacket(attackerHandler, mob.GeneralHandler, skillSlot, finalDmg).Serialize());
                        mob.Die();
                        client.Tamer.Partner.NextSkillTime = DateTime.UtcNow.AddMilliseconds(skill.SkillInfo.Cooldown);
                    }

                    client.Tamer.Partner.NextSkillTimeDict[skillSlot] = DateTime.UtcNow.AddMilliseconds(skill.SkillInfo.Cooldown);
                }

                // Aggro fix
                if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId) && client.Tamer.InBattle)
                {
                    client.Tamer.StopIBattle();
                    await Task.Delay(500);
                    broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        if (!broadcastMobs(client.Tamer.Location.MapId, client.TamerId) && !client.Tamer.InBattle)
                            broadcastAction(client.TamerId, new SetCombatOffPacket(attackerHandler).Serialize());
                    });
                }

                // Atualiza cooldown evolução
                var evolution = client.Tamer.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Tamer.Partner.CurrentType);
                if (evolution != null && skill.SkillInfo.Cooldown / 1000 >= 20)
                {
                    evolution.Skills[skillSlot].SetCooldown(skill.SkillInfo.Cooldown / 1000);
                    await _sender.Send(new UpdateEvolutionCommand(evolution));
                }

                _logger.Information("[PartnerSkill] Skill {Skill} lançada por Client={Client} Slot={Slot} Targets={Count}.",
                    skill.SkillInfo.Name, client.TamerId, skillSlot, targetMobs.Count);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                try { client?.Partner?.SetEndCasting(0); } catch { }
                _logger.Information(ex, "[PartnerSkill] Excepção ao processar skill. Client={Client}", client?.TamerId);
            }
        }
    }
}
