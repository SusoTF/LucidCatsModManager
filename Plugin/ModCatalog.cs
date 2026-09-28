using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Mono.Cecil;

namespace LucidCatsModManager
{
    internal enum ModState
    {
        Active,
        Disabled,
        Pending,
        FailedToLoad,
    }

    internal sealed class ModEntry
    {
        public string Guid = "";
        public string Name = "";
        public string Version = "";
        public string Description = "";
        public string Author = "";
        public string AssemblyName = "";
        public string FilePath = "";
        public string ConfigPath = "";

        public ConfigFile Config;

        public string RelativePath = "";

        public bool Loaded;
        public bool EnabledOnDisk;
        public bool AddedThisSession;
        public bool IsSelf;

        public readonly List<string> Dependencies = new List<string>();
        public readonly List<LogScanner.Entry> Errors = new List<LogScanner.Entry>();

        public bool WantsEnabled => PendingChanges.TryGet(RelativePath, out bool enable) ? enable : EnabledOnDisk;

        public bool HasPendingChange => PendingChanges.TryGet(RelativePath, out _);

        public ModState State
        {
            get
            {
                if (HasPendingChange || AddedThisSession)
                    return ModState.Pending;
                if (Loaded)
                    return ModState.Active;
                if (!EnabledOnDisk)
                    return ModState.Disabled;
                return ModState.FailedToLoad;
            }
        }
    }

    internal static class ModCatalog
    {
        private const string DisabledSuffix = ".disabled";

        private static readonly Dictionary<string, (string description, string author)> Known =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["lucidcats.bestiary"] = ("An in-game encyclopedia of every monster you've encountered, with rotating 3D models.", "SusoTF"),
                ["lucidcats.savefiles"] = ("Save your runs and continue them later, solo or in co-op.", "SusoTF"),
                [ModManagerPlugin.PluginGuid] = ("Manage your installed mods from the main menu.", "SusoTF"),
            };

        public static List<ModEntry> Scan()
        {
            var byGuid = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos)
            {
                try
                {
                    ModEntry entry = FromLoaded(pair.Value);
                    byGuid[entry.Guid] = entry;
                }
                catch (Exception e)
                {
                    ModManagerPlugin.Log.LogDebug($"Could not read {pair.Key}: {e.Message}");
                }
            }

            DateTime sessionStart = SessionStart();
            if (Directory.Exists(Paths.PluginPath))
            {
                foreach (string file in Directory.GetFiles(Paths.PluginPath, "*", SearchOption.AllDirectories))
                {
                    bool enabled = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                    bool disabled = file.EndsWith(".dll" + DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                    if (!enabled && !disabled)
                        continue;

                    foreach (ModEntry entry in ReadFromFile(file))
                    {
                        if (byGuid.TryGetValue(entry.Guid, out ModEntry existing) && existing.Loaded)
                            continue;

                        entry.EnabledOnDisk = enabled;
                        entry.AddedThisSession = enabled && File.GetLastWriteTime(file) > sessionStart;
                        byGuid[entry.Guid] = entry;
                    }
                }
            }

            List<ModEntry> mods = byGuid.Values.ToList();
            foreach (ModEntry mod in mods)
            {
                mod.IsSelf = string.Equals(mod.Guid, ModManagerPlugin.PluginGuid, StringComparison.OrdinalIgnoreCase);
                if (Known.TryGetValue(mod.Guid, out var known))
                {
                    if (string.IsNullOrEmpty(mod.Description))
                        mod.Description = known.description;
                    if (string.IsNullOrEmpty(mod.Author))
                        mod.Author = known.author;
                }
                if (string.IsNullOrEmpty(mod.ConfigPath))
                {
                    string guess = Path.Combine(Paths.ConfigPath, mod.Guid + ".cfg");
                    if (File.Exists(guess))
                        mod.ConfigPath = guess;
                }
            }

            mods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return mods;
        }

