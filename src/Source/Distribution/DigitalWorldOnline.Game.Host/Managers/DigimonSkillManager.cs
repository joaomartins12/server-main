using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost.EventsServer;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.GameHost;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DigitalWorldOnline.Commons.Packets.GameServer.AddBuffPacket;
using DigitalWorldOnline.Application;
using System.Data;
using Newtonsoft.Json.Serialization;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Enums.Map;
using DigitalWorldOnline.Commons.Models;

namespace DigitalWorldOnline.Game.Managers
{
    public class DigimonSkillManager
    {
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly AttackManager _attackManager;
        private readonly ISender _sender;

        private const bool LOG_DMG = false;
        private const double LOG_SAMPLE = 1.0; // log em 100% dos hits

        public DigimonSkillManager(
            AssetsLoader assets,
            MapServer mapServer,
            DungeonsServer dungeonServer,
            EventServer eventServer,
            PvpServer pvpServer,
            AttackManager attackManager,
            ISender sender)
        {
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _attackManager = attackManager;
            _sender = sender;
        }

        public int SkillDamage(GameClient client, DigimonSkillAssetModel targetSkill, byte skillSlot)
        {
            // ===== Guardas =====
            var partner = client?.Tamer?.Partner ?? client?.Partner;
            var target = client?.Tamer?.TargetIMob;
            if (partner == null || target == null)
                return 0;

            var skillInfo = _assets.DigimonSkillInfo
                .FirstOrDefault(x => x.Type == partner.CurrentType && x.Slot == skillSlot);
            if (skillInfo?.SkillInfo == null)
                return 0;

            var skill = _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == targetSkill.SkillId);
            if (skill == null)
                return 0;

            var skillValue = skill.Apply.Where(x => x.Type > 0).Take(3).ToList();
            if (skillValue.Count == 0)
                return 0;

            var partnerEvolution = partner.Evolutions.FirstOrDefault(x => x.Type == partner.CurrentType);
            if (partnerEvolution == null || partnerEvolution.Skills == null || skillSlot >= partnerEvolution.Skills.Count)
                return 0;

            var rnd = Random.Shared;

            // Duração de buff/debuff base por skill
            int skillDuration = GetDurationBySkillId((int)skill.SkillCode);
            var durationBuff = UtilitiesFunctions.RemainingTimeSeconds(skillDuration);

            // ===== Base do dano do skill =====
            double skillDamage;
            if (skillInfo.SkillInfo.AreaOfEffect > 0 && skillInfo.SkillInfo.AoEMaxDamage != 0)
                skillDamage = UtilitiesFunctions.RandomInt(skillInfo.SkillInfo.AoEMinDamage, skillInfo.SkillInfo.AoEMaxDamage);
            else
                skillDamage = skillValue[0].Value;

            var currentLevel = partnerEvolution.Skills[skillSlot].CurrentLevel;
            double levelScaling = currentLevel * skillValue[0].IncreaseValue;

            // Linha de base apenas com valores planos (nada de % aqui)
            double baseLine = Math.Max(0.0, skillDamage + levelScaling + partner.AT + partner.SKD);

            // ===== Montagem dos percentuais (todos aplicam só ao baseLine) =====
            double ToFrac(double v) => (v >= 1000.0) ? v / 10000.0 : v / 100.0;
            double ToPercent(double v) => (v >= 1000.0) ? v / 100.0 : v;

            // SCD (fração) – ex.: 150% => 1.5
            double scdPct = Math.Max(0.0, ToFrac(partner.SCD));

            // ATT (fração) – ex.: 6641 -> 0.6641 (66.41%)
            double attPct = Math.Max(0.0, ToFrac(partner.ATT));

            // Elemento/Atributo já vêm como fração (-0.25..+1.0)
            double attributePct = AttackManager.GetAttributeDamage(client);
            double elementPct = AttackManager.GetElementDamage(client);

            // Digiclone → converte fator para fração
            double cloneAT = Math.Max(0.0, (double)partner.Digiclone.ATValue);
            double cloneFactor = cloneAT > 0.0 ? Math.Round(1.0 + (0.43 / (144.0 / cloneAT)), 2) : 1.0;
            double clonePct = cloneFactor - 1.0;

