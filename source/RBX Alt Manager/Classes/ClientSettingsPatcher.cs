using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace RBX_Alt_Manager.Classes
{
    public static class ClientSettingsPatcher
    {
        private static DirectoryInfo ResolveRobloxVersionFolder()
        {
            string rawPath = Registry.ClassesRoot.OpenSubKey(@"roblox\DefaultIcon")?.GetValue("") as string;
            if (string.IsNullOrWhiteSpace(rawPath))
                return null;

            string candidatePath = rawPath.Trim().Trim('"');
            int iconSuffixIndex = candidatePath.IndexOf(',');
            if (iconSuffixIndex > 1 && !File.Exists(candidatePath))
                candidatePath = candidatePath.Substring(0, iconSuffixIndex);

            candidatePath = candidatePath.Trim().Trim('"');

            if (File.Exists(candidatePath))
                return Directory.GetParent(candidatePath);

            if (Directory.Exists(candidatePath))
                return new DirectoryInfo(candidatePath);

            return null;
        }

        public static void PatchSettings()
        {
            string CustomFN = AccountManager.General.Exists("CustomClientSettings") ? AccountManager.General.Get<string>("CustomClientSettings") : string.Empty;
            bool HasCustomSettings = !string.IsNullOrEmpty(CustomFN) && File.Exists(CustomFN);
            bool UnlockFps = AccountManager.General.Get<bool>("UnlockFPS");

            if (!HasCustomSettings && !UnlockFps)
                return;

            DirectoryInfo VersionFolder = ResolveRobloxVersionFolder();
            if (VersionFolder == null || !VersionFolder.Exists)
                return;

            if (!VersionFolder.Name.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
                return;

            bool hasPlayerBinary =
                File.Exists(Path.Combine(VersionFolder.FullName, "RobloxPlayerBeta.exe"))
                || File.Exists(Path.Combine(VersionFolder.FullName, "RobloxPlayerLauncher.exe"));

            if (!hasPlayerBinary)
                return;

            DirectoryInfo SettingsFolder = new DirectoryInfo(Path.Combine(VersionFolder.FullName, "ClientSettings"));
            if (!SettingsFolder.Exists)
                SettingsFolder.Create();

            string SettingsFN = Path.Combine(SettingsFolder.FullName, "ClientAppSettings.json");

            if (HasCustomSettings)
            {
                File.Copy(CustomFN, SettingsFN, true);
            }
            else if (UnlockFps)
            {
                if (File.Exists(SettingsFN) && File.ReadAllText(SettingsFN).TryParseJson(out JObject Settings))
                {
                    Settings["DFIntTaskSchedulerTargetFps"] = AccountManager.General.Exists("MaxFPSValue") ? AccountManager.General.Get<int>("MaxFPSValue") : 240;
                    File.WriteAllText(SettingsFN, Settings.ToString(Newtonsoft.Json.Formatting.None));
                }
                else
                {
                    int targetFps = AccountManager.General.Exists("MaxFPSValue") ? AccountManager.General.Get<int>("MaxFPSValue") : 240;
                    File.WriteAllText(SettingsFN, $"{{\"DFIntTaskSchedulerTargetFps\":{targetFps}}}");
                }
            }
        }
    }
}
