using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using YamlDotNet.RepresentationModel;

namespace LoadoutBuffs
{
    internal enum StatKind
    {
        /// <summary>Percent in the file, fraction in the field (10 → 0.1).</summary>
        Percent,

        /// <summary>Percent in the file, multiplier in the field (10 → 1.1).</summary>
        RegenPercent,

        /// <summary>Same number in the file and the field.</summary>
        Flat,

        /// <summary>An on-parry amount (no SE_Stats field): read by the parry hook, see <see cref="StatBlock.ToParryAssist"/>.</summary>
        Parry,

        /// <summary>On or off (<c>true</c> in the file, stored as 1): an item class, read by the chop/mine hook.</summary>
        Toggle,

        /// <summary>A flat amount for whatever you block with (no SE_Stats field): added by the block hook, see <see cref="BundleStatCatalog.WithBlockArmor"/>.</summary>
        Blocker,
    }

    /// <summary>A scalar stat of the curated set: file key, window label and step, and the SE_Stats field it sets.</summary>
    internal sealed class StatDef
    {
        public string Key;
        public string Label;
        public string Group;
        public string Field;
        public StatKind Kind;
        public float Step;
        public float Min;
        public float Max;

        /// <summary>Value when not set (the window steps from here; the file never stores it).</summary>
        public float Default;

        /// <summary>Several slots: the largest value instead of the sum (a reach, not an amount).</summary>
        public bool TakesLargest;

        /// <summary>Shown after the number, e.g. " m".</summary>
        public string Unit = "";

        /// <summary>A cost or a penalty: a negative value helps (stamina costs, fall damage).</summary>
        public bool LowerIsBetter;

        /// <summary>Whether a value helps the player (the window shows it green) or hurts (red).</summary>
        public bool Helps(float value) => LowerIsBetter ? value < 0f : value > 0f;

        public bool IsPercent => Kind == StatKind.Percent || Kind == StatKind.RegenPercent;
    }

    /// <summary>The curated stats and the names the nested groups accept.</summary>
    internal static class BundleStatCatalog
    {
        public const string General = "General";
        public const string Regen = "Regen";
        public const string Stamina = "Stamina costs";
        public const string OnParry = "On parry";
        public const string Class = "Class";
        public const string WoodcutterKey = "woodcutter";
        public const string MinerKey = "miner";
        public const string Resistances = "Resistances";
        public const string Damage = "Damage %";
        public const string Skills = "Skills";

        public const string ResistKey = "resist";
        public const string DamageKey = "damage";
        public const string SkillsKey = "skills";
        public const string FieldsKey = "fields";

        public static readonly StatDef[] Scalars =
        {
            Def("movementSpeed", "Movement speed", General, "m_speedModifier", StatKind.Percent, 5, -50, 100),
            Def("carryWeight", "Carry weight", General, "m_addMaxCarryWeight", StatKind.Flat, 25, -100, 500),
            Def("armor", "Armor", General, "m_addArmor", StatKind.Flat, 5, -50, 200),
            Def(BlockArmorKey, "Block armor", General, null, StatKind.Blocker, 5, -50, 200),
            Def(BlockForceKey, "Block force", General, null, StatKind.Blocker, 5, -50, 300),
            Cost("fallDamage", "Fall damage", General, "m_fallDamageModifier", 10, -100, 100),
            Def("healthRegen", "Health regen", Regen, "m_healthRegenMultiplier", StatKind.RegenPercent, 10, -50, 300),
            Def("staminaRegen", "Stamina regen", Regen, "m_staminaRegenMultiplier", StatKind.RegenPercent, 10, -50, 300),
            Def("eitrRegen", "Eitr regen", Regen, "m_eitrRegenMultiplier", StatKind.RegenPercent, 10, -50, 300),
            Cost("runStamina", "Run stamina cost", Stamina, "m_runStaminaDrainModifier", 5, -100, 100),
            Cost("jumpStamina", "Jump stamina cost", Stamina, "m_jumpStaminaUseModifier", 5, -100, 100),
            Cost("attackStamina", "Attack stamina cost", Stamina, "m_attackStaminaUseModifier", 5, -100, 100),
            Cost("blockStamina", "Block stamina cost", Stamina, "m_blockStaminaUseModifier", 5, -100, 100),
            Cost("dodgeStamina", "Dodge stamina cost", Stamina, "m_dodgeStaminaUseModifier", 5, -100, 100),
            Def(Catalog.HealOnParry, "Heal you", OnParry, null, StatKind.Parry, 10, 0, 300),
            Def(Catalog.StaminaOnParry, "Stamina to you", OnParry, null, StatKind.Parry, 10, 0, 300),
            Def(Catalog.HealAlliesOnParry, "Heal allies", OnParry, null, StatKind.Parry, 10, 0, 300),
            Def(Catalog.StaminaAlliesOnParry, "Stamina to allies", OnParry, null, StatKind.Parry, 10, 0, 300),
            Def(Catalog.HealTamedOnParry, "Heal tamed", OnParry, null, StatKind.Parry, 10, 0, 300),
            Def(Catalog.ShieldOnParry, "Bubble", OnParry, null, StatKind.Parry, 100, 0, 3000),
            new StatDef
            {
                Key = Catalog.ShieldMinutes, Label = "Bubble time", Group = OnParry, Kind = StatKind.Parry, Step = 1, Min = 1, Max = 10,
                Default = 1, TakesLargest = true, Unit = " min",
            },
            new StatDef
            {
                Key = ParryRadiusKey, Label = "Reach", Group = OnParry, Kind = StatKind.Parry, Step = 5, Min = 5, Max = 50,
                Default = Catalog.DefaultHealAlliesRadius, TakesLargest = true, Unit = " m",
            },
        };

