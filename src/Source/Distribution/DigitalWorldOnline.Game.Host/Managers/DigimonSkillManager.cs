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
        public DigimonSkillManager(AssetsLoader assets, MapServer mapServer, DungeonsServer dungeonServer, EventServer eventServer, PvpServer pvpServer, AttackManager attackManager, ISender sender)
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
            double skillDamage = 0;

            var skill = _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == targetSkill.SkillId);
            var skillValue = skill.Apply
                .Where(x => x.Type > 0)
                .Take(3)
                .ToList();

            var skillInfo = _assets.DigimonSkillInfo.FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            var partnerEvolution = client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType);

            if (skillInfo.SkillInfo.AreaOfEffect > 0 && skillInfo.SkillInfo.AoEMaxDamage != 0)
            {
                skillDamage += UtilitiesFunctions.RandomInt(skillInfo.SkillInfo.AoEMinDamage, skillInfo.SkillInfo.AoEMaxDamage);
            }
            else
            {
                skillDamage += skillValue[0].Value;
            }

            double f1BaseDamage = skillDamage + ((partnerEvolution.Skills[skillSlot].CurrentLevel) * skillValue[0].IncreaseValue);
            int skillDuration = GetDurationBySkillId((int)skill.SkillCode);
            var durationBuff = UtilitiesFunctions.RemainingTimeSeconds(skillDuration);

            double SkillFactor = 0;
            int clonDamage = 0;
            double attributeDamage = AttackManager.GetAttributeDamage(client);
            double elementDamage = AttackManager.GetElementDamage(client);

            var Percentual = (decimal)client.Partner.SCD / 100;
            SkillFactor = (double)Percentual;
            var activationChance = 0.0;

            // -- CLON -------------------------------------------------------------------
            double clonPercent = client.Tamer.Partner.Digiclone.ATValue / 100.0;
            int cloValue = (int)(client.Tamer.Partner.BaseStatus.ATValue * clonPercent);

            double factorFromPF = 144.0 / client.Tamer.Partner.Digiclone.ATValue;
            double cloneFactor = Math.Round(1.0 + (0.43 / factorFromPF), 2);
            // ---------------------------------------------------------------------------

            f1BaseDamage = Math.Floor(f1BaseDamage * cloneFactor);
            double addedf1Damage = Math.Floor(f1BaseDamage * SkillFactor / 100.0);
            // ---------------------------------------------------------------------------

            // Cálculo do dano base somando o valor de ATT
            int baseDamage = (int)Math.Floor(f1BaseDamage + addedf1Damage + client.Tamer.Partner.AT + client.Tamer.Partner.SKD);

            // Agora, somando o ATT para aumentar o dano proporcionalmente com um fator ajustado
            double attBonusFactor = 1 + (client.Tamer.Partner.ATT / 9900.0); // Ajustado para 10000 ao invés de 1000
            baseDamage = (int)(baseDamage * attBonusFactor);  // Aumento proporcional ao ATT


            // clon Verification
            if (client.Tamer.Partner.Digiclone.ATLevel > 0)
                clonDamage = (int)(baseDamage * 0.301);
            else
                clonDamage = 0;

            // ---------------------------------------------------------------------------
            if (skillValue.Count > 1)
            {
                var currentLevel = partnerEvolution.Skills[skillSlot].CurrentLevel;

                if ((int)skillValue[1].Attribute != 39)
                {
                    activationChance += skillValue[1].Chance + currentLevel * 0;
                }
                else
                {
                    activationChance += skillValue[1].Chance + currentLevel * 0;
                }

                if ((int)skillValue[1].Attribute != 37 && (int)skillValue[1].Attribute != 38)
                {
                    durationBuff += currentLevel;
                    skillDuration += currentLevel + 2; // 2 is for server clock???? something calculates 2 extra seconds
                }
            }

            if (skillValue.Count > 2)
            {
                var currentLevel = partnerEvolution.Skills[skillSlot].CurrentLevel;

                if ((int)skillValue[2].Attribute != 39)
                {
                    activationChance += skillValue[2].Chance + currentLevel * 0;
                }
                else
                {
                    activationChance += skillValue[2].Chance + currentLevel * 0;
                }

                if ((int)skillValue[2].Attribute != 37 && (int)skillValue[2].Attribute != 38 && (int)skillValue[2].Attribute != 39)
                {
                    durationBuff += currentLevel;
                    skillDuration += currentLevel + 2; // 2 is for server clock???? something calculates 2 extra seconds
                }
            }

            int attributeBonus = (int)Math.Floor(f1BaseDamage * attributeDamage);
            int elementBonus = (int)Math.Floor(f1BaseDamage * elementDamage);

            double activationProbability = activationChance / 100.0;
            Random random = new Random();

            bool isActivated = activationProbability >= 1.0 || random.NextDouble() <= activationProbability;

            if (isActivated &&
                ((skillValue.Count > 1 && skillValue[1].Type != 0) ||
                 (skillValue.Count > 2 && skillValue[2].Type != 0)))
            {
                BuffSkill(client, durationBuff, skillDuration, skillSlot);
            }

            int totalDamage = baseDamage + clonDamage + attributeBonus + elementBonus;

            // Cálculo da chance de crítico com base no CC
            int CCValue = client.Partner.CC;

            // Calcula a chance de crítico como 7% do valor de CC
            double criticalHitChance = Math.Min(Math.Max(CCValue / 142857.0, 0.0), 1.0);

            bool isCriticalHit = random.NextDouble() < criticalHitChance;

            if (isCriticalHit)
            {
                // Cálculo do bônus SKD no dano crítico
                int skdValue = client.Tamer.Partner.SKD;
                double skdBonus = (skdValue / 500) * 0.01; // 1% a cada 500 de SKD
                totalDamage = (int)(totalDamage * 1.5); // Dano crítico com a base de 100%

                // Calcular a porcentagem total de dano crítico (100% + bônus SKD)
                double totalCritPercentage = 50.0 + (skdBonus * 100); // 100% base + bônus SKD

                // Verifica se a mensagem de crítico deve ser exibida
                if (client.EnableCriticalMessages)
                {
                    client.Send(UtilitiesFunctions.GroupPackets(
                        new SystemMessagePacket(
                            $"Crítico ativado! Chance de Crítico: {criticalHitChance:P2} | " +
                            $"{client.Tamer.Partner.Name} usou {skillInfo.SkillInfo.Name} e causou {totalDamage} de dano. " +
                            $"Dano Crítico: {totalCritPercentage:F2}% | By Takamura"
                        ).Serialize()
                    ));
                }
            }

            // Envio da mensagem no chat de batalha (caso não tenha sido crítico)
            if (totalDamage > 0 && AttackManager.IsBattle && !isCriticalHit)
            {
                client.Send(UtilitiesFunctions.GroupPackets(
                    new SystemMessagePacket(
                        $"Usou {skillInfo.SkillInfo.Name} E causou {totalDamage} de dano | " +
                        $"Chance de Critico: {criticalHitChance:P2}"
                    ).Serialize()
                ));
            }

            return totalDamage;
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
                                    case SkillCodeApplyAttributeEnum.DR: //reflect damage
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
                                        {
                                            // AT base do Digimon
                                            int baseAT = client.Tamer.Partner.AT;

                                            // Percentual: DOT = 40% do AT | DOT2 = 60% do AT
                                            double percent = (skillValue[i].Attribute == SkillCodeApplyAttributeEnum.DOT2) ? 0.6 : 0.4;

                                            // Dano fixo por tick (1 segundo)
                                            int perTick = (int)Math.Max(1, Math.Floor(baseAT * percent));

                                            // Mostra o buff no alvo
                                            broadcastAction(client.TamerId, new AddBuffPacket(
                                                selectedMob.GeneralHandler,
                                                buff,
                                                partnerEvolution.Skills[skillSlot].CurrentLevel,
                                                duration
                                            ).Serialize());

                                            // Registra o debuff
                                            var dotDebuff = selectedMob.DebuffList.Buffs.FirstOrDefault(x => x.BuffId == buff.BuffId);
                                            if (dotDebuff != null)
                                            {
                                                dotDebuff.IncreaseEndDate(skillDuration);
                                            }
                                            else
                                            {
                                                var newDot = MobDebuffModel.Create(buff.BuffId, (int)skillCode.SkillCode, 0, skillDuration);
                                                newDot.SetBuffInfo(buff);
                                                selectedMob.DebuffList.Buffs.Add(newDot);
                                            }

                                            // Loop de DOT a cada 1 segundo
                                            _ = Task.Run(async () =>
                                            {
                                                int remaining = skillDuration;

                                                while (remaining > 0 && selectedMob != null && selectedMob.CurrentHP > 0)
                                                {
                                                    int tickDamage = Math.Min(perTick, selectedMob.CurrentHP);
                                                    int newHp = selectedMob.ReceiveDamage(tickDamage, client.TamerId);

                                                    broadcastAction(client.TamerId, new AddDotDebuffPacket(
                                                        client.Tamer.Partner.GeneralHandler,
                                                        selectedMob.GeneralHandler,
                                                        buff.BuffId,
                                                        selectedMob.CurrentHpRate,
                                                        tickDamage,
                                                        (byte)((newHp > 0) ? 0 : 1)
                                                    ).Serialize());

                                                    if (newHp <= 0)
                                                    {
                                                        selectedMob.Die();
                                                        break;
                                                    }

                                                    await Task.Delay(1000);
                                                    remaining--;
                                                }

                                                // Remove debuff no fim
                                                if (selectedMob != null)
                                                {
                                                    var left = selectedMob.DebuffList.Buffs.FirstOrDefault(x => x.BuffId == buff.BuffId);
                                                    if (left != null)
                                                    {
                                                        selectedMob.DebuffList.Buffs.Remove(left);
                                                        broadcastAction(client.Tamer.Id,
                                                            new RemoveBuffPacket(client.Tamer.Partner.GeneralHandler, left.BuffId).Serialize());
                                                    }
                                                }
                                            });

                                            break;
                                        }

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

    }
}