            // Crítico
            double critChance = Math.Clamp(ToFrac(partner.CC * 100.0), 0.0, 1.0);
            double overcapCC = Math.Max(0.0, ToPercent(partner.CC) - 100.0);

            // CD em "percent" normalizado (22359 -> 223.59)
            double cdPercent = ToPercent(partner.CD);

            // regra: cada 1% de CD => +0.5% de multiplicador crítico * 0.4
            double cdEffect = (cdPercent * 0.01) * 0.75; // fração

            double critMult = 1.0 + cdEffect + (overcapCC / 200.0);
            critMult += ((double)partner.SKD / 500.0) * 0.01;

            bool isCriticalHit = rnd.NextDouble() < critChance && critMult > 1.0;
            double critPct = isCriticalHit ? (critMult - 1.0) : 0.0;

            // A cada 1% ATT -> +0.1% dano final
            double attFinalMult = 1.0 + ((partner.ATT / 100.0) * 0.01);

            // A cada 1% CD -> +0.005x dano final
            double cdFinalMult = 1.0 + cdEffect;

            // ===== Soma “plana” dos percentuais e aplicação de soft cap =====
            double sumPct = 0.0;
            sumPct += scdPct;
            sumPct += attFinalMult;
            sumPct += attributePct;
            sumPct += elementPct;
            sumPct += clonePct;
            sumPct += critPct;
            sumPct += cdFinalMult;

            // Ajusta limites conforme o meta — capUp: +200% (x3), capDown: -75% (min 25%)
            double effPct = sumPct;

            // Dano antes da mitigação (uma única multiplicação)
            double preMitigation = Math.Floor(baseLine * (1.0 + effPct));

            // ===== Ativação de efeitos secundários (buffs/debuffs) =====
            double activationChance = 0.0;
            if (skillValue.Count > 1)
            {
                activationChance += skillValue[1].Chance;
                if ((int)skillValue[1].Attribute != 37 && (int)skillValue[1].Attribute != 38)
                {
                    durationBuff += currentLevel;
                    skillDuration += currentLevel + 2;
                }
            }
            if (skillValue.Count > 2)
            {
                activationChance += skillValue[2].Chance;
                if ((int)skillValue[2].Attribute != 37 && (int)skillValue[2].Attribute != 38 && (int)skillValue[2].Attribute != 39)
                {
                    durationBuff += currentLevel;
                    skillDuration += currentLevel + 2;
                }
            }

            double activationProbability = Math.Max(0.0, Math.Min(1.0, activationChance / 100.0));
            bool proc = activationProbability >= 1.0 || rnd.NextDouble() <= activationProbability;
            if (proc && ((skillValue.Count > 1 && skillValue[1].Type != 0) || (skillValue.Count > 2 && skillValue[2].Type != 0)))
            {
                try { BuffSkill(client, durationBuff, skillDuration, skillSlot); }
                catch { /* não quebrar o tick se o proc falhar */ }
            }

            // ===== Mitigação por DEF (suave, sem negativos) =====
            double enemyDef = Math.Max(0.0, (double)target.DEValue);

            const double MaxDefForCap = 50000.0;
            const double MaxReduction = 0.50;
            double defReductionPct = Math.Clamp(enemyDef / MaxDefForCap * MaxReduction, 0.0, MaxReduction);

            double defMult = 1.0 - defReductionPct;
            double totalDamage = preMitigation * defMult;

            // Variação aleatória final ±10%
            double rng = 0.90 + rnd.NextDouble() * 0.20; // 0.90..1.10
            totalDamage *= rng;

            int finalDamage = Math.Max(0, (int)Math.Floor(totalDamage));

