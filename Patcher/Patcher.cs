using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace LucidCatsModManager.Patcher
{
    public static class ModManagerPatcher
    {
        private const string ManagerFile = "LucidCatsModManager.dll";
        private const int FailuresBeforeSuspects = 2;
        private const int FailuresBeforeSafeMode = 3;

        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        public static void Patch(AssemblyDefinition assembly) { }

        private static string PendingFile => Path.Combine(Paths.ConfigPath, "lucidcats.modmanager.pending.txt");
        private static string LaunchFile => Path.Combine(Paths.ConfigPath, "lucidcats.modmanager.launch.txt");
        private static string KnownFile => Path.Combine(Paths.ConfigPath, "lucidcats.modmanager.known.txt");
        private static string RecoveryFile => Path.Combine(Paths.ConfigPath, "lucidcats.modmanager.recovery.txt");

        public static void Initialize()
        {
            ManualLogSource log = Logger.CreateLogSource("Mod Manager");
            try
            {
                var suspects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                ApplyPendingChanges(log, suspects);

                bool managerActive = EnabledPluginFiles().Any(f => Path.GetFileName(f).Equals(ManagerFile, StringComparison.OrdinalIgnoreCase));
                if (!managerActive)
                {
                    TryDelete(LaunchFile);
                    return;
                }

                int failures = 0;
                if (File.Exists(LaunchFile))
                {
                    ReadLaunchFile(out int previousFailures, suspects);
                    failures = previousFailures + 1;
                    log.LogWarning($"The previous launch didn't reach the main menu ({failures} time(s) in a row).");
                }

                HashSet<string> known = ReadKnownFiles();
                if (known != null)
                    foreach (string file in EnabledPluginFiles())
                        if (!known.Contains(file))
                            suspects.Add(file);

                if (failures >= FailuresBeforeSuspects)
                {
                    List<string> toDisable = suspects
                        .Where(f => File.Exists(Path.Combine(Paths.PluginPath, f)) && !IsManager(f))
                        .ToList();
                    string reason = "suspects";

                    if (toDisable.Count == 0 && failures >= FailuresBeforeSafeMode)
                    {
                        toDisable = EnabledPluginFiles().Where(f => !IsManager(f)).ToList();
                        reason = "safe";
                    }

                    if (toDisable.Count > 0)
                    {
                        foreach (string file in toDisable)
                            Disable(file, log);

                        WriteRecoveryFile(reason, toDisable);
                        log.LogWarning(reason == "safe"
                            ? $"Safe mode: turned off {toDisable.Count} mod(s) so the game can start."
                            : $"Turned off {toDisable.Count} recently added mod(s) so the game can start.");

                        failures = 0;
                        suspects.Clear();
                    }
                }

                WriteLaunchFile(failures, suspects);
            }
            catch (Exception e)
            {
                log.LogError($"Mod Manager patcher error: {e.Message}");
            }
            finally
            {
                Logger.Sources.Remove(log);
            }
        }

        private static void ApplyPendingChanges(ManualLogSource log, HashSet<string> suspects)
        {
            if (!File.Exists(PendingFile))
                return;

            int applied = 0;
            foreach (string raw in File.ReadAllLines(PendingFile))
            {
                string line = raw.Trim();
                int tab = line.IndexOf('\t');
                if (line.Length == 0 || line.StartsWith("#") || tab <= 0)
                    continue;

                string action = line.Substring(0, tab).Trim().ToLowerInvariant();
                string relative = line.Substring(tab + 1).Trim();
                if (!IsSafePath(relative, out string enabledPath))
                {
                    log.LogWarning($"Ignoring an unexpected entry: {relative}");
                    continue;
                }
                string disabledPath = enabledPath + ".disabled";

                if (action == "disable" && File.Exists(enabledPath) && !File.Exists(disabledPath))
                {
                    File.Move(enabledPath, disabledPath);
                    log.LogInfo($"Disabled {relative}");
                    applied++;
                }
                else if (action == "enable" && File.Exists(disabledPath) && !File.Exists(enabledPath))
                {
                    File.Move(disabledPath, enabledPath);
                    log.LogInfo($"Enabled {relative}");
                    suspects.Add(relative);
                    applied++;
                }
            }

            File.Delete(PendingFile);
            log.LogInfo($"Applied {applied} change(s) from the Mod Manager.");
        }

        private static void Disable(string relative, ManualLogSource log)
        {
            if (!IsSafePath(relative, out string enabledPath))
                return;
            string disabledPath = enabledPath + ".disabled";
            try
            {
                if (File.Exists(enabledPath) && !File.Exists(disabledPath))
                {
                    File.Move(enabledPath, disabledPath);
                    log.LogWarning($"Turned off {relative}");
                }
            }
            catch (Exception e)
            {
                log.LogError($"Could not turn off {relative}: {e.Message}");
            }
        }

        private static bool IsSafePath(string relative, out string fullPath)
        {
            fullPath = Path.GetFullPath(Path.Combine(Paths.PluginPath, relative));
            string root = Path.GetFullPath(Paths.PluginPath);
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   fullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManager(string relative) =>
            Path.GetFileName(relative).Equals(ManagerFile, StringComparison.OrdinalIgnoreCase);

        private static List<string> EnabledPluginFiles()
        {
            var files = new List<string>();
            if (!Directory.Exists(Paths.PluginPath))
                return files;

            string root = Path.GetFullPath(Paths.PluginPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (string file in Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
            {
                string full = Path.GetFullPath(file);
                files.Add(full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : Path.GetFileName(full));
            }
            return files;
        }

        private static HashSet<string> ReadKnownFiles()
        {
            if (!File.Exists(KnownFile))
                return null;
            return new HashSet<string>(
                File.ReadAllLines(KnownFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")),
                StringComparer.OrdinalIgnoreCase);
        }

        private static void ReadLaunchFile(out int failures, HashSet<string> suspects)
        {
            failures = 0;
            foreach (string raw in File.ReadAllLines(LaunchFile))
            {
                string line = raw.Trim();
                if (line.StartsWith("failures=") && int.TryParse(line.Substring(9), out int value))
                    failures = value;
                else if (line.StartsWith("suspect="))
                    suspects.Add(line.Substring(8));
            }
        }

        private static void WriteLaunchFile(int failures, IEnumerable<string> suspects)
        {
            var lines = new List<string>
            {
                "# Written when the game starts and deleted by the Mod Manager once the main menu is reached.",
                "failures=" + failures,
            };
            lines.AddRange(suspects.Select(s => "suspect=" + s));
            File.WriteAllLines(LaunchFile, lines);
        }

        private static void WriteRecoveryFile(string reason, IEnumerable<string> disabled)
        {
            var lines = new List<string>
            {
                "# The Mod Manager turned these mods off because the game couldn't reach the main menu.",
                "reason=" + reason,
                "time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            };
            lines.AddRange(disabled.Select(d => "disabled=" + d));
            File.WriteAllLines(RecoveryFile, lines);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