        public const string ParryRadiusKey = "parryRadius";
        public const string BlockArmorKey = "blockArmor";
        public const string BlockForceKey = "blockForce";

        /// <summary>Block force (the push on a blocked melee attacker) with the bundle's bonus; never below 0 (no division by it).</summary>
        public static float WithBlockForce(float baseBlockForce, float bonus) => Math.Max(0f, baseBlockForce + bonus);

        /// <summary>
        /// Block armor with the bundle's bonus, added to the item's base value like its own blockPower. Never below 1 (or the
        /// item's own value if that's lower): Humanoid.BlockAttack divides by block armor, and 0 turns stamina into NaN.
        /// </summary>
        public static float WithBlockArmor(float baseBlockArmor, float bonus) =>
            Math.Max(Math.Min(baseBlockArmor, 1f), baseBlockArmor + bonus);

        /// <summary>Classes: melee hits fell trees (Woodcutter) or break rocks and ore (Miner).</summary>
        public static readonly StatDef[] Toggles =
        {
            new StatDef { Key = WoodcutterKey, Label = "Woodcutter", Group = Class, Kind = StatKind.Toggle, Step = 1, Min = 0, Max = 1, TakesLargest = true },
            new StatDef { Key = MinerKey, Label = "Miner", Group = Class, Kind = StatKind.Toggle, Step = 1, Min = 0, Max = 1, TakesLargest = true },
        };

        public const float DamageStep = 5, DamageMin = -50, DamageMax = 200;

        public const string AddedDamage = "Added damage";
        public const string AddDamageKey = "addDamage";
        public const float AddDamageStep = 5, AddDamageMin = 0, AddDamageMax = 300;

        /// <summary>Damage types a bundle can add to your weapon (no chop / pickaxe: Woodcutter / Miner cover those).</summary>
        public static readonly string[] AddDamageTypes = { "Blunt", "Slash", "Pierce", "Fire", "Frost", "Lightning", "Poison", "Spirit" };
        public const float SkillStep = 5, SkillMin = -50, SkillMax = 100;

        /// <summary>HitData.DamageType names that take a resistance or a damage %.</summary>
        public static readonly string[] DamageTypes = Catalog.ModifierTypes;

        /// <summary>Resistance settings in the window's cycle order (the file also accepts Ignore).</summary>
        public static readonly string[] ModifierCycle =
        {
            "Normal", "SlightlyResistant", "Resistant", "VeryResistant", "Immune", "SlightlyWeak", "Weak", "VeryWeak",
        };

        /// <summary>Most protective first: which of two resistances for one type wins.</summary>
        private static readonly string[] s_protection =
        {
            "Immune", "Ignore", "VeryResistant", "Resistant", "SlightlyResistant", "Normal", "SlightlyWeak", "Weak", "VeryWeak",
        };