        private static ModEntry FromLoaded(PluginInfo info)
        {
            var entry = new ModEntry
            {
                Guid = info.Metadata.GUID,
                Name = info.Metadata.Name,
                Version = info.Metadata.Version?.ToString() ?? "",
                FilePath = info.Location ?? "",
                Loaded = true,
                EnabledOnDisk = true,
            };
            entry.RelativePath = RelativeEnabledPath(entry.FilePath);

            foreach (BepInDependency dependency in info.Dependencies ?? Enumerable.Empty<BepInDependency>())
                if ((dependency.Flags & BepInDependency.DependencyFlags.HardDependency) != 0)
                    entry.Dependencies.Add(dependency.DependencyGUID);

            if (info.Instance != null)
            {
                Assembly assembly = info.Instance.GetType().Assembly;
                entry.AssemblyName = assembly.GetName().Name;
                entry.Description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "";
                string company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";
                if (!string.Equals(company, entry.AssemblyName, StringComparison.OrdinalIgnoreCase))
                    entry.Author = company;
                entry.Config = info.Instance.Config;
                entry.ConfigPath = info.Instance.Config?.ConfigFilePath ?? "";
            }
            return entry;
        }

        private static IEnumerable<ModEntry> ReadFromFile(string file)
        {
            var found = new List<ModEntry>();
            try
            {
                using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { InMemory = true }))
                {
                    string description = AttributeText(assembly.CustomAttributes, "System.Reflection.AssemblyDescriptionAttribute");
                    string company = AttributeText(assembly.CustomAttributes, "System.Reflection.AssemblyCompanyAttribute");
                    string assemblyName = assembly.Name.Name;

                    foreach (TypeDefinition type in assembly.MainModule.GetTypes())
                    {
                        CustomAttribute plugin = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
                        if (plugin == null || plugin.ConstructorArguments.Count < 3)
                            continue;

                        var entry = new ModEntry
                        {
                            Guid = plugin.ConstructorArguments[0].Value as string ?? "",
                            Name = plugin.ConstructorArguments[1].Value as string ?? "",
                            Version = plugin.ConstructorArguments[2].Value as string ?? "",
                            Description = description,
                            Author = string.Equals(company, assemblyName, StringComparison.OrdinalIgnoreCase) ? "" : company,
                            AssemblyName = assemblyName,
                            FilePath = file,
                            RelativePath = RelativeEnabledPath(file),
                        };

                        foreach (CustomAttribute dependency in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency"))
                        {
                            string guid = dependency.ConstructorArguments.Count > 0 ? dependency.ConstructorArguments[0].Value as string : null;
                            bool soft = dependency.ConstructorArguments.Count > 1 && dependency.ConstructorArguments[1].Value is int flags && (flags & 2) != 0;
                            if (!string.IsNullOrEmpty(guid) && !soft)
                                entry.Dependencies.Add(guid);
                        }

                        if (!string.IsNullOrEmpty(entry.Guid))
                            found.Add(entry);
                    }
                }
            }
            catch
            {
            }
            return found;
        }

        private static string AttributeText(IEnumerable<CustomAttribute> attributes, string fullName)
        {
            CustomAttribute attribute = attributes.FirstOrDefault(a => a.AttributeType.FullName == fullName);
            return attribute != null && attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value as string ?? "" : "";
        }

        private static string RelativeEnabledPath(string file)
        {
            if (string.IsNullOrEmpty(file))
                return "";
            string full = Path.GetFullPath(file);
            string root = Path.GetFullPath(Paths.PluginPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string relative = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : Path.GetFileName(full);
            if (relative.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
                relative = relative.Substring(0, relative.Length - DisabledSuffix.Length);
            return relative;
        }

        private static DateTime SessionStart()
        {
            try
            {
                return Process.GetCurrentProcess().StartTime;
            }
            catch
            {
                return DateTime.Now;
            }
        }
    }
}
