using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace LucidCatsModManager
{
    internal static class LaunchGuard
    {
        private static string LaunchFile => Path.Combine(Paths.ConfigPath, "lucidcats.modmanager.launch.txt");
        private static string KnownFile => Path.Combine(Paths.ConfigPath, "lucidcats.modmanager.known.txt");
        private static string RecoveryFile => Path.Combine(Paths.ConfigPath, "lucidcats.modmanager.recovery.txt");

        private static bool markedThisSession;

        internal sealed class Recovery
        {
            public bool SafeMode;
            public string Time = "";
            public readonly List<string> Disabled = new List<string>();
        }

        public static void MarkSuccess()
        {
            if (markedThisSession)
                return;
            markedThisSession = true;

            try
            {
                if (File.Exists(LaunchFile))
                    File.Delete(LaunchFile);

                var lines = new List<string> { "# Mods that were switched on the last time the game started correctly." };
                lines.AddRange(EnabledPluginFiles());
                File.WriteAllLines(KnownFile, lines);

                ModManagerPlugin.Log.LogInfo("Launch marked as successful.");
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not mark the launch as successful: {e.Message}");
            }
        }

        public static Recovery ReadRecovery()
        {
            if (!File.Exists(RecoveryFile))
                return null;

            try
            {
                var recovery = new Recovery();
                foreach (string raw in File.ReadAllLines(RecoveryFile))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("reason="))
                        recovery.SafeMode = line.Substring(7) == "safe";
                    else if (line.StartsWith("time="))
                        recovery.Time = line.Substring(5);
                    else if (line.StartsWith("disabled="))
                        recovery.Disabled.Add(line.Substring(9));
                }
                return recovery.Disabled.Count > 0 ? recovery : null;
            }
            catch
            {
                return null;
            }
        }

        public static void DismissRecovery()
        {
            try
            {
                if (File.Exists(RecoveryFile))
                    File.Delete(RecoveryFile);
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not dismiss the notice: {e.Message}");
            }
        }

        private static IEnumerable<string> EnabledPluginFiles()
        {
            if (!Directory.Exists(Paths.PluginPath))
                return Enumerable.Empty<string>();

            string root = Path.GetFullPath(Paths.PluginPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Select(full => full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : Path.GetFileName(full));
        }
    }
}
