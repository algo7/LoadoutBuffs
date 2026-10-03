using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ItemData = ItemDrop.ItemData;

namespace LoadoutBuffs
{
    /// <summary>
    /// Runtime side of bundles: reads the bundles file on each bundle pass (GameGlue), resolves the active bundle to the
    /// game's StatusEffect objects, and adds them to the local player's worn effects via the hook in
    /// Humanoid.UpdateEquipmentStatusEffects (see BundlePatches). Items are never modified.
    /// </summary>
    internal static class BundleEffects
    {
        /// <summary>Set by the transpiler when it found its spot; bundles stay off otherwise.</summary>
        public static bool HookInstalled;

        /// <summary>Set by the parry transpiler (Humanoid.BlockAttack); bundle on-parry stats need it.</summary>
        public static bool ParryHookInstalled;

        /// <summary>Set by Plugin when the block armor + force / added damage postfixes were patched in.</summary>
        public static bool BlockHookInstalled;
        public static bool DamageHookInstalled;

        /// <summary>Set by Plugin when the eitr cost prefixes (Player.UseEitr / HaveEitr) were patched in.</summary>
        public static bool EitrHookInstalled;

        public static BundleFile File { get; private set; } = new BundleFile();
        public static BundleState State { get; private set; } = new BundleState();
        public static GameEffectCatalog Catalog { get; private set; }

        private static readonly Dictionary<BundleSlot, StatusEffect> s_effects = new Dictionary<BundleSlot, StatusEffect>();
        private static readonly Dictionary<BundleSlot, StatBlock> s_stats = new Dictionary<BundleSlot, StatBlock>();
        private static string s_statsName;
        private static bool s_showHudBuff;
        private static ParryAssist s_parry;
        private static bool s_parryErrorLogged;
        private static bool s_woodcutter;
        private static bool s_miner;
        private static bool s_classErrorLogged;
        private static float s_blockArmor;
        private static float s_blockForce;
        private static bool s_blockErrorLogged;
        private static readonly Dictionary<string, float> s_addDamage = new Dictionary<string, float>(StringComparer.Ordinal);
        private static bool s_damageErrorLogged;
        private static float s_eitrCost;
        private static bool s_eitrErrorLogged;

        /// <summary>A tool tier above anything the game asks for (hits carry it as a 16-bit number).</summary>
        public const short ClassToolTier = 1000;
        private static bool s_hookErrorLogged;

        // Plain reflection (read only on gear changes): nothing in this type's initializer can fail.
        private static readonly FieldInfo s_nview = AccessTools.Field(typeof(Character), "m_nview");
        private static readonly FieldInfo s_helmet = AccessTools.Field(typeof(Humanoid), "m_helmetItem");
        private static readonly FieldInfo s_chest = AccessTools.Field(typeof(Humanoid), "m_chestItem");
        private static readonly FieldInfo s_legs = AccessTools.Field(typeof(Humanoid), "m_legItem");
        private static readonly FieldInfo s_cape = AccessTools.Field(typeof(Humanoid), "m_shoulderItem");
        private static readonly FieldInfo s_right = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static readonly FieldInfo s_left = AccessTools.Field(typeof(Humanoid), "m_leftItem");

        /// <summary>A bundle pass: read the bundles file and resolve it against this ObjectDB. Doesn't touch the player; see Refresh.</summary>
        public static BundleState Apply(ObjectDB db)
        {
            var text = Read(out var readError);
            File = BundleFile.Parse(text);
            if (readError != null) File.Warnings.Insert(0, readError);

            var all = GameGlue.CollectStatusEffects(db);
            Catalog = new GameEffectCatalog(db, all);
            if (File.OnlyMisplaced) RemoveMisplaced();
            State = BundleRules.Evaluate(File, Catalog);
            if (State.Enabled && !HookInstalled)
            {
                State.Enabled = false;
                State.OffReason = "this game version isn't supported by the buff hook (see LogOutput.log).";
            }

            s_effects.Clear();
            s_stats.Clear();
            s_parry = null;
            s_woodcutter = s_miner = false;
            s_blockArmor = s_blockForce = s_eitrCost = 0f;
            s_addDamage.Clear();
            s_statsName = State.Active?.Name;
            s_showHudBuff = BundleRules.ShowsHudBuff(State);
            if (State.Enabled)
                State.Warnings.AddRange(BundleRules.MissingHookWarnings(State.Stats.Values, ParryHookInstalled, BlockHookInstalled, DamageHookInstalled,
                    EitrHookInstalled));
            if (State.Enabled)
            {
                foreach (var pair in State.Effects)
                    if (all.TryGetValue(pair.Value, out var effect) && effect != null)
                        s_effects[pair.Key] = effect;
                foreach (var pair in State.Stats)
                    s_stats[pair.Key] = pair.Value;
            }
            return State;
        }

