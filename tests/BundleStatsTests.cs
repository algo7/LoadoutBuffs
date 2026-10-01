using System;
using System.Collections.Generic;
using System.Linq;
using LoadoutBuffs;

internal static partial class Tests
{
    // ---- bundle stats -----------------------------------------------------------------------

    private static void Test_Stats_EveryKindParses()
    {
        var file = BundleFile.Parse(
            "active: T\nbundles:\n  T:\n    chest:\n      effect: Vanguard\n" +
            "      stats: { movementSpeed: 10, CarryWeight: +50, armor: 15%, fallDamage: -100, healthRegen: 20, dodgeStamina: -15,\n" +
            "               resist: { fire: resistant, POISON: very resistant }, damage: { Slash: 10 }, skills: { bows: 15 },\n" +
            "               fields: { m_swimSpeedModifier: 0.2, m_staggerModifier: -0.5 } }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var entry = file.Find("T").Entries.Single();
        Eq("Vanguard", entry.Effect, "effect");
        var stats = entry.Stats;
        Eq(10f, stats.Scalars["movementSpeed"], "movement");
        Eq(50f, stats.Scalars["carryWeight"], "carry, key case and + sign");
        Eq(15f, stats.Scalars["armor"], "armor with % sign");
        Eq(-100f, stats.Scalars["fallDamage"], "fall damage");
        Eq(20f, stats.Scalars["healthRegen"], "regen");
        Eq(-15f, stats.Scalars["dodgeStamina"], "dodge stamina");
        Eq("Resistant", stats.Resist["Fire"], "resist, names canonicalized");
        Eq("VeryResistant", stats.Resist["Poison"], "resist with a space in the modifier");
        Eq(10f, stats.Damage["slash"], "damage");
        Eq(15f, stats.Skills["Bows"], "skill");
        Eq(2, stats.Fields.Count, "raw fields");
        Eq(12, stats.Count, "count");
    }

    private static void Test_Stats_StatsOnlySlotAndShortFormTogether()
    {
        var state = Evaluate("active: T\nbundles:\n  T:\n    helmet: Vanguard\n    legs:\n      stats: { movementSpeed: 10 }\n");
        Eq(0, state.Warnings.Count, "warnings: " + string.Join(" | ", state.Warnings));
        Eq("SetEffect_DeepNorthMediumArmor", state.Effects[BundleSlot.Helmet], "short form effect");
        False(state.Effects.ContainsKey(BundleSlot.Legs), "no effect on the stats-only slot");
        Eq(10f, state.Stats[BundleSlot.Legs].Scalars["movementSpeed"], "legs stats");
        Eq("Bundle 'T' active: 1 effect, 1 stat", state.StatusLine(), "status");
    }

    private static void Test_Stats_BadEntriesWarnAndTheRestApplies()
    {
        var file = BundleFile.Parse(
            "bundles:\n  T:\n    chest:\n      stats: { speed: 5, armor: lots, resist: { Wet: Resistant, Fire: Tough }, damage: { magic: 5 },\n" +
            "               skills: { Juggling: 5 }, fields: { m_nope: 1, m_icon: x, m_speedModifier: fast }, movementSpeed: 10 }\n      colour: red\n");
        Eq(10, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Warnings[0].StartsWith("T.chest.speed: unknown stat") && file.Warnings[0].Contains("movementSpeed"), file.Warnings[0]);
        True(file.Warnings[1].StartsWith("T.chest.armor: 'lots' is not a number"), file.Warnings[1]);
        True(file.Warnings[2].StartsWith("T.chest.resist.Wet: unknown damage type"), file.Warnings[2]);
        True(file.Warnings[3].StartsWith("T.chest.resist.Fire: 'Tough' is not one of"), file.Warnings[3]);
        True(file.Warnings[4].StartsWith("T.chest.damage.magic: unknown damage type"), file.Warnings[4]);
        True(file.Warnings[5].StartsWith("T.chest.skills.Juggling: unknown skill"), file.Warnings[5]);
        True(file.Warnings[6].StartsWith("T.chest.fields.m_nope: 'm_nope' is not a field of SE_Stats"), file.Warnings[6]);
        True(file.Warnings[7].StartsWith("T.chest.fields.m_icon") && file.Warnings[7].Contains("can't be set"), file.Warnings[7]);
        True(file.Warnings[8].StartsWith("T.chest.fields.m_speedModifier:"), file.Warnings[8]);
        True(file.Warnings[9].StartsWith("T.chest.colour: unknown key"), file.Warnings[9]);
        var entry = file.Find("T").Entries.Single();
        Eq(10f, entry.Stats.Scalars["movementSpeed"], "the valid stat still applies");
        Eq(1, entry.Stats.Count, "only the valid stat");
    }

    private static void Test_Stats_UnknownSlotKeyWarns()
    {
        var file = BundleFile.Parse("bundles:\n  T:\n    chest:\n      colour: red\n      stats: { armor: 5 }\n");
        Eq(1, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Warnings[0].StartsWith("T.chest.colour: unknown key"), file.Warnings[0]);
        Eq(5f, file.Find("T").GetStats(BundleSlot.Chest).Scalars["armor"], "armor");
    }

    private static void Test_Stats_SumAddsNumbersAndKeepsTheBestResistance()
    {
        var legs = new StatBlock();
        legs.Scalars["movementSpeed"] = 10;
        legs.Damage["slash"] = 10;
        legs.Skills["Bows"] = 5;
        legs.SetResist("Fire", "Weak");
        legs.SetResist("Frost", "Resistant");
        var chest = new StatBlock();
        chest.Scalars["movementSpeed"] = 5;
        chest.Scalars["armor"] = 15;
        chest.Damage["slash"] = 10;
        chest.Skills["Bows"] = 10;
        chest.SetResist("Fire", "Resistant");
        chest.SetResist("Frost", "SlightlyResistant");
        chest.Fields.Add(new System.Collections.Generic.KeyValuePair<string, string>("m_staggerModifier", "-0.5"));

        var total = StatBlock.Sum(new[] { legs, chest, null });
        Eq(15f, total.Scalars["movementSpeed"], "movement adds");
        Eq(15f, total.Scalars["armor"], "armor");
        Eq(20f, total.Damage["slash"], "damage adds within a bundle");
        Eq(15f, total.Skills["Bows"], "skills add");
        Eq("Resistant", total.Resist["Fire"], "resistant beats weak");
        Eq("Resistant", total.Resist["Frost"], "resistant beats slightly resistant");
        Eq(1, total.Fields.Count, "raw fields kept");
        Eq("+15% movement speed, +15 armor; resist: Fire Resistant, Frost Resistant; damage: slash +20%; skills: Bows +15; fields: m_staggerModifier", total.Summary(), "summary");
    }

    private static void Test_Stats_ZeroAndNormalRemoveEntries()
    {
        var block = new StatBlock();
        StatBlock.SetNumber(block.Scalars, "armor", 5);
        StatBlock.SetNumber(block.Scalars, "armor", 0);
        block.SetResist("Fire", "Resistant");
        block.SetResist("Fire", "Normal");
        True(block.IsEmpty, "empty");
        Eq("{}", block.Serialize(), "serialized");
        var cancel = StatBlock.Sum(new[] { Block("movementSpeed", 10), Block("movementSpeed", -10) });
        False(cancel.Scalars.ContainsKey("movementSpeed"), "opposites cancel out");
    }

    private static StatBlock Block(string key, float value)
    {
        var block = new StatBlock();
        block.Scalars[key] = value;
        return block;
    }

    private static void Test_Stats_SerializeRoundTrips()
    {
        var text =
            "active: T\nbundles:\n  T:\n    helmet: SlowFall\n    chest:\n      effect: SetEffect_DeepNorthMediumArmor\n" +
            "      stats: { armor: 15, movementSpeed: 12.5, resist: { Poison: VeryResistant, Fire: Resistant }, damage: { slash: 10 }, skills: { Bows: 15 }, fields: { m_staggerModifier: -0.5 } }\n" +
            "    legs:\n      stats: { runStamina: -20 }\n";
        var file = BundleFile.Parse(text);
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var names = new System.Collections.Generic.Dictionary<string, string> { ["SlowFall"] = "Feather fall", ["SetEffect_DeepNorthMediumArmor"] = "Vanguard" };
        var saved = file.Serialize(code => names.TryGetValue(code, out var n) ? n : null);

        True(saved.Contains("    helmet: SlowFall") && saved.Contains("# Feather fall"), saved);
        True(saved.Contains("      effect: SetEffect_DeepNorthMediumArmor") && saved.Contains("# Vanguard"), saved);
        True(saved.Contains("      stats: { movementSpeed: 12.5, armor: 15, resist: { Fire: Resistant, Poison: VeryResistant }, damage: { slash: 10 }, skills: { Bows: 15 }, fields: { m_staggerModifier: \"-0.5\" } }"), saved);
        True(saved.Contains("    legs:\n      stats: { runStamina: -20 }"), "stats-only slot has no effect line: " + saved);

        var again = BundleFile.Parse(saved);
        Eq(0, again.Warnings.Count, "re-parse warnings: " + string.Join(" | ", again.Warnings));
        Eq(saved, again.Serialize(code => names.TryGetValue(code, out var n) ? n : null), "stable");
    }

    private static void Test_Stats_WindowSettersKeepTheOtherPart()
    {
        var bundle = new BundleDef { Name = "T" };
        bundle.Set(BundleSlot.Chest, "Vanguard");
        bundle.SetStats(BundleSlot.Chest, Block("armor", 5));
        bundle.Set(BundleSlot.Chest, null);
        Eq(5f, bundle.GetStats(BundleSlot.Chest).Scalars["armor"], "clearing the effect keeps the stats");
        bundle.Set(BundleSlot.Chest, "Vanguard");
        bundle.SetStats(BundleSlot.Chest, new StatBlock());
        Eq("Vanguard", bundle.Get(BundleSlot.Chest), "clearing the stats keeps the effect");
        Eq(null, bundle.GetStats(BundleSlot.Chest), "no stats");
        bundle.Set(BundleSlot.Chest, null);
        Eq(0, bundle.Entries.Count, "slot with neither is removed");
    }

    private static void Test_Stats_CatalogIsConsistent()
    {
        var keys = BundleStatCatalog.Scalars.Select(d => d.Key).ToList();
        Eq(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "unique keys");
        foreach (var def in BundleStatCatalog.Scalars)
        {
            if (def.Kind == StatKind.Parry)
            {
                True(def.Field == null && def.Group == BundleStatCatalog.OnParry, $"{def.Key}: on-parry stats have no field");
                True(def.Min >= 0, $"{def.Key}: no negative parry amounts");
                continue;
            }
            if (def.Kind == StatKind.Blocker)
            {
                True(def.Field == null, $"{def.Key}: applied by the block hook, no SE_Stats field");
                continue;
            }
            var field = typeof(SE_Stats).GetField(def.Field);
            True(field != null && field.FieldType == typeof(float), $"{def.Key} → {def.Field} is a float field of SE_Stats");
            True(def.Min < 0 || def.Min == 0, $"{def.Key} min");
            True(def.Max > 0 && def.Step > 0, $"{def.Key} max/step");
        }
        foreach (var modifier in BundleStatCatalog.ModifierCycle)
            True(Enum.IsDefined(typeof(HitData.DamageModifier), modifier), modifier);
        True(BundleStatCatalog.WindowSkills.Contains("Bows") && !BundleStatCatalog.WindowSkills.Contains("All") && !BundleStatCatalog.WindowSkills.Contains("None"), "skills");
    }
}

internal static partial class Tests
{
    // ---- bundle slots: melee / ranged / shield ------------------------------------------------