            if (LOG_DMG)
            {
                string P(double x) => (x * 100.0).ToString("0.##"); // percent helper

                var sb = new StringBuilder();
                sb.Append("[DMG] ");
                sb.Append($"base={baseLine:0} | ");
                sb.Append($"rawPct{{scd:{P(scdPct)}%,att:{P(attPct)}%,attr:{P(attributePct)}%,elem:{P(elementPct)}%,clone:{P(clonePct)}%,crit:{P(critPct)}%}} | ");
                sb.Append($"sumRaw={P(sumPct)}% | ");
                sb.Append($"CD={P(cdFinalMult)}% | ");
                sb.Append($"ATTBonus={P(attFinalMult)}% | ");
                sb.Append($"effPct={P(effPct)}% (x{1.0 + effPct:0.###}) | ");
                sb.Append($"preMit={preMitigation:0} | ");
                sb.Append($"DEF={enemyDef:0} (x{defMult:0.###}) => final={finalDamage}");

                // envia para consola do servidor
                Console.WriteLine(sb.ToString());

                // Se preferires ver in-game (apenas para GM, por ex.):
                // if (client.Tamer?.IsGM == true)
                //     client.Send(UtilitiesFunctions.GroupPackets(new SystemMessagePacket("[DBG] " + sb.ToString()).Serialize()));
            }

            // ===== Aplica dano no alvo + aggro consistente =====
            try
            {
                var hp = target.ReceiveDamage(finalDamage, client.TamerId);
                // Threat.Add(target.Id, client.TamerId, finalDamage, ThreatTag.SkillHit);
                if (hp <= 0) target.Die();
            }
            catch { /* nunca deixar o tick do skill morrer por exceção aqui */ }

            // ===== Feedback (não deixar UI quebrar o tick) =====
            if (finalDamage > 0 && AttackManager.IsBattle)
            {
                try
                {
                    var msg = isCriticalHit
                        ? $"{partner.Name} usou {skillInfo.SkillInfo.Name} e CRITOU {finalDamage} | DEF alvo {enemyDef}"
                        : $"{partner.Name} usou {skillInfo.SkillInfo.Name} e causou {finalDamage} | DEF alvo {enemyDef}";

                    client.Send(UtilitiesFunctions.GroupPackets(new SystemMessagePacket(msg).Serialize()));
                }
                catch { }
            }

            return finalDamage;
        }