        /// <summary>
        /// 1.0.0 allowed classes and damage stats on armor and the shield, 1.1.0 on-parry stats on armor and the cape; now
        /// they're skipped. When those are the file's only problems, save it once without them, as the window would, so the
        /// dead lines and their warnings don't stay forever.
        /// </summary>
        private static void RemoveMisplaced()
        {
            try
            {
                var text = File.Serialize(code => Catalog?.DisplayName(code));
                System.IO.File.WriteAllText(Plugin.BundlesPath, text);
                foreach (var note in File.Misplaced)
                    Plugin.Log.LogInfo($"{BundleFile.FileName}: removed {note}.");
                File = BundleFile.Parse(text);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not remove the stats on the wrong slots from {BundleFile.FileName}: {e.Message}");
            }
        }

        /// <summary>Re-evaluate the file (after the window saved it) and refresh the player.</summary>
        public static BundleState ReloadFile()
        {
            var db = ObjectDB.instance;
            if (!GameGlue.IsReady(db)) return State;
            Apply(db);
            foreach (var warning in State.Warnings) Plugin.Log.LogWarning(warning);
            Plugin.Log.LogInfo(State.StatusLine());
            Refresh();
            return State;
        }

        /// <summary>Writes the file the way the window shows it, then applies it.</summary>
        public static BundleState Save(BundleFile file)
        {
            var text = file.Serialize(code => Catalog?.DisplayName(code));
            System.IO.File.WriteAllText(Plugin.BundlesPath, text);
            return ReloadFile();
        }

        /// <summary>Makes the game recompute the local player's worn effects (our hook adds the bundle's).</summary>
        public static void Refresh() => GameGlue.RefreshEquipmentEffects(Player.m_localPlayer);

        /// <summary>
        /// Called by the transpiled Humanoid.UpdateEquipmentStatusEffects right after it creates its set of
        /// worn effects. Only for the player this game owns (that's also true during spawn, before
        /// Player.m_localPlayer is set). Must never throw.
        /// </summary>
        public static void AddTo(Humanoid humanoid, HashSet<StatusEffect> set)
        {
            try
            {
                if ((!s_showHudBuff && s_effects.Count == 0 && s_stats.Count == 0) || set == null || !(humanoid is Player)) return;
                var nview = s_nview?.GetValue(humanoid) as ZNetView;
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
                foreach (var pair in s_effects)
                    if (pair.Value != null && Worn(humanoid, pair.Key) != null)
                        set.Add(pair.Value);

                // The HUD buff: the buff's name, its worn effects and the sum of the filled slots' custom stats,
                // updated in place when already active. Shown whenever a buff is in use, with or without stats.
                var total = StatBlock.Sum(s_stats.Where(p => Worn(humanoid, p.Key) != null).Select(p => p.Value));
                s_parry = total.ToParryAssist();
                s_woodcutter = total.Scalars.ContainsKey(BundleStatCatalog.WoodcutterKey);
                s_miner = total.Scalars.ContainsKey(BundleStatCatalog.MinerKey);
                s_blockArmor = total.BlockArmor;
                s_blockForce = total.BlockForce;
                s_eitrCost = total.EitrCost;
                s_addDamage.Clear();
                foreach (var p in total.AddDamage) s_addDamage[p.Key] = p.Value;
                if (!s_showHudBuff) return;
                var effectNames = string.Join(", ", s_effects
                    .Where(p => p.Value != null && Worn(humanoid, p.Key) != null)
                    .OrderBy(p => p.Key)
                    .Select(p => EffectText.DisplayName(p.Value.m_name)));
                var template = BundleStatsEffect.Template;
                template.SetTotals(total, s_statsName ?? "Buff", effectNames);
                if (humanoid.GetSEMan().GetStatusEffect(template.NameHash()) is BundleStatsEffect active && active != template)
                    active.SetTotals(total, s_statsName ?? "Buff", effectNames);
                set.Add(template);
            }
            catch (Exception e)
            {
                if (s_hookErrorLogged) return;
                s_hookErrorLogged = true;
                Plugin.Log.LogError($"Buff effects failed (logged once): {e}");
            }
        }

