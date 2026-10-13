using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ItemData = ItemDrop.ItemData;

namespace LoadoutBuffs
{
    /// <summary>
    /// Runtime side of bundles: reads the bundles file on each bundle pass (GameGlue), resolves the active bundle to the
    /// game's StatusEffect objects, and adds them to the local player's worn effects via the hook in
    /// Humanoid.UpdateEquipmentStatusEffects (see BundlePatches). Items are never modified.
    /// </summary>
    internal static class BundleEffects
    {
        /// <summary>Set by the transpiler when it found its spot; bundles stay off otherwise.</summary>
        public static bool HookInstalled;

        /// <summary>Set by the parry transpiler (Humanoid.BlockAttack); bundle on-parry stats need it.</summary>
        public static bool ParryHookInstalled;

        /// <summary>Set by Plugin when the block armor + force / added damage postfixes were patched in.</summary>
        public static bool BlockHookInstalled;
        public static bool DamageHookInstalled;

        /// <summary>Set by Plugin when the eitr cost prefixes (Player.UseEitr / HaveEitr) were patched in.</summary>
        public static bool EitrHookInstalled;

        /// <summary>Set by Plugin when the attack-start postfix (Humanoid.StartAttack) was patched in: Thundering needs it.</summary>
        public static bool HitHookInstalled;

        /// <summary>
        /// Set by Plugin when the direct-hit hooks (Projectile.OnHit / Setup, Character.Damage) were patched in: Primal on
        /// shots that explode or splinter then roots only the enemy hit directly, and never a friend; without them those
        /// shots get no Primal.
        /// </summary>
        public static bool RootHookInstalled;

        /// <summary>Set by Plugin when the building hook (WearNTear.Damage) was patched in: Spare buildings needs it.</summary>
        public static bool BuildingHookInstalled;

        public static BundleFile File { get; private set; } = new BundleFile();
        public static BundleState State { get; private set; } = new BundleState();
        public static GameEffectCatalog Catalog { get; private set; }

        private static readonly Dictionary<BundleSlot, StatusEffect> s_effects = new Dictionary<BundleSlot, StatusEffect>();
        private static readonly Dictionary<BundleSlot, StatBlock> s_stats = new Dictionary<BundleSlot, StatBlock>();
        private static string s_statsName;
        private static bool s_showHudBuff;
        private static ParryAssist s_parry;
        private static bool s_parryErrorLogged;
        private static bool s_woodcutter;
        private static bool s_miner;
        private static bool s_classErrorLogged;
        private static float s_blockArmor;
        private static float s_blockForce;
        private static bool s_blockErrorLogged;
        private static readonly Dictionary<string, float> s_addDamage = new Dictionary<string, float>(StringComparer.Ordinal);
        private static bool s_damageErrorLogged;
        private static float s_eitrCost;
        private static bool s_eitrErrorLogged;
        private static float s_parryBonus;
        private static bool s_tooltipErrorLogged;
        private static bool s_bleeding;
        private static bool s_thundering;
        private static bool s_primal;
        private static bool s_attackErrorLogged;
        private static bool s_onHitErrorLogged;
        private static bool s_chainMissingLogged;
        private static bool s_rootMissingLogged;
        private static GameObject s_chainLightning;
        private static int s_rootHash;
        private static ObjectDB s_rootHashDb;
        private static bool s_rootHookErrorLogged;
        private static bool s_spareBuildings;
        private static bool s_buildingErrorLogged;

        /// <summary>The shot whose impact the game is handling (Projectile.OnHit): the local player's, and what it hit directly.</summary>
        private sealed class Impact
        {
            public Projectile Shot;
            public GameObject Direct;
        }

        private static Impact s_impact;

        /// <summary>The game's Thundering weapons' chain lightning (Aoe: 75 lightning, jumps to more enemies, never allies).</summary>
        private const string ChainLightningPrefab = "ChainLightning";

        /// <summary>The game's Primal weapons' root (10 s, can't move or jump).</summary>
        private const string RootEffect = "ImmobilizedAshlands";

        /// <summary>A tool tier above anything the game asks for (hits carry it as a 16-bit number).</summary>
        public const short ClassToolTier = 1000;
        private static bool s_hookErrorLogged;

        // Plain reflection (read only on gear changes): nothing in this type's initializer can fail.
        private static readonly FieldInfo s_nview = AccessTools.Field(typeof(Character), "m_nview");
        private static readonly FieldInfo s_helmet = AccessTools.Field(typeof(Humanoid), "m_helmetItem");
        private static readonly FieldInfo s_chest = AccessTools.Field(typeof(Humanoid), "m_chestItem");
        private static readonly FieldInfo s_legs = AccessTools.Field(typeof(Humanoid), "m_legItem");
        private static readonly FieldInfo s_cape = AccessTools.Field(typeof(Humanoid), "m_shoulderItem");
        private static readonly FieldInfo s_right = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static readonly FieldInfo s_left = AccessTools.Field(typeof(Humanoid), "m_leftItem");
        private static readonly FieldInfo s_currentAttack = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
        private static readonly FieldInfo s_projectileOwner = AccessTools.Field(typeof(Projectile), "m_owner");
        private static readonly FieldInfo s_projectileEffect = AccessTools.Field(typeof(Projectile), "m_statusEffectHash");
        private static readonly FieldInfo s_projectileHitData = AccessTools.Field(typeof(Projectile), "m_originalHitData");

