using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LoadoutBuffs
{
    /// <summary>Names bundles share: on-parry stat keys, damage and resistance type names.</summary>
    internal static class Catalog
    {
        public const string HealOnParry = "healOnParry";
        public const string HealAlliesOnParry = "healAlliesOnParry";
        public const string HealTamedOnParry = "healTamedOnParry";
        public const string StaminaOnParry = "staminaOnParry";
        public const string StaminaAlliesOnParry = "staminaAlliesOnParry";
        public const string ShieldOnParry = "shieldOnParry";
        public const string ShieldMinutes = "shieldMinutes";

        /// <summary>Metres around the parrying player that on-parry help reaches when no reach is set.</summary>
        public const float DefaultHealAlliesRadius = 10f;

        /// <summary><c>HitData.DamageTypes</c> fields (without <c>m_</c>), in display order.</summary>
        public static readonly string[] DamageKeys =
            { "damage", "blunt", "slash", "pierce", "chop", "pickaxe", "fire", "frost", "lightning", "poison", "spirit" };

        /// <summary><c>HitData.DamageType</c> names that take a resistance. The composite flags (Physical, Elemental…) aren't per-type modifiers.</summary>
        public static readonly string[] ModifierTypes =
            { "Blunt", "Slash", "Pierce", "Chop", "Pickaxe", "Fire", "Frost", "Lightning", "Poison", "Spirit" };

        public static string MatchIgnoreCase(IEnumerable<string> names, string value) =>
            names.FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parsing raw values (bundle <c>fields:</c>) and quoting text for the bundles file.</summary>
    internal static class Values
    {
        public static bool TryParse(string raw, Type type, out object value, out string error)
        {
            value = null;
            error = null;
            if (raw == null)
            {
                error = "missing value";
                return false;
            }
            if (type == typeof(string))
            {
                value = raw;
                return true;
            }

            var s = raw.Trim();
            if (type == typeof(bool))
            {
                switch (s.ToLowerInvariant())
                {
                    case "true": case "yes": case "on": value = true; return true;
                    case "false": case "no": case "off": value = false; return true;
                }
                error = $"'{raw}' is not true or false";
                return false;
            }
            if (type.IsEnum)
            {
                var name = Catalog.MatchIgnoreCase(Enum.GetNames(type), s);
                if (name == null)
                {
                    error = $"'{raw}' is not one of: {string.Join(", ", Enum.GetNames(type))}";
                    return false;
                }
                value = Enum.Parse(type, name);
                return true;
            }
            if (type == typeof(float) || type == typeof(double))
            {
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ||
                    double.IsNaN(d) || double.IsInfinity(d) || (type == typeof(float) && Math.Abs(d) > float.MaxValue))
                {
                    error = $"'{raw}' is not a number";
                    return false;
                }
                value = type == typeof(float) ? (object)(float)d : d;
                return true;
            }
            if (IsInteger(type))
            {
                if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                {
                    error = $"'{raw}' is not a whole number";
                    return false;
                }
                try
                {
                    value = Convert.ChangeType(l, type, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (OverflowException)
                {
                    error = $"{raw} is out of range for {type.Name}";
                    return false;
                }
            }
            error = $"fields of type {type.Name} can't be set";
            return false;
        }

        /// <summary>YAML-safe scalar: plain when unambiguous, double-quoted otherwise.</summary>
        public static string Quote(string s)
        {
            if (s.Length > 0 && IsPlainSafe(s)) return s;
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c < ' ') sb.Append("\\x").Append(((int)c).ToString("x2"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        private static bool IsPlainSafe(string s)
        {
            if (!(char.IsLetter(s[0]) || s[0] == '_' || s[0] == '$')) return false;
            if (s.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '.' || c == '-' || c == '(' || c == ')'))) return false;
            switch (s.ToLowerInvariant())
            {
                case "null": case "true": case "false": case "yes": case "no": case "on": case "off": case "none":
                    return false;
            }
            return true;
        }

        private static bool IsInteger(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
            t == typeof(sbyte) || t == typeof(ushort) || t == typeof(uint) || t == typeof(ulong);
    }
}
