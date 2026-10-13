using LoadoutBuffs;

internal static partial class Tests
{
    // ---- missing hooks: Woodcutter / Miner, the Parry bonus tooltip line, the window's problem line --------------

    private const string ClassHookWarning =
        "The buff's Woodcutter / Miner may not work (the class hooks aren't all installed, see LogOutput.log).";

    private const string TooltipHookWarning =
        "The item tooltip's Parry bonus doesn't include the buff's (the tooltip hook isn't installed, see LogOutput.log); " +
        "your parries still get it.";

    private static void Test_HookWarnings_ClassAndTooltip()
    {
        var wood = new StatBlock();
        wood.Scalars["woodcutter"] = 1;
        var miner = new StatBlock();
        miner.Scalars["miner"] = 1;
        var parry = new StatBlock();
        parry.Scalars["parryBonus"] = 2;

        var w = BundleRules.MissingHookWarnings(new[] { wood, parry }, true, true, true, true, true, classHook: false, tooltipHook: false);
        Eq(2, w.Count, "warnings: " + string.Join(" | ", w));
        Eq(ClassHookWarning, w[0], "class hooks");
        Eq(TooltipHookWarning, w[1], "tooltip hook");

        var minerOnly = BundleRules.MissingHookWarnings(new[] { miner }, true, true, true, true, true, classHook: false, tooltipHook: false);
        True(minerOnly.Count == 1 && minerOnly[0] == ClassHookWarning, "Miner alone: " + string.Join(" | ", minerOnly));

        Eq(0, BundleRules.MissingHookWarnings(new[] { wood, miner, parry }, true, true, true, true, true).Count, "hooks installed (the default)");
        var other = new StatBlock();
        other.Scalars["carryWeight"] = 50;
        Eq(0, BundleRules.MissingHookWarnings(new[] { other }, true, true, true, true, true, classHook: false, tooltipHook: false).Count,
            "no stat needs those hooks");
    }

    private static void Test_HookWarnings_CountedApartFromTheFile()
    {
        var state = Evaluate("active: T\nbuffs:\n  T:\n    cape: NoSuchEffect\n");
        Eq(1, state.Warnings.Count, "warnings: " + string.Join(" | ", state.Warnings));
        Eq(1, state.FileWarnings, "the file's own problem");
        state.AddHookWarnings(new[] { ClassHookWarning, TooltipHookWarning });
        Eq(3, state.Warnings.Count, "all warnings stay in one list (log, lb_buffs)");
        Eq(1, state.FileWarnings, "hook warnings aren't the file's");
        Eq(ClassHookWarning, state.Warnings[1], "hook warnings come after the file's");
        Eq(0, new BundleState().FileWarnings, "none");
    }

    private static void Test_HookWarnings_ProblemsLine()
    {
        Eq("1 problem in the buffs file: X", BundleWindowRules.ProblemsLine(1, 1, "X"), "one, the file's");
        Eq("2 problems in the buffs file: X", BundleWindowRules.ProblemsLine(2, 2, "X"), "two, the file's");
        Eq("1 problem: X", BundleWindowRules.ProblemsLine(1, 0, "X"), "one, a hook's");
        Eq("2 problems: X", BundleWindowRules.ProblemsLine(2, 0, "X"), "two, hooks'");
        Eq("3 problems (1 in the buffs file): X", BundleWindowRules.ProblemsLine(3, 1, "X"), "both kinds");
    }
}
