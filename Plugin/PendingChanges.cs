using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace LucidCatsModManager
{
    internal static class PendingChanges
    {
        private static string FilePath => Path.Combine(Paths.ConfigPath, "lucidcats.modmanager.pending.txt");

        private static readonly Dictionary<string, bool> Changes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static int Count => Changes.Count;

        public static void Load()
        {
            Changes.Clear();
            if (!File.Exists(FilePath))
                return;

            try
            {
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    int tab = line.IndexOf('\t');
                    if (line.StartsWith("#") || tab <= 0)
                        continue;
                    string action = line.Substring(0, tab).Trim();
                    string path = line.Substring(tab + 1).Trim();
                    if (action.Equals("enable", StringComparison.OrdinalIgnoreCase))
                        Changes[path] = true;
                    else if (action.Equals("disable", StringComparison.OrdinalIgnoreCase))
                        Changes[path] = false;
                }
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not read the pending changes: {e.Message}");
            }
        }

        public static bool TryGet(string relativePath, out bool enable)
        {
            enable = false;
            return !string.IsNullOrEmpty(relativePath) && Changes.TryGetValue(relativePath, out enable);
        }

        public static void SetWanted(ModEntry mod, bool enable)
        {
            if (mod.IsSelf || string.IsNullOrEmpty(mod.RelativePath))
                return;

            if (enable == mod.EnabledOnDisk)
                Changes.Remove(mod.RelativePath);
            else
                Changes[mod.RelativePath] = enable;
            Save();
        }

        private static void Save()
        {
            try
            {
                if (Changes.Count == 0)
                {
                    if (File.Exists(FilePath))
                        File.Delete(FilePath);
                    return;
                }

                var lines = new List<string> { "# Changes applied by the Mod Manager the next time the game starts." };
                foreach (KeyValuePair<string, bool> change in Changes)
                    lines.Add((change.Value ? "enable" : "disable") + "\t" + change.Key);
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogError($"Could not save the pending changes: {e.Message}");
            }
        }

        public static bool PatcherInstalled()
        {
            try
            {
                return Directory.Exists(Paths.PatcherPluginPath) &&
                       Directory.GetFiles(Paths.PatcherPluginPath, "LucidCatsModManager.Patcher.dll", SearchOption.AllDirectories).Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