        /// <summary>A bundle pass: read the bundles file and resolve it against this ObjectDB. Doesn't touch the player; see Refresh.</summary>
        public static BundleState Apply(ObjectDB db)
        {
            var text = Read(out var readError);
            File = BundleFile.Parse(text);
            if (readError != null) File.Warnings.Insert(0, readError);

            var all = GameGlue.CollectStatusEffects(db);
            Catalog = new GameEffectCatalog(db, all);
            if (File.OnlyMisplaced) RemoveMisplaced();
            State = BundleRules.Evaluate(File, Catalog);
            if (State.Enabled && !HookInstalled)
            {
                State.Enabled = false;
                State.OffReason = "this game version isn't supported by the buff hook (see LogOutput.log).";
            }

            s_effects.Clear();
            s_stats.Clear();
            s_parry = null;
            s_woodcutter = s_miner = false;
            s_bleeding = s_thundering = s_primal = false;
            s_blockArmor = s_blockForce = s_eitrCost = s_parryBonus = 0f;
            s_addDamage.Clear();
            s_statsName = State.Active?.Name;
            // Without the hook the switch does nothing: the tooltip and status line don't claim it (the warning says why).
            s_spareBuildings = State.Enabled && State.Active != null && State.Active.SpareBuildings && BuildingHookInstalled;
            s_showHudBuff = BundleRules.ShowsHudBuff(State);
            if (State.Enabled)
                State.Warnings.AddRange(BundleRules.MissingHookWarnings(State.Stats.Values, ParryHookInstalled, BlockHookInstalled, DamageHookInstalled,
                    EitrHookInstalled, HitHookInstalled, State.Active?.SpareBuildings == true, BuildingHookInstalled));
            if (State.Enabled)
            {
                foreach (var pair in State.Effects)
                    if (all.TryGetValue(pair.Value, out var effect) && effect != null)
                        s_effects[pair.Key] = effect;
                foreach (var pair in State.Stats)
                    s_stats[pair.Key] = pair.Value;
            }
            return State;
        }

        /// <summary>
        /// 1.0.0 allowed classes and damage stats on armor and the shield, 1.1.0 on-parry stats on armor and the cape; now
        /// they're skipped. When those are the file's only problems, save it once without them, as the window would, so the
        /// dead lines and their warnings don't stay forever.
        /// </summary>
        private static void RemoveMisplaced()
        {
            try
            {
                var text = File.Serialize(code => Catalog?.DisplayName(code));
                System.IO.File.WriteAllText(Plugin.BundlesPath, text);
                foreach (var note in File.Misplaced)
                    Plugin.Log.LogInfo($"{BundleFile.FileName}: removed {note}.");
                File = BundleFile.Parse(text);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not remove the stats on the wrong slots from {BundleFile.FileName}: {e.Message}");
            }
        }

        /// <summary>Re-evaluate the file (after the window saved it) and refresh the player.</summary>
        public static BundleState ReloadFile()
        {
            var db = ObjectDB.instance;
            if (!GameGlue.IsReady(db)) return State;
            Apply(db);
            foreach (var warning in State.Warnings) Plugin.Log.LogWarning(warning);
            Plugin.Log.LogInfo(State.StatusLine());
            Refresh();
            return State;
        }

        /// <summary>Writes the file the way the window shows it, then applies it.</summary>
        public static BundleState Save(BundleFile file)
        {
            var text = file.Serialize(code => Catalog?.DisplayName(code));
            System.IO.File.WriteAllText(Plugin.BundlesPath, text);
            return ReloadFile();
        }

        /// <summary>Makes the game recompute the local player's worn effects (our hook adds the bundle's).</summary>
        public static void Refresh() => GameGlue.RefreshEquipmentEffects(Player.m_localPlayer);

        /// <summary>
        /// Called by the transpiled Humanoid.UpdateEquipmentStatusEffects right after it creates its set of
        /// worn effects. Only for the player this game owns (that's also true during spawn, before
        /// Player.m_localPlayer is set). Must never throw.
        /// </summary>
        public static void AddTo(Humanoid humanoid, HashSet<StatusEffect> set)
        {
            try
            {
                if ((!s_showHudBuff && s_effects.Count == 0 && s_stats.Count == 0) || set == null || !(humanoid is Player)) return;
                var nview = s_nview?.GetValue(humanoid) as ZNetView;
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
                foreach (var pair in s_effects)
                    if (pair.Value != null && Worn(humanoid, pair.Key) != null)
                        set.Add(pair.Value);

                // The HUD buff: the buff's name, its worn effects and the sum of the filled slots' custom stats,
                // updated in place when already active. Shown whenever a buff is in use, with or without stats.
                var total = StatBlock.Sum(s_stats.Where(p => Worn(humanoid, p.Key) != null).Select(p => p.Value));
                s_parry = total.ToParryAssist();
                s_woodcutter = total.Scalars.ContainsKey(BundleStatCatalog.WoodcutterKey);
                s_miner = total.Scalars.ContainsKey(BundleStatCatalog.MinerKey);
                s_bleeding = total.Scalars.ContainsKey(BundleStatCatalog.BleedingKey);
                s_thundering = total.Scalars.ContainsKey(BundleStatCatalog.ThunderingKey);
                s_primal = total.Scalars.ContainsKey(BundleStatCatalog.PrimalKey);
                s_blockArmor = total.BlockArmor;
                s_blockForce = total.BlockForce;
                s_eitrCost = total.EitrCost;
                s_parryBonus = total.ParryBonus;
                s_addDamage.Clear();
                foreach (var p in total.AddDamage) s_addDamage[p.Key] = p.Value;
                if (!s_showHudBuff) return;
                var effectNames = string.Join(", ", s_effects
                    .Where(p => p.Value != null && Worn(humanoid, p.Key) != null)
                    .OrderBy(p => p.Key)
                    .Select(p => EffectText.DisplayName(p.Value.m_name)));
                var template = BundleStatsEffect.Template;
                template.SetTotals(total, s_statsName ?? "Buff", effectNames, s_spareBuildings);
                if (humanoid.GetSEMan().GetStatusEffect(template.NameHash()) is BundleStatsEffect active && active != template)
                    active.SetTotals(total, s_statsName ?? "Buff", effectNames, s_spareBuildings);
                set.Add(template);
            }
            catch (Exception e)
            {
                if (s_hookErrorLogged) return;
                s_hookErrorLogged = true;
                Plugin.Log.LogError($"Buff effects failed (logged once): {e}");
            }
        }

