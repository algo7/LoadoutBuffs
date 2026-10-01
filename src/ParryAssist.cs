using System;

namespace LoadoutBuffs
{
    /// <summary>
    /// What a parry does for you and those around you: health and stamina for you, for other players and health
    /// for tamed creatures within the radii, and a protection bubble for all of them (bundle On-parry stats).
    /// </summary>
    internal sealed class ParryAssist
    {
        public float Heal;
        public float Stamina;
        public float HealAllies;
        public float StaminaAllies;
        public float HealTamed;

        /// <summary>Absorb amount of the Magic barrier for you, players within Radius and tamed within TamedRadius (see AllyAssist.RequestBubble).</summary>
        public float Shield;

        /// <summary>How long the bubble lasts, in whole minutes (rounded, at least 1: see ShieldMath.ItemLevelFor).</summary>
        public float ShieldMinutes = 1f;
        public float Radius = Catalog.DefaultHealAlliesRadius;
        public float TamedRadius = Catalog.DefaultHealAlliesRadius;

        public bool DoesSomething => Heal > 0f || Stamina > 0f || HealAllies > 0f || StaminaAllies > 0f || HealTamed > 0f || Shield > 0f;

        /// <summary>Heal or stamina for others (the bubble has its own path).</summary>
        public bool ReachesOthers => HealAllies > 0f || StaminaAllies > 0f || HealTamed > 0f;
    }

    /// <summary>
    /// The vanilla Magic barrier (Staff of Protection). The game sets its strength in SE_Shield.SetLevel:
    /// absorb = m_absorbDamage + m_absorbDamagePerSkillLevel × skill + m_absorbDamageWorldLevel × world level (if > 0).
    /// </summary>
    internal static class ShieldMath
    {
        public const string EffectName = "Staff_shield";

        /// <summary>
        /// The item level to send for a duration: SE_Shield.SetLevel sets ttl = m_ttlPerItemLevel (60 s) × item level, so
        /// whole minutes, rounded. Never below 1: item level 0 gives ttl 0, a bubble that never expires.
        /// </summary>
        public static int ItemLevelFor(float minutes) => Math.Max(1, (int)Math.Round(minutes, MidpointRounding.AwayFromZero));

        /// <summary>
        /// The skill level that makes the game's formula come out at <paramref name="amount"/> (negative below the base).
        /// False when the per-skill factor is 0: then skill 0, the game's base strength.
        /// </summary>
        public static bool TrySkillFor(float amount, float absorbBase, float perSkill, float perWorldLevel, int worldLevel, out float skill)
        {
            skill = 0f;
            if (perSkill == 0f || float.IsNaN(perSkill) || float.IsInfinity(perSkill)) return false;
            skill = (amount - absorbBase - (worldLevel > 0 ? perWorldLevel * worldLevel : 0f)) / perSkill;
            return true;
        }
    }

    /// <summary>One frame's bubble. Several requests in one frame merge: the largest amount, reaches and time win.</summary>
    internal sealed class ShieldRequest
    {
        public readonly float Amount;
        public readonly float PlayerReach;
        public readonly float TamedReach;
        public readonly float Minutes;

        public ShieldRequest(float amount, float playerReach, float tamedReach, float minutes)
        {
            Amount = amount;
            PlayerReach = playerReach;
            TamedReach = tamedReach;
            Minutes = minutes;
        }

        public static ShieldRequest Merge(ShieldRequest pending, ShieldRequest next)
        {
            if (pending == null) return next;
            if (next == null) return pending;
            return new ShieldRequest(Math.Max(pending.Amount, next.Amount), Math.Max(pending.PlayerReach, next.PlayerReach),
                Math.Max(pending.TamedReach, next.TamedReach), Math.Max(pending.Minutes, next.Minutes));
        }
    }
}
