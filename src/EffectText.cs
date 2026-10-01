using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LoadoutBuffs
{
    /// <summary>Effect names and descriptions as the game shows them (Bundles window, lb_stats).</summary>
    internal static class EffectText
    {
        private static readonly Regex s_richText = new Regex("<[^>]+>");

        /// <summary>
        /// The effect's own tooltip text (what the Active effects page shows), localized, without color tags.
        /// <paramref name="statsOnly"/> drops the flavour text line (the effect's m_tooltip) and keeps the numbers.
        /// </summary>
        internal static IEnumerable<string> Describe(StatusEffect se, bool statsOnly = false)
        {
            string text;
            try
            {
                text = se.GetTooltipString();
                text = Localization.instance?.Localize(text) ?? text;
            }
            catch (Exception e)
            {
                return new[] { $"(no description: {e.GetType().Name})" };
            }
            var lines = s_richText.Replace(text ?? "", "")
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
            if (statsOnly && !string.IsNullOrEmpty(se.m_tooltip) && lines.Count > 0) lines.RemoveAt(0);
            return lines;
        }

        /// <summary>English name for a $token when localization is up, else the token itself.</summary>
        public static string DisplayName(string token)
        {
            if (string.IsNullOrEmpty(token)) return "(no name)";
            try
            {
                var localized = Localization.instance?.Localize(token);
                return string.IsNullOrEmpty(localized) ? token : localized;
            }
            catch
            {
                return token;
            }
        }
    }
}
