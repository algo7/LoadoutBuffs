using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LoadoutBuffs
{
    /// <summary>
    /// Runtime glue between the game and bundles: a bundle pass whenever an ObjectDB is ready (main menu, every world
    /// load) and on lb_reload, and refreshing the local player's worn effects.
    /// </summary>
    internal static class GameGlue
    {
        private static readonly HashSet<string> s_loggedWarnings = new HashSet<string>();
        private static readonly MethodInfo s_updateEquipmentStatusEffects = AccessTools.Method(typeof(Humanoid), "UpdateEquipmentStatusEffects");

        public static bool IsReady(ObjectDB db) => db != null && db.m_items != null && db.m_items.Count > 0;

        /// <summary>Postfix target for ObjectDB.Awake / CopyOtherDB (the patch catches exceptions).</summary>
        public static void OnObjectDBReady(ObjectDB db, string source)
        {
            if (!IsReady(db)) return;
            LogIdentity(db, source);
            ApplyBundles(db, logAllWarnings: false);
        }

        /// <summary>lb_reload: re-read the bundles file, then make the local player's worn effects pick it up.</summary>
        public static BundleState Reload(ObjectDB db)
        {
            var state = ApplyBundles(db, logAllWarnings: true);
            RefreshEquipmentEffects(Player.m_localPlayer);
            return state;
        }

        /// <summary>Bundles never break the game: on an unexpected error they're simply off.</summary>
        private static BundleState ApplyBundles(ObjectDB db, bool logAllWarnings)
        {
            BundleState state;
            try
            {
                state = BundleEffects.Apply(db);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Buffs failed to load: {e}");
                state = new BundleState { Enabled = false, OffReason = "error while loading them (see LogOutput.log)." };
            }
            if (logAllWarnings) s_loggedWarnings.Clear();
            // Each ObjectDB (menu, then every world load) runs a pass; don't repeat identical warnings.
            foreach (var warning in state.Warnings)
                if (s_loggedWarnings.Add(warning))
                    Plugin.Log.LogWarning(warning);
            Plugin.Log.LogInfo(state.StatusLine());
            return state;
        }

        /// <summary>Recompute worn effects (item effects and, through the bundle hook, bundle effects).</summary>
        public static void RefreshEquipmentEffects(Player player)
        {
            if (player == null) return;
            // Same guard as vanilla Humanoid.SetupEquipment: status effects need the player's ZDO.
            var nview = player.GetComponent<ZNetView>();
            if (nview != null && nview.GetZDO() != null)
                CallWithDefaults(s_updateEquipmentStatusEffects, player);
        }

        /// <summary>
        /// ObjectDB.m_StatusEffects plus effects only referenced by items (not every equip/set/parry effect is registered
        /// in ObjectDB). Keyed by asset name, which is what ObjectDB.GetStatusEffect hashes.
        /// </summary>
        public static Dictionary<string, StatusEffect> CollectStatusEffects(ObjectDB db)
        {
            var result = new Dictionary<string, StatusEffect>(StringComparer.Ordinal);
            foreach (var se in db.m_StatusEffects)
                AddEffect(result, se);
            foreach (var go in db.m_items)
            {
                if (go == null) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null) continue;
                var shared = drop.m_itemData.m_shared;
                AddEffect(result, shared.m_equipStatusEffect);
                AddEffect(result, shared.m_setStatusEffect);
                AddEffect(result, shared.m_perfectBlockStatusEffect);
            }
            return result;
        }

        private static void AddEffect(Dictionary<string, StatusEffect> into, StatusEffect se)
        {
            if (se != null && !into.ContainsKey(se.name)) into[se.name] = se;
        }

        /// <summary>Diagnostic: the ObjectDB's size and the Wood item's SharedData identity (the same id in the menu and in a world means item prefabs survive the scene change).</summary>
        private static void LogIdentity(ObjectDB db, string source)
        {
            var wood = db.GetItemPrefab("Wood");
            var drop = wood != null ? wood.GetComponent<ItemDrop>() : null;
            var id = drop != null ? RuntimeHelpers.GetHashCode(drop.m_itemData.m_shared).ToString("X8") : "n/a";
            Plugin.Log.LogInfo($"{source}: ObjectDB has {db.m_items.Count} items, {db.m_StatusEffects.Count} status effects (Wood SharedData #{id})");
        }

        private static void CallWithDefaults(MethodInfo method, object target)
        {
            if (method == null || target == null) return;
            var args = method.GetParameters()
                .Select(p => p.HasDefaultValue ? p.DefaultValue
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                .ToArray();
            method.Invoke(target, args);
        }
    }
}
