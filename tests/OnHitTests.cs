using System;
using System.Linq;
using LoadoutBuffs;

internal static partial class Tests
{
    // ---- bundle stats: on-hit effects (Bleeding, Thundering, Primal) ------------------------

    private static void Test_OnHit_ParseSumSerialize()
    {
        var file = BundleFile.Parse(
            "active: T\nbuffs:\n  T:\n    melee:\n      stats: { Bleeding: true, thundering: on, woodcutter: true }\n" +
            "    ranged:\n      stats: { primal: yes, thundering: false }\n");
        Eq(0, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        var bundle = file.Find("T");
        Eq(3, bundle.GetStats(BundleSlot.Melee).Count, "melee: two on-hit switches + a class");
        Eq(1, bundle.GetStats(BundleSlot.Ranged).Count, "ranged: primal (thundering: false sets nothing)");

        var total = StatBlock.Sum(new[] { bundle.GetStats(BundleSlot.Melee), bundle.GetStats(BundleSlot.Ranged),
                                          bundle.GetStats(BundleSlot.Melee) });
        Eq(1f, total.Scalars["bleeding"], "a switch from two slots is still just on");
        Eq("class: Woodcutter; on hit: Bleeding, Thundering, Primal", total.Summary(), "summary");

        var saved = file.Serialize(null);
        True(saved.Contains("    melee:\n      stats: { woodcutter: true, bleeding: true, thundering: true }"), saved);
        True(saved.Contains("    ranged:\n      stats: { primal: true }"), saved);
        var again = BundleFile.Parse(saved);
        Eq(0, again.Warnings.Count, "re-parse warnings: " + string.Join(" | ", again.Warnings));
        Eq(saved, again.Serialize(null), "stable");

        Eq("bleeding, thundering, primal", string.Join(", ", BundleStatCatalog.OnHitEffects.Select(d => d.Key)), "keys");
        Eq("Bleeding, Thundering, Primal", string.Join(", ", BundleStatCatalog.OnHitEffects.Select(d => d.Label)), "labels");
        True(BundleStatCatalog.OnHitEffects.All(d => d.Group == BundleStatCatalog.OnHit && d.Kind == StatKind.Toggle), "group and kind");
        Eq("Thundering", BundleStatCatalog.Find("THUNDERING")?.Label, "Find knows them");
    }

    private static void Test_OnHit_OnlyOnWeaponSlots()
    {
        var file = BundleFile.Parse(
            "active: T\nbuffs:\n  T:\n    helmet:\n      stats: { bleeding: true, armor: 5 }\n" +
            "    shield:\n      stats: { thundering: true }\n    melee:\n      stats: { primal: true }\n");
        Eq(2, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Warnings[0].StartsWith("T.helmet.bleeding: only works on the melee or ranged slot; skipped"), file.Warnings[0]);
        True(file.Warnings[1].StartsWith("T.shield.thundering: only works on the melee or ranged slot; skipped"), file.Warnings[1]);
        True(file.OnlyMisplaced, "only misplaced switches: the mod may clean them up");
        Eq("Bleeding from T's helmet; it only goes on the melee or ranged slot now | " +
           "Thundering from T's shield; it only goes on the melee or ranged slot now", string.Join(" | ", file.Misplaced), "notes");
        var again = BundleFile.Parse(file.Serialize(null));
        Eq(0, again.Warnings.Count, "saved without them: " + string.Join(" | ", again.Warnings));
        Eq(1f, again.Find("T").GetStats(BundleSlot.Melee).Scalars["primal"], "melee keeps its switch");

        foreach (var def in BundleStatCatalog.OnHitEffects)
            foreach (var slot in BundleSlots.All)
                Eq(slot == BundleSlot.Melee || slot == BundleSlot.Ranged, def.Allows(slot), $"{def.Key} on {slot}");
    }

    private static void Test_OnHit_BadValueAndUnknownStat()
    {
        var file = BundleFile.Parse("buffs:\n  T:\n    melee:\n      stats: { primal: maybe, thunder: true }\n");
        Eq(2, file.Warnings.Count, "warnings: " + string.Join(" | ", file.Warnings));
        True(file.Warnings[0].StartsWith("T.melee.primal: 'maybe' is not true or false"), file.Warnings[0]);
        True(file.Warnings[1].StartsWith("T.melee.thunder: unknown stat") && file.Warnings[1].Contains("bleeding, thundering, primal"),
            file.Warnings[1]);
    }

    private static void Test_OnHit_FileHeaderNamesThem()
    {
        foreach (var key in new[] { "bleeding: true", "thundering: true", "primal: true" })
            True(string.Join("\n", BundleFile.Header).Contains(key), "the buffs file header explains " + key);
    }

    private static void Test_OnHit_Bleeding()
    {
        Eq(0.002f, BundleStatCatalog.BleedingPerMissingHealth, "the game's Bleeding weapons: 0.2 % per missing health");
        Near(1.2f, BundleStatCatalog.BleedingFactor(true, false, 200f, 100f), "100 health missing: +20 %");
        Near(1.5f, BundleStatCatalog.BleedingFactor(true, false, 300f, 50f), "250 missing: +50 %");
        Eq(1f, BundleStatCatalog.BleedingFactor(true, false, 200f, 200f), "full health: nothing");
        Eq(1f, BundleStatCatalog.BleedingFactor(true, false, 200f, 230f), "above max health: nothing, never less");
        Eq(1f, BundleStatCatalog.BleedingFactor(false, false, 200f, 100f), "switch off");
        Eq(1f, BundleStatCatalog.BleedingFactor(true, true, 200f, 100f), "a Bleeding weapon keeps its own: no doubling");
    }

    private static void Test_OnHit_ChainChance()
    {
        // (on, secondary, bow, attack spawns its own, the shot spawns its own, rapid fire)
        Eq(0.25f, BundleStatCatalog.ChainChance(true, false, false, false, false, false), "primary attack: 25 % like the game's swords");
        Eq(0.20f, BundleStatCatalog.ChainChance(true, true, false, false, false, false), "secondary attack: 20 %");
        Eq(0.20f, BundleStatCatalog.ChainChance(true, false, true, false, false, false), "bows: 20 % (Storm Fang)");
        Eq(0.20f, BundleStatCatalog.ChainChance(true, true, true, false, false, false), "bow secondary: 20 %");
        Eq(0f, BundleStatCatalog.ChainChance(false, false, false, false, false, false), "switch off");
        Eq(0f, BundleStatCatalog.ChainChance(true, false, false, true, false, false), "a Thundering weapon keeps its own");
        Eq(0f, BundleStatCatalog.ChainChance(true, false, false, false, true, false), "Lightning Strike & co keep their own effect");
        Eq(0f, BundleStatCatalog.ChainChance(true, false, false, false, false, true), "rapid fire: none");
    }

    private static void Test_OnHit_Root()
    {
        Eq(0.2f, BundleStatCatalog.RootChance, "the game's Primal weapons: 20 %");
        // (on, the hit's status effect hash, the weapon has its own, rapid fire, the shot spares friends, roll)
        True(BundleStatCatalog.ShouldRoot(true, 0, false, false, false, 0.1f), "roll under 20 %");
        False(BundleStatCatalog.ShouldRoot(true, 0, false, false, false, 0.2f), "roll of 20 % misses");
        False(BundleStatCatalog.ShouldRoot(false, 0, false, false, false, 0f), "switch off");
        False(BundleStatCatalog.ShouldRoot(true, 1234, false, false, false, 0f), "the hit already carries an effect (fire arrows)");
        False(BundleStatCatalog.ShouldRoot(true, 0, true, false, false, 0f), "the weapon has its own effect: it rolls its own");
        False(BundleStatCatalog.ShouldRoot(true, 0, false, true, false, 0f), "rapid fire: none");
        False(BundleStatCatalog.ShouldRoot(true, 0, false, false, true, 0f),
            "a shot that zeroes friends' damage (Lightning Strike, Echo Spike) would still root them with PvP on: none");
    }

    private static void Test_OnHit_RapidFire()
    {
        True(BundleStatCatalog.IsRapidFire(Attack.AttackType.Projectile, 12, 1, false), "Dundr: 12 projectiles a cast");
        True(BundleStatCatalog.IsRapidFire(Attack.AttackType.Projectile, 1, 999, true), "Staff of Frost: looping bursts");
        True(BundleStatCatalog.IsRapidFire(Attack.AttackType.TriggerProjectile, 1, 3, false), "bursts");
        True(BundleStatCatalog.IsRapidFire(Attack.AttackType.Projectile, 1, 1, true), "looping");
        False(BundleStatCatalog.IsRapidFire(Attack.AttackType.Projectile, 1, 1, false), "a bow shot");
        False(BundleStatCatalog.IsRapidFire(Attack.AttackType.Horizontal, 12, 1, false), "melee swings don't fire projectiles");
    }

    private static void Test_OnHit_WindowText()
    {
        Eq("Your melee hits: Bleeding +0.2% damage per missing health, Thundering 20–25% chain lightning, Primal 20% root (10 s).",
            BundleWindowRules.OnHitInfo(ranged: false), "melee info line (secondary attacks and throws: 20 %)");
        Eq("Your shots: Bleeding +0.2% damage per missing health, Thundering 20–25% chain lightning, Primal 20% root (10 s).",
            BundleWindowRules.OnHitInfo(ranged: true), "ranged info line");
        // (name, rapid fire, the shot spawns its own effect, the shot spares friends, it only summons)
        Eq("Dundr: no Thundering or Primal (rapid fire).", BundleWindowRules.OnHitException("Dundr", true, false, false, false), "rapid fire");
        Eq("Dundr: no Thundering or Primal (rapid fire).", BundleWindowRules.OnHitException("Dundr", true, true, true, false),
            "rapid fire says it all");
        Eq("Staff of the Wild: no Thundering (its shot spawns its own effect).",
            BundleWindowRules.OnHitException("Staff of the Wild", false, true, false, false), "own spawn");
        Eq("Lightning Strike: no Thundering (its shot spawns its own effect) or Primal (it would root friends).",
            BundleWindowRules.OnHitException("Lightning Strike", false, true, true, false), "own spawn, spares friends");
        Eq("Modded Staff: no Primal (it would root friends).",
            BundleWindowRules.OnHitException("Modded Staff", false, false, true, false), "spares friends only");
        Eq("Dead Raiser: On hit does nothing (it summons instead of hitting).",
            BundleWindowRules.OnHitException("Dead Raiser", false, false, false, true), "summons");
        Eq(null, BundleWindowRules.OnHitException("Nidhögg", false, false, false, false), "no exception");
    }

    private static void Near(float expected, float actual, string what)
    {
        if (Math.Abs(expected - actual) > 0.0001f) throw new Exception($"{what}: expected <{expected}>, got <{actual}>");
    }
}
