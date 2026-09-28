using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LucidCatsModManager
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ModManagerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "lucidcats.modmanager";
        public const string PluginName = "Mod Manager";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            var harmony = new Harmony(PluginGuid);
            MenuWatcher.Install(harmony);
            PendingChanges.Load();
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (!PendingChanges.PatcherInstalled())
                Log.LogWarning("The Mod Manager patcher isn't installed, so turning mods on/off won't work. See the installation guide.");

            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup > 60f)
                LaunchGuard.MarkSuccess();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MainMenu")
            {
                LaunchGuard.MarkSuccess();
                ModManagerMenu.Create(scene);
            }
        }
    }
}
