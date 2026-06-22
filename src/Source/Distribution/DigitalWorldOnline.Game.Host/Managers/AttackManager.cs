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
            critBonusMultiplier = 1.0;
            blocked = false;

            // Guardas
            if (client?.Tamer?.Partner == null || client.Tamer.TargetIMob == null)
                return 0;

            var partner = client.Tamer.Partner;
            var target = client.Tamer.TargetIMob;
            var rnd = Random.Shared;

            // ===== Base Damage =====
            double baseDamage = partner.AT;

            // ATT scaling (DMO-like, leve)
            double attBonusFactor = 1.0 + (partner.ATT / 200000.0); // 60k = progressivo sem explodir
            baseDamage *= attBonusFactor;

            // Pequena variação aleatória (±5%)
            baseDamage *= 0.92 + rnd.NextDouble() * 0.16;

            // ===== Attribute + Element (±25% combinado) =====
            double attrMul = GetAttributeDamage(client);
            double elemMul = GetElementDamage(client);
            double combinedMul = Math.Clamp(attrMul + elemMul, -0.25, 0.25);
            baseDamage *= 1.0 + combinedMul;

            // ===== Block chance =====
            var blockChance = Math.Clamp(target.BLValue / 100.0, 0.0, 1.0);
            blocked = rnd.NextDouble() < blockChance;
            if (blocked)
                baseDamage *= 0.25; // DMO-like: bloqueio reduz para 25%

            // ===== Crítico =====
            double critChance = Math.Clamp(partner.CC / 100.0, 0.0, 1.0);
            bool isCrit = rnd.NextDouble() < critChance;

            // CD afeta leve (máx 2.0x)
            double critMult = 1.5 + Math.Min(partner.CD / 500.0, 0.5);
            if (isCrit)
            {
                blocked = false; // crítico ignora block
                baseDamage *= critMult;
                critBonusMultiplier = critMult;
            }

            // ===== Mitigação por DEF (curva DMO-like) =====
            double enemyDef = Math.Max(1.0, target.DEValue);
            double mitigated = (baseDamage * baseDamage) / (baseDamage + enemyDef);

            // ===== Dano final =====
            int totalDamage = (int)Math.Max(1, Math.Floor(mitigated));

            // ===== Multiplicador global de dano básico =====
            totalDamage = (int)Math.Floor(totalDamage * 2.0);

            // ===== Logs (debug / batalha) =====
            if (IsBattle)
            {
                try
                {
                    string message = isCrit
                        ? $"CRIT! {partner.Name} causou {totalDamage:N0} DMG (DEF {enemyDef})"
                        : $"{partner.Name} causou {totalDamage:N0} DMG (DEF {enemyDef})";

                }
                catch { /* nunca deixar log quebrar combate */ }
            }

            return totalDamage;
        }


        public static double GetAttributeDamage(GameClient client)
        {
            var partner = client?.Tamer?.Partner;
            var target = client?.Tamer?.TargetIMob;
            if (partner == null || target == null) return 0;

            double multiplier = 0;
            if (partner.BaseInfo.Attribute.HasAttributeAdvantage(target.Attribute))
                multiplier = 0.25; // +25%
            else if (target.Attribute.HasAttributeAdvantage(partner.BaseInfo.Attribute))
                multiplier = -0.25; // -25%

            return multiplier;
        }


        public static double GetElementDamage(GameClient client)
        {
            var partner = client?.Tamer?.Partner;
            var target = client?.Tamer?.TargetIMob;
            if (partner == null || target == null) return 0;

            //var targetMob = client.Tamer.TargetIMob.Element;
            double multiplier = 0;
            if (partner.BaseInfo.Element.HasElementAdvantage(target.Element))
            {
                double currentExperience = partner.GetElementExperience();
                const double maxExperience = 10000;

                double bonusMultiplier = currentExperience / maxExperience;
                multiplier += Math.Min(bonusMultiplier, 1.00);
            }
            else if (target.Element.HasElementAdvantage(partner.BaseInfo.Element))
            {
                multiplier = -0.25;
            }

            return multiplier;
        }


    }
}
