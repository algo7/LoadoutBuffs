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
            "active: T\nbuffs:\n  T:\n    chest:\n      effect: Vanguard\n" +
            "      stats: { movementSpeed: 10, CarryWeight: +50, armor: 15%, fallDamage: -100, healthRegen: 20, dodgeStamina: -15,\n" +
            "               resist: { fire: resistant, POISON: very resistant }, skills: { bows: 15 },\n" +
            "               fields: { m_swimSpeedModifier: 0.2, m_staggerModifier: -0.5 } }\n" +
            "    melee:\n      stats: { damage: { Slash: 10 } }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var entry = file.Find("T").Entries.Single(e => e.Slot == BundleSlot.Chest);
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
        Eq(15f, stats.Skills["Bows"], "skill");
        Eq(2, stats.Fields.Count, "raw fields");
        Eq(11, stats.Count, "count");
        Eq(10f, file.Find("T").GetStats(BundleSlot.Melee).Damage["slash"], "damage");
    }

    private static void Test_Stats_StatsOnlySlotAndShortFormTogether()
    {
        var state = Evaluate("active: T\nbuffs:\n  T:\n    helmet: Vanguard\n    legs:\n      stats: { movementSpeed: 10 }\n");
        Eq(0, state.Warnings.Count, "warnings: " + string.Join(" | ", state.Warnings));
        Eq("SetEffect_DeepNorthMediumArmor", state.Effects[BundleSlot.Helmet], "short form effect");
        False(state.Effects.ContainsKey(BundleSlot.Legs), "no effect on the stats-only slot");
        Eq(10f, state.Stats[BundleSlot.Legs].Scalars["movementSpeed"], "legs stats");
        Eq("Buff 'T' active: 1 effect, 1 stat", state.StatusLine(), "status");
    }

    private static void Test_Stats_BadEntriesWarnAndTheRestApplies()
    {
        var file = BundleFile.Parse(
            "buffs:\n  T:\n    melee:\n      stats: { speed: 5, carryWeight: lots, damage: { magic: 5 },\n" +
            "               skills: { Juggling: 5 }, fields: { m_nope: 1, m_icon: x, m_speedModifier: fast }, movementSpeed: 10 }\n      colour: red\n" +
            "    chest:\n      stats: { resist: { Wet: Resistant, Fire: Tough } }\n");
        Eq(10, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Warnings[0].StartsWith("T.melee.speed: unknown stat") && file.Warnings[0].Contains("movementSpeed"), file.Warnings[0]);
        True(file.Warnings[1].StartsWith("T.melee.carryWeight: 'lots' is not a number"), file.Warnings[1]);
        True(file.Warnings[2].StartsWith("T.melee.damage.magic: unknown damage type"), file.Warnings[2]);
        True(file.Warnings[3].StartsWith("T.melee.skills.Juggling: unknown skill"), file.Warnings[3]);
        True(file.Warnings[4].StartsWith("T.melee.fields.m_nope: 'm_nope' is not a field of SE_Stats"), file.Warnings[4]);
        True(file.Warnings[5].StartsWith("T.melee.fields.m_icon") && file.Warnings[5].Contains("can't be set"), file.Warnings[5]);
        True(file.Warnings[6].StartsWith("T.melee.fields.m_speedModifier:"), file.Warnings[6]);
        True(file.Warnings[7].StartsWith("T.melee.colour: unknown key"), file.Warnings[7]);
        True(file.Warnings[8].StartsWith("T.chest.resist.Wet: unknown damage type"), file.Warnings[8]);
        True(file.Warnings[9].StartsWith("T.chest.resist.Fire: 'Tough' is not one of"), file.Warnings[9]);
        var entry = file.Find("T").Entries.Single();
        Eq(10f, entry.Stats.Scalars["movementSpeed"], "the valid stat still applies");
        Eq(1, entry.Stats.Count, "only the valid stat");
    }

    private static void Test_Stats_UnknownSlotKeyWarns()
    {
        var file = BundleFile.Parse("buffs:\n  T:\n    chest:\n      colour: red\n      stats: { armor: 5 }\n");
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
        Eq(20f, total.Damage["slash"], "damage adds within a buff");
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
            "active: T\nbuffs:\n  T:\n    helmet: SlowFall\n    chest:\n      effect: SetEffect_DeepNorthMediumArmor\n" +
            "      stats: { armor: 15, movementSpeed: 12.5, resist: { Poison: VeryResistant, Fire: Resistant }, skills: { Bows: 15 }, fields: { m_staggerModifier: -0.5 } }\n" +
            "    legs:\n      stats: { runStamina: -20 }\n    melee:\n      stats: { damage: { slash: 10 } }\n";
        var file = BundleFile.Parse(text);
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var names = new System.Collections.Generic.Dictionary<string, string> { ["SlowFall"] = "Feather fall", ["SetEffect_DeepNorthMediumArmor"] = "Vanguard" };
        var saved = file.Serialize(code => names.TryGetValue(code, out var n) ? n : null);

        True(saved.Contains("    helmet: SlowFall") && saved.Contains("# Feather fall"), saved);
        True(saved.Contains("      effect: SetEffect_DeepNorthMediumArmor") && saved.Contains("# Vanguard"), saved);
        True(saved.Contains("      stats: { movementSpeed: 12.5, armor: 15, resist: { Fire: Resistant, Poison: VeryResistant }, skills: { Bows: 15 }, fields: { m_staggerModifier: \"-0.5\" } }"), saved);
        True(saved.Contains("    melee:\n      stats: { damage: { slash: 10 } }"), saved);
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
            if (def.Kind == StatKind.Blocker || def.Kind == StatKind.EitrCost || def.Kind == StatKind.ParryBonus)
            {
                True(def.Field == null, $"{def.Key}: applied by a hook, no SE_Stats field");
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
            Eq(expected, BundleSlots.HandSlot(type, skill, usedOnAllies: false), $"{type}/{skill}");

        Is(BundleSlot.Ranged, ItemDrop.ItemData.ItemType.Bow, Skills.SkillType.Bows);                    // bows
        Is(BundleSlot.Ranged, ItemDrop.ItemData.ItemType.Bow, Skills.SkillType.Crossbows);               // crossbows are Bow type
        Is(BundleSlot.Ranged, ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.ElementalMagic); // Staff of Embers
        Is(BundleSlot.Ranged, ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft, Skills.SkillType.BloodMagic); // Dead Raiser
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

    private static void Test_Slots_WeaponsUsedOnAlliesFillNoSlot()
    {
        // Staff of Protection, Northern Vengeance: their spell hits you, players and tamed, never enemies, and carries
        // the weapon's damage, so the Ranged slot's Added damage would hurt them. The Abyssal Harpoon drags tamed
        // animals (its shot hits them with PvP on), so the Melee slot's Added damage would kill them.
        Eq(null, BundleSlots.HandSlot(ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.BloodMagic, usedOnAllies: true), "Staff of Protection");
        Eq(null, BundleSlots.HandSlot(ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft, Skills.SkillType.BloodMagic, usedOnAllies: true), "Northern Vengeance");
        Eq(null, BundleSlots.HandSlot(ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.ElementalMagic, usedOnAllies: true), "elemental");
        Eq(null, BundleSlots.HandSlot(ItemDrop.ItemData.ItemType.OneHandedWeapon, Skills.SkillType.Swords, usedOnAllies: true), "any weapon");
        Eq(BundleSlot.Ranged, BundleSlots.HandSlot(ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.BloodMagic, usedOnAllies: false), "other staffs");
        Eq(null, BundleSlots.HandSlot(ItemDrop.ItemData.ItemType.OneHandedWeapon, Skills.SkillType.Spears, usedOnAllies: true), "Abyssal Harpoon");
        Eq(BundleSlot.Melee, BundleSlots.HandSlot(ItemDrop.ItemData.ItemType.OneHandedWeapon, Skills.SkillType.Spears, usedOnAllies: false), "other spears");
        Eq(BundleSlot.Shield, BundleSlots.HandSlot(ItemDrop.ItemData.ItemType.Shield, Skills.SkillType.Blocking, usedOnAllies: true), "shields have no spell");
    }

    private static void Test_Slots_RemovedSlotsWarnWithAHint()
    {
        var file = BundleFile.Parse("buffs:\n  T:\n    weapon: SlowFall\n    Utility: Wishbone\n    trinket: Demister\n    ranged: SetEffect_RootArmor\n");
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
            "active: T\nbuffs:\n  T:\n    shield:\n      stats: { healAlliesOnParry: 40, staminaAlliesOnParry: 30, parryRadius: 15 }\n" +
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
        var state = Evaluate("active: T\nbuffs:\n  T:\n    shield:\n      stats: { healAlliesOnParry: 40, parryRadius: 15 }\n");
        Eq(0, state.Warnings.Count, "warnings: " + string.Join(" | ", state.Warnings));
        Eq("Buff 'T' active: 0 effects, 2 stats", state.StatusLine(), "status");
    }

    private static void Test_ParryStats_Bubble()
    {
        var file = BundleFile.Parse(
            "active: T\nbuffs:\n  T:\n    shield:\n      stats: { shieldOnParry: 300, parryRadius: 15 }\n" +
            "    melee:\n      stats: { ShieldOnParry: 200 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Melee) });
        Eq(500f, total.Scalars["shieldOnParry"], "bubbles add across slots");

        var assist = total.ToParryAssist();
        True(assist != null, "a bubble alone is an on-parry stat");
        Eq(500f, assist.Shield, "bubble");
        Eq(15f, assist.Radius, "reach");
        Eq(15f, assist.TamedRadius, "tamed share the reach");
        Eq("on parry: bubble health +500, reach 15 m", total.Summary(), "summary");

        var again = BundleFile.Parse(file.Serialize(_ => null));
        Eq(0, again.Warnings.Count, "round trip warnings: " + string.Join(" | ", again.Warnings));
        Eq(300f, again.Find("T").GetStats(BundleSlot.Shield).Scalars["shieldOnParry"], "round trip keeps the bubble");

        var def = BundleStatCatalog.Find("shieldOnParry");
        Eq("Bubble health", def.Label, "window label");
        Eq(100f, def.Step, "step");
        Eq(3000f, def.Max, "window max");
    }

    private static void Test_ParryStats_BubbleTime()
    {
        var file = BundleFile.Parse(
            "active: T\nbuffs:\n  T:\n    shield:\n      stats: { shieldOnParry: 300, shieldMinutes: 3 }\n" +
            "    melee:\n      stats: { ShieldMinutes: 2 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Melee) });
        Eq(3f, total.Scalars["shieldMinutes"], "the longest time, not the sum");
        Eq(3f, total.ToParryAssist().ShieldMinutes, "assist");
        Eq("on parry: bubble health +300, bubble time 3 min", total.Summary(), "summary");
        Eq(null, bundle.GetStats(BundleSlot.Melee).ToParryAssist(), "a time without a bubble does nothing");

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
            "buffs:\n  T:\n    shield:\n      stats: { shieldOnParry: -500, healAlliesOnParry: 0, parryRadius: -5, healOnParry: 20 }\n");
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
            "active: T\nbuffs:\n  T:\n    melee:\n      stats: { Woodcutter: true, miner: yes, carryWeight: 5 }\n" +
            "    ranged:\n      stats: { woodcutter: on, miner: false }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        Eq(3, bundle.GetStats(BundleSlot.Melee).Count, "melee: two classes + carry weight");
        Eq(1, bundle.GetStats(BundleSlot.Ranged).Count, "ranged: one class (miner: false sets nothing)");

        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Melee), bundle.GetStats(BundleSlot.Ranged) });
        Eq(1f, total.Scalars["woodcutter"], "a class from two slots is still just on");
        Eq("+5 carry weight; class: Woodcutter, Miner", total.Summary(), "summary");

        var saved = file.Serialize(null);
        True(saved.Contains("stats: { carryWeight: 5, woodcutter: true, miner: true }"), saved);
        True(saved.Contains("    ranged:\n      stats: { woodcutter: true }"), saved);
        var again = BundleFile.Parse(saved);
        Eq(0, again.Warnings.Count, "re-parse warnings: " + string.Join(" | ", again.Warnings));
        Eq(saved, again.Serialize(null), "stable");
    }

    private static void Test_Classes_OnlyOnWeaponSlots()
    {
        var file = BundleFile.Parse(
            "buffs:\n  T:\n    helmet:\n      stats: { woodcutter: true }\n    chest:\n      stats: { miner: true, armor: 5 }\n" +
            "    legs:\n      stats: { woodcutter: true }\n    cape:\n      stats: { miner: true }\n" +
            "    shield:\n      effect: Sneaky\n      stats: { woodcutter: true }\n");
        Eq(5, file.Warnings.Count, "one warning per armor / shield class: " + string.Join(" | ", file.Warnings));
        Eq("T.chest.miner: only works on the melee or ranged slot; skipped (line 6).", file.Warnings[1], "warning text");
        True(file.Warnings[4].StartsWith("T.shield.woodcutter: only works on the melee or ranged slot"), file.Warnings[4]);

        var bundle = file.Find("T");
        Eq(null, bundle.GetStats(BundleSlot.Helmet), "helmet: nothing left");
        Eq(1, bundle.GetStats(BundleSlot.Chest).Count, "chest keeps its armor only");
        False(bundle.GetStats(BundleSlot.Chest).Scalars.ContainsKey("miner"), "chest: no class");
        Eq(null, bundle.GetStats(BundleSlot.Shield), "shield: class dropped");
        Eq("Sneaky", bundle.Entries.Single(e => e.Slot == BundleSlot.Shield).Effect, "shield keeps its effect");
        var body = file.Serialize(null);
        body = body.Substring(body.IndexOf("\nbuffs:", StringComparison.Ordinal));
        False(body.Contains("woodcutter") || body.Contains("miner"), "the window's save drops them: " + body);

        foreach (var def in BundleStatCatalog.Toggles)
            foreach (var slot in BundleSlots.All)
                Eq(slot == BundleSlot.Melee || slot == BundleSlot.Ranged, def.Allows(slot), $"{def.Key} on {slot}");
        Eq("movementSpeed, carryWeight, runStamina, jumpStamina, attackStamina, blockStamina, dodgeStamina, eitrCost",
            string.Join(", ", BundleStatCatalog.Scalars.Where(d => BundleSlots.All.All(d.Allows)).Select(d => d.Key)),
            "the stats every slot takes");
    }

    private static void Test_Classes_MisplacedAreCleanedUp()
    {
        // A 1.0.0 file with classes on armor / shield: the only problems, so the mod may save it without them.
        var file = BundleFile.Parse(
            "active: Bro\nbuffs:\n  Bro:\n    chest:\n      stats: { woodcutter: true, armor: 5, damage: { slash: 10 }, healOnParry: 20 }\n" +
            "    shield:\n      stats: { miner: true, addDamage: { spirit: 30 } }\n    melee:\n      stats: { miner: true }\n");
        Eq(5, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.OnlyMisplaced, "only misplaced classes, damage and parry stats");
        Eq("Woodcutter from Bro's chest; it only goes on the melee or ranged slot now | " +
           "Damage % from Bro's chest; it only goes on the melee or ranged slot now | " +
           "Heal you from Bro's chest; it only goes on the melee, ranged or shield slot now | " +
           "Miner from Bro's shield; it only goes on the melee or ranged slot now | " +
           "Added damage from Bro's shield; it only goes on the melee or ranged slot now",
            string.Join(" | ", file.Misplaced), "notes");

        var again = BundleFile.Parse(file.Serialize(null));
        Eq(0, again.Warnings.Count, "saved without them: " + string.Join(" | ", again.Warnings));
        False(again.OnlyMisplaced, "nothing left to clean up");
        Eq(5f, again.Find("Bro").GetStats(BundleSlot.Chest).Scalars["armor"], "the chest keeps its other stats");
        Eq(1, again.Find("Bro").GetStats(BundleSlot.Chest).Count, "and nothing else");
        Eq(null, again.Find("Bro").GetStats(BundleSlot.Shield), "shield: nothing left");
        Eq(1f, again.Find("Bro").GetStats(BundleSlot.Melee).Scalars["miner"], "melee keeps its class");

        // Any other problem: hands off (saving would also drop that entry).
        var typo = BundleFile.Parse("buffs:\n  T:\n    chest:\n      stats: { woodcutter: true, armr: 5 }\n");
        Eq(2, typo.Warnings.Count, "class + typo");
        False(typo.OnlyMisplaced, "a typo blocks the cleanup");
        False(BundleFile.Parse("buffs:\n  T:\n    melee:\n      stats: { woodcutter: true }\n").OnlyMisplaced, "clean file");
    }

    private static void Test_Damage_OnlyOnWeaponSlots()
    {
        var file = BundleFile.Parse(
            "buffs:\n  T:\n    helmet:\n      stats: { damage: { slash: 10 }, armor: 5 }\n    legs:\n      stats: { addDamage: { spirit: 30 } }\n" +
            "    shield:\n      stats: { blockArmor: 20, damage: { blunt: 5 } }\n" +
            "    melee:\n      stats: { damage: { slash: 10 }, addDamage: { spirit: 30 } }\n    ranged:\n      stats: { damage: { pierce: 10 } }\n");
        Eq(3, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        Eq("T.helmet.damage: only works on the melee or ranged slot; skipped (line 4).", file.Warnings[0], "damage %");
        Eq("T.legs.addDamage: only works on the melee or ranged slot; skipped (line 6).", file.Warnings[1], "added damage");
        True(file.Warnings[2].StartsWith("T.shield.damage: only works on the melee or ranged slot"), file.Warnings[2]);
        var bundle = file.Find("T");
        Eq(0, bundle.GetStats(BundleSlot.Helmet).Damage.Count, "helmet: no damage");
        Eq(5f, bundle.GetStats(BundleSlot.Helmet).Scalars["armor"], "helmet keeps armor");
        Eq(null, bundle.GetStats(BundleSlot.Legs), "legs: nothing left");
        Eq(20f, bundle.GetStats(BundleSlot.Shield).BlockArmor, "shield keeps block armor");
        Eq(10f, bundle.GetStats(BundleSlot.Melee).Damage["slash"], "melee damage %");
        Eq(30f, bundle.GetStats(BundleSlot.Melee).AddDamage["spirit"], "melee added damage");
        Eq(10f, bundle.GetStats(BundleSlot.Ranged).Damage["pierce"], "ranged damage %");
        foreach (var slot in BundleSlots.All)
            Eq(slot == BundleSlot.Melee || slot == BundleSlot.Ranged, BundleStatCatalog.IsWeaponSlot(slot), $"weapon slot: {slot}");

        var caps = BundleFile.Parse("buffs:\n  T:\n    chest:\n      stats: { DAMAGE: { slash: 10 }, AddDamage: { fire: 5 } }\n");
        Eq(2, caps.Warnings.Count, "any key case: " + string.Join(" | ", caps.Warnings));
        True(caps.Warnings[0].StartsWith("T.chest.damage: only works") && caps.Warnings[1].StartsWith("T.chest.addDamage: only works"),
            "warnings use the file's key names: " + string.Join(" | ", caps.Warnings));
        True(caps.OnlyMisplaced, "cleaned up too");
    }

    private static void Test_Parry_OnlyOnHandSlots()
    {
        var file = BundleFile.Parse(
            "buffs:\n  T:\n    helmet:\n      stats: { healOnParry: 20, armor: 5 }\n    chest:\n      stats: { shieldOnParry: 300, shieldMinutes: 2 }\n" +
            "    legs:\n      stats: { parryRadius: 15 }\n    cape:\n      effect: Feather fall\n      stats: { staminaAlliesOnParry: 10 }\n" +
            "    melee:\n      stats: { healOnParry: 20 }\n    ranged:\n      stats: { healAlliesOnParry: 30 }\n" +
            "    shield:\n      stats: { shieldOnParry: 300, parryRadius: 15 }\n");
        Eq(5, file.Warnings.Count, "one warning per parry stat on armor / cape: " + string.Join(" | ", file.Warnings));
        Eq("T.helmet.healOnParry: only works on the melee, ranged or shield slot; skipped (line 4).", file.Warnings[0], "warning text");
        True(file.Warnings[4].StartsWith("T.cape.staminaAlliesOnParry: only works on the melee, ranged or shield slot"), file.Warnings[4]);
        True(file.OnlyMisplaced, "cleaned up like classes and damage");

        var bundle = file.Find("T");
        Eq(1, bundle.GetStats(BundleSlot.Helmet).Count, "helmet keeps its armor only");
        Eq(5f, bundle.GetStats(BundleSlot.Helmet).Scalars["armor"], "helmet armor");
        Eq(null, bundle.GetStats(BundleSlot.Chest), "chest: nothing left");
        Eq(null, bundle.GetStats(BundleSlot.Legs), "legs: nothing left");
        Eq("Feather fall", bundle.Entries.Single(e => e.Slot == BundleSlot.Cape).Effect, "cape keeps its effect");
        Eq(20f, bundle.GetStats(BundleSlot.Melee).Scalars["healOnParry"], "melee keeps its parry heal");
        Eq(30f, bundle.GetStats(BundleSlot.Ranged).Scalars["healAlliesOnParry"], "ranged keeps its ally heal");
        Eq(300f, bundle.GetStats(BundleSlot.Shield).Scalars["shieldOnParry"], "shield keeps its bubble");

        foreach (var def in BundleStatCatalog.Scalars.Where(d => d.Group == BundleStatCatalog.OnParry))
        {
            Eq("melee, ranged or shield slot", def.WhereAllowed, $"{def.Key}: where");
            foreach (var slot in BundleSlots.All)
                Eq(slot == BundleSlot.Melee || slot == BundleSlot.Ranged || slot == BundleSlot.Shield, def.Allows(slot), $"{def.Key} on {slot}");
        }
        Eq(9, BundleStatCatalog.Scalars.Count(d => d.Group == BundleStatCatalog.OnParry), "all 9 parry stats");
        Eq("melee or ranged slot", BundleStatCatalog.Find("woodcutter").WhereAllowed, "two slots: no comma");
        Eq("any slot", BundleStatCatalog.Find("carryWeight").WhereAllowed, "no limit");

        var zero = BundleFile.Parse("buffs:\n  T:\n    chest:\n      stats: { healOnParry: 0 }\n");
        Eq(1, zero.Warnings.Count, "the slot check comes first: " + string.Join(" | ", zero.Warnings));
        True(zero.Warnings[0].StartsWith("T.chest.healOnParry: only works on"), zero.Warnings[0]);
        True(zero.OnlyMisplaced, "so it's still cleaned up");
    }

    private static void Test_ArmorStats_OnlyOnArmorSlots()
    {
        var file = BundleFile.Parse(
            "buffs:\n  T:\n    helmet:\n      stats: { healthRegen: 20, resist: { Fire: Resistant }, fallDamage: -50 }\n" +
            "    cape:\n      stats: { eitrRegen: 10 }\n" +
            "    melee:\n      stats: { staminaRegen: 50, movementSpeed: 5 }\n    ranged:\n      stats: { resist: { Blunt: SlightlyResistant }, fallDamage: -20 }\n" +
            "    shield:\n      stats: { healthRegen: 10, resist: { Pierce: Resistant }, blockArmor: 20 }\n");
        Eq(5, file.Warnings.Count, "one warning per regen / resist / fall damage on a hand slot: " + string.Join(" | ", file.Warnings));
        Eq("T.melee.staminaRegen: only works on the helmet, chest, legs or cape slot; skipped (line 8).", file.Warnings[0], "regen");
        Eq("T.ranged.resist: only works on the helmet, chest, legs or cape slot; skipped (line 10).", file.Warnings[1], "resist");
        Eq("T.ranged.fallDamage: only works on the helmet, chest, legs or cape slot; skipped (line 10).", file.Warnings[2], "fall damage");
        True(file.Warnings[3].StartsWith("T.shield.healthRegen: only works on the helmet, chest, legs or cape slot"), file.Warnings[3]);
        True(file.Warnings[4].StartsWith("T.shield.resist: only works on the helmet, chest, legs or cape slot"), file.Warnings[4]);
        True(file.OnlyMisplaced, "cleaned up like classes and damage");
        Eq("Stamina regen from T's melee; it only goes on the helmet, chest, legs or cape slot now | " +
           "Resistances from T's ranged; it only goes on the helmet, chest, legs or cape slot now | " +
           "Fall damage from T's ranged; it only goes on the helmet, chest, legs or cape slot now | " +
           "Health regen from T's shield; it only goes on the helmet, chest, legs or cape slot now | " +
           "Resistances from T's shield; it only goes on the helmet, chest, legs or cape slot now",
            string.Join(" | ", file.Misplaced), "notes");

        var bundle = file.Find("T");
        Eq(20f, bundle.GetStats(BundleSlot.Helmet).Scalars["healthRegen"], "helmet keeps its regen");
        Eq(-50f, bundle.GetStats(BundleSlot.Helmet).Scalars["fallDamage"], "helmet keeps its fall damage");
        Eq("Resistant", bundle.GetStats(BundleSlot.Helmet).Resist["Fire"], "helmet keeps its resistance");
        Eq(10f, bundle.GetStats(BundleSlot.Cape).Scalars["eitrRegen"], "the cape counts as armor");
        Eq(1, bundle.GetStats(BundleSlot.Melee).Count, "melee keeps its movement speed only");
        Eq(null, bundle.GetStats(BundleSlot.Ranged), "ranged: nothing left");
        Eq(1, bundle.GetStats(BundleSlot.Shield).Count, "shield keeps its block armor only");

        var armor = new[] { BundleSlot.Helmet, BundleSlot.Chest, BundleSlot.Legs, BundleSlot.Cape };
        foreach (var slot in BundleSlots.All)
            Eq(armor.Contains(slot), BundleStatCatalog.IsArmorSlot(slot), $"armor slot: {slot}");
        foreach (var def in BundleStatCatalog.Scalars.Where(d => d.Group == BundleStatCatalog.Regen || d.Key == "fallDamage" || d.Key == "armor"))
            foreach (var slot in BundleSlots.All)
                Eq(armor.Contains(slot), def.Allows(slot), $"{def.Key} on {slot}");
        Eq(3, BundleStatCatalog.Scalars.Count(d => d.Group == BundleStatCatalog.Regen), "all 3 regen stats");
    }

    private static void Test_ParryBonus_ParseSumSerialize()
    {
        var file = BundleFile.Parse(
            "active: T\nbuffs:\n  T:\n    shield:\n      stats: { parryBonus: 2 }\n    melee:\n      stats: { ParryBonus: 1.5, healOnParry: 20 }\n" +
            "    ranged:\n      stats: { parryBonus: -1 }\n    chest:\n      stats: { parryBonus: 3, armor: 5 }\n");
        Eq(2, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        Eq("T.ranged.parryBonus: '-1' must be more than 0; skipped (line 9).", file.Warnings[0], "no negative bonus");
        Eq("T.chest.parryBonus: only works on the melee, ranged or shield slot; skipped (line 11).", file.Warnings[1], "wrong slot");

        var bundle = file.Find("T");
        Eq(null, bundle.GetStats(BundleSlot.Ranged), "the negative bonus is skipped");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Melee) });
        Eq(3.5f, total.Scalars["parryBonus"], "sword + shield in hand: both count");
        Eq("on parry: parry bonus +3.5, heal you +20", total.Summary(), "summary");
        Eq(null, bundle.GetStats(BundleSlot.Shield).ToParryAssist(), "a bonus alone helps no one else");
        Eq(20f, StatBlock.Sum(new[] { Block("parryBonus", 15), Block("parryBonus", 10) }).Scalars["parryBonus"], "the total stops at +20");

        var def = BundleStatCatalog.Find("parryBonus");
        Eq("Parry bonus", def.Label, "window label");
        Eq(BundleStatCatalog.OnParry, def.Group, "On parry group");
        True(BundleStatCatalog.Scalars.First(d => d.Group == BundleStatCatalog.OnParry) == def, "first row of On parry");
        Eq(StatKind.ParryBonus, def.Kind, "applied by the buff's own parry step, no SE_Stats field");
        Eq(null, def.Field, "no field");
        Eq(0.5f, def.Step, "step");
        Eq(0f, def.Min, "min");
        Eq(20f, def.Max, "max");
        Eq("", def.Unit, "shown as +2");
        False(def.TakesLargest, "slots add up");

        var saved = file.Serialize(null);
        True(saved.Contains("    melee:\n      stats: { parryBonus: 1.5, healOnParry: 20 }"), saved);
        Eq(saved, BundleFile.Parse(saved).Serialize(null), "stable");
    }

    private static void Test_ParryBonus_AboveTheMaxLoadsAsTheMax()
    {
        // A value past +20 (e.g. 200 from a percent test build) works as +20: the window shows and saves what applies.
        var file = BundleFile.Parse("buffs:\n  T:\n    melee:\n      stats: { parryBonus: 200 }\n    ranged:\n      stats: { parryBonus: 119 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        Eq(20f, file.Find("T").GetStats(BundleSlot.Melee).Scalars["parryBonus"], "200 → 20");
        Eq(20f, file.Find("T").GetStats(BundleSlot.Ranged).Scalars["parryBonus"], "119 → 20");
        True(file.Serialize(null).Contains("    melee:\n      stats: { parryBonus: 20 }"), "saved as 20");
    }

    private static void Test_ParryBonus_AddsToTheItemsMultiplier()
    {
        Eq(4.5f, BundleStatCatalog.WithParryBonus(2.5f, 2f), "buckler 2.5x + 2 = 4.5x");
        Eq(1.5f, BundleStatCatalog.WithParryBonus(1.5f, 0f), "no bonus: the item's own");
        Eq(22.5f, BundleStatCatalog.WithParryBonus(2.5f, 25f), "a hand-edited bonus stops at +20");
        // Humanoid.BlockAttack divides a parry's durability drain by the multiplier: never below the item's own.
        Eq(2.5f, BundleStatCatalog.WithParryBonus(2.5f, -5f), "never less than the item's own");
    }

    private static void Test_ParryBonus_ShowsInTheItemTooltip()
    {
        // ItemData.AddBlockTooltip writes the item's own parry multiplier; the buff's bonus goes into that number,
        // like added damage and block armor go into theirs.
        var before = "Old text\n$item_parrybonus: <color=orange>2.5x</color>";
        var text = new System.Text.StringBuilder(before);
        var start = text.Length;
        text.AppendFormat("\n$item_blockforce: <color=orange>{0}</color>", 50f);
        text.AppendFormat("\n$item_parrybonus: <color=orange>{0}x</color>", 2.5f);
        text.AppendFormat("\n$item_parryadrenaline: <color=orange>{0}</color>", 5f);

        True(BundleStatCatalog.ShowParryBonus(text, start, 2.5f, 2f), "found the line");
        var shown = text.ToString();
        True(shown.EndsWith("\n$item_blockforce: <color=orange>50</color>\n$item_parrybonus: <color=orange>4.5x</color>\n$item_parryadrenaline: <color=orange>5</color>"), shown);
        True(shown.StartsWith(before), "only the block part is touched: " + shown);

        var none = new System.Text.StringBuilder("\n$item_blockforce: <color=orange>50</color>");
        False(BundleStatCatalog.ShowParryBonus(none, 0, 2.5f, 2f), "no parry line (a tower shield): nothing to change");
    }

    private static void Test_Classes_BadValueWarns()
    {
        var file = BundleFile.Parse("buffs:\n  T:\n    melee:\n      stats: { woodcutter: maybe }\n");
        Eq(1, file.Warnings.Count, "warnings");
        True(file.Warnings[0].StartsWith("T.melee.woodcutter: 'maybe' is not true or false"), file.Warnings[0]);
    }

    // ---- bundle stats: block armor ------------------------------------------------------------

    // ---- bundle stats: eitr cost ---------------------------------------------------------------

    private static void Test_EitrCost_ParseSumSerialize()
    {
        var file = BundleFile.Parse(
            "active: T\nbuffs:\n  T:\n    helmet:\n      stats: { eitrCost: -20, runStamina: -10 }\n" +
            "    ranged:\n      stats: { eitrcost: -15 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Helmet), bundle.GetStats(BundleSlot.Ranged) });
        Eq(-35f, total.EitrCost, "slots add up");
        Eq("-35% eitr cost; stamina cost: run -10%", total.Summary(), "summary");

        var def = BundleStatCatalog.Find("eitrCost");
        Eq(BundleStatCatalog.Costs, def.Group, "in the Costs group");
        True(def.LowerIsBetter && def.Helps(-20f) && !def.Helps(20f), "cheaper is better");
        Eq(null, def.Field, "no SE_Stats field: read by the eitr hook");
        True(BundleStatCatalog.Scalars.Last(d => d.Group == BundleStatCatalog.Costs) == def, "last row of Costs");

        var saved = file.Serialize(null);
        True(saved.Contains("stats: { runStamina: -10, eitrCost: -20 }"), saved);
        Eq(saved, BundleFile.Parse(saved).Serialize(null), "stable");
    }

    private static void Test_EitrCost_Math()
    {
        Eq(80f, BundleStatCatalog.WithEitrCost(100f, -20f), "-20 % → 0.8×");
        Eq(150f, BundleStatCatalog.WithEitrCost(100f, 50f), "+50 % → 1.5×");
        Eq(0f, BundleStatCatalog.WithEitrCost(35f, -100f), "-100 % → free");
        Eq(0f, BundleStatCatalog.WithEitrCost(35f, -150f), "never negative (a raw file value past the window's limit)");
        Eq(35f, BundleStatCatalog.WithEitrCost(35f, 0f), "no stat, no change");
    }

    private static void Test_BlockArmor_ParseSumAndSummary()
    {
        var file = BundleFile.Parse(
            "active: T\nbuffs:\n  T:\n    shield:\n      stats: { blockArmor: 20 }\n" +
            "    melee:\n      stats: { BlockArmor: 15, movementSpeed: 10 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Melee) });
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
        Eq(1000f, def.Max, "window max");
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
            "active: T\nbuffs:\n  T:\n    shield:\n      stats: { blockForce: 20 }\n    melee:\n      stats: { BlockForce: 15 }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Shield), bundle.GetStats(BundleSlot.Melee) });
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

    private static void Test_Block_OnlyOnHandSlots()
    {
        var file = BundleFile.Parse(
            "buffs:\n  T:\n    helmet:\n      stats: { blockArmor: 20, armor: 5 }\n    cape:\n      stats: { blockForce: 15 }\n" +
            "    melee:\n      stats: { blockArmor: 10 }\n    ranged:\n      stats: { blockForce: 10 }\n" +
            "    shield:\n      stats: { blockArmor: 30, blockForce: 25 }\n");
        Eq(2, file.Warnings.Count, "one warning per block stat on armor: " + string.Join(" | ", file.Warnings));
        Eq("T.helmet.blockArmor: only works on the melee, ranged or shield slot; skipped (line 4).", file.Warnings[0], "block armor");
        Eq("T.cape.blockForce: only works on the melee, ranged or shield slot; skipped (line 6).", file.Warnings[1], "block force");
        True(file.OnlyMisplaced, "cleaned up like the other slot rules");
        Eq("Block armor from T's helmet; it only goes on the melee, ranged or shield slot now | " +
           "Block force from T's cape; it only goes on the melee, ranged or shield slot now",
            string.Join(" | ", file.Misplaced), "notes");

        var bundle = file.Find("T");
        Eq(1, bundle.GetStats(BundleSlot.Helmet).Count, "helmet keeps its armor only");
        Eq(null, bundle.GetStats(BundleSlot.Cape), "cape: nothing left");
        Eq(10f, bundle.GetStats(BundleSlot.Melee).BlockArmor, "melee keeps block armor");
        Eq(10f, bundle.GetStats(BundleSlot.Ranged).BlockForce, "ranged keeps block force");
        Eq(30f, bundle.GetStats(BundleSlot.Shield).BlockArmor, "shield keeps block armor");
        foreach (var def in BundleStatCatalog.Scalars.Where(d => d.Kind == StatKind.Blocker))
            foreach (var slot in BundleSlots.All)
                Eq(slot == BundleSlot.Melee || slot == BundleSlot.Ranged || slot == BundleSlot.Shield, def.Allows(slot), $"{def.Key} on {slot}");
        Eq(2, BundleStatCatalog.Scalars.Count(d => d.Kind == StatKind.Blocker), "block armor and block force");
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
            "active: T\nbuffs:\n  T:\n    melee:\n      stats: { addDamage: { Spirit: 30, fire: 10 } }\n" +
            "    ranged:\n      stats: { AddDamage: { spirit: 5 } }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        var melee = bundle.GetStats(BundleSlot.Melee);
        Eq(2, melee.Count, "one stat per type");
        var total = StatBlock.Sum(new[] { melee, bundle.GetStats(BundleSlot.Ranged) });
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
            "buffs:\n  T:\n    melee:\n      stats: { addDamage: { spirit: 0, chop: 20, frost: abc, holy: 5, fire: -5, poison: 15 } }\n");
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
        var eitr = new StatBlock();
        eitr.Scalars["eitrCost"] = -20;
        var all = new[] { parry, block, damage, eitr };
        Eq(0, BundleRules.MissingHookWarnings(all, true, true, true, true).Count, "all hooks installed");
        var w = BundleRules.MissingHookWarnings(all, false, false, false, false);
        Eq(4, w.Count, "warnings: " + string.Join(" | ", w));
        True(w[0].StartsWith("The buff's on-parry stats can't work"), w[0]);
        True(w[1].StartsWith("The buff's block armor / force can't work"), w[1]);
        True(w[2].StartsWith("The buff's added damage can't work"), w[2]);
        True(w[3].StartsWith("The buff's eitr cost can't work"), w[3]);
        Eq(0, BundleRules.MissingHookWarnings(new[] { new StatBlock() }, false, false, false, false).Count, "no stats that need a hook");
        var force = new StatBlock();
        force.Scalars["blockForce"] = 20;
        var forceOnly = BundleRules.MissingHookWarnings(new[] { force }, true, false, true, true);
        True(forceOnly.Count == 1 && forceOnly[0].StartsWith("The buff's block armor / force can't work"), string.Join(" | ", forceOnly));
    }
}
