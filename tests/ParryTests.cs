using LoadoutBuffs;

internal static partial class Tests
{
    // ---- parry bubble math (ShieldMath, ShieldRequest) ----------------------------------------

    private static void Test_ShieldMath_ItemLevelFor()
    {
        Eq(1, ShieldMath.ItemLevelFor(1f), "1 minute = item level 1 (60 s)");
        Eq(3, ShieldMath.ItemLevelFor(3f), "3 minutes");
        Eq(2, ShieldMath.ItemLevelFor(2.4f), "rounded to whole minutes");
        Eq(1, ShieldMath.ItemLevelFor(0.2f), "never 0 (0 would never expire)");
        Eq(1, ShieldMath.ItemLevelFor(-5f), "never below 1");
    }

    private static void Test_ShieldMath_SkillFor()
    {
        // Staff_shield in l-1.0.16: absorb = 200 + 5 × skill + 300 × world level (world level only when > 0).
        True(ShieldMath.TrySkillFor(500f, 200f, 5f, 300f, 0, out var skill), "ok");
        Eq(60f, skill, "500 absorb = skill 60");
        ShieldMath.TrySkillFor(150f, 200f, 5f, 300f, 0, out skill);
        Eq(-10f, skill, "below the base: a negative skill level");
        ShieldMath.TrySkillFor(1000f, 200f, 5f, 300f, 2, out skill);
        Eq(40f, skill, "world level 2 adds 600, which the sender takes off");
        ShieldMath.TrySkillFor(500f, 200f, 5f, 300f, -1, out skill);
        Eq(60f, skill, "the game ignores world levels below 1");
        True(!ShieldMath.TrySkillFor(500f, 200f, 0f, 300f, 0, out skill), "no per-skill factor: can't hit the amount");
        Eq(0f, skill, "falls back to skill 0 (the game's base strength)");
    }

    private static void Test_ShieldRequest_MergeTakesLargest()
    {
        var item = new ShieldRequest(300f, 20f, 20f, 3f);
        var bundle = new ShieldRequest(500f, 10f, 30f, 1f);
        var merged = ShieldRequest.Merge(item, bundle);
        Eq(500f, merged.Amount, "largest amount");
        Eq(20f, merged.PlayerReach, "largest player reach");
        Eq(30f, merged.TamedReach, "largest tamed reach");
        Eq(3f, merged.Minutes, "longest time");
        True(ReferenceEquals(bundle, ShieldRequest.Merge(null, bundle)), "nothing pending: the new request as is");
    }
}
