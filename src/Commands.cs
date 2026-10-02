using System;
using System.Linq;

namespace LoadoutBuffs
{
    /// <summary>
    /// lb_reload / lb_bundles / lb_stats. Not cheats: they can't do anything the bundles file can't, so they work
    /// without devcommands.
    /// </summary>
    internal static class Commands
    {
        private const int MaxWarningsShown = 15;

        public static void Register()
        {
            new Terminal.ConsoleCommand("lb_reload",
                "LoadoutBuffs: re-read the buffs file and apply it",
                (Terminal.ConsoleEvent)Reload);
            new Terminal.ConsoleCommand("lb_buffs",
                "LoadoutBuffs: open the Buffs window (extra effects and stats for your equipment slots)",
                (Terminal.ConsoleEvent)Bundles);
            new Terminal.ConsoleCommand("lb_stats",
                "LoadoutBuffs: your current armor, movement, item modifiers, resistances, weapon and active effects",
                (Terminal.ConsoleEvent)Stats);
        }

        private static void Reload(Terminal.ConsoleEventArgs args)
        {
            var db = ObjectDB.instance;
            if (!GameGlue.IsReady(db))
            {
                Print(args, "LoadoutBuffs: the item database isn't loaded yet.");
                return;
            }
            try
            {
                var bundles = GameGlue.Reload(db);
                Print(args, "LoadoutBuffs: " + bundles.StatusLine());
                foreach (var warning in bundles.Warnings.Take(MaxWarningsShown))
                    Print(args, "  " + warning);
                if (bundles.Warnings.Count > MaxWarningsShown)
                    Print(args, $"  … {bundles.Warnings.Count - MaxWarningsShown} more in BepInEx/LogOutput.log");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"lb_reload failed: {e}");
                Print(args, $"LoadoutBuffs: reload failed: {e.Message} (details in LogOutput.log)");
            }
        }

        private static void Bundles(Terminal.ConsoleEventArgs args)
        {
            if (Player.m_localPlayer == null)
            {
                Print(args, "LoadoutBuffs: load into a world first.");
                return;
            }
            try
            {
                BundleWindow.Open();
                Print(args, "LoadoutBuffs: Buffs window opened (close the console to use it). " + BundleEffects.State.StatusLine());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"lb_buffs failed: {e}");
                Print(args, $"LoadoutBuffs: could not open the Buffs window: {e.Message}");
            }
        }

        private static void Stats(Terminal.ConsoleEventArgs args)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                Print(args, "LoadoutBuffs: load into a world first.");
                return;
            }
            try
            {
                foreach (var line in StatsReport.Build(player))
                    Print(args, line);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"lb_stats failed: {e}");
                Print(args, $"LoadoutBuffs: lb_stats failed: {e.Message}");
            }
        }

        private static void Print(Terminal.ConsoleEventArgs args, string text)
        {
            if (args.Context != null) args.Context.AddString(text);
            else Plugin.Log.LogInfo(text);
        }
    }
}
