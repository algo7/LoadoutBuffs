using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LoadoutBuffs;

internal static partial class Tests
{
    // ---- bundles: fixtures ----------------------------------------------------------------

    /// <summary>Some effects vanilla gear has, plus two that exist but aren't gear effects.</summary>
    private sealed class FakeCatalog : IEffectCatalog
    {
        private readonly List<EffectInfo> _allowed = new List<EffectInfo>
        {
            new EffectInfo { Code = "SetEffect_DeepNorthMediumArmor", DisplayName = "Vanguard" },
            new EffectInfo { Code = "SetEffect_AshlandsMediumArmor", DisplayName = "Ask's Endurance" },
            new EffectInfo { Code = "SetEffect_LoxArmor", DisplayName = "Boon of the Lox" },
            new EffectInfo { Code = "SetEffect_FenringArmor", DisplayName = "Fenris blessing" },
            new EffectInfo { Code = "SetEffect_RootArmor", DisplayName = "Improved archery" },
            new EffectInfo { Code = "SetEffect_TrollArmor", DisplayName = "Sneaky" },
            new EffectInfo { Code = "SetEffect_HarvesterArmor", DisplayName = "Harvester" },
            new EffectInfo { Code = "SetEffect_FishingHat", DisplayName = "Fishing Hat" },
            new EffectInfo { Code = "SetEffect_BerserkerArmor", DisplayName = "Berserk" },
            new EffectInfo { Code = "SlowFall", DisplayName = "Feather fall" },
            new EffectInfo { Code = "WindRun", DisplayName = "Wind Run" },
            new EffectInfo { Code = "BeltStrength", DisplayName = "Megingjord" },
            new EffectInfo { Code = "Demister", DisplayName = "Wisplight" },
            new EffectInfo { Code = "Wishbone", DisplayName = "Wishbone" },
        };

        private readonly HashSet<string> _others = new HashSet<string> { "Potion_health_major", "GP_Eikthyr" };

        public IReadOnlyList<EffectInfo> Allowed => _allowed;
        public bool Exists(string code) => _allowed.Any(e => e.Code == code) || _others.Contains(code);

        public void AddAllowed(string code, string name) => _allowed.Add(new EffectInfo { Code = code, DisplayName = name });
    }

    private static BundleState Evaluate(string bundles) =>
        BundleRules.Evaluate(BundleFile.Parse(bundles), new FakeCatalog());

    private static string StarterBundles()
    {
        using (var stream = typeof(Tests).Assembly.GetManifestResourceStream("LoadoutBuffs.buffs.example.yaml"))
        using (var reader = new StreamReader(stream))
            return reader.ReadToEnd();
    }

    // ---- bundles: file --------------------------------------------------------------------

    private static void Test_Bundles_StarterFileIsCleanAndChangesNothing()
    {
        var file = BundleFile.Parse(StarterBundles());
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        Eq(null, file.Active, "active");
        True(file.Bundles.Count >= 3, "starter buffs");

        var state = BundleRules.Evaluate(file, new FakeCatalog());
        Eq(0, state.Warnings.Count, "every starter effect resolves: " + string.Join(" | ", state.Warnings));
        True(state.Enabled && state.Active == null && state.Effects.Count == 0, "installing activates nothing");
        Eq("No buff active", state.StatusLine(), "status");
    }

    private static void Test_Readme_ExampleFileLoadsWithoutWarnings()
    {
        string readme;
        using (var stream = typeof(Tests).Assembly.GetManifestResourceStream("LoadoutBuffs.readme.md"))
        using (var reader = new StreamReader(stream))
            readme = reader.ReadToEnd();
        var start = readme.IndexOf("```yaml\n", StringComparison.Ordinal);
        True(start >= 0, "the README has a yaml example");
        start += "```yaml\n".Length;
        var example = readme.Substring(start, readme.IndexOf("```", start, StringComparison.Ordinal) - start);
        var state = Evaluate(example);
        Eq(0, state.Warnings.Count, "the README's example follows the slot rules: " + string.Join(" | ", state.Warnings));
        Eq(2f, state.Stats[BundleSlot.Melee].Scalars["parryBonus"], "it shows the Parry bonus");
    }