        public static int Protection(string modifier)
        {
            var i = Array.IndexOf(s_protection, modifier);
            return i < 0 ? int.MaxValue : i;
        }

        /// <summary>Skills.SkillType names bundles accept (All too; not None).</summary>
        public static readonly string[] SkillNames = Enum.GetNames(typeof(global::Skills.SkillType))
            .Where(n => n != nameof(global::Skills.SkillType.None) && n != "Step")
            .ToArray();

        /// <summary>The skills the window lists (not All).</summary>
        public static readonly string[] WindowSkills = SkillNames.Where(n => n != nameof(global::Skills.SkillType.All)).ToArray();

        public static StatDef Find(string key) =>
            Scalars.Concat(Toggles).FirstOrDefault(d => string.Equals(d.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static string ModifierLabel(string modifier)
        {
            switch (modifier)
            {
                case "SlightlyResistant": return "Slightly resistant";
                case "VeryResistant": return "Very resistant";
                case "SlightlyWeak": return "Slightly weak";
                case "VeryWeak": return "Very weak";
                default: return modifier;
            }
        }

        /// <summary>SE_Stats fields <c>fields:</c> may set: numbers, bools and enums the game declares on SE_Stats.</summary>
        public static FieldInfo RawField(string name, out string error)
        {
            error = null;
            var field = typeof(SE_Stats).GetField(name?.Trim() ?? "", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                error = $"'{name}' is not a field of SE_Stats";
                return null;
            }
            if (field.DeclaringType != typeof(SE_Stats))
            {
                error = $"{field.Name} can't be set from a bundle (only SE_Stats' own stat fields can)";
                return null;
            }
            var t = field.FieldType;
            if (t == typeof(float) || t == typeof(int) || t == typeof(bool) || t.IsEnum) return field;
            error = $"{name} ({t.Name}) can't be set from a bundle";
            return null;
        }

        private static StatDef Def(string key, string label, string group, string field, StatKind kind, float step, float min, float max) =>
            new StatDef { Key = key, Label = label, Group = group, Field = field, Kind = kind, Step = step, Min = min, Max = max };

        private static StatDef Cost(string key, string label, string group, string field, float step, float min, float max)
        {
            var def = Def(key, label, group, field, StatKind.Percent, step, min, max);
            def.LowerIsBetter = true;
            return def;
        }
    }

    /// <summary>
    /// Custom stats of one bundle slot (or the sum of several). Values are in file units: percent for percent
    /// stats, plain numbers otherwise. Names are canonical (as in <see cref="BundleStatCatalog"/>).
    /// </summary>
    internal sealed class StatBlock
    {
        public readonly Dictionary<string, float> Scalars = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Resist = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Damage = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Skills = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>Flat damage for the weapon you attack with: lowercase type → amount (> 0).</summary>
        public readonly Dictionary<string, float> AddDamage = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>Raw SE_Stats fields; a sum keeps every slot's value (combined against the field's default in game).</summary>
        public readonly List<KeyValuePair<string, string>> Fields = new List<KeyValuePair<string, string>>();

        /// <summary>The block armor bonus (0 when not set).</summary>
        public float BlockArmor => Scalars.TryGetValue(BundleStatCatalog.BlockArmorKey, out var v) ? v : 0f;

        /// <summary>The block force bonus (0 when not set).</summary>
        public float BlockForce => Scalars.TryGetValue(BundleStatCatalog.BlockForceKey, out var v) ? v : 0f;

        public int Count => Scalars.Count + Resist.Count + Damage.Count + AddDamage.Count + Skills.Count + Fields.Select(f => f.Key).Distinct().Count();
        public bool IsEmpty => Count == 0;

        public StatBlock Clone()
        {
            var copy = new StatBlock();
            foreach (var p in Scalars) copy.Scalars[p.Key] = p.Value;
            foreach (var p in Resist) copy.Resist[p.Key] = p.Value;
            foreach (var p in Damage) copy.Damage[p.Key] = p.Value;
            foreach (var p in Skills) copy.Skills[p.Key] = p.Value;
            foreach (var p in AddDamage) copy.AddDamage[p.Key] = p.Value;
            copy.Fields.AddRange(Fields);
            return copy;
        }

        /// <summary>The on-parry part, or null when there is none. Allies, tamed creatures and the bubble share one reach (default 10 m).</summary>
        public ParryAssist ToParryAssist()
        {
            float Get(string key) => Scalars.TryGetValue(key, out var v) ? v : 0f;
            var assist = new ParryAssist
            {
                Heal = Get(Catalog.HealOnParry),
                Stamina = Get(Catalog.StaminaOnParry),
                HealAllies = Get(Catalog.HealAlliesOnParry),
                StaminaAllies = Get(Catalog.StaminaAlliesOnParry),
                HealTamed = Get(Catalog.HealTamedOnParry),
                Shield = Get(Catalog.ShieldOnParry),
            };
            if (Scalars.TryGetValue(BundleStatCatalog.ParryRadiusKey, out var radius)) assist.Radius = radius;
            if (Scalars.TryGetValue(Catalog.ShieldMinutes, out var minutes)) assist.ShieldMinutes = minutes;
            assist.TamedRadius = assist.Radius;
            return assist.DoesSomething ? assist : null;
        }

        /// <summary>Sets a scalar/damage/skill value; 0 removes it.</summary>
        public static void SetNumber(Dictionary<string, float> into, string key, float value)
        {
            if (Math.Abs(value) < 0.0001f) into.Remove(key);
            else into[key] = value;
        }

        public void SetResist(string type, string modifier)
        {
            if (modifier == null || modifier == "Normal") Resist.Remove(type);
            else Resist[type] = modifier;
        }

        /// <summary>Stats of several slots together: numbers add, the most protective resistance wins.</summary>
        public static StatBlock Sum(IEnumerable<StatBlock> blocks)
        {
            var total = new StatBlock();
            foreach (var block in blocks)
            {
                if (block == null) continue;
                foreach (var p in block.Scalars)
                {
                    var largest = BundleStatCatalog.Find(p.Key)?.TakesLargest == true;
                    SetNumber(total.Scalars, p.Key, !total.Scalars.TryGetValue(p.Key, out var v) ? p.Value : largest ? Math.Max(v, p.Value) : v + p.Value);
                }
                foreach (var p in block.Damage) SetNumber(total.Damage, p.Key, total.Damage.TryGetValue(p.Key, out var v) ? v + p.Value : p.Value);
                foreach (var p in block.Skills) SetNumber(total.Skills, p.Key, total.Skills.TryGetValue(p.Key, out var v) ? v + p.Value : p.Value);
                foreach (var p in block.AddDamage) SetNumber(total.AddDamage, p.Key, total.AddDamage.TryGetValue(p.Key, out var v) ? v + p.Value : p.Value);
                foreach (var p in block.Resist)
                    if (!total.Resist.TryGetValue(p.Key, out var current) ||
                        BundleStatCatalog.Protection(p.Value) < BundleStatCatalog.Protection(current))
                        total.Resist[p.Key] = p.Value;
                total.Fields.AddRange(block.Fields);
            }
            return total;
        }

        /// <summary>
        /// One compact line for people, grouped: "+15% movement speed, +15 armor; stamina cost: run -10%; on parry:
        /// heal allies +40, reach 15 m; class: Woodcutter; resist: Fire Resistant; damage: slash +10%; skills: Bows +15".
        /// </summary>
        public string Summary()
        {
            var groups = new List<string>();
            void Group(string title, IEnumerable<string> items)
            {
                var list = items.ToList();
                if (list.Count > 0) groups.Add(title == null ? string.Join(", ", list) : $"{title}: {string.Join(", ", list)}");
            }

            string Value(StatDef def, float v) =>
                def.TakesLargest ? $"{Plain(v)}{def.Unit}" : $"{Number(v)}{(def.IsPercent ? "%" : "")}";

            var scalars = BundleStatCatalog.Scalars.Where(d => Scalars.ContainsKey(d.Key)).ToList();
            Group(null, scalars.Where(d => d.Group == BundleStatCatalog.General || d.Group == BundleStatCatalog.Regen)
                .Select(d => $"{Value(d, Scalars[d.Key])} {d.Label.ToLowerInvariant()}"));
            Group("stamina cost", scalars.Where(d => d.Group == BundleStatCatalog.Stamina)
                .Select(d => $"{d.Label.Split(' ')[0].ToLowerInvariant()} {Value(d, Scalars[d.Key])}"));
            Group("on parry", scalars.Where(d => d.Group == BundleStatCatalog.OnParry)
                .Select(d => $"{d.Label.ToLowerInvariant()} {Value(d, Scalars[d.Key])}"));
            Group("class", BundleStatCatalog.Toggles.Where(d => Scalars.ContainsKey(d.Key)).Select(d => d.Label));
            Group("resist", BundleStatCatalog.DamageTypes.Where(Resist.ContainsKey).Select(t => $"{t} {BundleStatCatalog.ModifierLabel(Resist[t])}"));
            Group("damage", BundleStatCatalog.DamageTypes.Select(t => t.ToLowerInvariant()).Where(Damage.ContainsKey).Select(t => $"{t} {Number(Damage[t])}%"));
            Group("added", AddedTypes().Select(t => $"{t} {Number(AddDamage[t])}"));
            Group("skills", Skills.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} {Number(p.Value)}"));
            Group("fields", Fields.Select(f => f.Key).Distinct());
            return groups.Count == 0 ? "none" : string.Join("; ", groups);
        }

        /// <summary>Flow mapping for the bundles file, e.g. <c>{ movementSpeed: 10, resist: { Fire: Resistant } }</c>.</summary>
        public string Serialize()
        {
            var parts = new List<string>();
            foreach (var def in BundleStatCatalog.Scalars)
                if (Scalars.TryGetValue(def.Key, out var v)) parts.Add($"{def.Key}: {Plain(v)}");
            foreach (var def in BundleStatCatalog.Toggles)
                if (Scalars.ContainsKey(def.Key)) parts.Add($"{def.Key}: true");
            if (Resist.Count > 0)
                parts.Add($"{BundleStatCatalog.ResistKey}: {{ " +
                          string.Join(", ", BundleStatCatalog.DamageTypes.Where(Resist.ContainsKey).Select(t => $"{t}: {Resist[t]}")) + " }");
            if (Damage.Count > 0)
                parts.Add($"{BundleStatCatalog.DamageKey}: {{ " +
                          string.Join(", ", BundleStatCatalog.DamageTypes.Select(t => t.ToLowerInvariant()).Where(Damage.ContainsKey).Select(t => $"{t}: {Plain(Damage[t])}")) + " }");
            if (AddDamage.Count > 0)
                parts.Add($"{BundleStatCatalog.AddDamageKey}: {{ " + string.Join(", ", AddedTypes().Select(t => $"{t}: {Plain(AddDamage[t])}")) + " }");
            if (Skills.Count > 0)
                parts.Add($"{BundleStatCatalog.SkillsKey}: {{ " +
                          string.Join(", ", Skills.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}: {Plain(p.Value)}")) + " }");
            if (Fields.Count > 0)
                parts.Add($"{BundleStatCatalog.FieldsKey}: {{ " + string.Join(", ", Fields.Select(p => $"{p.Key}: {Values.Quote(p.Value)}")) + " }");
            return parts.Count == 0 ? "{}" : "{ " + string.Join(", ", parts) + " }";
        }

        /// <summary>Reads a <c>stats:</c> mapping; problems become warnings (prefixed with <paramref name="label"/>) and are skipped.</summary>
        public static StatBlock Parse(YamlMappingNode map, string label, List<string> warnings)
        {
            var block = new StatBlock();
            foreach (var pair in map.Children)
            {
                var key = (pair.Key as YamlScalarNode)?.Value?.Trim() ?? "";
                var line = pair.Key.Start.Line;
                var def = BundleStatCatalog.Find(key);
                if (def != null && def.Kind == StatKind.Toggle)
                {
                    var text = (pair.Value as YamlScalarNode)?.Value?.Trim().ToLowerInvariant();
                    if (text == "true" || text == "yes" || text == "on" || text == "1") block.Scalars[def.Key] = 1f;
                    else if (text == "false" || text == "no" || text == "off" || text == "0") block.Scalars.Remove(def.Key);
                    else warnings.Add($"{label}.{def.Key}: '{text}' is not true or false; skipped (line {line}).");
                    continue;
                }
                if (def != null)
                {
                    if (!TryNumber(pair.Value, out var v, out var error)) warnings.Add($"{label}.{def.Key}: {error}; skipped (line {line}).");
                    else if (def.Kind == StatKind.Parry && v <= 0f) // a negative amount would cancel another slot's
                        warnings.Add($"{label}.{def.Key}: '{(pair.Value as YamlScalarNode)?.Value}' must be more than 0; skipped (line {line}).");
                    else SetNumber(block.Scalars, def.Key, v);
                    continue;
                }
                switch (key.ToLowerInvariant())
                {
                    case BundleStatCatalog.ResistKey:
                        foreach (var (name, value, l) in Entries(pair.Value, $"{label}.{BundleStatCatalog.ResistKey}", "{ Fire: Resistant }", warnings))
                        {
                            var type = Catalog.MatchIgnoreCase(BundleStatCatalog.DamageTypes, name);
                            var modifier = Catalog.MatchIgnoreCase(Enum.GetNames(typeof(HitData.DamageModifier)), value?.Replace(" ", ""));
                            if (type == null)
                                warnings.Add($"{label}.{BundleStatCatalog.ResistKey}.{name}: unknown damage type, skipped (line {l}). Use: {string.Join(", ", BundleStatCatalog.DamageTypes)}.");
                            else if (modifier == null)
                                warnings.Add($"{label}.{BundleStatCatalog.ResistKey}.{type}: '{value}' is not one of: {string.Join(", ", Enum.GetNames(typeof(HitData.DamageModifier)))}; skipped (line {l}).");
                            else block.SetResist(type, modifier);
                        }
                        break;
                    case BundleStatCatalog.DamageKey:
                        foreach (var (name, value, l) in Entries(pair.Value, $"{label}.{BundleStatCatalog.DamageKey}", "{ slash: 10 }", warnings))
                        {
                            var type = Catalog.MatchIgnoreCase(BundleStatCatalog.DamageTypes, name);
                            if (type == null)
                                warnings.Add($"{label}.{BundleStatCatalog.DamageKey}.{name}: unknown damage type, skipped (line {l}). Use: {string.Join(", ", BundleStatCatalog.DamageTypes.Select(t => t.ToLowerInvariant()))}.");
                            else if (TryNumber(value, out var v, out var error)) SetNumber(block.Damage, type.ToLowerInvariant(), v);
                            else warnings.Add($"{label}.{BundleStatCatalog.DamageKey}.{type.ToLowerInvariant()}: {error}; skipped (line {l}).");
                        }
                        break;
                    case "adddamage": // BundleStatCatalog.AddDamageKey (keys are compared lowercased)
                        foreach (var (name, value, l) in Entries(pair.Value, $"{label}.{BundleStatCatalog.AddDamageKey}", "{ spirit: 30 }", warnings))
                        {
                            var type = Catalog.MatchIgnoreCase(BundleStatCatalog.AddDamageTypes, name)?.ToLowerInvariant();
                            var where = $"{label}.{BundleStatCatalog.AddDamageKey}.{type ?? name}";
                            if (type == null)
                                warnings.Add($"{where}: unknown or unsupported damage type, skipped (line {l}). Use: " +
                                             string.Join(", ", BundleStatCatalog.AddDamageTypes.Select(t => t.ToLowerInvariant())) + ".");
                            else if (!TryNumber(value, out var v, out var error)) warnings.Add($"{where}: {error}; skipped (line {l}).");
                            else if (v <= 0f) warnings.Add($"{where}: '{value}' must be more than 0; skipped (line {l}).");
                            else SetNumber(block.AddDamage, type, v);
                        }
                        break;
                    case BundleStatCatalog.SkillsKey:
                        foreach (var (name, value, l) in Entries(pair.Value, $"{label}.{BundleStatCatalog.SkillsKey}", "{ Bows: 15 }", warnings))
                        {
                            var skill = Catalog.MatchIgnoreCase(BundleStatCatalog.SkillNames, name);
                            if (skill == null)
                                warnings.Add($"{label}.{BundleStatCatalog.SkillsKey}.{name}: unknown skill, skipped (line {l}). Skills: {string.Join(", ", BundleStatCatalog.SkillNames)}.");
                            else if (TryNumber(value, out var v, out var error)) SetNumber(block.Skills, skill, v);
                            else warnings.Add($"{label}.{BundleStatCatalog.SkillsKey}.{skill}: {error}; skipped (line {l}).");
                        }
                        break;
                    case BundleStatCatalog.FieldsKey:
                        foreach (var (name, value, l) in Entries(pair.Value, $"{label}.{BundleStatCatalog.FieldsKey}", "{ m_swimSpeedModifier: 0.2 }", warnings))
                        {
                            var field = BundleStatCatalog.RawField(name, out var error);
                            if (field == null)
                                warnings.Add($"{label}.{BundleStatCatalog.FieldsKey}.{name}: {error}; skipped (line {l}).");
                            else if (!Values.TryParse(value, field.FieldType, out _, out error))
                                warnings.Add($"{label}.{BundleStatCatalog.FieldsKey}.{field.Name}: {error}; skipped (line {l}).");
                            else block.Fields.Add(new KeyValuePair<string, string>(field.Name, value.Trim()));
                        }
                        break;
                    default:
                        warnings.Add($"{label}.{key}: unknown stat, skipped (line {line}). Stats: " +
                                     string.Join(", ", BundleStatCatalog.Scalars.Concat(BundleStatCatalog.Toggles).Select(d => d.Key)) +
                                     $", {BundleStatCatalog.ResistKey}, {BundleStatCatalog.DamageKey}, {BundleStatCatalog.AddDamageKey}, {BundleStatCatalog.SkillsKey}, {BundleStatCatalog.FieldsKey}.");
                        break;
                }
            }
            return block;
        }

        private static IEnumerable<(string name, string value, long line)> Entries(YamlNode node, string label, string example, List<string> warnings)
        {
            if (!(node is YamlMappingNode map))
            {
                warnings.Add($"{label}: expected a mapping like {example}; skipped (line {node.Start.Line}).");
                yield break;
            }
            foreach (var pair in map.Children)
            {
                var name = (pair.Key as YamlScalarNode)?.Value ?? "";
                if (pair.Value is YamlScalarNode scalar) yield return (name, scalar.Value, pair.Key.Start.Line);
                else warnings.Add($"{label}.{name}: expected a single value; skipped (line {pair.Key.Start.Line}).");
            }
        }

        private static bool TryNumber(YamlNode node, out float value, out string error)
        {
            value = 0;
            if (!(node is YamlScalarNode scalar))
            {
                error = "expected a number";
                return false;
            }
            return TryNumber(scalar.Value, out value, out error);
        }

        /// <summary>Accepts <c>10</c>, <c>+10</c>, <c>-5.5</c>, <c>10%</c>.</summary>
        public static bool TryNumber(string text, out float value, out string error)
        {
            error = null;
            value = 0;
            var s = (text ?? "").Trim();
            if (s.EndsWith("%", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1).Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
                !double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e6)
            {
                value = (float)d;
                return true;
            }
            error = $"'{text}' is not a number";
            return false;
        }

        /// <summary>The added-damage types this block holds, lowercase, in the catalog's order.</summary>
        private IEnumerable<string> AddedTypes() =>
            BundleStatCatalog.AddDamageTypes.Select(t => t.ToLowerInvariant()).Where(AddDamage.ContainsKey);

        /// <summary>A weapon's damage with flat amounts added (lowercase type → amount); other types unchanged.</summary>
        public static HitData.DamageTypes AddDamageTo(HitData.DamageTypes damages, IReadOnlyDictionary<string, float> add)
        {
            foreach (var p in add)
                switch (p.Key)
                {
                    case "blunt": damages.m_blunt += p.Value; break;
                    case "slash": damages.m_slash += p.Value; break;
                    case "pierce": damages.m_pierce += p.Value; break;
                    case "fire": damages.m_fire += p.Value; break;
                    case "frost": damages.m_frost += p.Value; break;
                    case "lightning": damages.m_lightning += p.Value; break;
                    case "poison": damages.m_poison += p.Value; break;
                    case "spirit": damages.m_spirit += p.Value; break;
                }
            return damages;
        }

        /// <summary>Signed, for people: +10, -5, +2.5.</summary>
        public static string Number(float v) => v.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);

        private static string Plain(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