        /// <summary>
        /// Called by the transpiled Humanoid.BlockAttack right where the game handles a parry (perfect timing, an
        /// attacker, stamina left, not staggered). Uses the on-parry totals the last worn-effects update left for
        /// this game's own player. Must never throw.
        /// </summary>
        public static void OnParry(Humanoid humanoid)
        {
            try
            {
                var assist = s_parry;
                if (assist == null || !(humanoid is Player)) return;
                var nview = s_nview?.GetValue(humanoid) as ZNetView;
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
                AllyAssist.Self(humanoid, assist);
                AllyAssist.Others(humanoid, assist);
                AllyAssist.RequestBubble(humanoid, assist);
            }
            catch (Exception e)
            {
                if (s_parryErrorLogged) return;
                s_parryErrorLogged = true;
                Plugin.Log.LogError($"Buff on-parry stats failed (logged once): {e}");
            }
        }

        /// <summary>
        /// Prefix of Damage(HitData) on trees, logs, rocks and other destructibles, on the attacker's side (before
        /// the hit is sent to the object's owner). With Woodcutter / Miner on the slot of the weapon in hand (classes
        /// only go on melee and ranged, and only the held weapon's slot is filled), this player's hits get the max tool
        /// tier and chop (trees, logs, stumps) or pickaxe (everything else) damage equal to all of the hit's damage,
        /// unless it already does more: swings, and also arrows, bolts, staff spells and the blasts they spawn
        /// (Projectile / Aoe hits are the owner's, sent through the same Damage). Must never throw.
        /// </summary>
        public static void OnDestructibleHit(object target, HitData hit)
        {
            try
            {
                if (!(s_woodcutter || s_miner) || hit == null) return;
                var player = Player.m_localPlayer;
                if (player == null || hit.GetAttacker() != player) return;
                var weapon = player.GetCurrentWeapon();
                var slot = HandSlotOf(weapon);
                if (slot != BundleSlot.Melee && slot != BundleSlot.Ranged) return;

                var tree = target is TreeBase || target is TreeLog ||
                           (target is Destructible d && d.m_destructibleType == DestructibleType.Tree);
                if (!BundleStatCatalog.WithClass(ref hit.m_damage, tree, s_woodcutter, s_miner)) return;
                if (hit.m_toolTier < ClassToolTier) hit.m_toolTier = ClassToolTier;
            }
            catch (Exception e)
            {
                if (s_classErrorLogged) return;
                s_classErrorLogged = true;
                Plugin.Log.LogError($"Buff classes failed (logged once): {e}");
            }
        }

        /// <summary>
        /// Prefix of WearNTear.Damage(HitData) (walls, doors, chests, portals, ships, carts...), on the attacker's side
        /// before the hit is sent to the piece's owner: with Spare buildings on the buff in use, true (drop the hit) when the
        /// local player made it and a player placed the piece. Swings, shots, thrown weapons, blasts and bomb clouds all
        /// name their owner as the attacker, whatever is in hand; spreading fire, falling trees and siege machines (the
        /// catapult's shots and the battering ram's Aoe have no owner) name nobody, so they still land. Must never throw
        /// (on an error the hit lands).
        /// </summary>
        public static bool OnBuildingHit(WearNTear target, HitData hit)
        {
            try
            {
                if (!s_spareBuildings || hit == null || target == null) return false;
                var player = Player.m_localPlayer;
                if (player == null) return false;
                // GetZDOID is None without a valid network object, which is also what hits without an attacker carry.
                var you = player.GetZDOID();
                if (you.IsNone() || hit.m_attacker != you) return false;
                var piece = target.GetComponent<Piece>();
                return BundleRules.SparesBuilding(true, true, piece != null ? piece.GetCreator() : 0L);
            }
            catch (Exception e)
            {
                if (!s_buildingErrorLogged)
                {
                    s_buildingErrorLogged = true;
                    Plugin.Log.LogError($"Buff Spare buildings failed (logged once): {e}");
                }
                return false;
            }
        }

