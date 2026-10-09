using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LoadoutBuffs
{
    internal enum BundleSlot
    {
        Helmet,
        Chest,
        Legs,
        Cape,
        Melee,
        Ranged,
        Shield,
    }

    internal static class BundleSlots
    {
        public static readonly BundleSlot[] All = (BundleSlot[])Enum.GetValues(typeof(BundleSlot));

        /// <summary>The key in the bundles file: <c>helmet</c>, <c>chest</c>, …</summary>
        public static string Key(BundleSlot slot) => slot.ToString().ToLowerInvariant();

        /// <summary>Slots of earlier versions, with what to use instead; null for any other key.</summary>
        public static string RemovedSlotHint(string key)
        {
            switch (key?.Trim().ToLowerInvariant())
            {
                case "weapon": return "the weapon slot was split: use melee or ranged";
                case "utility":
                case "trinket": return $"the {key.Trim().ToLowerInvariant()} slot was removed (those items already have their own effects)";
                default: return null;
            }
        }

        private static readonly HashSet<Skills.SkillType> s_ranged = new HashSet<Skills.SkillType>
        {
            Skills.SkillType.Bows, Skills.SkillType.Crossbows, Skills.SkillType.ElementalMagic, Skills.SkillType.BloodMagic,
        };

        private static readonly HashSet<Skills.SkillType> s_melee = new HashSet<Skills.SkillType>
        {
            Skills.SkillType.Swords, Skills.SkillType.Knives, Skills.SkillType.Clubs, Skills.SkillType.Polearms,
            Skills.SkillType.Spears, Skills.SkillType.Axes, Skills.SkillType.Unarmed,
        };

        /// <summary>
        /// Which hand slot an item in the hands fills, by its weapon skill (the item type can't tell: staffs,
        /// atgeirs, pickaxes and the fishing rod are all two-handed weapons, crossbows are bows). Ranged: bows,
        /// crossbows, magic staffs. Melee: swords, knives, clubs, atgeirs, spears, axes, fists. Shields: shield.
        /// Pickaxes, the fishing rod, torches and tools fill none, and so does a weapon you use on your own
        /// (<paramref name="usedOnAllies"/>: Staff of Protection and Northern Vengeance, whose spell hits only you, players
        /// and tamed; the Abyssal Harpoon, which drags tamed animals): its hit would carry the slot's Added damage to them.
        /// </summary>
        public static BundleSlot? HandSlot(ItemDrop.ItemData.ItemType type, Skills.SkillType skill, bool usedOnAllies)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Shield:
                    return BundleSlot.Shield;
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                    if (usedOnAllies) return null;
                    if (s_ranged.Contains(skill)) return BundleSlot.Ranged;
                    if (s_melee.Contains(skill)) return BundleSlot.Melee;
                    return null;
                default:
                    return null;
            }
        }

        public static bool TryParse(string key, out BundleSlot slot)
        {
            slot = default;
            if (string.IsNullOrEmpty(key)) return false;
            foreach (var s in All)
            {
                if (!string.Equals(Key(s), key.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                slot = s;
                return true;
            }
            return false;
        }
    }

    /// <summary>An effect bundles may use: code name (the StatusEffect asset name) and in-game name.</summary>
    internal sealed class EffectInfo
    {
        public string Code;
        public string DisplayName;
    }

    /// <summary>What the game offers, as plain strings (the tests fake it).</summary>
    internal interface IEffectCatalog
    {
        /// <summary>Effects bundles may use, i.e. effects gear has in vanilla.</summary>
        IReadOnlyList<EffectInfo> Allowed { get; }

        /// <summary>Any status effect the game knows, allowed or not (for clearer errors).</summary>
        bool Exists(string code);
    }

    /// <summary>The outcome of a bundle pass.</summary>
    internal sealed class BundleState
    {
        /// <summary>Bundles may apply (the file is readable and the worn-effects hook is installed).</summary>
        public bool Enabled = true;

        /// <summary>Why bundles are off; null while enabled.</summary>
        public string OffReason;

        /// <summary>The active bundle; null for none (or bundles off).</summary>
        public BundleDef Active;

        /// <summary>The active bundle's usable effects (code names) per slot.</summary>
        public readonly Dictionary<BundleSlot, string> Effects = new Dictionary<BundleSlot, string>();

        /// <summary>The active bundle's custom stats per slot (validated when the file was read).</summary>
        public readonly Dictionary<BundleSlot, StatBlock> Stats = new Dictionary<BundleSlot, StatBlock>();

        /// <summary>Distinct stats the active bundle sets, over all its slots.</summary>
        public int StatCount => StatBlock.Sum(Stats.Values).Count;

        public readonly List<string> Warnings = new List<string>();

        public string StatusLine()
        {
            if (!Enabled) return "Buffs off: " + OffReason;
            if (Active == null) return "No buff active";
            var stats = StatCount;
            return $"Buff '{Active.Name}' active: {Effects.Count} effect{(Effects.Count == 1 ? "" : "s")}" +
                   (stats > 0 ? $", {stats} stat{(stats == 1 ? "" : "s")}" : "");
        }
    }

    internal static class BundleRules
    {
        /// <summary>The HUD shows the buff in use (icon + name; "N stats" only with custom stats) whenever one is in use.</summary>
        public static bool ShowsHudBuff(BundleState state) => state != null && state.Enabled && state.Active != null;

        /// <summary>
        /// Warnings for stats whose game hook isn't installed (game update, another mod): the window and the log say why
        /// they do nothing instead of the stat silently not working.
        /// </summary>
        public static List<string> MissingHookWarnings(IEnumerable<StatBlock> stats, bool parryHook, bool blockHook, bool damageHook,
            bool eitrHook)
        {
            var list = stats.Where(b => b != null).ToList();
            var warnings = new List<string>();
            if (!parryHook && list.Any(b => b.ToParryAssist() != null))
                warnings.Add("The buff's on-parry stats can't work with this game version (the parry hook isn't installed, see LogOutput.log).");
            if (!blockHook && list.Any(b => b.BlockArmor != 0f || b.BlockForce != 0f))
                warnings.Add("The buff's block armor / force can't work (the block hook isn't installed, see LogOutput.log).");
            if (!damageHook && list.Any(b => b.AddDamage.Count > 0))
                warnings.Add("The buff's added damage can't work (the added damage hook isn't installed, see LogOutput.log).");
            if (!eitrHook && list.Any(b => b.EitrCost != 0f))
                warnings.Add("The buff's eitr cost can't work (the eitr hook isn't installed, see LogOutput.log).");
            return warnings;
        }

        /// <summary>
        /// Code name for what the file says: a code name or an in-game name, both ignoring case.
        /// Only allowed effects resolve; the error explains why anything else doesn't.
        /// </summary>
        public static string Resolve(string text, IEffectCatalog catalog, out string error)
        {
            error = null;
            var t = text?.Trim() ?? "";
            var byCode = catalog.Allowed.FirstOrDefault(e => string.Equals(e.Code, t, StringComparison.Ordinal))
                         ?? catalog.Allowed.FirstOrDefault(e => string.Equals(e.Code, t, StringComparison.OrdinalIgnoreCase));
            if (byCode != null) return byCode.Code;

            var byName = catalog.Allowed.Where(e => string.Equals(e.DisplayName, t, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 1) return byName[0].Code;
            if (byName.Count > 1)
            {
                error = $"'{t}' is the in-game name of several effects; use one of their code names: {string.Join(", ", byName.Select(e => e.Code))}";
                return null;
            }
            error = catalog.Exists(t)
                ? $"'{t}' can't be used in a buff: only effects that gear has in the game can (the Buffs window lists them)"
                : $"unknown effect '{t}'; use a code name or in-game name from the Buffs window's list";
            return null;
        }

        /// <summary>
        /// Checks every bundle (so mistakes show up before a bundle is used) and resolves the active
        /// bundle's effects. The same effect twice in one bundle: the later slot is skipped.
        /// </summary>
        public static BundleState Evaluate(BundleFile file, IEffectCatalog catalog)
        {
            var state = new BundleState();
            file = file ?? new BundleFile();
            state.Warnings.AddRange(file.Warnings);

            var resolved = new Dictionary<BundleDef, Dictionary<BundleSlot, string>>();
            foreach (var bundle in file.Bundles)
                resolved[bundle] = ResolveBundle(bundle, catalog, state.Warnings);

            if (file.Invalid)
            {
                state.Enabled = false;
                state.OffReason = $"{BundleFile.FileName} is not valid YAML (see the warning).";
                return state;
            }

            state.Active = file.Find(file.Active);
            if (state.Active == null) return state;
            foreach (var pair in resolved[state.Active])
                state.Effects[pair.Key] = pair.Value;
            foreach (var entry in state.Active.Entries)
                if (entry.Stats != null && !entry.Stats.IsEmpty)
                    state.Stats[entry.Slot] = entry.Stats;
            return state;
        }

        /// <summary>Slot → code name for the entries that resolve; warnings for the rest.</summary>
        public static Dictionary<BundleSlot, string> ResolveBundle(BundleDef bundle, IEffectCatalog catalog, List<string> warnings)
        {
            var result = new Dictionary<BundleSlot, string>();
            var usedBy = new Dictionary<string, BundleSlot>(StringComparer.Ordinal);
            foreach (var entry in bundle.Entries)
            {
                if (entry.Effect == null) continue; // a stats-only slot
                var label = $"{bundle.Name}.{BundleSlots.Key(entry.Slot)}";
                var line = entry.Line > 0 ? $" (line {entry.Line})" : "";
                var code = Resolve(entry.Effect, catalog, out var error);
                if (code == null)
                {
                    warnings?.Add($"{label}: {error}; skipped{line}.");
                    continue;
                }
                if (usedBy.TryGetValue(code, out var first))
                {
                    warnings?.Add($"{label}: {code} is already in the {BundleSlots.Key(first)} slot, and an effect only counts once; skipped{line}.");
                    continue;
                }
                usedBy[code] = entry.Slot;
                result[entry.Slot] = code;
            }
            return result;
        }
    }

    /// <summary>What the Bundles window shows for the gear in hand (kept Unity-free for the tests).</summary>
    internal static class BundleWindowRules
    {
        /// <summary>A − / + click moves one step; with Shift held, ten (up to the window's limit).</summary>
        public static float ClickStep(float step, bool shift) => shift ? step * 10f : step;

        /// <summary>
        /// Damage % rows for the weapon in hand: the types it deals (its own damage plus what bundles add) and any type
        /// that already has a % set, since stats belong to the slot, not to one weapon. A % of a type the weapon doesn't
        /// deal multiplies 0. No weapon known (null): every type.
        /// </summary>
        public static List<string> DamagePercentRows(IReadOnlyDictionary<string, float> dealt, IReadOnlyDictionary<string, float> set)
        {
            if (dealt == null) return BundleStatCatalog.DamageTypes.ToList();
            return BundleStatCatalog.DamageTypes
                .Where(t =>
                {
                    var key = t.ToLowerInvariant();
                    return (dealt.TryGetValue(key, out var d) && d > 0f) || (set != null && set.ContainsKey(key));
                })
                .ToList();
        }

        /// <summary>"has 90": the note on an added-damage row, what the weapon in hand already deals of that type (null: none).</summary>
        public static string AddedDamageNote(float own) =>
            own > 0f ? "has " + own.ToString("0.##", CultureInfo.InvariantCulture) : null;

        /// <summary>In-game names of the bundle's slot effects whose slot is filled now, in slot order (unresolvable ones left out).</summary>
        public static List<string> WornEffectNames(BundleDef bundle, IEffectCatalog catalog, Func<BundleSlot, bool> isWorn)
        {
            if (bundle == null || catalog == null) return new List<string>();
            return BundleRules.ResolveBundle(bundle, catalog, null)
                .Where(p => isWorn(p.Key))
                .OrderBy(p => p.Key)
                .Select(p => catalog.Allowed.FirstOrDefault(e => e.Code == p.Value)?.DisplayName ?? p.Value)
                .ToList();
        }
    }
}