    private static void Test_Slots_HandsAreSplitByWeaponSkill()
    {
        void Is(BundleSlot? expected, ItemDrop.ItemData.ItemType type, Skills.SkillType skill) =>
            Eq(expected, BundleSlots.HandSlot(type, skill), $"{type}/{skill}");

        Is(BundleSlot.Ranged, ItemDrop.ItemData.ItemType.Bow, Skills.SkillType.Bows);                    // bows
        Is(BundleSlot.Ranged, ItemDrop.ItemData.ItemType.Bow, Skills.SkillType.Crossbows);               // crossbows are Bow type
        Is(BundleSlot.Ranged, ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.ElementalMagic); // Staff of Embers
        Is(BundleSlot.Ranged, ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft, Skills.SkillType.ElementalMagic); // Northern Vengeance
        Is(BundleSlot.Ranged, ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.BloodMagic);
        Is(BundleSlot.Melee, ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.Polearms);     // atgeir
        Is(BundleSlot.Melee, ItemDrop.ItemData.ItemType.OneHandedWeapon, Skills.SkillType.Spears);
        Is(BundleSlot.Melee, ItemDrop.ItemData.ItemType.OneHandedWeapon, Skills.SkillType.Axes);
        Is(BundleSlot.Melee, ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.Unarmed);      // Flesh Rippers
        Is(BundleSlot.Shield, ItemDrop.ItemData.ItemType.Shield, Skills.SkillType.Blocking);
        Is(null, ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.Pickaxes);                 // pickaxes
        Is(null, ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.Fishing);                  // fishing rod
        Is(null, ItemDrop.ItemData.ItemType.Torch, Skills.SkillType.Clubs);                              // torch
        Is(null, ItemDrop.ItemData.ItemType.Tool, Skills.SkillType.Swords);                              // hammer, hoe, cultivator
    }