        /// <summary>
        /// Postfix of Humanoid.StartAttack (the attack started): with Thundering on the slot of the weapon in hand, the
        /// swing's own copy of the attack (cloned per swing, never the item's data) gets the game's chain lightning (35 %). The game then spawns it on melee hits (Attack.SpawnOnHit) and hands it to each projectile it
        /// fires (arrows, bolts, thrown spears, single-shot staffs). Skipped when the attack spawns something already (a
        /// Thundering weapon), the shot's projectile spawns its own effect (the game would replace it: Lightning Strike,
        /// Echo Spike, Staff of Fracturing, Staff of the Wild) or the weapon fires rapidly. Must never throw.
        /// </summary>
        public static void OnAttackStart(Humanoid humanoid)
        {
            try
            {
                if (!s_thundering || humanoid == null || !ReferenceEquals(humanoid, Player.m_localPlayer)) return;
                var weapon = humanoid.GetCurrentWeapon();
                if (!FillsWeaponSlot(weapon) || !(s_currentAttack?.GetValue(humanoid) is Attack attack)) return;
                var rapid = Facts(weapon.m_shared).RapidFire;
                var ownSpawn = attack.m_spawnOnHit != null && attack.m_spawnOnHitChance > 0f;
                if (ownSpawn)
                {
                    // A Thundering weapon keeps its own chain lightning (Thunderblood: the red one), raised to 35 %.
                    if (!rapid)
                        attack.m_spawnOnHitChance = BundleStatCatalog.RaisedOwnChance(true, IsChainLightning(attack.m_spawnOnHit),
                            attack.m_spawnOnHitChance);
                    return;
                }
                var shot = attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackType == Attack.AttackType.TriggerProjectile;
                var chance = BundleStatCatalog.ChainChance(true, false,
                    shot && SpawnsOwn(AmmoProjectile(humanoid, weapon) ?? attack.m_attackProjectile), rapid);
                if (chance <= 0f) return;
                var prefab = ChainLightning();
                if (prefab == null) return;
                attack.m_spawnOnHit = prefab;
                attack.m_spawnOnHitChance = chance;
            }
            catch (Exception e)
            {
                if (s_attackErrorLogged) return;
                s_attackErrorLogged = true;
                Plugin.Log.LogError($"Buff Thundering failed (logged once): {e}");
            }
        }

        /// <summary>
        /// From the buff's StatusEffect.ModifyAttack, which the game calls on each hit of the local player's attacks (melee
        /// per target, a shot when fired), after the weapon's own effect roll: Bleeding scales the hit by the missing health
        /// and Primal may root, for the weapon in hand's slot. Not for a shield's block-charge blast (skill Blocking).
        /// Must never throw.
        /// </summary>
        public static void OnModifyAttack(Character character, Skills.SkillType skill, HitData hit)
        {
            try
            {
                if (!(s_bleeding || s_primal) || hit == null || skill == Skills.SkillType.Blocking) return;
                var player = Player.m_localPlayer;
                if (player == null || !ReferenceEquals(character, player)) return;
                var weapon = player.GetCurrentWeapon();
                if (!FillsWeaponSlot(weapon)) return;
                var facts = ShotFacts(player, weapon);
                var factor = BundleStatCatalog.BleedingFactor(s_bleeding, facts.OwnBleeding, player.GetMaxHealth(), player.GetHealth());
                if (factor != 1f) hit.m_damage.Modify(factor);
                var onShots = BundleStatCatalog.PrimalOnShots(facts.ShotExplodes, facts.ShotSparesFriends, RootHookInstalled);
                var root = s_primal && (facts.OwnRoot
                    // A Primal weapon rolled its own 20 %; a miss rolls again so the total is 35 %.
                    ? BundleStatCatalog.ShouldTopUpRoot(true, hit.m_statusEffectHash, weapon.m_shared.m_attackStatusEffectChance,
                        facts.RapidFire, UnityEngine.Random.value)
                    : BundleStatCatalog.ShouldRoot(true, hit.m_statusEffectHash, weapon.m_shared.m_attackStatusEffect != null,
                        facts.RapidFire, onShots == ShotPrimal.NoneFriends, onShots == ShotPrimal.NoneBlast, UnityEngine.Random.value));
                if (root)
                {
                    var hash = RootHash();
                    if (hash != 0) hit.m_statusEffectHash = hash;
                }
            }
            catch (Exception e)
            {
                if (s_onHitErrorLogged) return;
                s_onHitErrorLogged = true;
                Plugin.Log.LogError($"Buff Bleeding / Primal failed (logged once): {e}");
            }
        }

