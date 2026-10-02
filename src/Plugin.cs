using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using CompatibilityLevel = Jotunn.Utils.CompatibilityLevel;
using NetworkCompatibility = Jotunn.Utils.NetworkCompatibilityAttribute;
using VersionStrictness = Jotunn.Utils.VersionStrictness;

namespace LoadoutBuffs
{
    [BepInPlugin(Guid, Name, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Client-side only: servers and other players never need this mod (Jötunn would otherwise decide).
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "algo7.loadoutbuffs";
        public const string Name = "LoadoutBuffs";
        public const string PluginVersion = "1.0.0"; // = the csproj Version (the build checks)

        internal static ManualLogSource Log;

        /// <summary>BepInEx/config of whichever BepInEx loaded us (the r2modman profile, not the game folder).</summary>
        internal static string BundlesPath => Path.Combine(Paths.ConfigPath, BundleFile.FileName);

        private void Awake()
        {
            Log = Logger;
            EnsureFile(BundlesPath, "LoadoutBuffs.buffs.example.yaml", "the starter buffs (none active)");

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Patches));
            // Each hook is patched on its own: if the game changed under one, the others still load.
            TryPatch(harmony, typeof(BundlePatches), "buff hook");
            TryPatch(harmony, typeof(BundleParryPatches), "buff parry hook");
            TryPatch(harmony, typeof(BundleClassPatches), "buff class hooks");
            BundleEffects.BlockHookInstalled = TryPatch(harmony, typeof(BundleBlockPatches), "buff block armor / force hooks");
            BundleEffects.DamageHookInstalled = TryPatch(harmony, typeof(BundleDamagePatches), "buff added damage hook");
            TryPatch(harmony, typeof(BundleUiPatches), "buff window");

            Commands.Register();
            Log.LogInfo($"{Name} loaded (v{PluginVersion}), buffs: {BundlesPath}");
        }

        /// <summary>Sends a queued parry bubble once per frame (AllyAssist.FlushBubble).</summary>
        private void LateUpdate() => AllyAssist.FlushBubble();

        /// <summary>True when patched; false (logged) on failure.</summary>
        private static bool TryPatch(Harmony harmony, Type patches, string what)
        {
            try
            {
                harmony.PatchAll(patches);
                return true;
            }
            catch (Exception e)
            {
                Log.LogError($"Could not install the {what}; buffs may not work: {e}");
                return false;
            }
        }

        /// <summary>First run: write the embedded default, which changes nothing in the game.</summary>
        private static void EnsureFile(string path, string resource, string what)
        {
            try
            {
                if (File.Exists(path)) return;
                using (var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream))
                    File.WriteAllText(path, reader.ReadToEnd());
                Log.LogInfo($"Created {path} with {what}");
            }
            catch (Exception e)
            {
                Log.LogError($"Could not create {path}: {e.Message}");
            }
        }
    }
}