    private static void Test_Bundles_HeaderHasNoYamlOrDump()
    {
        foreach (var (what, text) in new[] { ("window header", string.Join("\n", BundleFile.Header)), ("starter file", StarterBundles()) })
        {
            False(text.Contains("ItemStatOverrides.yaml"), $"{what}: no rule (i) / YAML line");
            False(text.Contains("dump"), $"{what}: no dump reference");
            True(text.StartsWith("# LoadoutBuffs"), $"{what}: named after the mod");
            True(text.Contains("lb_buffs") && text.Contains("lb_reload") && !text.Contains("iso_"), $"{what}: lb_ commands");
            True(text.Contains("Melee or ranged slot only: damage (%), addDamage"), $"{what}: damage and classes are for weapon slots");
            True(text.Contains("Melee, ranged or shield slot only, on every parry:"), $"{what}: parry stats are for hand slots");
            True(text.Contains("Helmet, chest, legs or cape slot only: armor, fallDamage, healthRegen, staminaRegen, eitrRegen,"), $"{what}: armor, fall damage, regen and resist are for armor slots");
            True(text.Contains("Melee, ranged or shield slot only: blockArmor, blockForce"), $"{what}: block stats are for hand slots");
            True(StarterBundles().StartsWith(string.Join("\n", BundleFile.Header) + "\n\n"), "the starter file starts with the window's header");
            foreach (var key in new[] { "blockArmor", "blockForce", "addDamage", "shieldOnParry", "shieldMinutes", "eitrCost", "parryBonus" })
                True(text.Contains(key), $"{what} lists {key}");
        }
    }

    private static void Test_Bundles_OldHeaderStillLoads()
    {
        var old = "# Buffs are off while ItemStatOverrides.yaml sets equipStatusEffect, setStatusEffect,\n" +
                  "# setName or setSize on any item.\n" +
                  "# (the ones marked \"(equip)\" or \"(set ...)\" at the end of ItemStatOverrides.dump.yaml).\n\n" +
                  "active: none\n\nbuffs:\n  T:\n    melee:\n      stats: { addDamage: { spirit: 30 } }\n";
        var file = BundleFile.Parse(old);
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        Eq(30f, file.Find("T").GetStats(BundleSlot.Melee).AddDamage["spirit"], "a 0.8.0 file loads as is");
    }

    private static void Test_Bundles_ActiveBundleResolvesPerSlot()
    {
        var state = Evaluate("active: Tank\nbuffs:\n  Tank:\n    chest: SetEffect_DeepNorthMediumArmor\n    cape: SlowFall\n");
        Eq(0, state.Warnings.Count, "warnings: " + string.Join(" | ", state.Warnings));
        Eq("Tank", state.Active?.Name, "active");
        Eq("SetEffect_DeepNorthMediumArmor", state.Effects[BundleSlot.Chest], "chest");
        Eq("SlowFall", state.Effects[BundleSlot.Cape], "cape");
        Eq(2, state.Effects.Count, "effects");
        Eq("Buff 'Tank' active: 2 effects", state.StatusLine(), "status");
    }

    private static void Test_Bundles_InGameNamesAndCaseAreAccepted()
    {
        var state = Evaluate("active: tank\nbuffs:\n  Tank:\n    CHEST: vanguard\n    legs: \"ask's endurance\"\n    helmet: slowfall\n");
        Eq(0, state.Warnings.Count, "warnings: " + string.Join(" | ", state.Warnings));
        Eq("SetEffect_DeepNorthMediumArmor", state.Effects[BundleSlot.Chest], "in-game name");
        Eq("SetEffect_AshlandsMediumArmor", state.Effects[BundleSlot.Legs], "quoted in-game name with apostrophe");
        Eq("SlowFall", state.Effects[BundleSlot.Helmet], "code name, other case");
    }