        /// <summary>
        /// "Dundr: no Thundering or Primal (rapid fire).": why the on-hit switches skip this weapon, or null; with the ammo
        /// <paramref name="humanoid"/> would shoot. Only for the switches given (the window passes all). Never throws.
        /// </summary>
        public static string OnHitNote(ItemData item, Humanoid humanoid, bool bleeding = true, bool thundering = true, bool primal = true)
        {
            try
            {
                if (item == null) return null;
                var facts = ShotFacts(humanoid, item);
                var onShots = primal ? BundleStatCatalog.PrimalOnShots(facts.ShotExplodes, facts.ShotSparesFriends, RootHookInstalled)
                    : ShotPrimal.Rolls;
                return BundleWindowRules.OnHitException(EffectText.DisplayName(item.m_shared.m_name),
                    facts.RapidFire && (thundering || primal), facts.ShotSpawnsOwn && thundering, onShots,
                    facts.Summons && (bleeding || thundering || primal), facts.OwnChain && thundering, facts.OwnRoot && primal);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Prefix of Projectile.OnHit: a shot of the local player's that carries the buff's root lands; remember what it
        /// hit directly until the impact is handled (see <see cref="OnCharacterHit"/>, <see cref="OnProjectileSetup"/>).
        /// Returns the impact this one interrupts, for <see cref="EndImpact"/>. Must never throw.
        /// </summary>
        public static object BeginImpact(Projectile shot, Collider collider)
        {
            var previous = s_impact;
            try
            {
                var root = s_rootHash;
                if (!RootHookInstalled || root == 0 || shot == null || !(s_projectileEffect.GetValue(shot) is int hash) || hash != root ||
                    !ReferenceEquals(s_projectileOwner.GetValue(shot), Player.m_localPlayer))
                    s_impact = null;
                else
                    s_impact = new Impact { Shot = shot, Direct = collider != null ? Projectile.FindHitObject(collider) : null };
            }
            catch (Exception e)
            {
                s_impact = null;
                LogRootHookError(e);
            }
            return previous;
        }

        /// <summary>Finalizer of Projectile.OnHit: the impact is handled (even if the game threw).</summary>
        public static void EndImpact(object previous) => s_impact = previous as Impact;

        /// <summary>
        /// Postfix of Projectile.Setup: the root never passes on to projectiles a shot spawns. A projectile spawned during
        /// the buff's root-carrying impact drops it; a root-carrying shot that hands its hit to what it spawns (Staff of
        /// Fracturing's splinters, also when it bursts at the end of its flight, outside any impact) hands on a copy without
        /// it. Must never throw.
        /// </summary>
        public static void OnProjectileSetup(Projectile spawned)
        {
            try
            {
                var root = s_rootHash;
                if (!RootHookInstalled || root == 0 || spawned == null || !(s_projectileEffect.GetValue(spawned) is int hash) || hash != root)
                    return;
                if (s_impact != null)
                {
                    s_projectileEffect.SetValue(spawned, 0);
                    return;
                }
                if (spawned.m_spawnOnHit != null && spawned.m_projectilesInheritHitData &&
                    s_projectileHitData.GetValue(spawned) is HitData inherited)
                {
                    var copy = inherited.Clone();
                    copy.m_statusEffectHash = 0;
                    s_projectileHitData.SetValue(spawned, copy);
                }
            }
            catch (Exception e)
            {
                LogRootHookError(e);
            }
        }

        /// <summary>
        /// Prefix of Character.Damage, on the attacker's side before the hit is sent: the buff's root stays only where
        /// <see cref="BundleStatCatalog.RootStays"/> says (not on friends; a shot's only on what it hit directly). The game's
        /// own Primal weapons are left alone (the weapon in hand has its own effect). Must never throw.
        /// </summary>
        public static void OnCharacterHit(Character target, HitData hit)
        {
            try
            {
                var root = s_rootHash;
                if (!RootHookInstalled || root == 0 || hit == null || hit.m_statusEffectHash != root || target == null) return;
                var player = Player.m_localPlayer;
                if (player == null || !ReferenceEquals(hit.GetAttacker(), player)) return;
                // A Primal weapon's own swing: left alone. Shots are judged by what they carry, not by what's in hand when
                // they land (a fireball, then Staff of Protection).
                if (!hit.m_ranged && player.GetCurrentWeapon()?.m_shared.m_attackStatusEffect != null) return;
                var ai = target.GetBaseAI();
                var friend = !BaseAI.IsEnemy(player, target) && !(ai != null && ai.IsAggravatable());
                var impact = s_impact;
                var direct = impact != null && impact.Direct != null && ReferenceEquals(impact.Direct, target.gameObject);
                if (!BundleStatCatalog.RootStays(friend, hit.m_ranged, impact != null, direct))
                    hit.m_statusEffectHash = 0;
                else if (hit.m_ranged && impact?.Shot != null)
                    s_projectileEffect.SetValue(impact.Shot, 0); // one root per shot (Echo Spike passes through a line of enemies)
            }
            catch (Exception e)
            {
                LogRootHookError(e);
            }
        }

        /// <summary>Whether the fields the direct-hit hooks read were found (Plugin counts them as part of the hooks).</summary>
        public static bool RootFieldsFound => s_projectileOwner != null && s_projectileEffect != null && s_projectileHitData != null;

        private static void LogRootHookError(Exception e)
        {
            if (s_rootHookErrorLogged) return;
            s_rootHookErrorLogged = true;
            Plugin.Log.LogError($"Buff Primal direct-hit hooks failed (logged once): {e}");
        }

        /// <summary>lb_stats: the note for the weapon in hand and the on-hit switches in use now (null: none).</summary>
        public static string OnHitNoteInUse(Player player, ItemData weapon) =>
            s_bleeding || s_thundering || s_primal ? OnHitNote(weapon, player, s_bleeding, s_thundering, s_primal) : null;

        /// <summary>Whether Humanoid.m_currentAttack was found (Thundering needs it); Plugin counts it as part of the hook.</summary>
        public static bool AttackFieldFound => s_currentAttack != null;

        private static bool FillsWeaponSlot(ItemData weapon)
        {
            var slot = HandSlotOf(weapon);
            return slot.HasValue && BundleStatCatalog.IsWeaponSlot(slot.Value);
        }

        /// <summary>
        /// The projectile of the ammo a bow or crossbow would shoot (picked like Attack.FindAmmo), which replaces the
        /// attack's own; null for weapons without ammo, without a humanoid or without ammo left.
        /// </summary>
        private static GameObject AmmoProjectile(Humanoid humanoid, ItemData weapon)
        {
            var type = weapon.m_shared.m_ammoType;
            if (humanoid == null || string.IsNullOrEmpty(type)) return null;
            var ammo = humanoid.GetAmmoItem();
            if (ammo == null || ammo.m_shared.m_ammoType != type || !humanoid.GetInventory().ContainsItem(ammo))
                ammo = humanoid.GetInventory().GetAmmoItem(type);
            var projectile = ammo?.m_shared.m_attack?.m_attackProjectile;
            return projectile != null ? projectile : null;
        }

        /// <summary>The game's chain lightning (ChainLightning, Thunderblood's ChainLightningRed), by prefab name.</summary>
        private static bool IsChainLightning(GameObject prefab) =>
            prefab != null && prefab.name.StartsWith(ChainLightningPrefab, StringComparison.Ordinal);

        /// <summary>A projectile that spawns something of its own on hit (the game's handover would replace it).</summary>
        private static bool SpawnsOwn(GameObject projectile)
        {
            var component = projectile == null ? null : projectile.GetComponent<Projectile>();
            return component != null && component.m_spawnOnHit != null;
        }

        /// <summary>
        /// A projectile that zeroes friends' damage instead of missing them (Lightning Strike, Echo Spike): their hit still
        /// carries the status effect, so with PvP on a root would reach your tamed animals.
        /// </summary>
        private static bool SparesFriends(GameObject projectile)
        {
            var component = projectile == null ? null : projectile.GetComponent<Projectile>();
            return component != null && component.m_noDamageFriendly;
        }

        /// <summary>
        /// A projectile that spreads its hit, effect included, to several targets, so one Primal roll would root them all:
        /// it explodes (<c>m_aoe</c>, Projectile.DoAOE: Staff of Embers, Staff of the Wild, Lightning Strike) or splits into
        /// projectiles that inherit its hit (<c>m_projectilesInheritHitData</c>: Staff of Fracturing's 12 splinters).
        /// </summary>
        private static bool Explodes(GameObject projectile)
        {
            var component = projectile == null ? null : projectile.GetComponent<Projectile>();
            return component != null &&
                   (component.m_aoe > 0f || (component.m_spawnOnHit != null && component.m_projectilesInheritHitData));
        }

        /// <summary>A shot that is neither a projectile nor a blast (Dead Raiser, Spirit Caller, Trollstav): it only summons.</summary>
        private static bool OnlySummons(GameObject projectile) =>
            projectile != null && projectile.GetComponent<Projectile>() == null && projectile.GetComponent<Aoe>() == null;

        /// <summary>The weapon's facts with the ammo <paramref name="humanoid"/> would shoot (modded arrows may spawn their own effect).</summary>
        private static HitFacts ShotFacts(Humanoid humanoid, ItemData weapon)
        {
            var facts = Facts(weapon.m_shared);
            var ammo = AmmoProjectile(humanoid, weapon);
            if (ammo == null) return facts;
            facts.ShotSpawnsOwn |= SpawnsOwn(ammo);
            facts.ShotSparesFriends |= SparesFriends(ammo);
            facts.ShotExplodes |= Explodes(ammo);
            return facts;
        }

        private struct HitFacts
        {
            public bool RapidFire;
            public bool ShotSpawnsOwn;
            public bool ShotSparesFriends;
            public bool ShotExplodes;
            public bool Summons;
            public bool OwnBleeding;
            public bool OwnChain;
            public bool OwnRoot;
        }

        private static readonly Dictionary<ItemData.SharedData, HitFacts> s_hitFacts = new Dictionary<ItemData.SharedData, HitFacts>();

        /// <summary>
        /// A weapon's attacks, as the on-hit switches see them: rapid fire (several projectiles at once or repeated), a shot
        /// whose own projectile spawns its effect, spares friends or explodes, only summoning (every used attack), the Bleeding
        /// weapons' own damage per missing health. Unused attacks (no animation) don't count. Cached per SharedData.
        /// Never throws.
        /// </summary>
        private static HitFacts Facts(ItemData.SharedData shared)
        {
            if (shared == null) return default;
            if (s_hitFacts.TryGetValue(shared, out var known)) return known;
            var facts = new HitFacts();
            try
            {
                var used = 0;
                var summoning = 0;
                foreach (var attack in new[] { shared.m_attack, shared.m_secondaryAttack })
                {
                    if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation)) continue;
                    used++;
                    facts.RapidFire |= BundleStatCatalog.IsRapidFire(attack.m_attackType, attack.m_projectiles, attack.m_projectileBursts,
                        attack.m_loopingAttack);
                    var shot = attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackType == Attack.AttackType.TriggerProjectile;
                    facts.ShotSpawnsOwn |= shot && SpawnsOwn(attack.m_attackProjectile);
                    facts.ShotSparesFriends |= shot && SparesFriends(attack.m_attackProjectile);
                    facts.ShotExplodes |= shot && Explodes(attack.m_attackProjectile);
                    if (shot && OnlySummons(attack.m_attackProjectile)) summoning++;
                    facts.OwnBleeding |= attack.m_damageMultiplierPerMissingHP > 0f;
                    facts.OwnChain |= attack.m_spawnOnHitChance > 0f && IsChainLightning(attack.m_spawnOnHit);
                }
                facts.Summons = used > 0 && summoning == used;
                facts.OwnRoot = shared.m_attackStatusEffect != null && shared.m_attackStatusEffect.name == RootEffect;
            }
            catch (Exception) { }
            s_hitFacts[shared] = facts;
            return facts;
        }

