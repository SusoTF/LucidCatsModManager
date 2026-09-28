using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace LucidCatsModManager
{
    internal sealed class Profile
    {
        public string Name = "";

        public readonly Dictionary<string, bool> Mods = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class ProfileStore
    {
        public const int MaxNameLength = 24;

        private static string FilePath => Path.Combine(Paths.ConfigPath, "lucidcats.modmanager.profiles.txt");

        public static List<Profile> Load()
        {
            var profiles = new List<Profile>();
            if (!File.Exists(FilePath))
                return profiles;

            try
            {
                Profile current = null;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        current = new Profile { Name = line.Substring(1, line.Length - 2) };
                        profiles.Add(current);
                    }
                    else if (current != null)
                    {
                        int equals = line.LastIndexOf('=');
                        if (equals > 0)
                            current.Mods[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not read the profiles: {e.Message}");
            }
            return profiles;
        }

        public static void Save(List<Profile> profiles)
        {
            try
            {
                var lines = new List<string> { "# Mod Manager profiles: [Name], then one line per mod file (on/off)." };
                foreach (Profile profile in profiles)
                {
                    lines.Add("");
                    lines.Add("[" + profile.Name + "]");
                    foreach (KeyValuePair<string, bool> mod in profile.Mods)
                        lines.Add(mod.Key + "=" + (mod.Value ? "on" : "off"));
                }
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogError($"Could not save the profiles: {e.Message}");
            }
        }

        public static string NextDefaultName(List<Profile> profiles)
        {
            for (int n = 1; ; n++)
            {
                string candidate = "Profile " + n;
                if (!profiles.Exists(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
            }
        }

        public static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;
            string cleaned = new string(raw.Where(c => c != '[' && c != ']' && c != '<' && c != '>' && c != '=' && !char.IsControl(c)).ToArray()).Trim();
            if (cleaned.Length > MaxNameLength)
                cleaned = cleaned.Substring(0, MaxNameLength).Trim();
            return cleaned;
        }

        public static Profile Capture(string name, IEnumerable<ModEntry> mods)
        {
            var profile = new Profile { Name = name };
            foreach (ModEntry mod in mods)
                if (!mod.IsSelf && !string.IsNullOrEmpty(mod.RelativePath))
                    profile.Mods[mod.RelativePath] = mod.WantsEnabled;
            return profile;
        }

        public static bool Matches(Profile profile, IEnumerable<ModEntry> mods)
        {
            foreach (ModEntry mod in mods)
                if (!mod.IsSelf && profile.Mods.TryGetValue(mod.RelativePath, out bool on) && on != mod.WantsEnabled)
                    return false;
            return true;
        }
    }
}