    private static void Test_Slots_RemovedSlotsWarnWithAHint()
    {
        var file = BundleFile.Parse("bundles:\n  T:\n    weapon: SlowFall\n    Utility: Wishbone\n    trinket: Demister\n    ranged: SetEffect_RootArmor\n");
        Eq(3, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Warnings[0].StartsWith("T.weapon: the weapon slot was split: use melee or ranged"), file.Warnings[0]);
        True(file.Warnings[1].StartsWith("T.Utility: the utility slot was removed"), file.Warnings[1]);
        True(file.Warnings[2].StartsWith("T.trinket: the trinket slot was removed"), file.Warnings[2]);
        Eq("SetEffect_RootArmor", file.Find("T").Get(BundleSlot.Ranged), "ranged kept");
        Eq(7, BundleSlots.All.Length, "slots");
    }
}

internal static partial class Tests
{
    // ---- bundle stats: on parry ---------------------------------------------------------------

    private static void Test_ParryStats_ParseSumAndAssist()
    {
        var file = BundleFile.Parse(
            "active: T\nbundles:\n  T:\n    shield:\n      stats: { healAlliesOnParry: 40, staminaAlliesOnParry: 30, parryRadius: 15 }\n" +
            "    melee:\n      stats: { HealOnParry: 50, healAlliesOnParry: 10, healTamedOnParry: 20, parryradius: 25 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Melee) });
        Eq(50f, total.Scalars["healAlliesOnParry"], "amounts add");
        Eq(25f, total.Scalars["parryRadius"], "reach takes the largest, not the sum");

        var assist = total.ToParryAssist();
        Eq(50f, assist.Heal, "heal you");
        Eq(0f, assist.Stamina, "stamina you");
        Eq(50f, assist.HealAllies, "heal allies");
        Eq(30f, assist.StaminaAllies, "stamina allies");
        Eq(20f, assist.HealTamed, "heal tamed");
        Eq(25f, assist.Radius, "radius");
        Eq(25f, assist.TamedRadius, "tamed share the reach");

        var shieldOnly = bundle.GetStats(BundleSlot.Shield).ToParryAssist();
        Eq(15f, shieldOnly.Radius, "shield alone: its own reach");
        True(shieldOnly.ReachesOthers, "reaches others");
    }

    private static void Test_ParryStats_ReachAloneDoesNothingAndDefaultsTo10()
    {
        var onlyReach = new StatBlock();
        onlyReach.Scalars["parryRadius"] = 30;
        Eq(null, onlyReach.ToParryAssist(), "a reach without amounts does nothing");

        var noReach = new StatBlock();
        noReach.Scalars["healAlliesOnParry"] = 40;
        Eq(10f, noReach.ToParryAssist().Radius, "default reach 10 m");
        Eq("on parry: heal allies +40", noReach.Summary(), "summary");

        noReach.Scalars["parryRadius"] = 20;
        Eq("on parry: heal allies +40, reach 20 m", noReach.Summary(), "summary with reach");
    }

    private static void Test_ParryStats_CountAndStatusLine()
    {
        var state = Evaluate("active: T\nbundles:\n  T:\n    shield:\n      stats: { healAlliesOnParry: 40, parryRadius: 15 }\n");
        Eq(0, state.Warnings.Count, "warnings: " + string.Join(" | ", state.Warnings));
        Eq("Bundle 'T' active: 0 effects, 2 stats", state.StatusLine(), "status");
    }

    private static void Test_ParryStats_Bubble()
    {
        var file = BundleFile.Parse(
            "active: T\nbundles:\n  T:\n    shield:\n      stats: { shieldOnParry: 300, parryRadius: 15 }\n" +
            "    chest:\n      stats: { ShieldOnParry: 200 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Chest) });
        Eq(500f, total.Scalars["shieldOnParry"], "bubbles add across slots");

        var assist = total.ToParryAssist();
        True(assist != null, "a bubble alone is an on-parry stat");
        Eq(500f, assist.Shield, "bubble");
        Eq(15f, assist.Radius, "reach");
        Eq(15f, assist.TamedRadius, "tamed share the reach");
        Eq("on parry: bubble +500, reach 15 m", total.Summary(), "summary");

        var again = BundleFile.Parse(file.Serialize(_ => null));
        Eq(0, again.Warnings.Count, "round trip warnings: " + string.Join(" | ", again.Warnings));
        Eq(300f, again.Find("T").GetStats(BundleSlot.Shield).Scalars["shieldOnParry"], "round trip keeps the bubble");

        var def = BundleStatCatalog.Find("shieldOnParry");
        Eq("Bubble", def.Label, "window label");
        Eq(100f, def.Step, "step");
        Eq(3000f, def.Max, "window max");
    }

    private static void Test_ParryStats_BubbleTime()
    {
        var file = BundleFile.Parse(
            "active: T\nbundles:\n  T:\n    shield:\n      stats: { shieldOnParry: 300, shieldMinutes: 3 }\n" +
            "    chest:\n      stats: { ShieldMinutes: 2 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Chest) });
        Eq(3f, total.Scalars["shieldMinutes"], "the longest time, not the sum");
        Eq(3f, total.ToParryAssist().ShieldMinutes, "assist");
        Eq("on parry: bubble +300, bubble time 3 min", total.Summary(), "summary");
        Eq(null, bundle.GetStats(BundleSlot.Chest).ToParryAssist(), "a time without a bubble does nothing");

        var noTime = new StatBlock();
        noTime.Scalars["shieldOnParry"] = 300;
        Eq(1f, noTime.ToParryAssist().ShieldMinutes, "default 1 minute");

        var again = BundleFile.Parse(file.Serialize(_ => null));
        Eq(3f, again.Find("T").GetStats(BundleSlot.Shield).Scalars["shieldMinutes"], "round trip");

        var def = BundleStatCatalog.Find("shieldMinutes");
        Eq("Bubble time", def.Label, "window label");
        Eq(1f, def.Step, "step");
        Eq(1f, def.Min, "min");
        Eq(10f, def.Max, "max");
        Eq(1f, def.Default, "default");
        True(def.TakesLargest, "largest across slots");
    }

    private static void Test_ParryStats_ZeroOrNegativeWarn()
    {
        var file = BundleFile.Parse(
            "bundles:\n  T:\n    shield:\n      stats: { shieldOnParry: -500, healAlliesOnParry: 0, parryRadius: -5, healOnParry: 20 }\n");
        Eq(3, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Warnings.Any(w => w.StartsWith("T.shield.shieldOnParry: '-500' must be more than 0")), "negative bubble");
        True(file.Warnings.Any(w => w.StartsWith("T.shield.healAlliesOnParry: '0' must be more than 0")), "zero heal");
        True(file.Warnings.Any(w => w.StartsWith("T.shield.parryRadius: '-5' must be more than 0")), "negative reach");
        var stats = file.Find("T").GetStats(BundleSlot.Shield);
        Eq(1, stats.Count, "only the valid heal is kept");
        Eq(20f, stats.Scalars["healOnParry"], "heal");
    }
}

internal static partial class Tests
{
    // ---- bundle stats: item classes ---------------------------------------------------------

    private static void Test_Classes_ParseSumSerialize()
    {
        var file = BundleFile.Parse(
            "active: T\nbundles:\n  T:\n    melee:\n      stats: { Woodcutter: true, miner: yes, armor: 5 }\n" +
            "    chest:\n      stats: { woodcutter: on }\n    legs:\n      stats: { miner: false }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        Eq(3, bundle.GetStats(BundleSlot.Melee).Count, "melee: two classes + armor");
        Eq(null, bundle.GetStats(BundleSlot.Legs), "miner: false sets nothing");

        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Melee), bundle.GetStats(BundleSlot.Chest) });
        Eq(1f, total.Scalars["woodcutter"], "a class from two slots is still just on");
        Eq("+5 armor; class: Woodcutter, Miner", total.Summary(), "summary");

        var saved = file.Serialize(null);
        True(saved.Contains("stats: { armor: 5, woodcutter: true, miner: true }"), saved);
        True(saved.Contains("    chest:\n      stats: { woodcutter: true }"), saved);
        var again = BundleFile.Parse(saved);
        Eq(0, again.Warnings.Count, "re-parse warnings: " + string.Join(" | ", again.Warnings));
        Eq(saved, again.Serialize(null), "stable");
    }

    private static void Test_Classes_BadValueWarns()
    {
        var file = BundleFile.Parse("bundles:\n  T:\n    melee:\n      stats: { woodcutter: maybe }\n");
        Eq(1, file.Warnings.Count, "warnings");
        True(file.Warnings[0].StartsWith("T.melee.woodcutter: 'maybe' is not true or false"), file.Warnings[0]);
    }

    // ---- bundle stats: block armor ------------------------------------------------------------

    private static void Test_BlockArmor_ParseSumAndSummary()
    {
        var file = BundleFile.Parse(
            "active: T\nbundles:\n  T:\n    shield:\n      stats: { blockArmor: 20 }\n" +
            "    chest:\n      stats: { BlockArmor: 15, movementSpeed: 10 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Chest) });
        Eq(35f, total.Scalars["blockArmor"], "block armor adds across slots");
        Eq(35f, total.BlockArmor, "total for the block hook");
        Eq(0f, new StatBlock().BlockArmor, "none set");
        Eq("+10% movement speed, +35 block armor", total.Summary(), "summary");

        var again = BundleFile.Parse(file.Serialize(_ => null));
        Eq(0, again.Warnings.Count, "round trip warnings: " + string.Join(" | ", again.Warnings));
        Eq(20f, again.Find("T").GetStats(BundleSlot.Shield).Scalars["blockArmor"], "round trip");

        var def = BundleStatCatalog.Find("blockArmor");
        Eq("Block armor", def.Label, "window label");
        Eq(BundleStatCatalog.General, def.Group, "General group");
        Eq(5f, def.Step, "step");
        Eq(-50f, def.Min, "window min");
        Eq(200f, def.Max, "window max");
    }

    private static void Test_BlockArmor_AddsToBaseNeverBelowOne()
    {
        Eq(60f, BundleStatCatalog.WithBlockArmor(40f, 20f), "added to the item's base block armor");
        Eq(40f, BundleStatCatalog.WithBlockArmor(40f, 0f), "no bonus: unchanged");
        // Humanoid.BlockAttack divides by block armor: 0 gives NaN stamina (saved with the character).
        Eq(1f, BundleStatCatalog.WithBlockArmor(10f, -50f), "never below 1");
        Eq(1f, BundleStatCatalog.WithBlockArmor(2f, -5f), "fists / knife (base 2) with one − click");
        Eq(0.5f, BundleStatCatalog.WithBlockArmor(0.5f, -5f), "an item already below 1 keeps its own value");
    }

    private static void Test_BlockForce_ParseSumAndSummary()
    {
        var file = BundleFile.Parse(
            "active: T\nbundles:\n  T:\n    shield:\n      stats: { blockForce: 20 }\n    chest:\n      stats: { BlockForce: 15 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Chest) });
        Eq(35f, total.BlockForce, "block force adds across slots");
        Eq(0f, new StatBlock().BlockForce, "none set");
        Eq("+35 block force", total.Summary(), "summary");
        Eq(20f, BundleFile.Parse(file.Serialize(_ => null)).Find("T").GetStats(BundleSlot.Shield).Scalars["blockForce"], "round trip");

        var def = BundleStatCatalog.Find("blockForce");
        Eq("Block force", def.Label, "window label");
        Eq(BundleStatCatalog.General, def.Group, "General group");
        Eq(StatKind.Blocker, def.Kind, "applied by the block hook");
        Eq(5f, def.Step, "step");
        Eq(-50f, def.Min, "window min");
        Eq(300f, def.Max, "window max");
    }

    private static void Test_BlockForce_NeverBelowZero()
    {
        Eq(60f, BundleStatCatalog.WithBlockForce(40f, 20f), "added to the item's own block force");
        Eq(40f, BundleStatCatalog.WithBlockForce(40f, 0f), "no bonus: unchanged");
        Eq(0f, BundleStatCatalog.WithBlockForce(40f, -50f), "never below 0 (it's only a push)");
    }

    // ---- bundle stats: added damage -----------------------------------------------------------

    private static void Test_AddDamage_ParseSumSummarySerialize()
    {
        var file = BundleFile.Parse(
            "active: T\nbundles:\n  T:\n    melee:\n      stats: { addDamage: { Spirit: 30, fire: 10 } }\n" +
            "    chest:\n      stats: { AddDamage: { spirit: 5 } }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var melee = bundle.GetStats(BundleSlot.Melee);
        Eq(2, melee.Count, "one stat per type");
        var total = StatBlock.Sum(new[] { melee, bundle.GetStats(BundleSlot.Chest) });
        Eq(35f, total.AddDamage["spirit"], "adds across slots");
        Eq(10f, total.AddDamage["fire"], "fire");
        Eq("added: fire +10, spirit +35", total.Summary(), "summary");

        var saved = file.Serialize(_ => null);
        True(saved.Contains("addDamage: { fire: 10, spirit: 30 }"), "serialized lowercase, in type order: " + saved);
        var again = BundleFile.Parse(saved);
        Eq(0, again.Warnings.Count, "round trip warnings: " + string.Join(" | ", again.Warnings));
        Eq(30f, again.Find("T").GetStats(BundleSlot.Melee).AddDamage["spirit"], "round trip");
    }

    private static void Test_AddDamage_Warnings()
    {
        var file = BundleFile.Parse(
            "bundles:\n  T:\n    melee:\n      stats: { addDamage: { spirit: 0, chop: 20, frost: abc, holy: 5, fire: -5, poison: 15 } }\n");
        Eq(5, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Warnings.Any(w => w.StartsWith("T.melee.addDamage.spirit: '0' must be more than 0")), "zero");
        True(file.Warnings.Any(w => w.StartsWith("T.melee.addDamage.chop: unknown or unsupported damage type")), "chop");
        True(file.Warnings.Any(w => w.StartsWith("T.melee.addDamage.frost: 'abc' is not a number")), "not a number");
        True(file.Warnings.Any(w => w.StartsWith("T.melee.addDamage.holy: unknown or unsupported damage type")), "unknown");
        True(file.Warnings.Any(w => w.StartsWith("T.melee.addDamage.fire: '-5' must be more than 0")), "negative");
        var stats = file.Find("T").GetStats(BundleSlot.Melee);
        Eq(1, stats.AddDamage.Count, "only poison kept");
        Eq(15f, stats.AddDamage["poison"], "poison");
    }

    private static void Test_AddDamage_AddsToWeaponDamage()
    {
        foreach (var type in BundleStatCatalog.AddDamageTypes)
            True(typeof(HitData.DamageTypes).GetField("m_" + type.ToLowerInvariant()) != null, $"{type} is a DamageTypes field");
        var weapon = new HitData.DamageTypes { m_pierce = 190, m_blunt = 180, m_chop = 80 };
        var result = StatBlock.AddDamageTo(weapon, new Dictionary<string, float> { ["spirit"] = 30, ["blunt"] = 20 });
        Eq(30f, result.m_spirit, "spirit added where there was none");
        Eq(200f, result.m_blunt, "blunt added to the existing value");
        Eq(190f, result.m_pierce, "other types unchanged");
        Eq(80f, result.m_chop, "chop unchanged");
        Eq(0f, weapon.m_spirit, "the input is a copy (struct)");

        foreach (var type in BundleStatCatalog.AddDamageTypes)
        {
            var key = type.ToLowerInvariant();
            var added = StatBlock.AddDamageTo(new HitData.DamageTypes(), new Dictionary<string, float> { [key] = 7 });
            Eq(7f, (float)typeof(HitData.DamageTypes).GetField("m_" + key).GetValue(added), $"{type} lands in m_{key}");
            var sum = typeof(HitData.DamageTypes).GetFields().Where(f => f.FieldType == typeof(float)).Sum(f => (float)f.GetValue(added));
            Eq(7f, sum, $"{type}: no other field changes");
        }
    }

    private static void Test_MissingHookWarnings()
    {
        var parry = new StatBlock();
        parry.Scalars["healOnParry"] = 20;
        var block = new StatBlock();
        block.Scalars["blockArmor"] = 10;
        var damage = new StatBlock();
        damage.AddDamage["spirit"] = 30;
        var all = new[] { parry, block, damage };
        Eq(0, BundleRules.MissingHookWarnings(all, true, true, true).Count, "all hooks installed");
        var w = BundleRules.MissingHookWarnings(all, false, false, false);
        Eq(3, w.Count, "warnings: " + string.Join(" | ", w));
        True(w[0].StartsWith("The bundle's on-parry stats can't work"), w[0]);
        True(w[1].StartsWith("The bundle's block armor / force can't work"), w[1]);
        True(w[2].StartsWith("The bundle's added damage can't work"), w[2]);
        Eq(0, BundleRules.MissingHookWarnings(new[] { new StatBlock() }, false, false, false).Count, "no stats that need a hook");
        var force = new StatBlock();
        force.Scalars["blockForce"] = 20;
        var forceOnly = BundleRules.MissingHookWarnings(new[] { force }, true, false, true);
        True(forceOnly.Count == 1 && forceOnly[0].StartsWith("The bundle's block armor / force can't work"), string.Join(" | ", forceOnly));
    }
}