    private static void Test_Bundles_AmbiguousInGameNameListsCodeNames()
    {
        var catalog = new FakeCatalog();
        catalog.AddAllowed("SetEffect_OtherVanguard", "Vanguard");
        var state = BundleRules.Evaluate(BundleFile.Parse("active: T\nbuffs:\n  T:\n    chest: Vanguard\n"), catalog);
        Eq(1, state.Warnings.Count, "warnings");
        True(state.Warnings[0].Contains("several effects") && state.Warnings[0].Contains("SetEffect_OtherVanguard"), state.Warnings[0]);
        Eq(0, state.Effects.Count, "nothing resolved");
    }

    private static void Test_Bundles_BadEntriesWarnAndTheRestApplies()
    {
        var state = Evaluate(
            "active: T\nbuffs:\n  T:\n    chest: Vanguard\n    boots: SlowFall\n    legs: Nope\n    helmet: Potion_health_major\n    cape: [a, b]\n");
        Eq(4, state.Warnings.Count, "warnings: " + string.Join(" | ", state.Warnings));
        True(state.Warnings.Any(w => w.StartsWith("T.boots: unknown slot") && w.Contains("line 5")), "unknown slot");
        True(state.Warnings.Any(w => w.StartsWith("T.cape: expected an effect name")), "list value");
        True(state.Warnings.Any(w => w.StartsWith("T.legs: unknown effect 'Nope'")), "unknown effect");
        True(state.Warnings.Any(w => w.StartsWith("T.helmet: 'Potion_health_major' can't be used in a buff")), "not a gear effect");
        Eq(1, state.Effects.Count, "chest still applies");
    }

    private static void Test_Bundles_SameEffectTwiceKeepsTheFirstSlot()
    {
        var state = Evaluate("active: T\nbuffs:\n  T:\n    legs: SlowFall\n    cape: Feather fall\n");
        Eq(1, state.Warnings.Count, "warnings");
        True(state.Warnings[0].StartsWith("T.cape: SlowFall is already in the legs slot"), state.Warnings[0]);
        Eq("SlowFall", state.Effects[BundleSlot.Legs], "first slot kept");
        False(state.Effects.ContainsKey(BundleSlot.Cape), "second slot skipped");
    }

    private static void Test_Bundles_InactiveBundlesAreCheckedToo()
    {
        var state = Evaluate("active: none\nbuffs:\n  A:\n    chest: Nope\n");
        Eq(1, state.Warnings.Count, "warnings");
        True(state.Warnings[0].StartsWith("A.chest: unknown effect"), state.Warnings[0]);
    }

    private static void Test_Bundles_MissingActiveBundleWarnsAndUsesNone()
    {
        var state = Evaluate("active: Tank\nbuffs:\n  Warrior: {}\n");
        Eq(1, state.Warnings.Count, "warnings");
        True(state.Warnings[0].Contains("active buff 'Tank' doesn't exist") && state.Warnings[0].Contains("Warrior"), state.Warnings[0]);
        Eq(null, state.Active, "active");
        True(state.Enabled, "still enabled");
    }

    private static void Test_Bundles_DuplicateNamesAndUnknownKeysWarn()
    {
        var file = BundleFile.Parse("active: none\nbuff: x\nbuffs:\n  Tank: {}\n  tank: {}\n");
        Eq(2, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Warnings[0].Contains("unknown key 'buff'"), file.Warnings[0]);
        True(file.Warnings[1].Contains("buff 'tank' appears twice"), file.Warnings[1]);
        Eq(1, file.Bundles.Count, "buffs");
    }

