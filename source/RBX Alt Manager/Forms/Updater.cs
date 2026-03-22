using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using File = System.IO.File;

namespace Auto_Update
{
    public partial class AutoUpdater : Form
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/megafartCc/tttt/releases/latest";
        private const string MainExecutableName = "Roblox Account Manager.exe";
        private const string ReleaseTagFileName = "RAMReleaseTag.txt";

        private static readonly string[] PreferredAssetTokens = { "ramv2", "custom", "release" };
        private static readonly HashSet<string> PreservedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AccountData.json",
            "AccountData.json.backup",
            "RAMSettings.ini",
            "RAMTheme.ini",
            "RecentGames.json",
            "log.txt"
        };

        private string DownloadArchivePath;
        private string WorkingRootPath;
        private long TotalDownloadSize;

        private delegate void SafeCallDelegateSetProgress(int progress);
        private delegate void SafeCallDelegateSetStatus(string text);

        private sealed class ReleaseAsset
        {
            public string TagName { get; set; }
            public string Name { get; set; }
            public string Url { get; set; }
        }

        public static string GetLocalReleaseTag()
        {
            try
            {
                string PathValue = Path.Combine(Environment.CurrentDirectory, ReleaseTagFileName);
                return File.Exists(PathValue) ? File.ReadAllText(PathValue).Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static async Task<string> TryGetLatestReleaseTagAsync()
        {
            try
            {
                using HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                Client.DefaultRequestHeaders.Add("User-Agent", "RAMV2-Custom-Updater");

                string ReleasesJson = await Client.GetStringAsync(LatestReleaseApi);
                JObject Release = JObject.Parse(ReleasesJson);
                return Release["tag_name"]?.Value<string>() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public AutoUpdater()
        {
            InitializeComponent();
        }

        private async void AutoUpdater_Load(object sender, EventArgs e)
        {
            try
            {
                await RunUpdateAsync();
            }
            catch (Exception x)
            {
                SetStatus("Error");
                DialogResult result = MessageBox.Show(
                    this,
                    $"Update failed.\n\n{x.Message}\n\nMake sure the latest GitHub release has a .zip asset with app files.",
                    "Custom Updater",
                    MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Error);

                if (result == DialogResult.Retry)
                    await RunUpdateAsync();
                else
                    Close();
            }
        }

        private async Task RunUpdateAsync()
        {
            PrepareWorkingPaths();

            using HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            Client.DefaultRequestHeaders.Add("User-Agent", "RAMV2-Custom-Updater");

            SetStatus("Checking latest custom release...");
            ReleaseAsset Asset = await FetchLatestReleaseAsset(Client);

            string LocalReleaseTag = GetLocalReleaseTag();
            if (!string.IsNullOrWhiteSpace(LocalReleaseTag)
                && string.Equals(LocalReleaseTag, Asset.TagName, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"Already up to date ({Asset.TagName})");
                SetProgress(100);
                await Task.Delay(1250);
                Close();
                return;
            }

            SetStatus($"Downloading {Asset.Name}...");
            TotalDownloadSize = await TryGetContentLengthAsync(Client, Asset.Url) ?? 0;

            using (FileStream File = new FileStream(DownloadArchivePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Progress<float> Progress = new Progress<float>(OnProgressChanged);
                await Client.DownloadAsync(Asset.Url, File, Progress);
            }

            SetStatus("Extracting files...");
            string ExtractPath = Path.Combine(WorkingRootPath, "extract");
            Directory.CreateDirectory(ExtractPath);
            ZipFile.ExtractToDirectory(DownloadArchivePath, ExtractPath);

            string PayloadRoot = ResolvePayloadRoot(ExtractPath);
            if (!File.Exists(Path.Combine(PayloadRoot, MainExecutableName)))
                throw new InvalidDataException($"{MainExecutableName} was not found in the update archive.");

            SetStatus("Preparing apply step...");
            string ScriptPath = Path.Combine(WorkingRootPath, "apply_update.cmd");
            string ScriptBody = BuildApplyScript(
                Process.GetCurrentProcess().Id,
                Environment.CurrentDirectory,
                PayloadRoot,
                WorkingRootPath,
                Asset.TagName);
            File.WriteAllText(ScriptPath, ScriptBody, Encoding.ASCII);

            SetStatus("Applying update...");
            StartApplyScript(ScriptPath);

            await Task.Delay(350);
            Environment.Exit(0);
        }

        private void PrepareWorkingPaths()
        {
            string BasePath = Path.Combine(Path.GetTempPath(), "RAMV2_CustomUpdater");
            WorkingRootPath = Path.Combine(BasePath, Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(WorkingRootPath);
            DownloadArchivePath = Path.Combine(WorkingRootPath, "update.zip");
        }

        private static async Task<long?> TryGetContentLengthAsync(HttpClient Client, string Url)
        {
            try
            {
                using HttpRequestMessage HeadRequest = new HttpRequestMessage(HttpMethod.Head, Url);
                using HttpResponseMessage Response = await Client.SendAsync(HeadRequest, HttpCompletionOption.ResponseHeadersRead);
                if (Response.IsSuccessStatusCode)
                    return Response.Content.Headers.ContentLength;
            }
            catch { }

            return null;
        }

        private static async Task<ReleaseAsset> FetchLatestReleaseAsset(HttpClient Client)
        {
            string ReleasesJson = await Client.GetStringAsync(LatestReleaseApi);
            JObject Release = JObject.Parse(ReleasesJson);
            string TagName = Release["tag_name"]?.Value<string>() ?? "latest";

            JArray Assets = Release["assets"] as JArray;

            if (Assets == null || Assets.Count == 0)
                throw new InvalidOperationException("No release assets found on latest release.");

            List<JObject> ZipAssets = Assets
                .OfType<JObject>()
                .Where(Asset => Asset["name"]?.Value<string>()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (ZipAssets.Count == 0)
                throw new InvalidOperationException("No .zip asset found on latest release.");

            JObject Selected = ZipAssets
                .OrderByDescending(Asset => ScoreAsset(Asset["name"]?.Value<string>() ?? string.Empty))
                .ThenByDescending(Asset => Asset["size"]?.Value<long>() ?? 0L)
                .First();

            string Name = Selected["name"]?.Value<string>() ?? "update.zip";
            string Url = Selected["browser_download_url"]?.Value<string>();

            if (string.IsNullOrWhiteSpace(Url))
                throw new InvalidOperationException("Selected release asset does not include a download URL.");

            return new ReleaseAsset
            {
                TagName = TagName,
                Name = Name,
                Url = Url
            };
        }

        private static int ScoreAsset(string Name)
        {
            string Lower = (Name ?? string.Empty).ToLowerInvariant();
            int Score = 0;

            foreach (string Token in PreferredAssetTokens)
                if (Lower.Contains(Token))
                    Score++;

            if (Lower.Contains("full")) Score++;
            if (Lower.Contains("portable")) Score++;

            return Score;
        }

        private static string ResolvePayloadRoot(string ExtractRoot)
        {
            if (File.Exists(Path.Combine(ExtractRoot, MainExecutableName)))
                return ExtractRoot;

            string[] ExeMatches = Directory.GetFiles(ExtractRoot, MainExecutableName, SearchOption.AllDirectories);
            if (ExeMatches.Length == 0)
                throw new InvalidDataException($"Could not find {MainExecutableName} in extracted archive.");

            return Path.GetDirectoryName(
                ExeMatches
                    .OrderBy(PathValue => PathValue.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
                    .First());
        }

        private static string BuildApplyScript(int CurrentPid, string AppDir, string PayloadDir, string WorkRoot, string LatestTag)
        {
            string PreservedFilesArg = string.Join(" ", PreservedFileNames.Select(FileName => $"\"{FileName}\""));

            StringBuilder Builder = new StringBuilder();
            Builder.AppendLine("@echo off");
            Builder.AppendLine("setlocal");
            Builder.AppendLine($"set \"APPDIR={AppDir}\"");
            Builder.AppendLine($"set \"PAYLOAD={PayloadDir}\"");
            Builder.AppendLine($"set \"WORKROOT={WorkRoot}\"");
            Builder.AppendLine(":wait_for_exit");
            Builder.AppendLine($"tasklist /FI \"PID eq {CurrentPid}\" 2>NUL | find \"{CurrentPid}\" >NUL");
            Builder.AppendLine("if not errorlevel 1 (");
            Builder.AppendLine("  timeout /t 1 /nobreak >NUL");
            Builder.AppendLine("  goto wait_for_exit");
            Builder.AppendLine(")");
            Builder.AppendLine($"robocopy \"%PAYLOAD%\" \"%APPDIR%\" /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XF {PreservedFilesArg} >NUL");
            if (!string.IsNullOrWhiteSpace(LatestTag))
                Builder.AppendLine($"echo {LatestTag} > \"%APPDIR%\\{ReleaseTagFileName}\"");
            Builder.AppendLine($"start \"\" \"%APPDIR%\\{MainExecutableName}\"");
            Builder.AppendLine("rmdir /S /Q \"%WORKROOT%\" >NUL 2>&1");
            Builder.AppendLine("del \"%~f0\" >NUL 2>&1");
            Builder.AppendLine("endlocal");

            return Builder.ToString();
        }

        private static void StartApplyScript(string ScriptPath)
        {
            ProcessStartInfo StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{ScriptPath}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(ScriptPath)
            };

            Process.Start(StartInfo);
        }

        private static double BytesToMb(long bytes) => Math.Round(bytes / 1024d / 1024d, 2);

        private void OnProgressChanged(float Value)
        {
            Value = Math.Max(0f, Math.Min(1f, Value));
            string SizeLabel = TotalDownloadSize > 0 ? $" of {BytesToMb(TotalDownloadSize):0.00}MB" : string.Empty;

            SetStatus(Value >= 1f
                ? "Finalizing download..."
                : $"Downloaded {(Value * 100f):0}%{SizeLabel}");

            SetProgress((int)(Value * 100f));
        }

        private void SetStatus(string Text)
        {
            if (Status.InvokeRequired)
            {
                SafeCallDelegateSetStatus SetStatusDelegate = SetStatus;
                Status.Invoke(SetStatusDelegate, new object[] { Text });
            }
            else
                Status.Text = Text;
        }

        private void SetProgress(int Progress)
        {
            int Clamped = Math.Max(0, Math.Min(100, Progress));

            if (ProgressBar.InvokeRequired)
            {
                SafeCallDelegateSetProgress SetProgressDelegate = SetProgress;
                ProgressBar.Invoke(SetProgressDelegate, new object[] { Clamped });
            }
            else
                ProgressBar.Value = Clamped;
        }
    }
}