        /// <summary>The chain lightning prefab (looked up once per ZNetScene), or null (logged once) if the game lacks it.</summary>
        private static GameObject ChainLightning()
        {
            if (s_chainLightning != null) return s_chainLightning;
            s_chainLightning = ZNetScene.instance == null ? null : ZNetScene.instance.GetPrefab(ChainLightningPrefab);
            if (s_chainLightning == null && !s_chainMissingLogged)
            {
                s_chainMissingLogged = true;
                Plugin.Log.LogWarning($"Thundering does nothing: the game has no {ChainLightningPrefab} (game update?).");
            }
            return s_chainLightning;
        }

        /// <summary>The root effect's hash (looked up once per ObjectDB), or 0 (logged once) if the game lacks it.</summary>
        private static int RootHash()
        {
            var db = ObjectDB.instance;
            if (db == null) return 0;
            if (ReferenceEquals(db, s_rootHashDb)) return s_rootHash;
            var effect = db.GetStatusEffect(RootEffect.GetStableHashCode());
            s_rootHash = effect == null ? 0 : effect.NameHash();
            s_rootHashDb = db;
            if (s_rootHash == 0 && !s_rootMissingLogged)
            {
                s_rootMissingLogged = true;
                Plugin.Log.LogWarning($"Primal does nothing: the game has no {RootEffect} effect (game update?).");
            }
            return s_rootHash;
        }