        /// <summary>
        /// Called by the transpiled Humanoid.BlockAttack right where the game handles a parry (perfect timing, an
        /// attacker, stamina left, not staggered). Uses the on-parry totals the last worn-effects update left for
        /// this game's own player. Must never throw.
        /// </summary>
        public static void OnParry(Humanoid humanoid)
        {
            try
            {
                var assist = s_parry;
                if (assist == null || !(humanoid is Player)) return;
                var nview = s_nview?.GetValue(humanoid) as ZNetView;
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
                AllyAssist.Self(humanoid, assist);
                AllyAssist.Others(humanoid, assist);
                AllyAssist.RequestBubble(humanoid, assist);
            }
            catch (Exception e)
            {
                if (s_parryErrorLogged) return;
                s_parryErrorLogged = true;
                Plugin.Log.LogError($"Buff on-parry stats failed (logged once): {e}");
            }
        }

        /// <summary>
        /// Prefix of Damage(HitData) on trees, logs, rocks and other destructibles, on the attacker's side (before
        /// the hit is sent to the object's owner). With Woodcutter / Miner on the slot of the weapon in hand (classes
        /// only go on melee and ranged, and only the held weapon's slot is filled), this player's hits get the max tool
        /// tier and chop (trees, logs, stumps) or pickaxe (everything else) damage equal to the hit's physical damage,
        /// unless it already does more: swings, and also arrows, bolts, staff spells and the blasts they spawn
        /// (Projectile / Aoe hits are the owner's, sent through the same Damage). Must never throw.
        /// </summary>
        public static void OnDestructibleHit(object target, HitData hit)
        {
            try
            {
                if (!(s_woodcutter || s_miner) || hit == null) return;
                var player = Player.m_localPlayer;
                if (player == null || hit.GetAttacker() != player) return;
                var weapon = player.GetCurrentWeapon();
                var slot = weapon == null ? null : BundleSlots.HandSlot(weapon.m_shared.m_itemType, weapon.m_shared.m_skillType);
                if (slot != BundleSlot.Melee && slot != BundleSlot.Ranged) return;

                var tree = target is TreeBase || target is TreeLog ||
                           (target is Destructible d && d.m_destructibleType == DestructibleType.Tree);
                var physical = hit.m_damage.m_blunt + hit.m_damage.m_slash + hit.m_damage.m_pierce;
                if (tree && s_woodcutter)
                    hit.m_damage.m_chop = Math.Max(hit.m_damage.m_chop, physical);
                else if (!tree && s_miner)
                    hit.m_damage.m_pickaxe = Math.Max(hit.m_damage.m_pickaxe, physical);
                else
                    return;
                if (hit.m_toolTier < ClassToolTier) hit.m_toolTier = ClassToolTier;
            }
            catch (Exception e)
            {
                if (s_classErrorLogged) return;
                s_classErrorLogged = true;
                Plugin.Log.LogError($"Buff classes failed (logged once): {e}");
            }
        }

