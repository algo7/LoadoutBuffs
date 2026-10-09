using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace LoadoutBuffs
{
    /// <summary>
    /// The bundle's custom stats as one buff: named after the bundle, the mod's icon, "N stats" under it.
    /// A single template (constant asset name, so the game keeps one instance). The hook writes the summed stats
    /// of the filled slots into the template and into the active instance (Clone is MemberwiseClone), so
    /// values change in place when gear changes, without the buff being removed and re-added.
    /// SE_Stats applies the standard fields itself; skills use an own table because SE_Stats has two skill slots.
    /// </summary>
    public sealed class BundleStatsEffect : SE_Stats
    {
        public const string AssetName = "LoadoutBuffs_BundleStats";

        public Dictionary<Skills.SkillType, float> m_bundleSkills = new Dictionary<Skills.SkillType, float>();
        public int m_statCount;

        /// <summary>The on-parry part of the totals (for the tooltip); the hook reads BundleEffects' copy.</summary>
        internal ParryAssist m_parry;

        /// <summary>"Woodcutter, Miner" (for the tooltip); the chop/mine hook reads BundleEffects' flags.</summary>
        internal string m_classes;

        /// <summary>"Wisplight, Megingjord": the buff's effects on the slots you fill now (for the tooltip).</summary>
        internal string m_effectNames;

        /// <summary>Block armor bonus (for the tooltip); the block hook reads BundleEffects' copy.</summary>
        internal float m_blockArmor;

        /// <summary>Block force bonus (for the tooltip); the block hook reads BundleEffects' copy.</summary>
        internal float m_blockForce;

        /// <summary>Eitr cost percent (for the tooltip); the eitr hook reads BundleEffects' copy.</summary>
        internal float m_eitrCost;

        /// <summary>Added damage (for the tooltip); the damage hook reads BundleEffects' copy.</summary>
        internal Dictionary<string, float> m_addDamage = new Dictionary<string, float>();

        /// <summary>Added to the parry multiplier of what you parry with, see <see cref="ModifyTimedBlockBonus"/>.</summary>
        internal float m_parryBonus;

        private static bool s_parryErrorLogged;

        private static BundleStatsEffect s_template;
        private static SE_Stats s_defaults;
        private static Sprite s_icon;
        private static readonly HashSet<string> s_rawFieldsSet = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The template the hook adds to the worn-effects set (created on first use).</summary>
        internal static BundleStatsEffect Template
        {
            get
            {
                if (s_template != null) return s_template;
                s_template = CreateInstance<BundleStatsEffect>();
                s_template.name = AssetName;
                s_template.m_name = "Buff";
                s_template.m_tooltip = "Your buff in use: its effects and custom stats from the slots you fill.";
                s_template.m_icon = Icon();
                s_template.m_ttl = 0f;
                s_template.hideFlags = HideFlags.HideAndDontSave; // runtime-only, survives scene changes
                return s_template;
            }
        }

        public override void ModifySkillLevel(Skills.SkillType skill, ref float value)
        {
            base.ModifySkillLevel(skill, ref value);
            if (m_bundleSkills == null) return;
            foreach (var pair in m_bundleSkills)
                if (pair.Key == skill || pair.Key == Skills.SkillType.All)
                    value += pair.Value;
        }

        /// <summary>
        /// Humanoid.BlockAttack calls this on a parry with block power × the parry multiplier of what you parry with: the
        /// buff adds its Parry bonus to that multiplier. Must never throw.
        /// </summary>
        public override void ModifyTimedBlockBonus(ref float timedBlockBonus)
        {
            base.ModifyTimedBlockBonus(ref timedBlockBonus);
            try
            {
                if (m_parryBonus <= 0f || !(m_character is Humanoid humanoid)) return;
                var blocker = humanoid.LeftItem ?? humanoid.GetCurrentWeapon(); // like Humanoid.GetCurrentBlocker
                var item = blocker?.m_shared.m_timedBlockBonus ?? 0f;
                if (item > 0f) timedBlockBonus *= BundleStatCatalog.WithParryBonus(item, m_parryBonus) / item;
            }
            catch (Exception e)
            {
                if (s_parryErrorLogged) return;
                s_parryErrorLogged = true;
                Plugin.Log.LogError($"Buff parry bonus failed (logged once): {e}");
            }
        }

        public override string GetIconText() =>
            m_statCount > 0 ? $"{m_statCount} stat{(m_statCount == 1 ? "" : "s")}" : "";

        public override string GetTooltipString()
        {
            var sb = new StringBuilder(base.GetTooltipString());
            if (!string.IsNullOrEmpty(m_effectNames))
                sb.AppendFormat("Effects: <color=orange>{0}</color>\n", m_effectNames);
            if (m_bundleSkills != null)
                foreach (var pair in m_bundleSkills.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
                    sb.AppendFormat("{0}: <color=orange>{1}</color>\n", SkillName(pair.Key), StatBlock.Number(pair.Value));
            if (m_parry != null)
                sb.AppendFormat("On parry: <color=orange>{0}</color>\n", StatsReport.DescribeParry(m_parry));
            if (m_addDamage != null && m_addDamage.Count > 0)
                sb.AppendFormat("Added damage: <color=orange>{0}</color> (the weapon you attack with)\n",
                    string.Join(", ", BundleStatCatalog.AddDamageTypes.Select(t => t.ToLowerInvariant()).Where(m_addDamage.ContainsKey)
                        .Select(t => $"{t} {StatBlock.Number(m_addDamage[t])}")));
            if (m_blockArmor != 0f)
                sb.AppendFormat("Block armor: <color=orange>{0}</color> (what you block with)\n", StatBlock.Number(m_blockArmor));
            if (m_blockForce != 0f)
                sb.AppendFormat("Block force: <color=orange>{0}</color> (what you block with)\n", StatBlock.Number(m_blockForce));
            if (m_eitrCost != 0f)
                sb.AppendFormat("Eitr cost: <color=orange>{0}%</color>\n", StatBlock.Number(m_eitrCost));
            if (m_parryBonus > 0f)
                sb.AppendFormat("Parry bonus: <color=orange>{0}</color> (added to what you parry with)\n", StatBlock.Number(m_parryBonus));
            if (!string.IsNullOrEmpty(m_classes))
                sb.AppendFormat("Class: <color=orange>{0}</color> (your weapon's hits)\n", m_classes);
            return sb.ToString();
        }

        /// <summary>Writes summed stats into this effect; every field not in <paramref name="total"/> goes back to its default.</summary>
        internal void SetTotals(StatBlock total, string bundleName, string effectNames)
        {
            m_name = bundleName;
            m_effectNames = effectNames;
            m_statCount = total.Count;

            m_parry = total.ToParryAssist();
            m_blockArmor = total.BlockArmor;
            m_blockForce = total.BlockForce;
            m_eitrCost = total.EitrCost;
            m_parryBonus = total.ParryBonus;
            m_addDamage = new Dictionary<string, float>(total.AddDamage);
            m_classes = string.Join(", ", BundleStatCatalog.Toggles.Where(d => total.Scalars.ContainsKey(d.Key)).Select(d => d.Label));
            foreach (var def in BundleStatCatalog.Scalars)
            {
                if (def.Field == null) continue; // on-parry amounts and block armor: read by hooks, not SE_Stats fields
                total.Scalars.TryGetValue(def.Key, out var v);
                float value;
                switch (def.Kind)
                {
                    case StatKind.Percent: value = v / 100f; break;
                    case StatKind.RegenPercent: value = 1f + v / 100f; break;
                    default: value = v; break;
                }
                SetField(def.Field, value);
            }

            m_mods = total.Resist
                .Select(p => new HitData.DamageModPair
                {
                    m_type = (HitData.DamageType)Enum.Parse(typeof(HitData.DamageType), p.Key),
                    m_modifier = (HitData.DamageModifier)Enum.Parse(typeof(HitData.DamageModifier), p.Value),
                })
                .ToList();

            object damage = new HitData.DamageTypes();
            foreach (var pair in total.Damage)
                typeof(HitData.DamageTypes).GetField("m_" + pair.Key)?.SetValue(damage, pair.Value / 100f);
            m_percentigeDamageModifiers = (HitData.DamageTypes)damage;

            m_bundleSkills = total.Skills.ToDictionary(p => (Skills.SkillType)Enum.Parse(typeof(Skills.SkillType), p.Key), p => p.Value);

            SetRawFields(total.Fields);
        }

        /// <summary>
        /// Raw fields: every slot's value counts against the field's default (a multiplier defaults to 1), so
        /// two slots with <c>m_x: 1.2</c> give 1.4, not 2.4. Bools and enums: the last slot wins.
        /// </summary>
        private void SetRawFields(List<KeyValuePair<string, string>> fields)
        {
            var defaults = Defaults();
            foreach (var name in s_rawFieldsSet) // undo the last pass first
            {
                var f = typeof(SE_Stats).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                f?.SetValue(this, f.GetValue(defaults));
            }
            foreach (var group in fields.GroupBy(p => p.Key))
            {
                var field = BundleStatCatalog.RawField(group.Key, out _);
                if (field == null) continue;
                var start = field.GetValue(defaults);
                object value = start;
                foreach (var pair in group)
                {
                    if (!Values.TryParse(pair.Value, field.FieldType, out var v, out _)) continue;
                    if (field.FieldType == typeof(float)) value = (float)value + ((float)v - (float)start);
                    else if (field.FieldType == typeof(int)) value = (int)value + ((int)v - (int)start);
                    else value = v;
                }
                field.SetValue(this, value);
                s_rawFieldsSet.Add(field.Name);
            }
        }

        private void SetField(string name, float value) =>
            typeof(SE_Stats).GetField(name)?.SetValue(this, value);

        private static SE_Stats Defaults()
        {
            if (s_defaults != null) return s_defaults;
            s_defaults = CreateInstance<SE_Stats>();
            s_defaults.hideFlags = HideFlags.HideAndDontSave;
            return s_defaults;
        }

        private static string SkillName(Skills.SkillType skill)
        {
            var token = "$skill_" + skill.ToString().ToLowerInvariant();
            var localized = Localization.instance?.Localize(token);
            return string.IsNullOrEmpty(localized) || localized.StartsWith("[", StringComparison.Ordinal) ? skill.ToString() : localized;
        }

        /// <summary>The package icon (shield with an up arrow), embedded in the DLL.</summary>
        private static Sprite Icon()
        {
            if (s_icon != null) return s_icon;
            try
            {
                using (var stream = typeof(BundleStatsEffect).Assembly.GetManifestResourceStream("LoadoutBuffs.icon.png"))
                {
                    var bytes = new byte[stream.Length];
                    var read = 0;
                    while (read < bytes.Length) read += stream.Read(bytes, read, bytes.Length - read);
                    var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    // ImageConversionModule targets netstandard 2.1, which a net48 build can't reference: call it by name.
                    var loadImage = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                        ?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                    if (loadImage == null || !(bool)loadImage.Invoke(null, new object[] { texture, bytes }))
                        throw new InvalidOperationException("ImageConversion.LoadImage unavailable or failed");
                    s_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    s_icon.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Buff stats icon could not be loaded, the buff shows no icon: {e.Message}");
            }
            return s_icon;
        }
    }
}
