using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Utils;
using Serilog;
using DigitalWorldOnline.Commons.Packets.Chat;
using Microsoft.Extensions.Configuration;
using DigitalWorldOnline.GameHost;

namespace DigitalWorldOnline.Game.Managers
{
    public class AttackManager
    {
        private static bool isBattle;
        public AttackManager()
        {
            isBattle = false;
        }
        public static bool GetBattleStatus()
        {
            return isBattle;
        }

        public static void SetBattleStatus(bool status)
        {
            isBattle = status;
        }
        public static bool IsBattle => isBattle;


        public static int CalculateDamage(GameClient client, out double critBonusMultiplier, out bool blocked)
        {
            double baseDamage = client.Tamer.Partner.AT;

            // Adicionando o fator de aumento de dano com base no ATT do parceiro, semelhante ao primeiro código
            double attBonusFactor = 1 + (client.Tamer.Partner.ATT / 20000.0); // Ajuste conforme necessário
            baseDamage *= attBonusFactor;

            var random = new Random();
            double percentageBonus = random.NextDouble() * 0.08;
            baseDamage *= (1.0 + percentageBonus);

            double enemyDefence = client.Tamer.TargetIMob.DEValue;
            double enemyBlock = client.Tamer.TargetIMob.BLValue;
            int enemyLevel = client.Tamer.TargetIMob.Level;
            string receiverName = client.Tamer.Partner.Name;

            if (enemyDefence > 3000) enemyDefence = 3000;

            if (baseDamage < 0) baseDamage = 0;

            double levelBonusMultiplier = client.Tamer.Partner.Level > enemyLevel ? (0.01 * (client.Tamer.Partner.Level - enemyLevel)) : 0;
            int levelBonusDamage = (int)(baseDamage * levelBonusMultiplier);

            double defenceScalingFactor = 1 + (enemyDefence / 100.0);

            double attributeDamage = GetAttributeDamage(client);
            double elementDamage = GetElementDamage(client);

            int attributeBonus = (int)Math.Floor(baseDamage * attributeDamage);
            int elementBonus = (int)Math.Floor(baseDamage * elementDamage); //element calculation???

            blocked = enemyBlock >= UtilitiesFunctions.RandomDouble();
            if (blocked)
            {
                baseDamage /= 2;
            }
            double criticalChance = client.Tamer.Partner.CC / 100;
            double criticalDamage = client.Tamer.Partner.CD / 100;
            double critChance = Math.Min(criticalChance, 100);

            double excessCritChance = Math.Max(criticalChance - 100, 0);

            double adjustedCritDamage = criticalDamage + (excessCritChance / 2);

            bool isCriticalHit = critChance >= UtilitiesFunctions.RandomDouble() && adjustedCritDamage > 0;

            if (isCriticalHit)
            {
                blocked = false;
                critBonusMultiplier = 1.0;
                double crit = baseDamage * (1.0 + adjustedCritDamage / 100.0);
                baseDamage = crit;
            }
            else
            {
                critBonusMultiplier = 0;
            }

            double totalDamage = (baseDamage + attributeBonus + elementBonus + levelBonusDamage) - enemyDefence;

            //BattleLog
            //----------------------------------------------------------------------------------------------------------------- 
            if (GetBattleStatus())  // Check if battle is active
            {
                string attributeMessage = $"{attributeBonus} Attribute DMG!";
                string elementMessage = $"{elementBonus} Element DMG!";
                client.Send(new GuildMessagePacket(client.Tamer.Partner.Name, attributeMessage).Serialize());

                client.Send(new ChatMessagePacket(elementMessage, ChatTypeEnum.Whisper, WhisperResultEnum.Success, client.Tamer.Partner.Name, receiverName));

                if (totalDamage < 0)
                {
                    string message = $"Enemy Digimon's defence is way too high";
                    client.Send(new ChatMessagePacket(message, ChatTypeEnum.Shout, client.Tamer.Partner.Name).Serialize());
                }
                else if (totalDamage > 0)
                {
                    string message = isCriticalHit
                        ? $"Total {Math.Floor(totalDamage)} Crit DMG @{enemyDefence} enemy defence"
                        : $"Total {Math.Floor(totalDamage)} DMG @{enemyDefence} enemy defence";

                    client.Send(new ChatMessagePacket(message, ChatTypeEnum.Shout, client.Tamer.Partner.Name).Serialize());
                }
            }
            //-----------------------------------------------------------------------------------------------------------------
            return (int)totalDamage;
        }


        public static double GetAttributeDamage(GameClient client)
        {
            double multiplier = 0;
            var targetMob = client.Tamer.TargetIMob.Attribute;   


            if (client.Tamer.Partner.BaseInfo.Attribute.HasAttributeAdvantage(targetMob))
            {
                double currentExperience = client.Tamer.Partner.GetAttributeExperience();
                const double maxExperience = 10000;

                double bonusMultiplier = currentExperience / maxExperience;
                multiplier += Math.Min(bonusMultiplier,1.00);
            }
            else if (targetMob.HasAttributeAdvantage(client.Tamer.Partner.BaseInfo.Attribute))
            {
                multiplier = -0.25;
            }

            return multiplier;
        }

        public static double GetElementDamage(GameClient client)
        {
            double multiplier = 0;
            var targetMob = client.Tamer.TargetIMob.Element;

            if (client.Tamer.Partner.BaseInfo.Element.HasElementAdvantage(targetMob))
            {
                double currentExperience = client.Tamer.Partner.GetElementExperience();
                const double maxExperience = 10000;

                double bonusMultiplier = currentExperience / maxExperience;
                multiplier += Math.Min(bonusMultiplier,1.00);
            }
            else if (targetMob.HasElementAdvantage(client.Tamer.Partner.BaseInfo.Element))
            {
                multiplier = -0.25;
            }

            return multiplier;
        }


    }
}