        private void BuffSkill(GameClient client, int duration, int skillDuration, byte skillSlot)
        {
            var skillInfo = _assets.DigimonSkillInfo.FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            Action<long, byte[]> broadcastAction = client.DungeonMap
                ? (id, data) => _dungeonServer.BroadcastForTamerViewsAndSelf(id, data)
                : (id, data) => _mapServer.BroadcastForTamerViewsAndSelf(id, data);

            var skillCode = _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == skillInfo.SkillId);
            var buff = _assets.BuffInfo.FirstOrDefault(x => x.SkillCode == skillCode.SkillCode);
            var skillValue = skillCode.Apply.Where(x => x.Type > 0).Take(3).ToList();
            var partnerEvolution = client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType);
            var selectedMob = client.Tamer.TargetIMob;

            if (buff != null)
            {
                var debuffs = new List<SkillCodeApplyAttributeEnum>
                {
                    SkillCodeApplyAttributeEnum.CrowdControl,
                    SkillCodeApplyAttributeEnum.DOT,
                    SkillCodeApplyAttributeEnum.DOT2
                };

                var buffs = new List<SkillCodeApplyAttributeEnum>
                {
                    SkillCodeApplyAttributeEnum.MS,
                    SkillCodeApplyAttributeEnum.SCD,
                    SkillCodeApplyAttributeEnum.CC,
                    SkillCodeApplyAttributeEnum.AS,
                    SkillCodeApplyAttributeEnum.AT,
                    SkillCodeApplyAttributeEnum.HP,
                    SkillCodeApplyAttributeEnum.DamageShield,
                    SkillCodeApplyAttributeEnum.CA,
                    SkillCodeApplyAttributeEnum.Unbeatable,
                    SkillCodeApplyAttributeEnum.DR,
                    SkillCodeApplyAttributeEnum.EV
                };

                for (int i = 1; i <= 2; i++)
                {
                    if (skillValue.Count > i)
                    {
                        switch (skillValue[i].Attribute)
                        {
                            // Handling Buffs
                            case var attribute when buffs.Contains(attribute):
                                int buffsValue = skillValue[i].Value + (partnerEvolution.Skills[skillSlot].CurrentLevel * skillValue[i].IncreaseValue);
                                client.Tamer.Partner.BuffValueFromBuffSkill = buffsValue;

                                var newDigimonBuff = DigimonBuffModel.Create(buff.BuffId, buff.SkillId, 0, skillDuration);
                                var activeBuff = client.Tamer.Partner.BuffList.Buffs.FirstOrDefault(x => x.BuffId == buff.BuffId);
                                switch (attribute)
                                {
                                    case SkillCodeApplyAttributeEnum.DR: // reflect damage
                                        if (activeBuff == null)
                                        {
                                            newDigimonBuff.SetBuffInfo(buff);
                                            client.Tamer.Partner.BuffList.Add(newDigimonBuff);
                                            broadcastAction(client.TamerId, new SkillBuffPacket(
                                                client.Tamer.GeneralHandler,
                                                (int)buff.BuffId,
                                                0,
                                                duration,
                                                (int)skillCode.SkillCode).Serialize());

                                            var reflectDamageInterval = TimeSpan.FromMilliseconds(selectedMob.ASValue);
                                            var reflectDamageDuration = duration;
                                            var buffId = newDigimonBuff.BuffId;

                                            Task.Run(async () =>
                                            {
                                                await Task.Delay(1500);

                                                for (int i = 0; i < reflectDamageDuration; i++)
                                                {
                                                    if (selectedMob == null
                                                        || !client.Tamer.Partner.BuffList.Buffs.Any(b => b.BuffId == buffId)
                                                        || selectedMob.CurrentAction != MobActionEnum.Attack)
                                                    {
                                                        client.Tamer.Partner.BuffList.Remove(newDigimonBuff.BuffId);
                                                        broadcastAction(client.Tamer.Id,
                                                            new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, newDigimonBuff.BuffId).Serialize());
                                                        break;
                                                    }

                                                    var damageValue = selectedMob.ATValue * 3;
                                                    var newHp = selectedMob.ReceiveDamage(damageValue, client.TamerId);

                                                    broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                                        client.Tamer.Partner.GeneralHandler, selectedMob.GeneralHandler,
                                                        newDigimonBuff.BuffId, selectedMob.CurrentHpRate, damageValue,
                                                        (byte)((newHp > 0) ? 0 : 1)).Serialize());

                                                    if (newHp <= 0)
                                                    {
                                                        client.Tamer.Partner.BuffList.Remove(newDigimonBuff.BuffId);
                                                        broadcastAction(client.Tamer.Id,
                                                            new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, newDigimonBuff.BuffId).Serialize());
                                                        selectedMob.Die();
                                                        break;
                                                    }

                                                    await Task.Delay(reflectDamageInterval);
                                                }

                                            });

                                        }
                                        break;
                                    case SkillCodeApplyAttributeEnum.DamageShield:
                                        int shieldHp = skillValue[i].Value + (partnerEvolution.Skills[skillSlot].CurrentLevel * skillValue[i].IncreaseValue);

                                        if (client.Tamer.Partner.DamageShieldHp > 0)
                                        {
                                            break;
                                        }
                                        else
                                        {
                                            newDigimonBuff.SetBuffInfo(buff);
                                            client.Tamer.Partner.BuffList.Add(newDigimonBuff);
                                            client.Tamer.Partner.DamageShieldHp = shieldHp;

                                            broadcastAction(client.TamerId, new SkillBuffPacket(
                                                client.Tamer.GeneralHandler,
                                                (int)buff.BuffId,
                                                partnerEvolution.Skills[skillSlot].CurrentLevel,
                                                duration,
                                                (int)skillCode.SkillCode).Serialize());

                                            Task.Run(async () =>
                                            {
                                                int remainingDuration = skillDuration;
                                                while (remainingDuration > 0)
                                                {
                                                    await Task.Delay(1000);

                                                    if (client.Tamer.Partner.DamageShieldHp <= 0)
                                                    {
                                                        client.Tamer.Partner.DamageShieldHp = 0;
                                                        client.Tamer.Partner.BuffList.Remove(newDigimonBuff.BuffId);
                                                        broadcastAction(client.TamerId, new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, newDigimonBuff.BuffId).Serialize());
                                                        break;
                                                    }

                                                    remainingDuration--;
                                                }

                                                if (client.Tamer.Partner.DamageShieldHp > 0)
                                                {
                                                    client.Tamer.Partner.DamageShieldHp = 0;
                                                    client.Tamer.Partner.BuffList.Remove(newDigimonBuff.BuffId);
                                                    broadcastAction(client.TamerId, new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, newDigimonBuff.BuffId).Serialize());
                                                }


                                            });
                                        }
                                        break;

                                    case SkillCodeApplyAttributeEnum.Unbeatable:
                                        if (client.Tamer.Partner.IsUnbeatable)
                                        {
                                            break;
                                        }
                                        newDigimonBuff.SetBuffInfo(buff);
                                        client.Tamer.Partner.BuffList.Add(newDigimonBuff);

                                        client.Tamer.Partner.IsUnbeatable = true;

                                        broadcastAction(client.TamerId, new SkillBuffPacket(
                                            client.Tamer.GeneralHandler,
                                            (int)buff.BuffId,
                                            partnerEvolution.Skills[skillSlot].CurrentLevel,
                                            duration,
                                            (int)skillCode.SkillCode).Serialize());

                                        Task.Delay(skillDuration * 1000).ContinueWith(_ =>
                                        {
                                            client.Tamer.Partner.IsUnbeatable = false;
                                        });
                                        break;
                                    case SkillCodeApplyAttributeEnum.EV:
                                    case SkillCodeApplyAttributeEnum.MS:
                                    case SkillCodeApplyAttributeEnum.SCD:
                                    case SkillCodeApplyAttributeEnum.CA:
                                    case SkillCodeApplyAttributeEnum.AT:
                                    case SkillCodeApplyAttributeEnum.HP:
                                        if (activeBuff == null)
                                        {
                                            newDigimonBuff.SetBuffInfo(buff);
                                            client.Tamer.Partner.BuffList.Add(newDigimonBuff);

                                            broadcastAction(client.TamerId, new SkillBuffPacket(
                                                client.Tamer.GeneralHandler,
                                                (int)buff.BuffId,
                                                partnerEvolution.Skills[skillSlot].CurrentLevel,
                                                duration,
                                                (int)skillCode.SkillCode).Serialize());
                                        }
                                        client.Send(new UpdateStatusPacket(client.Tamer));
                                        break;
                                }
                                break;

                            // Handling Debuffs
                            case var attribute when debuffs.Contains(attribute):

                                var activeDebuff = selectedMob.DebuffList.Buffs.FirstOrDefault(x => x.BuffId == buff.BuffId);
                                var newMobDebuff = MobDebuffModel.Create(buff.BuffId, (int)skillCode.SkillCode, 0, skillDuration);

                                newMobDebuff.SetBuffInfo(buff);
                                int debuffsValue = skillValue[i].Value + (partnerEvolution.Skills[skillSlot].CurrentLevel * skillValue[i].IncreaseValue);

                                switch (attribute)
                                {
                                    case SkillCodeApplyAttributeEnum.CrowdControl:
                                        if (activeDebuff == null)
                                        {
                                            selectedMob.DebuffList.Buffs.Add(newMobDebuff);
                                        }

                                        if (selectedMob.CurrentAction != Commons.Enums.Map.MobActionEnum.CrowdControl)
                                        {
                                            selectedMob.UpdateCurrentAction(Commons.Enums.Map.MobActionEnum.CrowdControl);
                                        }

                                        broadcastAction(client.TamerId, new AddStunDebuffPacket(
                                            selectedMob.GeneralHandler, newMobDebuff.BuffId, newMobDebuff.SkillId, duration).Serialize());
                                        break;

                                    case SkillCodeApplyAttributeEnum.DOT:
                                    case SkillCodeApplyAttributeEnum.DOT2:
                                        if (debuffsValue > selectedMob.CurrentHP)
                                            debuffsValue = selectedMob.CurrentHP;

                                        broadcastAction(client.TamerId, new AddBuffPacket(
                                            selectedMob.GeneralHandler, buff, partnerEvolution.Skills[skillSlot].CurrentLevel, duration).Serialize());

                                        if (activeDebuff != null)
                                        {
                                            activeDebuff.IncreaseEndDate(skillDuration);
                                        }
                                        else
                                        {
                                            selectedMob.DebuffList.Buffs.Add(newMobDebuff);
                                        }

                                        Task.Delay(skillDuration * 1000).ContinueWith(_ =>
                                        {
                                            if (selectedMob == null) return;
                                            var newHp = selectedMob.ReceiveDamage(debuffsValue, client.TamerId);

                                            broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                                client.Tamer.Partner.GeneralHandler, selectedMob.GeneralHandler,
                                                newMobDebuff.BuffId, selectedMob.CurrentHpRate, debuffsValue, (byte)((newHp > 0) ? 0 : 1)).Serialize());

                                            if (newHp <= 0)
                                            {
                                                selectedMob.Die();
                                            }
                                        });
                                        break;
                                }
                                break;
                        }
                    }
                }

                _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));
            }
        }

        private int GetDurationBySkillId(int skillCode)
        {
            return skillCode switch
            {
                (int)SkillBuffAndDebuffDurationEnum.FireRocket => 5, //38 = attribute enums
                (int)SkillBuffAndDebuffDurationEnum.DynamiteHead => 4, //33
                (int)SkillBuffAndDebuffDurationEnum.BlueThunder => 2, //39
                (int)SkillBuffAndDebuffDurationEnum.NeedleRain => 10, //37  missing packet?
                (int)SkillBuffAndDebuffDurationEnum.MysticBell => 3, //
                (int)SkillBuffAndDebuffDurationEnum.GoldRush => 3, // 39 missing packet petrify?
                (int)SkillBuffAndDebuffDurationEnum.GaiaBrave => 5, // 39 missing packet petrify?
                (int)SkillBuffAndDebuffDurationEnum.NeedleStinger => 15, //6
                (int)SkillBuffAndDebuffDurationEnum.CurseOfQueen => 20, //24
                (int)SkillBuffAndDebuffDurationEnum.Purification => 30, //1
                (int)SkillBuffAndDebuffDurationEnum.GodsWill => 60, //48    
                (int)SkillBuffAndDebuffDurationEnum.WhiteStatue => 15, //40 //reflect damage packet?
                (int)SkillBuffAndDebuffDurationEnum.RedSun => 10, //24 
                (int)SkillBuffAndDebuffDurationEnum.PlasmaShot => 5, //38
                (int)SkillBuffAndDebuffDurationEnum.ExtremeJihad => 10, //24
                (int)SkillBuffAndDebuffDurationEnum.MomijiOroshi => 15, //8
                (int)SkillBuffAndDebuffDurationEnum.Ittouryoudan => 20, //41
                (int)SkillBuffAndDebuffDurationEnum.ShiningGoldSolarStorm => 6, //33 Invincible Silver Magnamon
                (int)SkillBuffAndDebuffDurationEnum.MagnaAttack => 5, // MagnaAttack Magnamon Worn F1
                (int)SkillBuffAndDebuffDurationEnum.PlasmaRage => 10, // MagnaAttack Magnamon Worn F2
                (int)SkillBuffAndDebuffDurationEnum.KyukyokuSenjin => 1, // AOA Magnamon Worn F2
                (int)SkillBuffAndDebuffDurationEnum.RamapageAlterBF3 => 10, // Alter B Rampage
                (int)SkillBuffAndDebuffDurationEnum.MandalaofLigh => 30, // Awaken Sakuya

                _ => 0
            };
        }

        // ===== Utilitário de soft cap para soma de percentuais =====
        // sumPct: ex. 0.25 = +25%, -0.30 = -30%
        // capUp: ganho máximo (ex. +200% => 2.0)
        // capDown: perda máxima (ex. -75% => -0.75)
        private static double SoftCapAdditive(double sumPct, double capUp = 2.0, double capDown = -0.75)
        {
            if (sumPct >= 0.0)
            {
                // Satura em +capUp (curva exponencial suave)
                return capUp * (1.0 - Math.Exp(-(sumPct / capUp)));
            }
            else
            {
                double cap = Math.Abs(capDown);
                double neg = cap * (1.0 - Math.Exp(-(-sumPct / cap)));
                return -neg;
            }
        }
    }
}
