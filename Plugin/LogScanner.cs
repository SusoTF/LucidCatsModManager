using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;

namespace LucidCatsModManager
{
    internal static class LogScanner
    {
        internal sealed class Entry
        {
            public string Level = "";
            public string Source = "";
            public string Text = "";

            public override string ToString() => $"[{Level} : {Source}] {Text}";
        }

        private static readonly Regex Header = new Regex(@"^\[(\w+)\s*:\s*(.*?)\]\s?(.*)$");

        public static string LogPath => Path.Combine(Paths.BepInExRootPath, "LogOutput.log");

        public static List<Entry> ReadErrors()
        {
            var errors = new List<Entry>();
            if (!File.Exists(LogPath))
                return errors;

            try
            {
                string content;
                using (var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    content = reader.ReadToEnd();

                Entry current = null;
                foreach (string line in content.Split('\n'))
                {
                    string text = line.TrimEnd('\r');
                    Match match = Header.Match(text);
                    if (match.Success)
                    {
                        string level = match.Groups[1].Value;
                        bool isError = level.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
                                       level.Equals("Fatal", StringComparison.OrdinalIgnoreCase);
                        current = isError
                            ? new Entry { Level = level, Source = match.Groups[2].Value.Trim(), Text = match.Groups[3].Value }
                            : null;
                        if (current != null)
                            errors.Add(current);
                    }
                    else if (current != null && text.Length > 0)
                    {
                        current.Text += "\n" + text;
                    }
                }
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogDebug($"Could not read the log: {e.Message}");
            }
            return errors;
        }

        public static void Assign(List<ModEntry> mods, List<Entry> errors)
        {
            foreach (ModEntry mod in mods)
                mod.Errors.Clear();

            foreach (Entry error in errors)
                foreach (ModEntry mod in mods)
                    if (Belongs(error, mod))
                        mod.Errors.Add(error);
        }

        private static bool Belongs(Entry error, ModEntry mod)
        {
            if (string.Equals(error.Source, mod.Name, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(error.Source, "BepInEx", StringComparison.OrdinalIgnoreCase) &&
                error.Text.IndexOf("[" + mod.Name + " ", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (string.Equals(error.Source, "Unity Log", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(mod.AssemblyName) &&
                error.Text.IndexOf(mod.AssemblyName + ".", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }
    }
}
