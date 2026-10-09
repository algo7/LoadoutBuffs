using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LoadoutBuffs
{
    /// <summary>
    /// lb_stats: the local player's current totals, read from the same getters the game uses. Read-only.
    /// Equipment modifiers are summed by Player.UpdateModifiers every frame; armor is GetBodyArmor (pieces + effect
    /// bonuses); resistances are GetDamageModifiers (base, then armor, then effects); speed factors use the game's
    /// GetJogSpeedFactor / GetRunSpeedFactor formulas (those methods are protected).
    /// </summary>
    internal static class StatsReport
    {
        public static List<string> Build(Player player)
        {
            var lines = new List<string> { $"{player.GetPlayerName()}: current totals" };
            AddArmor(lines, player);
            AddMovement(lines, player);
            AddEquipmentModifiers(lines, player);
            AddResistances(lines, player);
            lines.Add($"  Carry weight: {F(player.GetInventory().GetTotalWeight())} / {F(player.GetMaxCarryWeight())}");
            AddWeapon(lines, player);
            var bundles = BundleEffects.State;
            lines.Add("  " + bundles.StatusLine());
            if (bundles.Enabled && bundles.Active != null && bundles.Stats.Count > 0)
                lines.Add("    stats from the slots you wear: " + BundleEffects.WornTotals(player, bundles.Active).Summary());
            AddEffects(lines, player);
            return lines;
        }

        private static void AddArmor(List<string> lines, Player player)
        {
            // GetBodyArmor sums chest, legs, helmet and cape, then applies status-effect armor bonuses.
            var pieces = player.GetInventory().GetEquippedItems()
                .Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest ||
                            i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs ||
                            i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet ||
                            i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder)
                .ToList();
            var total = player.GetBodyArmor();
            var fromPieces = pieces.Sum(i => i.GetArmor());
            var parts = pieces.Select(i => $"{EffectText.DisplayName(i.m_shared.m_name)} {F(i.GetArmor())}").ToList();
            if (Math.Abs(total - fromPieces) > 0.05f) parts.Add($"effects {Signed(total - fromPieces)}");
            lines.Add($"  Armor: {F(total)}" + (parts.Count > 0 ? $"  ({string.Join(", ", parts)})" : ""));
        }

        private static void AddMovement(List<string> lines, Player player)
        {
            var m = player.GetEquipmentMovementModifier();
            var runSkill = player.GetSkillFactor(Skills.SkillType.Run);
            var run = (1f + runSkill * 0.25f) * (1f + m * 1.5f);
            lines.Add($"  Movement (equipment): {Percent(m)}  → jog ×{F2(1f + m)}, run ×{F2(1f + m * 1.5f)} " +
                      $"(×{F2(run)} with your Run skill)");
        }

        private static void AddEquipmentModifiers(List<string> lines, Player player)
        {
            var mods = new (string Name, float Value)[]
            {
                ("run stamina", player.GetEquipmentRunStaminaModifier()),
                ("jump stamina", player.GetEquipmentJumpStaminaModifier()),
                ("attack stamina", player.GetEquipmentAttackStaminaModifier()),
                ("block stamina", player.GetEquipmentBlockStaminaModifier()),
                ("dodge stamina", player.GetEquipmentDodgeStaminaModifier()),
                ("swim stamina", player.GetEquipmentSwimStaminaModifier()),
                ("sneak stamina", player.GetEquipmentSneakStaminaModifier()),
                ("home items stamina", player.GetEquipmentHomeItemModifier()),
                ("heat resistance", player.GetEquipmentHeatResistanceModifier()),
                ("eitr regen", player.GetEquipmentEitrRegenModifier()),
            };
            var set = mods.Where(p => Math.Abs(p.Value) > 0.0001f).Select(p => $"{p.Name} {Percent(p.Value)}").ToList();
            lines.Add("  Item modifiers: " + (set.Count > 0 ? string.Join(", ", set) : "none") +
                      "  (effect bonuses are listed under Active effects)");
        }

        private static void AddResistances(List<string> lines, Player player)
        {
            var mods = player.GetDamageModifiers();
            var list = new List<string>();
            foreach (var name in Catalog.ModifierTypes)
            {
                var type = (HitData.DamageType)Enum.Parse(typeof(HitData.DamageType), name);
                var mod = mods.GetModifier(type);
                if (mod != HitData.DamageModifier.Normal) list.Add($"{name} {mod}");
            }
            lines.Add("  Resistances (armor + effects): " + (list.Count > 0 ? string.Join(", ", list) : "none"));
        }

        private static void AddWeapon(List<string> lines, Player player)
        {
            var weapon = player.GetCurrentWeapon();
            if (weapon == null) return;
            var damages = FormatDamages(weapon.GetDamage());
            lines.Add($"  Weapon: {EffectText.DisplayName(weapon.m_shared.m_name)} (quality {weapon.m_quality}): {damages}");

            // Blocks and parries use the left-hand item if there is one (shield, torch), else the weapon (Humanoid.GetCurrentBlocker).
            var blocker = player.LeftItem ?? weapon;
            if (!ReferenceEquals(blocker, weapon))
                lines.Add($"  Blocking with: {EffectText.DisplayName(blocker.m_shared.m_name)} (quality {blocker.m_quality})");
            var shared = blocker.m_shared;
            // A parry multiplies block armor by the item's bonus, then by every effect's Parry bonus (Humanoid.BlockAttack).
            var parryBonus = shared.m_timedBlockBonus;
            if (parryBonus > 1f) player.GetSEMan().ModifyTimedBlockBonus(ref parryBonus);
            string X(float v) => "×" + v.ToString("0.##", CultureInfo.InvariantCulture);
            var parryText = Math.Abs(parryBonus - shared.m_timedBlockBonus) < 0.001f ? X(parryBonus)
                : $"{X(parryBonus)} (item {X(shared.m_timedBlockBonus)})";
            lines.Add($"    block armor {F(blocker.GetBlockPowerTooltip(blocker.m_quality))} (with your Blocking skill), " +
                      $"block force {F(blocker.GetDeflectionForce())}, parry {parryText}");

            var parry = shared.m_perfectBlockStatusEffect;
            if (parry != null)
                lines.Add($"    effect on parry: {EffectText.DisplayName(parry.m_name)}");
        }

        /// <summary>"you +50 health, players within 20 m +40 health +30 stamina, tamed within 30 m +50 health".</summary>
        internal static string DescribeParry(ParryAssist a)
        {
            var parts = new List<string>();
            var you = Amounts(a.Heal, a.Stamina);
            if (you != null) parts.Add("you " + you);
            var players = Amounts(a.HealAllies, a.StaminaAllies);
            if (players != null) parts.Add($"players within {F(a.Radius)} m {players}");
            if (a.HealTamed > 0f) parts.Add($"tamed within {F(a.TamedRadius)} m +{F(a.HealTamed)} health");
            if (a.Shield > 0f)
                parts.Add($"bubble health {F(a.Shield)} ({ShieldMath.ItemLevelFor(a.ShieldMinutes)} min) for you, players within {F(a.Radius)} m and tamed within {F(a.TamedRadius)} m");
            return parts.Count > 0 ? string.Join(", ", parts) : "nothing";
        }

        private static string Amounts(float heal, float stamina)
        {
            var parts = new List<string>();
            if (heal > 0f) parts.Add($"+{F(heal)} health");
            if (stamina > 0f) parts.Add($"+{F(stamina)} stamina");
            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }

        private static void AddEffects(List<string> lines, Player player)
        {
            var effects = player.GetSEMan().GetStatusEffects().Where(se => se != null).ToList();
            lines.Add($"  Active effects ({effects.Count}):" + (effects.Count == 0 ? " none" : ""));
            foreach (var se in effects)
            {
                var stats = EffectText.Describe(se, statsOnly: true).ToList();
                lines.Add($"    {EffectText.DisplayName(se.m_name)}" + (stats.Count > 0 ? ": " + string.Join(", ", stats) : ""));
            }
        }

        private static string FormatDamages(HitData.DamageTypes damages)
        {
            object boxed = damages;
            var parts = new List<string>();
            foreach (var key in Catalog.DamageKeys)
            {
                var field = typeof(HitData.DamageTypes).GetField("m_" + key);
                var value = field != null ? (float)field.GetValue(boxed) : 0f;
                if (value != 0f) parts.Add($"{key} {F(value)}");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "no damage";
        }

        private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        private static string F2(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        private static string Signed(float v) => v.ToString("+0.#;-0.#", CultureInfo.InvariantCulture);
        private static string Percent(float v) => (v * 100f).ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture) + " %";
    }
}