        /// <summary>
        /// Postfix of ItemData.GetBaseBlockPower(int): the bundle's block armor goes onto whatever the local player blocks
        /// with (the shield if held, else the weapon or fists, like Humanoid.GetCurrentBlocker), as if it were the item's
        /// own blockPower. Blocks, the tooltip and lb_stats all read this getter. Must never throw.
        /// </summary>
        public static float WithBlockArmor(ItemData item, float value)
        {
            try
            {
                if (s_blockArmor == 0f || item == null) return value;
                var player = Player.m_localPlayer;
                if (player == null) return value;
                var blocker = player.LeftItem ?? player.GetCurrentWeapon();
                return ReferenceEquals(blocker, item) ? BundleStatCatalog.WithBlockArmor(value, s_blockArmor) : value;
            }
            catch (Exception e)
            {
                if (!s_blockErrorLogged)
                {
                    s_blockErrorLogged = true;
                    Plugin.Log.LogError($"Buff block armor failed (logged once): {e}");
                }
                return value;
            }
        }

        /// <summary>
        /// Postfix of ItemData.AddBlockTooltip: the item tooltip's parry multiplier includes the buff's Parry bonus when that
        /// item is what the local player parries with (like block armor). The game reads the field directly, so the line
        /// the method wrote (from <paramref name="start"/> on) is rewritten. Must never throw.
        /// </summary>
        public static void ShowParryBonus(ItemData item, System.Text.StringBuilder text, int start)
        {
            try
            {
                if (s_parryBonus <= 0f || item == null || text == null) return;
                var player = Player.m_localPlayer;
                if (player == null || !ReferenceEquals(player.LeftItem ?? player.GetCurrentWeapon(), item)) return;
                BundleStatCatalog.ShowParryBonus(text, start, item.m_shared.m_timedBlockBonus, s_parryBonus);
            }
            catch (Exception e)
            {
                if (s_tooltipErrorLogged) return;
                s_tooltipErrorLogged = true;
                Plugin.Log.LogError($"Buff parry bonus tooltip failed (logged once): {e}");
            }
        }

        /// <summary>
        /// Postfix of ItemData.GetDeflectionForce(int): the bundle's block force on whatever the local player blocks with,
        /// like <see cref="WithBlockArmor(ItemData, float)"/>. Humanoid.BlockAttack and the tooltip read this getter. Must never throw.
        /// </summary>
        public static float WithBlockForce(ItemData item, float value)
        {
            try
            {
                if (s_blockForce == 0f || item == null) return value;
                var player = Player.m_localPlayer;
                if (player == null) return value;
                var blocker = player.LeftItem ?? player.GetCurrentWeapon();
                return ReferenceEquals(blocker, item) ? BundleStatCatalog.WithBlockForce(value, s_blockForce) : value;
            }
            catch (Exception e)
            {
                if (!s_blockErrorLogged)
                {
                    s_blockErrorLogged = true;
                    Plugin.Log.LogError($"Buff block force failed (logged once): {e}");
                }
                return value;
            }
        }

        /// <summary>
        /// Postfix of ItemData.GetDamage(int, float): the bundle's added damage goes onto the weapon the local player attacks
        /// with (Humanoid.GetCurrentWeapon: right hand, else a left-hand weapon, else fists), as if it were the weapon's own
        /// damage, so attacks scale it like the rest and the tooltip / lb_stats show it. Arrows and bolts aren't the current
        /// weapon (the bow's share counts once per shot). Must never throw.
        /// </summary>
        public static void WithAddedDamage(ItemData item, ref HitData.DamageTypes damages)
        {
            try
            {
                if (s_addDamage.Count == 0 || item == null) return;
                var player = Player.m_localPlayer;
                if (player == null || !ReferenceEquals(player.GetCurrentWeapon(), item)) return;
                damages = StatBlock.AddDamageTo(damages, s_addDamage);
            }
            catch (Exception e)
            {
                if (s_damageErrorLogged) return;
                s_damageErrorLogged = true;
                Plugin.Log.LogError($"Buff added damage failed (logged once): {e}");
            }
        }

        /// <summary>
        /// Prefix of Player.UseEitr(v) and Player.HaveEitr(amount): every eitr cost the local player pays (spells, the
        /// lightning staff's charge, draw / reload drains) and its check, with the buff's eitr cost percent, so both agree.
        /// Other players are untouched. Must never throw.
        /// </summary>
        public static float WithEitrCost(Player player, float eitr)
        {
            try
            {
                if (s_eitrCost == 0f || player == null || !ReferenceEquals(player, Player.m_localPlayer)) return eitr;
                return BundleStatCatalog.WithEitrCost(eitr, s_eitrCost);
            }
            catch (Exception e)
            {
                if (!s_eitrErrorLogged)
                {
                    s_eitrErrorLogged = true;
                    Plugin.Log.LogError($"Buff eitr cost failed (logged once): {e}");
                }
                return eitr;
            }
        }

        /// <summary>The weapon the local player attacks with (fists when the hands are empty), or null without a player.</summary>
        public static ItemData CurrentWeapon()
        {
            var player = Player.m_localPlayer;
            return player == null ? null : player.GetCurrentWeapon();
        }