        /// <summary>
        /// Postfix of ItemData.GetBaseBlockPower(int): the bundle's block armor goes onto whatever the local player blocks
        /// with (the shield if held, else the weapon or fists, like Humanoid.GetCurrentBlocker), as if it were the item's
        /// own blockPower. Blocks, the tooltip and lb_stats all read this getter. Must never throw.
        /// </summary>
        public static float WithBlockArmor(ItemData item, float value)
        {
            try
            {
                if (s_blockArmor == 0f || item == null) return value;
                var player = Player.m_localPlayer;
                if (player == null) return value;
                var blocker = player.LeftItem ?? player.GetCurrentWeapon();
                return ReferenceEquals(blocker, item) ? BundleStatCatalog.WithBlockArmor(value, s_blockArmor) : value;
            }
            catch (Exception e)
            {
                if (!s_blockErrorLogged)
                {
                    s_blockErrorLogged = true;
                    Plugin.Log.LogError($"Buff block armor failed (logged once): {e}");
                }
                return value;
            }
        }

        /// <summary>
        /// Postfix of ItemData.GetDeflectionForce(int): the bundle's block force on whatever the local player blocks with,
        /// like <see cref="WithBlockArmor(ItemData, float)"/>. Humanoid.BlockAttack and the tooltip read this getter. Must never throw.
        /// </summary>
        public static float WithBlockForce(ItemData item, float value)
        {
            try
            {
                if (s_blockForce == 0f || item == null) return value;
                var player = Player.m_localPlayer;
                if (player == null) return value;
                var blocker = player.LeftItem ?? player.GetCurrentWeapon();
                return ReferenceEquals(blocker, item) ? BundleStatCatalog.WithBlockForce(value, s_blockForce) : value;
            }
            catch (Exception e)
            {
                if (!s_blockErrorLogged)
                {
                    s_blockErrorLogged = true;
                    Plugin.Log.LogError($"Buff block force failed (logged once): {e}");
                }
                return value;
            }
        }

        /// <summary>
        /// Postfix of ItemData.GetDamage(int, float): the bundle's added damage goes onto the weapon the local player attacks
        /// with (Humanoid.GetCurrentWeapon: right hand, else a left-hand weapon, else fists), as if it were the weapon's own
        /// damage, so attacks scale it like the rest and the tooltip / lb_stats show it. Arrows and bolts aren't the current
        /// weapon (the bow's share counts once per shot). Must never throw.
        /// </summary>
        public static void WithAddedDamage(ItemData item, ref HitData.DamageTypes damages)
        {
            try
            {
                if (s_addDamage.Count == 0 || item == null) return;
                var player = Player.m_localPlayer;
                if (player == null || !ReferenceEquals(player.GetCurrentWeapon(), item)) return;
                damages = StatBlock.AddDamageTo(damages, s_addDamage);
            }
            catch (Exception e)
            {
                if (s_damageErrorLogged) return;
                s_damageErrorLogged = true;
                Plugin.Log.LogError($"Buff added damage failed (logged once): {e}");
            }
        }

        /// <summary>
        /// Prefix of Player.UseEitr(v) and Player.HaveEitr(amount): every eitr cost the local player pays (spells, the
        /// lightning staff's charge, draw / reload drains) and its check, with the buff's eitr cost percent, so both agree.
        /// Other players are untouched. Must never throw.
        /// </summary>
        public static float WithEitrCost(Player player, float eitr)
        {
            try
            {
                if (s_eitrCost == 0f || player == null || !ReferenceEquals(player, Player.m_localPlayer)) return eitr;
                return BundleStatCatalog.WithEitrCost(eitr, s_eitrCost);
            }
            catch (Exception e)
            {
                if (!s_eitrErrorLogged)
                {
                    s_eitrErrorLogged = true;
                    Plugin.Log.LogError($"Buff eitr cost failed (logged once): {e}");
                }
                return eitr;
            }
        }

        /// <summary>The weapon the local player attacks with (fists when the hands are empty), or null without a player.</summary>
        public static ItemData CurrentWeapon()
        {
            var player = Player.m_localPlayer;
            return player == null ? null : player.GetCurrentWeapon();
        }

