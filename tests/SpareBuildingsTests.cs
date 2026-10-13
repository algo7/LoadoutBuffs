using System;
using System.Linq;
using LoadoutBuffs;

internal static partial class Tests
{
    // ---- buff switch: Spare buildings ---------------------------------------------------------

    private static void Test_Spare_ParseAndSerialize()
    {
        var file = BundleFile.Parse(
            "active: T\nbuffs:\n  T:\n    spareBuildings: true\n    cape: SlowFall\n" +
            "  U:\n    SpareBuildings: off\n  V:\n    spareBuildings: yes\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Find("T").SpareBuildings, "T: on");
        Eq(1, file.Find("T").Entries.Count, "T: the switch isn't a slot");
        Eq("SlowFall", file.Find("T").Get(BundleSlot.Cape), "T keeps its cape");
        False(file.Find("U").SpareBuildings, "U: off (any case of the key)");
        True(file.Find("V").SpareBuildings, "V: yes = on");
        Eq(0, file.Find("V").Entries.Count, "V: only the switch");

        var saved = file.Serialize(null);
        True(saved.Contains("  T:\n    spareBuildings: true\n    cape: SlowFall\n"), saved);
        True(saved.Contains("  U: {}\n"), "off isn't written: " + saved);
        True(saved.Contains("  V:\n    spareBuildings: true\n"), "a buff with only the switch: " + saved);
        var again = BundleFile.Parse(saved);
        Eq(0, again.Warnings.Count, "re-parse warnings: " + string.Join(" | ", again.Warnings));
        Eq(saved, again.Serialize(null), "stable");
        True(again.Find("V").SpareBuildings, "V still on");
    }

    private static void Test_Spare_BadValueAndTwice()
    {
        var bad = BundleFile.Parse("buffs:\n  T:\n    spareBuildings: maybe\n    cape: SlowFall\n  U:\n    spareBuildings: { a: 1 }\n");
        Eq(2, bad.Warnings.Count, "warnings: " + string.Join(" | ", bad.Warnings));
        Eq("T.spareBuildings: 'maybe' is not true or false; skipped (line 3).", bad.Warnings[0], "scalar");
        Eq("U.spareBuildings: expected true or false; skipped (line 6).", bad.Warnings[1], "not a scalar");
        False(bad.Find("T").SpareBuildings || bad.Find("U").SpareBuildings, "a bad value is off");
        Eq("SlowFall", bad.Find("T").Get(BundleSlot.Cape), "the slots still load");
        False(bad.OnlyMisplaced, "a bad value blocks the cleanup (its save would drop the line)");

        var twice = BundleFile.Parse("buffs:\n  T:\n    spareBuildings: true\n    SPAREBUILDINGS: false\n");
        Eq(1, twice.Warnings.Count, "warnings: " + string.Join(" | ", twice.Warnings));
        Eq("T.spareBuildings: appears twice (keys ignore upper/lower case); the second one is skipped (line 4).", twice.Warnings[0], "twice");
        True(twice.Find("T").SpareBuildings, "the first one counts");
    }

    private static void Test_Spare_CleanupKeepsTheSwitch()
    {
        var file = BundleFile.Parse("buffs:\n  T:\n    spareBuildings: true\n    helmet:\n      stats: { bleeding: true }\n");
        True(file.OnlyMisplaced, "only a misplaced stat: the mod saves the file once");
        var again = BundleFile.Parse(file.Serialize(null));
        Eq(0, again.Warnings.Count, "warnings: " + string.Join(" | ", again.Warnings));
        True(again.Find("T").SpareBuildings, "the cleanup save keeps the switch");
    }

    private static void Test_Spare_Rule()
    {
        True(BundleRules.SparesBuilding(true, true, 123456789L), "your hit on a player-built piece");
        True(BundleRules.SparesBuilding(true, true, -42L), "player IDs can be negative");
        False(BundleRules.SparesBuilding(true, true, 0L), "a fort or ruin (no builder) still breaks");
        False(BundleRules.SparesBuilding(false, true, 123456789L), "switch off");
        False(BundleRules.SparesBuilding(true, false, 123456789L), "someone else's hit (or fire, a falling tree)");
    }

    private static void Test_Spare_StatusLine()
    {
        var state = Evaluate("active: T\nbuffs:\n  T:\n    spareBuildings: true\n    cape: SlowFall\n");
        Eq(0, state.Warnings.Count, "warnings: " + string.Join(" | ", state.Warnings));
        True(state.Active.SpareBuildings, "the active buff carries the switch");
        Eq("Buff 'T' active: 1 effect, spares buildings", state.StatusLine(), "status");
        var stats = Evaluate("active: T\nbuffs:\n  T:\n    spareBuildings: true\n    cape:\n      stats: { carryWeight: 50 }\n");
        Eq("Buff 'T' active: 0 effects, 1 stat, spares buildings", stats.StatusLine(), "with stats");
        Eq("Buff 'T' active: 1 effect", Evaluate("active: T\nbuffs:\n  T:\n    cape: SlowFall\n").StatusLine(), "off: not named");
    }

    private static void Test_Spare_MissingHookWarning()
    {
        var none = new StatBlock[0];
        var w = BundleRules.MissingHookWarnings(none, true, true, true, true, true, spareBuildings: true, buildingHook: false);
        Eq(1, w.Count, "warnings: " + string.Join(" | ", w));
        Eq("The buff's Spare buildings can't work (the building hook isn't installed, see LogOutput.log).", w[0], "text");
        Eq(0, BundleRules.MissingHookWarnings(none, true, true, true, true, true, spareBuildings: true, buildingHook: true).Count, "hook in");
        Eq(0, BundleRules.MissingHookWarnings(none, true, true, true, true, true, spareBuildings: false, buildingHook: false).Count, "switch off");
    }

    private static void Test_Spare_HeaderAndWindowTexts()
    {
        var header = string.Join("\n", BundleFile.Header);
        True(header.Contains("spareBuildings: true"), "the buffs file header explains the switch");
        True(StarterBundles().StartsWith(header + "\n\n"), "the starter file has the same header");
        Eq("Spare buildings: Off", BundleWindowRules.SpareBuildingsButton(false), "button off");
        Eq("Spare buildings: <color=orange>On</color>", BundleWindowRules.SpareBuildingsButton(true), "button on");
        Eq("While this buff is in use, your hits don't damage anything players built.", BundleWindowRules.SpareBuildingsHint, "hint");
    }
}
