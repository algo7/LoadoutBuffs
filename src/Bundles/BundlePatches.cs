using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LoadoutBuffs
{
    /// <summary>
    /// Humanoid.UpdateEquipmentStatusEffects collects the worn items' effects into a
    /// <c>HashSet&lt;StatusEffect&gt;</c>, then removes the effects no longer in it and adds the new ones.
    /// Right after the set is created we insert <c>BundleEffects.AddTo(this, set)</c>, so bundle effects
    /// are just more entries in the game's own list: the same effect from an item and a bundle is one
    /// entry, and it's removed only when neither gives it. Patched on its own (Plugin), so a failure here
    /// can't affect the other hooks; if the IL isn't what we expect, nothing is changed.
    /// </summary>
    [HarmonyPatch]
    internal static class BundlePatches
    {
        [HarmonyPatch(typeof(Humanoid), "UpdateEquipmentStatusEffects")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> UpdateEquipmentStatusEffects(IEnumerable<CodeInstruction> instructions)
        {
            // Harmony re-runs transpilers whenever another mod patches this method: judge each run on its own.
            BundleEffects.HookInstalled = false;
            var list = instructions.ToList();
            var addTo = AccessTools.Method(typeof(BundleEffects), nameof(BundleEffects.AddTo));
            for (var i = 0; i < list.Count - 1; i++)
            {
                if (list[i].opcode != OpCodes.Newobj || !(list[i].operand is ConstructorInfo ctor) ||
                    ctor.DeclaringType != typeof(HashSet<StatusEffect>) || ctor.GetParameters().Length != 0)
                    continue;
                var load = LoadFor(list[i + 1]);
                if (load == null) break;
                list.InsertRange(i + 2, new[] { new CodeInstruction(OpCodes.Ldarg_0), load, new CodeInstruction(OpCodes.Call, addTo) });
                BundleEffects.HookInstalled = true;
                Plugin.Log.LogInfo("Buff hook installed in Humanoid.UpdateEquipmentStatusEffects");
                return list;
            }
            Plugin.Log.LogError("Buffs disabled: Humanoid.UpdateEquipmentStatusEffects doesn't look as expected (game update?).");
            return list;
        }

        /// <summary>The load matching the store that follows <c>newobj</c>; null for anything unexpected.</summary>
        private static CodeInstruction LoadFor(CodeInstruction store)
        {
            if (store.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
            if (store.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
            if (store.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
            if (store.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
            if (store.opcode == OpCodes.Stloc_S) return new CodeInstruction(OpCodes.Ldloc_S, store.operand);
            if (store.opcode == OpCodes.Stloc) return new CodeInstruction(OpCodes.Ldloc, store.operand);
            return null;
        }
    }

    /// <summary>
    /// The parry hook, in its own class so Plugin patches it separately: whatever happens here, the worn-effects
    /// hook above is unaffected.
    /// </summary>
    [HarmonyPatch]
    internal static class BundleParryPatches
    {
        /// <summary>
        /// Humanoid.BlockAttack's parry branch starts with <c>m_perfectBlockEffect.Create(…)</c> (its result popped).
        /// Right after it we insert <c>BundleEffects.OnParry(this)</c>. No match: unchanged, parry stats stay off.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> BlockAttack(IEnumerable<CodeInstruction> instructions)
        {
            BundleEffects.ParryHookInstalled = false;
            var list = instructions.ToList();
            var effectField = AccessTools.Field(typeof(Humanoid), "m_perfectBlockEffect");
            var onParry = AccessTools.Method(typeof(BundleEffects), nameof(BundleEffects.OnParry));
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Ldfld || !Equals(list[i].operand, effectField)) continue;
                for (var j = i + 1; j < list.Count - 1 && j < i + 20; j++)
                {
                    if (!(list[j].operand is MethodInfo method) || method.DeclaringType != typeof(EffectList) || method.Name != nameof(EffectList.Create))
                        continue;
                    if (list[j + 1].opcode != OpCodes.Pop) break;
                    list.InsertRange(j + 2, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, onParry) });
                    BundleEffects.ParryHookInstalled = true;
                    Plugin.Log.LogInfo("Buff parry hook installed in Humanoid.BlockAttack");
                    return list;
                }
                break;
            }
            Plugin.Log.LogError("Buff on-parry stats disabled: Humanoid.BlockAttack doesn't look as expected (game update?). " +
                                "Everything else is not affected.");
            return list;
        }
    }

    /// <summary>
    /// Bundle block armor and block force: postfixes on ItemData.GetBaseBlockPower(int) and GetDeflectionForce(int) (35 bytes
    /// of IL each, too big for Mono to inline, so every caller goes through them: Humanoid.BlockAttack, the tooltip, lb_stats).
    /// </summary>
    [HarmonyPatch]
    internal static class BundleBlockPatches
    {
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBaseBlockPower), typeof(int))]
        [HarmonyPostfix]
        private static void BlockArmor(ItemDrop.ItemData __instance, ref float __result) =>
            __result = BundleEffects.WithBlockArmor(__instance, __result);

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDeflectionForce), typeof(int))]
        [HarmonyPostfix]
        private static void BlockForce(ItemDrop.ItemData __instance, ref float __result) =>
            __result = BundleEffects.WithBlockForce(__instance, __result);
    }

    /// <summary>
    /// Bundle Parry bonus in the item tooltip: ItemData.AddBlockTooltip (private, writes block armor, block force and the
    /// parry multiplier into the tooltip) reads the multiplier field directly, so its line is rewritten afterwards.
    /// </summary>
    [HarmonyPatch]
    internal static class BundleTooltipPatches
    {
        [HarmonyPatch(typeof(ItemDrop.ItemData), "AddBlockTooltip")]
        [HarmonyPrefix]
        private static void Before(System.Text.StringBuilder text, out int __state) => __state = text?.Length ?? 0;

        [HarmonyPatch(typeof(ItemDrop.ItemData), "AddBlockTooltip")]
        [HarmonyPostfix]
        private static void After(ItemDrop.ItemData item, System.Text.StringBuilder text, int __state) =>
            BundleEffects.ShowParryBonus(item, text, __state);
    }

    /// <summary>
    /// Bundle added damage: a postfix on ItemData.GetDamage(int, float) (84 bytes of IL, too big for Mono to inline, so
    /// every attack, the item tooltip and lb_stats go through it).
    /// </summary>
    [HarmonyPatch]
    internal static class BundleDamagePatches
    {
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDamage), typeof(int), typeof(float))]
        [HarmonyPostfix]
        private static void AddedDamage(ItemDrop.ItemData __instance, ref HitData.DamageTypes __result) =>
            BundleEffects.WithAddedDamage(__instance, ref __result);
    }

    /// <summary>
    /// Bundle eitr cost: prefixes on Player.UseEitr and Player.HaveEitr (virtual overrides, so callers never inline them),
    /// which every eitr cost of the player goes through (Attack, Character.TryUseEitr, reload and draw drains).
    /// </summary>
    [HarmonyPatch]
    internal static class BundleEitrPatches
    {
        [HarmonyPatch(typeof(Player), nameof(Player.UseEitr), typeof(float))]
        [HarmonyPrefix]
        private static void Use(Player __instance, ref float v) => v = BundleEffects.WithEitrCost(__instance, v);

        [HarmonyPatch(typeof(Player), nameof(Player.HaveEitr), typeof(float))]
        [HarmonyPrefix]
        private static void Have(Player __instance, ref float amount) => amount = BundleEffects.WithEitrCost(__instance, amount);
    }

    /// <summary>Item classes (Woodcutter / Miner): prefixes on the attacker-side Damage(HitData) of trees, logs, rocks and destructibles.</summary>
    [HarmonyPatch]
    internal static class BundleClassPatches
    {
        [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
        [HarmonyPrefix]
        private static void Tree(TreeBase __instance, HitData hit) => BundleEffects.OnDestructibleHit(__instance, hit);

        [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage))]
        [HarmonyPrefix]
        private static void Log(TreeLog __instance, HitData hit) => BundleEffects.OnDestructibleHit(__instance, hit);

        [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
        [HarmonyPrefix]
        private static void Rock(MineRock __instance, HitData hit) => BundleEffects.OnDestructibleHit(__instance, hit);

        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
        [HarmonyPrefix]
        private static void BigRock(MineRock5 __instance, HitData hit) => BundleEffects.OnDestructibleHit(__instance, hit);

        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
        [HarmonyPrefix]
        private static void Other(Destructible __instance, HitData hit) => BundleEffects.OnDestructibleHit(__instance, hit);
    }
}
