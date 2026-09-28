using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BepInEx;
using Steamworks;
using UnityEngine;

namespace LucidCatsModManager
{
    internal static class GameActions
    {
        public static void ShowFile(string file)
        {
            try
            {
                if (File.Exists(file))
                    Process.Start("explorer.exe", $"/select,\"{file}\"");
                else
                    OpenFolder(Path.GetDirectoryName(file));
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not show the file: {e.Message}");
            }
        }

        public static void OpenFolder(string folder)
        {
            try
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    Process.Start("explorer.exe", $"\"{folder}\"");
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not open the folder: {e.Message}");
            }
        }

        public static void OpenFile(string file)
        {
            try
            {
                if (File.Exists(file))
                    Process.Start("explorer.exe", $"\"{file}\"");
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not open the file: {e.Message}");
            }
        }

        public static void CopyToClipboard(string text)
        {
            GUIUtility.systemCopyBuffer = text;
        }

        public static string BuildErrorReport(ModEntry mod)
        {
            var report = new StringBuilder();
            report.AppendLine($"Mod: {mod.Name} {mod.Version} ({mod.Guid})");
            report.AppendLine($"Game version: {Application.version}");
            report.AppendLine($"BepInEx: {typeof(Paths).Assembly.GetName().Version}");
            report.AppendLine($"Errors this session: {mod.Errors.Count}");
            report.AppendLine();
            foreach (LogScanner.Entry error in mod.Errors)
                report.AppendLine(error.ToString());
            return report.ToString();
        }

        public static void RestartGame()
        {
            try
            {
                uint appId = SteamClient.IsValid ? SteamClient.AppId.Value : 0;
                if (appId != 0)
                {
                    var relaunch = new ProcessStartInfo("cmd.exe", $"/c timeout /t 6 /nobreak >nul & start \"\" steam://rungameid/{appId}")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };
                    Process.Start(relaunch);
                }
                else
                {
                    ModManagerPlugin.Log.LogWarning("Steam isn't available, so the game will close without reopening.");
                }
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogWarning($"Could not schedule the restart: {e.Message}");
            }

            Application.Quit();
        }
    }
}
