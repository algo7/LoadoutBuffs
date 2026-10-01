using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoadoutBuffs
{
    /// <summary>
    /// What a bundle parry does for you and others (BundleEffects.OnParry). Everything goes through calls the other player's own game handles, so it works for
    /// players without this mod: Character.Heal → "RPC_Heal" (no PvP check; shows a green +X to everyone),
    /// Player.UseStamina with a negative amount → "UseStamina" (their game subtracts it, i.e. adds stamina, and
    /// restarts their regen delay; anything above max is trimmed by their next regen tick).
    /// The bubble is the vanilla Magic barrier, sent like the staff sends it: SEMan.AddStatusEffect → RPC_AddStatusEffect.
    /// </summary>
    internal static class AllyAssist
    {
        private static readonly List<Character> s_nearby = new List<Character>();
        private static readonly int s_shieldHash = ShieldMath.EffectName.GetStableHashCode();
        private static Character s_bubbleSource;
        private static ShieldRequest s_bubble;
        private static bool s_bubbleErrorLogged;
        private static bool s_noShieldWarned;
        private static bool s_noSkillFactorWarned;

        /// <summary>For you. Item parry effects do this through SE_Stats' up-front health and stamina instead.</summary>
        public static void Self(Character character, ParryAssist assist)
        {
            if (assist.Heal > 0f) character.Heal(assist.Heal);
            if (assist.Stamina > 0f) character.AddStamina(assist.Stamina);
        }

        /// <summary>
        /// For other players within <see cref="ParryAssist.Radius"/> and tamed creatures within
        /// <see cref="ParryAssist.TamedRadius"/> (never wild ones, never the dead). Only a player's parry reaches others.
        /// </summary>
        public static void Others(Character source, ParryAssist assist)
        {
            if (source == null || !assist.ReachesOthers || !source.IsPlayer()) return;
            var center = source.transform.position;
            var players = assist.HealAllies > 0f || assist.StaminaAllies > 0f ? assist.Radius : 0f;
            var tamed = assist.HealTamed > 0f ? assist.TamedRadius : 0f;
            s_nearby.Clear();
            Character.GetCharactersInRange(center, Mathf.Max(players, tamed), s_nearby);
            foreach (var other in s_nearby)
            {
                if (other == null || other == source || other.IsDead()) continue;
                var distance2 = (other.transform.position - center).sqrMagnitude;
                if (other.IsPlayer())
                {
                    if (distance2 >= players * players) continue;
                    if (assist.HealAllies > 0f) other.Heal(assist.HealAllies);
                    if (assist.StaminaAllies > 0f) other.UseStamina(-assist.StaminaAllies);
                }
                else if (other.IsTamed() && assist.HealTamed > 0f && distance2 < tamed * tamed)
                {
                    other.Heal(assist.HealTamed);
                }
            }
        }

        /// <summary>
        /// Queues this parry's bubble; FlushBubble (Plugin.LateUpdate) sends it once. Several parries in one frame merge
        /// (largest amount, reaches and time).
        /// </summary>
        public static void RequestBubble(Character source, ParryAssist assist)
        {
            if (source == null || assist == null || assist.Shield <= 0f || !source.IsPlayer()) return;
            var request = new ShieldRequest(assist.Shield, assist.Radius, assist.TamedRadius, assist.ShieldMinutes);
            s_bubble = ReferenceEquals(s_bubbleSource, source) ? ShieldRequest.Merge(s_bubble, request) : request;
            s_bubbleSource = source;
        }

        /// <summary>
        /// Sends a queued bubble to the parrying player, other players within reach and tamed creatures within their
        /// reach (never wild ones, never the dead). resetTime false: anyone who has a bubble keeps it as it is (a refresh
        /// could weaken a staff bubble or pop a worn one, SE_Shield never resets its damage taken). The item level sets
        /// the time: 60 s per level, never 0 (that would never expire). Must never throw.
        /// </summary>
        public static void FlushBubble()
        {
            if ((object)s_bubbleSource == null) return;
            var source = s_bubbleSource;
            var request = s_bubble;
            s_bubbleSource = null;
            s_bubble = null;
            try
            {
                if (source == null || request == null || source.IsDead()) return;
                var db = ObjectDB.instance;
                var shield = db == null ? null : db.GetStatusEffect(s_shieldHash) as SE_Shield;
                if (shield == null)
                {
                    WarnOnce(ref s_noShieldWarned, $"No bubble sent: the game has no {ShieldMath.EffectName} effect.");
                    return;
                }
                if (!ShieldMath.TrySkillFor(request.Amount, shield.m_absorbDamage, shield.m_absorbDamagePerSkillLevel,
                        shield.m_absorbDamageWorldLevel, Game.m_worldLevel, out var skill))
                    WarnOnce(ref s_noSkillFactorWarned,
                        $"{ShieldMath.EffectName} has no per-skill strength (another mod?): bubbles get the game's base strength.");

                var itemLevel = ShieldMath.ItemLevelFor(request.Minutes);
                Give(source, itemLevel, skill);
                var center = source.transform.position;
                s_nearby.Clear();
                Character.GetCharactersInRange(center, Mathf.Max(request.PlayerReach, request.TamedReach), s_nearby);
                foreach (var other in s_nearby)
                {
                    if (other == null || other == source || other.IsDead()) continue;
                    var distance2 = (other.transform.position - center).sqrMagnitude;
                    if (other.IsPlayer() ? distance2 < request.PlayerReach * request.PlayerReach
                                         : other.IsTamed() && distance2 < request.TamedReach * request.TamedReach)
                        Give(other, itemLevel, skill);
                }
            }
            catch (Exception e)
            {
                if (s_bubbleErrorLogged) return;
                s_bubbleErrorLogged = true;
                Plugin.Log.LogError($"Parry bubble failed (logged once): {e}");
            }
        }

        private static void Give(Character character, int itemLevel, float skill) =>
            character.GetSEMan()?.AddStatusEffect(s_shieldHash, resetTime: false, itemLevel: itemLevel, skillLevel: skill);

        private static void WarnOnce(ref bool warned, string message)
        {
            if (warned) return;
            warned = true;
            Plugin.Log.LogWarning(message);
        }
    }
}
