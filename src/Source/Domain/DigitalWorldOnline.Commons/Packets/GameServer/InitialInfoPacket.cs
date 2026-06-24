using System;
using System.Linq;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Models.Mechanics;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class InitialInfoPacket : PacketWriter
    {
        private const int PacketNumber = 1003;

        public InitialInfoPacket(CharacterModel character, GameParty? party)
        {
            Type(PacketNumber);

            Console.WriteLine($"[INIT_PACKET_V8] Writing InitialInfoPacket for {character.Id}:{character.Name} Map={character.Location.MapId} Ch={character.Channel}");

            WriteInt(1);

            WriteInt(character.Location.X);
            WriteInt(character.Location.Y);

            WriteInt(character.GeneralHandler);

            WriteInt(character.Model.GetHashCode());

            WriteString(character.Name);

            WriteInt64(character.CurrentExperience * 100);

            WriteShort(character.Level);

            WriteInt(character.HP);
            WriteInt(character.DS);

            WriteInt(character.CurrentHp);
            WriteInt(character.CurrentDs);

            WriteInt(CharacterModel.Fatigue);

            WriteInt(character.AT);
            WriteInt(character.DE);
            WriteInt(character.MS);

            WriteBytes(character.Equipment.ToArray());

            WriteBytes(character.ChipSets.ToArray());

            WriteBytes(character.Digivice.ToArray());

            WriteBytes(character.TamerSkill.ToArray());

            WriteBytes(character.Progress.ToArray());

            WriteInt(character.Incubator.EggId);

            WriteInt(character.Incubator.HatchLevel);

            WriteInt(-1); // Egg TradeLimitTime

            WriteInt(character.Incubator.BackupDiskId);

            WriteInt(-1); // BackupDisk TradeLimitTime

            WriteTamerBuffs(character);

            WriteByte(character.DigimonSlots);

            WritePartnerDigimon(character);

            WriteActiveDigimonsSafe(character);

            WriteByte(99); // End active digimon loop

            // COMPAT_487:
            // O client lê um int extra antes do CurrentChannel.
            WriteInt(0);

            WriteInt(character.Channel);

            WriteBytes(character.SerializeMapRegion());

            WriteInt(character.DigimonArchive.Slots);

            WritePartyInfo(character, party);

            WriteShort(character.CurrentTitle);

            // ItemCooldown max 32
            for (int i = 0; i < 32; i++)
            {
                WriteInt(0);
            }

            WriteInt(0); // Game version / sync option

            WriteInt(2); // nWorkDayHistory

            WriteInt(0); // nTodayAttendanceTimeTS

            WriteInt(0); // BossGenInfo terminator: nBossMonsterType = 0

            WriteByte(0); // PC Bang

            WriteConsignedShop(character);

            WriteInt(0); // clientOption

            WriteInt(0); // Achievement rank

            WriteByte(0); // hatch minigame already played

            WriteShort(0); // minigame success count

            WriteTamerActiveSkills(character);

            WriteByte(0); // chat block / restrict flag

            WriteByte(0); // master match

            if (character.DeckBuffId == null)
            {
                WriteByte(0);
            }
            else
            {
                WriteInt((int)character.DeckBuffId);
            }

            WriteByte(0); // Megaphone ban

            WriteInt(0);

            WriteBytes(new byte[29]);

            Console.WriteLine("[INIT_PACKET_V8] InitialInfoPacket write complete");
        }

        private void WriteTamerBuffs(CharacterModel character)
        {
            var buffs = character.BuffList.ActiveBuffs.ToList();

            WriteShort((short)buffs.Count);

            foreach (var buff in buffs)
            {
                // Client sPostBuff usa u4/u4/u4/u4.
                WriteUInt((uint)buff.BuffId);

                WriteUInt((uint)buff.TypeN);

                WriteUInt((uint)UtilitiesFunctions.RemainingTimeSeconds(buff.RemainingSeconds));

                WriteUInt((uint)buff.SkillId);
            }
        }

        private void WritePartnerDigimon(CharacterModel character)
        {
            var partner = character.Partner;

            WriteInt(partner.GeneralHandler);

            WriteInt(partner.CurrentType);

            WriteString(partner.Name);

            WriteByte((byte)partner.HatchGrade);

            WriteShort(partner.Size);

            WriteInt64(partner.CurrentExperience * 100);

            // COMPAT_487:
            // O client lê este u8 como ExpPt2.
            WriteInt64(partner.TranscendenceExperience);

            WriteShort(partner.Level);

            WriteInt(partner.HP);
            WriteInt(partner.DS);
            WriteInt(partner.DE);
            WriteInt(partner.AT);

            WriteInt(partner.CurrentHp);
            WriteInt(partner.CurrentDs);

            WriteInt(partner.FS);

            WriteInt(0);

            WriteInt(partner.EV);
            WriteInt(partner.CC);
            WriteInt(partner.MS);
            WriteInt(partner.AS);

            WriteInt(0);

            WriteInt(partner.HT);

            WriteInt(0);
            WriteInt(0);

            WriteInt(partner.AR);
            WriteInt(partner.BL);

            WriteInt(partner.BaseType);

            WriteByte((byte)partner.Evolutions.Count);

            for (int i = 0; i < partner.Evolutions.Count; i++)
            {
                var form = partner.Evolutions[i];

                WriteBytes(form.ToArray());
            }

            WriteShort(partner.Digiclone.CloneLevel);

            WriteShort(partner.Digiclone.ATValue);
            WriteShort(partner.Digiclone.BLValue);
            WriteShort(partner.Digiclone.CTValue);

            WriteShort(0); // DE Value - not implemented client-side

            WriteShort(partner.Digiclone.EVValue);

            WriteShort(0); // HT Value - not implemented client-side

            WriteShort(partner.Digiclone.HPValue);

            WriteShort(partner.Digiclone.ATLevel);
            WriteShort(partner.Digiclone.BLLevel);
            WriteShort(partner.Digiclone.CTLevel);

            WriteShort(0); // DE Level - not implemented client-side

            WriteShort(partner.Digiclone.EVLevel);

            WriteShort(0); // HT Level - not implemented client-side

            WriteShort(partner.Digiclone.HPLevel);

            WritePartnerBuffs(character);

            WriteShort(partner.AttributeExperience.Data);
            WriteShort(partner.AttributeExperience.Vaccine);
            WriteShort(partner.AttributeExperience.Virus);
            WriteShort(partner.AttributeExperience.Ice);
            WriteShort(partner.AttributeExperience.Water);
            WriteShort(partner.AttributeExperience.Fire);
            WriteShort(partner.AttributeExperience.Land);
            WriteShort(partner.AttributeExperience.Wind);
            WriteShort(partner.AttributeExperience.Wood);
            WriteShort(partner.AttributeExperience.Light);
            WriteShort(partner.AttributeExperience.Dark);
            WriteShort(partner.AttributeExperience.Thunder);
            WriteShort(partner.AttributeExperience.Steel);

            // Client Data_PostLoad::sDATA::s_nUID é u4.
            WriteUInt(0); // Partner nUID

            WriteByte(0); // Partner CashSkillCount
        }

        private void WritePartnerBuffs(CharacterModel character)
        {
            var buffs = character.Partner.BuffList.ActiveBuffs.ToList();

            WriteShort((short)buffs.Count);

            foreach (var buff in buffs)
            {
                // COMPAT_487 Partner Buff:
                // u4 BuffCode
                // u4 BuffEndTS
                // u4 SkillCode
                // NÃO escreve TypeN aqui.
                WriteUInt((uint)buff.BuffId);

                WriteUInt((uint)UtilitiesFunctions.RemainingTimeSeconds(buff.RemainingSeconds));

                WriteUInt((uint)buff.SkillId);
            }
        }

        private void WriteActiveDigimonsSafe(CharacterModel character)
        {
            // Temporariamente não enviamos Digimons ativos extra.
            // O Partner já foi enviado acima.
            // O próximo byte escrito depois deste método é 99.
            //
            // O client correto deve mostrar:
            // [RECV_INIT] First active digimon slot marker=99

            var activeDigimons = character.ActiveDigimons
                .Where(x => x != null)
                .Select(x => $"{x.Id}:{x.Name}:Slot={x.Slot}")
                .ToList();

            if (activeDigimons.Any())
            {
                Console.WriteLine("[INIT_PACKET_V8] ActiveDigimons skipped for packet alignment test: " +
                                  string.Join(", ", activeDigimons));
            }
            else
            {
                Console.WriteLine("[INIT_PACKET_V8] No extra ActiveDigimons to send.");
            }
        }

        private void WritePartyInfo(CharacterModel character, GameParty? party)
        {
            if (party == null)
            {
                WriteUInt(0); // Party Id

                WriteUInt(0); // Crop/Loot type

                WriteUInt(0); // Rare Rate

                // IMPORTANTE:
                // O client lê m_nDispRareGrade como u4/int, não byte.
                // Antes estava WriteByte(0), e isso fazia o 99 cair no sítio errado.
                WriteUInt(0); // Display Rare Grade

                WriteByte(0); // Master slot

                // COMPAT_487:
                // Client lê u2 antes do primeiro nSlotNo.
                WriteShort(0);

                WriteByte(99); // End party member loop

                return;
            }

            WriteUInt((uint)party.Id);

            WriteUInt((uint)party.LootType); // Crop/Loot type

            WriteUInt((uint)party.LootFilter); // Rare Rate

            // IMPORTANTE:
            // Display Rare Grade também é u4/int no client.
            WriteUInt(0);

            WriteByte((byte)party.LeaderSlot); // Master slot

            // COMPAT_487:
            // Client lê u2 antes do primeiro nSlotNo.
            WriteShort(0);

            foreach (var member in party.Members
                         .Where(x => x.Value.Id != character.Id)
                         .OrderBy(x => x.Key)
                         .Take(8))
            {
                WriteByte(member.Key); // SlotNo

                if (character.Channel == member.Value.Channel &&
                    character.Location.MapId == member.Value.Location.MapId)
                {
                    WriteUInt((uint)member.Value.GeneralHandler);
                    WriteUInt((uint)member.Value.Partner.GeneralHandler);
                }
                else
                {
                    WriteUInt(0);
                    WriteUInt(0);
                }

                WriteInt(member.Value.Model.GetHashCode());

                WriteShort(member.Value.Level);

                WriteString(member.Value.Name);

                WriteInt(member.Value.Partner.CurrentType);

                WriteShort(member.Value.Partner.Level);

                WriteString(member.Value.Partner.Name);

                WriteInt(member.Value.Location.MapId);

                WriteInt(member.Value.Channel);
            }

            WriteByte(99); // End party member loop
        }

        private void WriteConsignedShop(CharacterModel character)
        {
            if (character.ConsignedShop != null)
            {
                WriteInt(character.ConsignedShop.Location.MapId);
                WriteInt(character.ConsignedShop.Channel);
                WriteInt(character.ConsignedShop.Location.X);
                WriteInt(character.ConsignedShop.Location.Y);
                WriteInt(character.ConsignedShop.ItemId);
            }
            else
            {
                WriteInt(0);
            }
        }

        private void WriteTamerActiveSkills(CharacterModel character)
        {
            var normalSkills = character.ActiveSkill
                .Where(x =>
                    x.Type == Enums.ClientEnums.TamerSkillTypeEnum.Normal &&
                    x.SkillId > 0 &&
                    x.RemainingCooldownSeconds > 0)
                .ToList();

            if (normalSkills.Any())
            {
                WriteByte((byte)normalSkills.Count);

                foreach (var skill in normalSkills)
                {
                    WriteInt(skill.SkillId);

                    if (skill.RemainingCooldownSeconds > 0)
                    {
                        WriteInt(UtilitiesFunctions.RemainingTimeSeconds(skill.RemainingCooldownSeconds));
                    }
                    else
                    {
                        WriteInt(0);
                    }
                }
            }
            else
            {
                WriteByte(0);
            }

            var cashSkills = character.ActiveSkill
                .Where(x =>
                    x.Type == Enums.ClientEnums.TamerSkillTypeEnum.Cash &&
                    x.SkillId > 0 &&
                    x.RemainingMinutes > 0)
                .ToList();

            if (cashSkills.Any())
            {
                WriteByte((byte)cashSkills.Count);

                foreach (var skill in cashSkills)
                {
                    if (skill.RemainingMinutes > 0)
                    {
                        WriteInt(skill.SkillId);

                        WriteInt(UtilitiesFunctions.RemainingTimeMinutes(skill.RemainingMinutes));

                        if (skill.RemainingCooldownSeconds > 0)
                        {
                            WriteInt(UtilitiesFunctions.RemainingTimeSeconds(skill.RemainingCooldownSeconds));
                        }
                        else
                        {
                            WriteInt(0);
                        }
                    }
                    else
                    {
                        WriteInt(0);
                        WriteInt(0);
                        WriteInt(0);
                    }
                }
            }
            else
            {
                WriteByte(0);
            }
        }
    }
}