    private static void Test_Bundles_SlotTwiceWarns()
    {
        var file = BundleFile.Parse(
            "active: T\nbuffs:\n  T:\n    chest: Sneaky\n    CHEST: WindRun\n    legs: none\n    Legs:\n      stats: { armor: 5 }\n");
        Eq(2, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        Eq("T.chest: the slot appears twice (slots ignore upper/lower case); the second one is skipped (line 5).", file.Warnings[0], "text");
        True(file.Warnings[1].StartsWith("T.legs: the slot appears twice"), "also after an empty one: " + file.Warnings[1]);
        var bundle = file.Find("T");
        Eq("Sneaky", bundle.Entries.Single(e => e.Slot == BundleSlot.Chest).Effect, "the first one counts");
        Eq(null, bundle.GetStats(BundleSlot.Legs), "the second legs is skipped");

        // With a misplaced stat too, the cleanup must not run: its save would drop the second slot.
        var mixed = BundleFile.Parse("buffs:\n  T:\n    chest:\n      stats: { healOnParry: 20 }\n    CHEST: WindRun\n");
        Eq(2, mixed.Warnings.Count, "warnings: " + string.Join(" | ", mixed.Warnings));
        False(mixed.OnlyMisplaced, "a slot twice blocks the cleanup");
    }

    private static void Test_Bundles_InvalidYamlTurnsBundlesOff()
    {
        var state = Evaluate("active: T\nbuffs:\n  T:\n\tchest: Vanguard\n");
        False(state.Enabled, "enabled");
        True(state.Warnings.Count == 1 && state.Warnings[0].Contains("not valid YAML") && state.Warnings[0].Contains("line"), string.Join(" | ", state.Warnings));
        True(state.StatusLine().StartsWith("Buffs off: "), state.StatusLine());
    }

    private static void Test_Bundles_EmptyAndMissingFilesAreFine()
    {
        foreach (var text in new[] { null, "", "# only a comment\n", "active: none\n", "buffs: {}\n" })
        {
            var state = Evaluate(text);
            True(state.Enabled && state.Active == null && state.Warnings.Count == 0, $"'{text}': {string.Join(" | ", state.Warnings)}");
        }
    }

    private static void Test_Bundles_SerializeRoundTrips()
    {
        var file = BundleFile.Parse("active: \"My Tank\"\nbuffs:\n  \"My Tank\":\n    cape: SlowFall\n    chest: SetEffect_DeepNorthMediumArmor\n  Empty: {}\n  none: {}\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var names = new Dictionary<string, string> { ["SlowFall"] = "Feather fall", ["SetEffect_DeepNorthMediumArmor"] = "Vanguard" };
        var text = file.Serialize(code => names.TryGetValue(code, out var n) ? n : null);

        True(text.Contains("chest: SetEffect_DeepNorthMediumArmor") && text.Contains("# Vanguard"), text);
        True(text.IndexOf("chest:", StringComparison.Ordinal) < text.IndexOf("cape:", StringComparison.Ordinal), "slot order is canonical");
        True(text.Contains("\"none\": {}"), "a buff named none is quoted: " + text);

        var again = BundleFile.Parse(text);
        Eq(0, again.Warnings.Count, "re-parse warnings: " + string.Join(" | ", again.Warnings));
        Eq("My Tank", again.Active, "active");
        Eq(3, again.Bundles.Count, "buffs");
        Eq("SlowFall", again.Find("my tank").Get(BundleSlot.Cape), "cape");
        Eq(0, again.Find("Empty").Entries.Count, "empty buff");
        Eq(text, again.Serialize(code => names.TryGetValue(code, out var n) ? n : null), "stable");
    }

    private static void Test_Bundles_NoActiveSerializesAsNone()
    {
        var file = new BundleFile();
        var bundle = new BundleDef { Name = "A" };
        bundle.Set(BundleSlot.Legs, "WindRun");
        bundle.Set(BundleSlot.Legs, "SlowFall");
        bundle.Set(BundleSlot.Cape, null);
        file.Bundles.Add(bundle);
        var text = file.Serialize(null);
        True(text.Contains("active: none"), text);
        var again = BundleFile.Parse(text);
        Eq(null, again.Active, "active");
        Eq("SlowFall", again.Find("A").Get(BundleSlot.Legs), "Set replaces");
        Eq(1, again.Find("A").Entries.Count, "one entry");
    }

    private static void False(bool condition, string what) => True(!condition, what);

    // ---- window: what the rows and lines show -------------------------------------------------

    private static void Test_Window_DamagePercentRowsFollowTheWeapon()
    {
        var club = new Dictionary<string, float> { ["blunt"] = 90 };
        var none = new Dictionary<string, float>();
        Eq("Blunt", string.Join(",", BundleWindowRules.DamagePercentRows(club, none)), "a club deals blunt only (no chop row: the class chop doesn't use it)");

        var clubWithSpirit = new Dictionary<string, float> { ["blunt"] = 90, ["spirit"] = 30 };
        Eq("Blunt,Spirit", string.Join(",", BundleWindowRules.DamagePercentRows(clubWithSpirit, none)), "an added type shows up");

        var slashSet = new Dictionary<string, float> { ["slash"] = 10 };
        Eq("Blunt,Slash", string.Join(",", BundleWindowRules.DamagePercentRows(club, slashSet)), "a row with a value never hides, in catalog order");

        Eq(string.Join(",", BundleStatCatalog.DamageTypes), string.Join(",", BundleWindowRules.DamagePercentRows(null, none)), "no weapon known: every type");
    }

    private static void Test_Window_ShiftClickTakesTenSteps()
    {
        Eq(5f, BundleWindowRules.ClickStep(5f, shift: false), "a click: one step");
        Eq(50f, BundleWindowRules.ClickStep(5f, shift: true), "shift + click: ten steps");
        Eq(-5f, BundleWindowRules.ClickStep(-0.5f, shift: true), "down too, half steps included");
    }

    private static void Test_Window_AddedDamageNoteShowsWhatTheWeaponHas()
    {
        Eq("has 90", BundleWindowRules.AddedDamageNote(90f), "own damage");
        Eq("has 12.5", BundleWindowRules.AddedDamageNote(12.5f), "decimals");
        Eq(null, BundleWindowRules.AddedDamageNote(0f), "none of it: no note");
    }

    private static void Test_Window_DamageByTypeReadsTheWeapon()
    {
        var damages = new HitData.DamageTypes { m_damage = 5, m_blunt = 90, m_chop = 20, m_spirit = 0 };
        var map = StatBlock.DamageByType(damages);
        Eq("blunt=90,chop=20", string.Join(",", map.Select(p => $"{p.Key}={p.Value}")), "non-zero typed damage, lowercase, catalog order (not the untyped m_damage)");
    }

    private static void Test_Window_WornEffectNames()
    {
        var file = BundleFile.Parse("buffs:\n  E:\n    cape: SlowFall\n    helmet: Demister\n    chest: BeltStrength\n    legs: NotAnEffect\n");
        var worn = new HashSet<BundleSlot> { BundleSlot.Chest, BundleSlot.Helmet, BundleSlot.Legs };
        var names = BundleWindowRules.WornEffectNames(file.Find("E"), new FakeCatalog(), worn.Contains);
        Eq("Wisplight,Megingjord", string.Join(",", names), "worn slots only, in slot order, in-game names; an unknown effect is left out");
        Eq(0, BundleWindowRules.WornEffectNames(null, new FakeCatalog(), worn.Contains).Count, "no buff");
    }

    private static void Test_Hud_BuffShowsWheneverOneIsInUse()
    {
        True(BundleRules.ShowsHudBuff(Evaluate("active: E\nbuffs:\n  E:\n    chest: BeltStrength\n")), "effects only, no stats: still on the HUD");
        True(BundleRules.ShowsHudBuff(Evaluate("active: E\nbuffs:\n  E: {}\n")), "an empty buff in use: its name still shows");
        False(BundleRules.ShowsHudBuff(Evaluate("active: none\nbuffs:\n  E:\n    chest: BeltStrength\n")), "none in use");
        False(BundleRules.ShowsHudBuff(Evaluate("active: E\nbuffs:\n  E:\n\tchest: x\n")), "invalid file: buffs are off");
    }
}