        /// <summary>A weapon's own damage per type, without what the active bundle adds to it (for the window).</summary>
        public static Dictionary<string, float> OwnDamage(ItemData weapon)
        {
            var result = StatBlock.DamageByType(weapon.GetDamage());
            if (!ReferenceEquals(CurrentWeapon(), weapon)) return result;
            foreach (var p in s_addDamage)
            {
                if (!result.TryGetValue(p.Key, out var v)) continue;
                if (v - p.Value > 0.001f) result[p.Key] = v - p.Value;
                else result.Remove(p.Key);
            }
            return result;
        }

        /// <summary>What the window's gear-dependent parts depend on: the item in every slot and the weapon in hand.</summary>
        public static ItemData[] GearSnapshot(Humanoid h)
        {
            if (h == null) return new ItemData[0];
            var items = BundleSlots.All.Select(s => Worn(h, s)).ToList();
            items.Add(h.GetCurrentWeapon());
            return items.ToArray();
        }

        /// <summary>A bundle's stats summed over the slots the player fills now (window totals line, lb_stats).</summary>
        public static StatBlock WornTotals(Humanoid h, BundleDef bundle)
        {
            if (h == null || bundle == null) return new StatBlock();
            return StatBlock.Sum(bundle.Entries.Where(e => e.Stats != null && Worn(h, e.Slot) != null).Select(e => e.Stats));
        }

        /// <summary>The item that fills a slot. Hand slots: whichever hand holds an item of that kind (see BundleSlots.HandSlot).</summary>
        public static ItemData Worn(Humanoid h, BundleSlot slot)
        {
            switch (slot)
            {
                case BundleSlot.Helmet: return Get(s_helmet, h);
                case BundleSlot.Chest: return Get(s_chest, h);
                case BundleSlot.Legs: return Get(s_legs, h);
                case BundleSlot.Cape: return Get(s_cape, h);
                case BundleSlot.Melee:
                case BundleSlot.Ranged:
                case BundleSlot.Shield:
                    foreach (var item in new[] { Get(s_right, h), Get(s_left, h) })
                        if (HandSlotOf(item) == slot)
                            return item;
                    return null;
                default: return null;
            }
        }

        private static ItemData Get(FieldInfo field, Humanoid h) => h == null ? null : field?.GetValue(h) as ItemData;

        /// <summary>The hand slot an item fills (BundleSlots.HandSlot), null for none or no item.</summary>
        public static BundleSlot? HandSlotOf(ItemData item) =>
            item == null ? null : BundleSlots.HandSlot(item.m_shared.m_itemType, item.m_shared.m_skillType, UsedOnAllies(item.m_shared));

        private static readonly Dictionary<ItemData.SharedData, bool> s_usedOnAllies = new Dictionary<ItemData.SharedData, bool>();

        /// <summary>
        /// Whether the item is a weapon you use on your own, whose hit would carry the slot's damage to them:
        /// <list type="bullet">
        /// <item>a spell that can't hit enemies but takes the cast's damage: its projectile is an Aoe (on the root, where
        /// Attack hands over the hit) with m_useAttackSettings on and m_hitEnemy off (Staff of Protection, Northern
        /// Vengeance), so it hits you, players and tamed with it;</item>
        /// <item>a harpoon: its hit applies SE_Harpooned, the pull used to drag tamed animals (Abyssal Harpoon; a player's
        /// projectile hits them with PvP on).</item>
        /// </list>
        /// Cached per SharedData (Worn runs on every equipment update). Never throws.
        /// </summary>
        private static bool UsedOnAllies(ItemData.SharedData shared)
        {
            if (shared == null) return false;
            if (s_usedOnAllies.TryGetValue(shared, out var known)) return known;
            var result = false;
            try
            {
                result = shared.m_attackStatusEffect is SE_Harpooned;
                var projectile = shared.m_attack?.m_attackProjectile;
                var aoe = projectile == null ? null : projectile.GetComponent<Aoe>();
                result |= aoe != null && aoe.m_useAttackSettings && !aoe.m_hitEnemy;
            }
            catch (Exception) { }
            s_usedOnAllies[shared] = result;
            return result;
        }

        private static string Read(out string error)
        {
            error = null;
            try
            {
                return System.IO.File.Exists(Plugin.BundlesPath) ? System.IO.File.ReadAllText(Plugin.BundlesPath) : null;
            }
            catch (Exception e)
            {
                error = $"Could not read {Plugin.BundlesPath}: {e.Message}";
                return null;
            }
        }
    }

    /// <summary>
    /// Effects bundles may use: every effect some item has as its equip or set effect. Other mods' gear effects count too.
    /// </summary>
    internal sealed class GameEffectCatalog : IEffectCatalog
    {
        private readonly Dictionary<string, StatusEffect> _all;
        private readonly List<EffectInfo> _allowed;

        public GameEffectCatalog(ObjectDB db, Dictionary<string, StatusEffect> all)
        {
            _all = all;
            var found = new Dictionary<string, StatusEffect>(StringComparer.Ordinal);
            foreach (var go in db.m_items)
            {
                if (go == null) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null) continue;
                var shared = drop.m_itemData.m_shared;
                foreach (var effect in new[] { shared.m_equipStatusEffect, shared.m_setStatusEffect })
                    if (effect != null && !found.ContainsKey(effect.name)) found[effect.name] = effect;
            }
            _allowed = found.Values
                .Select(se => new EffectInfo { Code = se.name, DisplayName = EffectText.DisplayName(se.m_name) })
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<EffectInfo> Allowed => _allowed;

        public bool Exists(string code) => code != null && _all.ContainsKey(code);

        public StatusEffect Get(string code) => code != null && _all.TryGetValue(code, out var se) ? se : null;

        public string DisplayName(string code) => _allowed.FirstOrDefault(e => e.Code == code)?.DisplayName;
    }
}
