using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace LoadoutBuffs
{
    /// <summary>One slot of a bundle as written in the file (the effect is resolved later, against the game).</summary>
    internal sealed class BundleEntry
    {
        public BundleSlot Slot;

        /// <summary>Code name or in-game name, as written; null when the slot only has stats.</summary>
        public string Effect;

        /// <summary>Custom stats; null or empty for none. An entry always has an effect or stats.</summary>
        public StatBlock Stats;

        public long Line;
    }

    internal sealed class BundleDef
    {
        public string Name;
        public long Line;

        /// <summary>In file order, one per slot.</summary>
        public readonly List<BundleEntry> Entries = new List<BundleEntry>();

        public string Get(BundleSlot slot) => Entries.FirstOrDefault(e => e.Slot == slot)?.Effect;

        public StatBlock GetStats(BundleSlot slot) => Entries.FirstOrDefault(e => e.Slot == slot)?.Stats;

        /// <summary>Sets or clears (null/empty) a slot's effect, keeping its stats; used by the window.</summary>
        public void Set(BundleSlot slot, string effect) => Update(slot, string.IsNullOrEmpty(effect) ? null : effect, GetStats(slot));

        /// <summary>Sets or clears (null/empty) a slot's stats, keeping its effect; used by the window.</summary>
        public void SetStats(BundleSlot slot, StatBlock stats) => Update(slot, Get(slot), stats);

        private void Update(BundleSlot slot, string effect, StatBlock stats)
        {
            var entry = Entries.FirstOrDefault(e => e.Slot == slot);
            if (effect == null && (stats == null || stats.IsEmpty))
            {
                if (entry != null) Entries.Remove(entry);
                return;
            }
            if (entry == null) Entries.Add(entry = new BundleEntry { Slot = slot });
            entry.Effect = effect;
            entry.Stats = stats == null || stats.IsEmpty ? null : stats;
        }
    }

    /// <summary>
    /// LoadoutBuffs.bundles.yaml: the bundles plus which one is active (for the whole profile).
    /// Parsing checks structure and names; effects are checked against the game by <see cref="BundleRules"/>.
    /// </summary>
    internal sealed class BundleFile
    {
        public const string FileName = "LoadoutBuffs.bundles.yaml";

        /// <summary>Name of the active bundle as written; null for none.</summary>
        public string Active;

        public readonly List<BundleDef> Bundles = new List<BundleDef>();
        public readonly List<string> Warnings = new List<string>();

        /// <summary>The file isn't valid YAML: nothing in it is used.</summary>
        public bool Invalid;

        public BundleDef Find(string name) =>
            name == null ? null : Bundles.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

        public static BundleFile Parse(string text)
        {
            var file = new BundleFile();
            if (string.IsNullOrWhiteSpace(text)) return file;
            text = text.Replace(' ', ' ').Replace(' ', ' ').Replace(' ', ' ');

            var stream = new YamlStream();
            try
            {
                stream.Load(new StringReader(text));
            }
            catch (YamlException e)
            {
                file.Invalid = true;
                file.Warnings.Add($"{FileName} is not valid YAML (around line {e.Start.Line}, column {e.Start.Column}), " +
                                  $"so no bundle is used: {e.Message}");
                return file;
            }

            if (stream.Documents.Count == 0) return file;
            var root = stream.Documents[0].RootNode;
            if (IsNull(root)) return file;
            if (!(root is YamlMappingNode map))
            {
                file.Warnings.Add($"{FileName}: expected 'active:' and 'bundles:' (line {root.Start.Line}).");
                return file;
            }

            foreach (var pair in map.Children)
            {
                var key = Scalar(pair.Key);
                switch (key?.ToLowerInvariant())
                {
                    case "active":
                        file.ParseActive(pair.Value);
                        break;
                    case "bundles":
                        file.ParseBundles(pair.Value);
                        break;
                    default:
                        file.Warnings.Add($"{FileName}: unknown key '{key}', skipped (line {pair.Key.Start.Line}). Use 'active' and 'bundles'.");
                        break;
                }
            }

            if (file.Active != null && file.Find(file.Active) == null)
                file.Warnings.Add($"{FileName}: active bundle '{file.Active}' doesn't exist, so no bundle is used. " +
                                  $"Bundles: {(file.Bundles.Count == 0 ? "none" : string.Join(", ", file.Bundles.Select(b => b.Name)))}.");
            return file;
        }

        private void ParseActive(YamlNode node)
        {
            if (IsNull(node) || IsNone(node)) return;
            if (node is YamlScalarNode scalar)
            {
                Active = scalar.Value.Trim();
                return;
            }
            Warnings.Add($"{FileName}: 'active' must be a bundle name or none (line {node.Start.Line}).");
        }

        private void ParseBundles(YamlNode node)
        {
            if (IsNull(node)) return;
            if (!(node is YamlMappingNode map))
            {
                Warnings.Add($"{FileName}: 'bundles' must be a list of named bundles like 'Warrior: {{ chest: Vanguard }}' (line {node.Start.Line}).");
                return;
            }
            foreach (var pair in map.Children)
            {
                var name = Scalar(pair.Key)?.Trim();
                var line = pair.Key.Start.Line;
                if (string.IsNullOrEmpty(name))
                {
                    Warnings.Add($"{FileName}: expected a bundle name (line {line}).");
                    continue;
                }
                if (Find(name) != null)
                {
                    Warnings.Add($"{FileName}: bundle '{name}' appears twice (names ignore upper/lower case); the second one is skipped (line {line}).");
                    continue;
                }
                var bundle = new BundleDef { Name = name, Line = line };
                Bundles.Add(bundle);
                if (IsNull(pair.Value)) continue;
                if (!(pair.Value is YamlMappingNode slots))
                {
                    Warnings.Add($"{name}: expected slots like 'chest: Vanguard' (line {line}).");
                    continue;
                }
                foreach (var slotPair in slots.Children)
                    ParseSlot(bundle, slotPair.Key, slotPair.Value);
            }
        }

        private void ParseSlot(BundleDef bundle, YamlNode keyNode, YamlNode valueNode)
        {
            var key = Scalar(keyNode);
            var line = keyNode.Start.Line;
            if (!BundleSlots.TryParse(key, out var slot))
            {
                var hint = BundleSlots.RemovedSlotHint(key);
                Warnings.Add($"{bundle.Name}.{key}: " + (hint != null ? hint + ", so it's skipped" : "unknown slot, skipped") +
                             $" (line {line}). Slots: {string.Join(", ", BundleSlots.All.Select(BundleSlots.Key))}.");
                return;
            }
            var label = $"{bundle.Name}.{BundleSlots.Key(slot)}";
            if (IsNull(valueNode) || IsNone(valueNode)) return;
            if (valueNode is YamlScalarNode scalar)
            {
                bundle.Entries.Add(new BundleEntry { Slot = slot, Effect = scalar.Value.Trim(), Line = line });
                return;
            }
            if (!(valueNode is YamlMappingNode map))
            {
                Warnings.Add($"{label}: expected an effect name, or 'effect:' and 'stats:'; skipped (line {line}).");
                return;
            }

            // Long form: { effect: Vanguard, stats: { armor: 15 } }
            var entry = new BundleEntry { Slot = slot, Line = line };
            foreach (var pair in map.Children)
            {
                var part = Scalar(pair.Key)?.Trim();
                switch (part?.ToLowerInvariant())
                {
                    case "effect":
                        if (IsNull(pair.Value) || IsNone(pair.Value)) break;
                        if (pair.Value is YamlScalarNode effect) entry.Effect = effect.Value.Trim();
                        else Warnings.Add($"{label}.effect: expected one effect name; skipped (line {pair.Key.Start.Line}).");
                        break;
                    case "stats":
                        if (IsNull(pair.Value)) break;
                        if (pair.Value is YamlMappingNode stats) entry.Stats = StatBlock.Parse(stats, label, Warnings);
                        else Warnings.Add($"{label}.stats: expected stats like {{ armor: 15 }}; skipped (line {pair.Key.Start.Line}).");
                        break;
                    default:
                        Warnings.Add($"{label}.{part}: unknown key, skipped (line {pair.Key.Start.Line}). Use 'effect' and 'stats'.");
                        break;
                }
            }
            if (entry.Stats != null && entry.Stats.IsEmpty) entry.Stats = null;
            if (entry.Effect != null || entry.Stats != null) bundle.Entries.Add(entry);
        }

        /// <summary>
        /// The whole file, as the window saves it: code names, with the in-game name as a comment.
        /// <paramref name="displayName"/> maps a code name to its in-game name (null when unknown).
        /// </summary>
        public string Serialize(Func<string, string> displayName)
        {
            var sb = new StringBuilder();
            foreach (var line in Header) sb.Append(line).Append('\n');
            sb.Append('\n');
            sb.Append("active: ").Append(Active == null ? "none" : Values.Quote(Active)).Append('\n');
            sb.Append('\n');
            if (Bundles.Count == 0)
            {
                sb.Append("bundles: {}\n");
                return sb.ToString();
            }
            sb.Append("bundles:\n");
            foreach (var bundle in Bundles)
            {
                if (bundle.Entries.Count == 0)
                {
                    sb.Append("  ").Append(Values.Quote(bundle.Name)).Append(": {}\n");
                    continue;
                }
                sb.Append("  ").Append(Values.Quote(bundle.Name)).Append(":\n");
                foreach (var slot in BundleSlots.All)
                {
                    var effect = bundle.Get(slot);
                    var stats = bundle.GetStats(slot);
                    if (effect == null && stats == null) continue;
                    var name = effect == null ? null : displayName?.Invoke(effect);
                    var comment = !string.IsNullOrEmpty(name) && name != effect ? name.Replace('\n', ' ') : null;
                    if (stats == null)
                    {
                        sb.Append(WithComment($"    {BundleSlots.Key(slot)}: {Values.Quote(effect)}", comment)).Append('\n');
                        continue;
                    }
                    sb.Append($"    {BundleSlots.Key(slot)}:\n");
                    if (effect != null) sb.Append(WithComment($"      effect: {Values.Quote(effect)}", comment)).Append('\n');
                    sb.Append($"      stats: {stats.Serialize()}\n");
                }
            }
            return sb.ToString();
        }

        private static string WithComment(string line, string comment) =>
            comment == null ? line : line.PadRight(46) + "  # " + comment;

        public static readonly string[] Header =
        {
            "# LoadoutBuffs: extra \"while worn\" effects and stats, per equipment slot.",
            "#",
            "# Pick and edit bundles in game: inventory -> Bundles (or the console command lb_bundles).",
            "# Or edit this file and run lb_reload in the F5 console. The Bundles window rewrites this",
            "# file, so comments you add here are not kept.",
            "#",
            "# active: the bundle in use for every character in this profile, or none.",
            "# Slots: helmet, chest, legs, cape, melee (swords, knives, clubs, atgeirs, spears, axes, fists),",
            "# ranged (bows, crossbows, magic staffs), shield. A slot counts while something is equipped there",
            "# (weapons: in your hands); your gear keeps its own effects. Pickaxes, the fishing rod, torches and",
            "# tools fill no slot. Effect: code name or in-game name of an effect gear has in the game (the",
            "# Bundles window lists them).",
            "# A slot can also have custom stats (long form):",
            "#   chest:",
            "#     effect: SetEffect_DeepNorthMediumArmor",
            "#     stats: { movementSpeed: 10, armor: 15, resist: { Fire: Resistant }, damage: { slash: 10 }, skills: { Bows: 15 } }",
            "# Stats: movementSpeed, carryWeight, armor, blockArmor, blockForce (what you block with), fallDamage,",
            "# healthRegen, staminaRegen, eitrRegen, runStamina, jumpStamina, attackStamina, blockStamina,",
            "# dodgeStamina (percent stats in percent: 10 = +10 %), resist, damage (%), addDamage (flat, on the",
            "# weapon you attack with, e.g. { spirit: 30 }), skills, fields (raw SE_Stats fields). On every parry:",
            "# healOnParry, staminaOnParry (you), healAlliesOnParry, staminaAlliesOnParry (other players),",
            "# healTamedOnParry, shieldOnParry (a Magic barrier absorbing that much, for all of you), shieldMinutes",
            "# (its time, default 1), parryRadius (reach in metres, default 10). Classes: woodcutter: true (melee",
            "# hits fell any tree), miner: true (melee hits break any rock or ore). The stats of all filled slots",
            "# add up (reach and bubble time take the largest) and show as one buff named after the bundle.",
        };

        private static string Scalar(YamlNode node) => (node as YamlScalarNode)?.Value;

        private static bool IsNull(YamlNode node)
        {
            if (!(node is YamlScalarNode s) || s.Style != ScalarStyle.Plain) return false;
            var v = s.Value;
            return v == null || v.Length == 0 || v == "~" || string.Equals(v, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNone(YamlNode node) =>
            node is YamlScalarNode s && s.Style == ScalarStyle.Plain && string.Equals(s.Value?.Trim(), "none", StringComparison.OrdinalIgnoreCase);
    }
}
