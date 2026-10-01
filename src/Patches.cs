using System;
using HarmonyLib;

namespace LoadoutBuffs
{
    /// <summary>
    /// Main menu: FejdStartup.Start → AddComponent&lt;ObjectDB&gt;() (Awake, skipped) → CopyOtherDB (filled),
    /// then the character list and preview. In game: ObjectDB.Awake on the scene's ObjectDB, before the
    /// player spawns. Priority.Last so item-registering mods (Jotunn) run first.
    /// Nothing may escape a patch (it would break game startup), including type-initializer failures.
    /// </summary>
    [HarmonyPatch]
    internal static class Patches
    {
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void ObjectDBAwake(ObjectDB __instance) =>
            Guard("ObjectDB.Awake", () =>
            {
                // The main menu's DB (FejdStartup.SetupObjectDB) is only complete after CopyOtherDB.
                // At Awake it isn't necessarily empty: item mods (e.g. BruteWeapons) add theirs here.
                if (__instance.GetComponent<FejdStartup>() != null) return;
                GameGlue.OnObjectDBReady(__instance, "ObjectDB.Awake");
            });

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void ObjectDBCopyOtherDB(ObjectDB __instance) =>
            Guard("ObjectDB.CopyOtherDB", () => GameGlue.OnObjectDBReady(__instance, "ObjectDB.CopyOtherDB"));

        private static void Guard(string where, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{where} postfix failed: {e}");
            }
        }
    }
}