        /// <summary>A weapon's own damage per type, without what the active bundle adds to it (for the window).</summary>
        public static Dictionary<string, float> OwnDamage(ItemData weapon)
        {
            var result = StatBlock.DamageByType(weapon.GetDamage());
            if (!ReferenceEquals(CurrentWeapon(), weapon)) return result;
            foreach (var p in s_addDamage)
            {
                if (!result.TryGetValue(p.Key, out var v)) continue;
                if (v - p.Value > 0.001f) result[p.Key] = v - p.Value;
                else result.Remove(p.Key);
            }
            return result;
        }

        /// <summary>What the window's gear-dependent parts depend on: the item in every slot and the weapon in hand.</summary>
        public static ItemData[] GearSnapshot(Humanoid h)
        {
            if (h == null) return new ItemData[0];
            var items = BundleSlots.All.Select(s => Worn(h, s)).ToList();
            items.Add(h.GetCurrentWeapon());
            return items.ToArray();
        }

        /// <summary>A bundle's stats summed over the slots the player fills now (window totals line, lb_stats).</summary>
        public static StatBlock WornTotals(Humanoid h, BundleDef bundle)
        {
            if (h == null || bundle == null) return new StatBlock();
            return StatBlock.Sum(bundle.Entries.Where(e => e.Stats != null && Worn(h, e.Slot) != null).Select(e => e.Stats));
        }

        /// <summary>The item that fills a slot. Hand slots: whichever hand holds an item of that kind (see BundleSlots.HandSlot).</summary>
        public static ItemData Worn(Humanoid h, BundleSlot slot)
        {
            switch (slot)
            {
                case BundleSlot.Helmet: return Get(s_helmet, h);
                case BundleSlot.Chest: return Get(s_chest, h);
                case BundleSlot.Legs: return Get(s_legs, h);
                case BundleSlot.Cape: return Get(s_cape, h);
                case BundleSlot.Melee:
                case BundleSlot.Ranged:
                case BundleSlot.Shield:
                    foreach (var item in new[] { Get(s_right, h), Get(s_left, h) })
                        if (item != null && BundleSlots.HandSlot(item.m_shared.m_itemType, item.m_shared.m_skillType) == slot)
                            return item;
                    return null;
                default: return null;
            }
        }

        private static ItemData Get(FieldInfo field, Humanoid h) => h == null ? null : field?.GetValue(h) as ItemData;

        private static string Read(out string error)
        {
            error = null;
            try
            {
                return System.IO.File.Exists(Plugin.BundlesPath) ? System.IO.File.ReadAllText(Plugin.BundlesPath) : null;
            }
            catch (Exception e)
            {
                error = $"Could not read {Plugin.BundlesPath}: {e.Message}";
                return null;
            }
        }
    }

    /// <summary>
    /// Effects bundles may use: every effect some item has as its equip or set effect. Other mods' gear effects count too.
    /// </summary>
    internal sealed class GameEffectCatalog : IEffectCatalog
    {
        private readonly Dictionary<string, StatusEffect> _all;
        private readonly List<EffectInfo> _allowed;

        public GameEffectCatalog(ObjectDB db, Dictionary<string, StatusEffect> all)
        {
            _all = all;
            var found = new Dictionary<string, StatusEffect>(StringComparer.Ordinal);
            foreach (var go in db.m_items)
            {
                if (go == null) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null) continue;
                var shared = drop.m_itemData.m_shared;
                foreach (var effect in new[] { shared.m_equipStatusEffect, shared.m_setStatusEffect })
                    if (effect != null && !found.ContainsKey(effect.name)) found[effect.name] = effect;
            }
            _allowed = found.Values
                .Select(se => new EffectInfo { Code = se.name, DisplayName = EffectText.DisplayName(se.m_name) })
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<EffectInfo> Allowed => _allowed;

        public bool Exists(string code) => code != null && _all.ContainsKey(code);

        public StatusEffect Get(string code) => code != null && _all.TryGetValue(code, out var se) ? se : null;

        public string DisplayName(string code) => _allowed.FirstOrDefault(e => e.Code == code)?.DisplayName;
    }
}
