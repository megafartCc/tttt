using BrightIdeasSoftware;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;
using RBX_Alt_Manager.Classes;
using RBX_Alt_Manager.Forms;
using RBX_Alt_Manager.Properties;
using RestSharp;
using Sodium;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebSocketSharp;

#pragma warning disable CS0618 // parameter warnings

namespace RBX_Alt_Manager
{
    public partial class AccountManager : Form
    {
        public static AccountManager Instance;
        public static List<Account> AccountsList;
        public static List<Account> SelectedAccounts;
        public static List<Game> RecentGames;
        public static Account SelectedAccount;
        public static Account LastValidAccount; // this is used for the Batch class since getting place details requires authorization, auto updates whenever an account is used
        public static RestClient MainClient;
        public static RestClient AvatarClient;
        public static RestClient FriendsClient;
        public static RestClient UsersClient;
        public static RestClient AuthClient;
        public static RestClient EconClient;
        public static RestClient AccountClient;
        public static RestClient GameJoinClient;
        public static RestClient Web13Client;
        public static string CurrentPlaceId { get => Instance.PlaceID.Text; }
        public static string CurrentJobId { get => Instance.JobID.Text; }
        private ArgumentsForm afform;
        private ServerList ServerListForm;
        private AccountUtils UtilsForm;
        private ImportForm ImportAccountsForm;
        private AccountFields FieldsForm;
        private ThemeEditor ThemeForm;
        private AccountControl ControlForm;
        private SettingsForm SettingsForm;
        private RecentGamesForm RGForm;
        private readonly static DateTime startTime = DateTime.Now;
        public static bool IsTeleport = false;
        public static bool UseOldJoin = false;
        public static bool ShuffleJobID = false;
        private static bool PuppeteerSupported;
        public static string CurrentVersion;
        public OLVListItem SelectedAccountItem { get; private set; }
        private WebServer AltManagerWS;
        private string WSPassword { get; set; }
        public System.Timers.Timer AutoCookieRefresh { get; private set; }

        public static IniFile IniSettings;
        public static IniSection General;
        public static IniSection Developer;
        public static IniSection WebServer;
        public static IniSection AccountControl;
        public static IniSection Watcher;
        public static IniSection Prompts;

        private static Mutex rbxMultiMutex;
        private static Mutex rbxMultiEventNameMutex;
        private readonly static object saveLock = new object();
        private readonly static object rgSaveLock = new object();
        public event EventHandler<GameArgs> RecentGameAdded;

        private bool IsResettingPassword;
        private bool IsDownloadingChromium;
        private bool LaunchNext;
        private CancellationTokenSource LauncherToken;
        private System.Timers.Timer PresenceTimer;
        private System.Timers.Timer LiveStatusTimer;
        private System.Timers.Timer LastSeenTimer;
        private System.Timers.Timer ProcessOptimizerTimer;
        private readonly static Regex BrowserTrackerRegex = new Regex(@"(?:\-b\s+|browsertrackerid[:=])(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly static Regex MultiRbxHandleRegex = new Regex(@"^\s*([0-9A-F]+):\s+\w+\s+.+\\(ROBLOX_singletonMutex|ROBLOX_singletonEvent)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
        private int PresenceUpdateInProgress;
        private int LiveStatusUpdateInProgress;
        private int ProcessOptimizerInProgress;
        private int BulkLaunchInProgress;
        private DateTime LastOpenInstancePresenceUpdate = DateTime.MinValue;
        private OLVColumn CurrentPidColumn;
        private OLVColumn CurrentGameColumn;
        private OLVColumn LastSeenColumn;
        private readonly ConcurrentDictionary<long, string> PlaceNameCache = new ConcurrentDictionary<long, string>();
        private readonly ConcurrentDictionary<long, byte> PlaceNameLookups = new ConcurrentDictionary<long, byte>();
        private readonly ConcurrentDictionary<long, ScriptLiveStatusOverride> ScriptLiveStatusOverrides = new ConcurrentDictionary<long, ScriptLiveStatusOverride>();
        private static readonly TimeSpan ScriptLiveStatusOverrideTtl = TimeSpan.FromSeconds(15);
        private const long AutoRejoinPlaceId = 109983668079237;
        private static readonly TimeSpan AutoRejoinOfflineThreshold = TimeSpan.FromSeconds(105);
        private static readonly TimeSpan AutoRejoinRetryCooldown = TimeSpan.FromSeconds(30);
        private readonly ConcurrentDictionary<long, AutoRejoinState> AutoRejoinStates = new ConcurrentDictionary<long, AutoRejoinState>();
        private readonly ConcurrentDictionary<long, byte> AutoRejoinInProgress = new ConcurrentDictionary<long, byte>();
        private readonly ConcurrentDictionary<string, ServerClaimState> ScriptServerClaims = new ConcurrentDictionary<string, ServerClaimState>();
        private readonly object ScriptServerClaimsLock = new object();
        private static readonly TimeSpan ScriptServerClaimMinLease = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ScriptServerClaimMaxLease = TimeSpan.FromMinutes(30);

        private sealed class ScriptLiveStatusOverride
        {
            public DateTime UpdatedAtUtc { get; set; }
            public bool HasOpenInstance { get; set; }
            public bool IsOnServer { get; set; }
            public long? PlaceId { get; set; }
            public string GameName { get; set; }
        }

        private sealed class OpenInstanceState
        {
            public bool? IsConnectedToServer { get; set; }
            public int ProcessId { get; set; }
        }

        private sealed class AutoRejoinState
        {
            public bool HasSeenActiveSession { get; set; }
            public DateTime? OfflineSinceUtc { get; set; }
            public DateTime LastAttemptUtc { get; set; } = DateTime.MinValue;
        }

        private sealed class ServerClaimState
        {
            public string Owner { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
        }

        private static readonly byte[] Entropy = new byte[] { 0x52, 0x4f, 0x42, 0x4c, 0x4f, 0x58, 0x20, 0x41, 0x43, 0x43, 0x4f, 0x55, 0x4e, 0x54, 0x20, 0x4d, 0x41, 0x4e, 0x41, 0x47, 0x45, 0x52, 0x20, 0x7c, 0x20, 0x3a, 0x29, 0x20, 0x7c, 0x20, 0x42, 0x52, 0x4f, 0x55, 0x47, 0x48, 0x54, 0x20, 0x54, 0x4f, 0x20, 0x59, 0x4f, 0x55, 0x20, 0x42, 0x55, 0x59, 0x20, 0x69, 0x63, 0x33, 0x77, 0x30, 0x6c, 0x66 };

        [DllImport("DwmApi")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass, ref int processInformation, int processInformationLength);

        private const int ProcessIoPriorityClass = 33;

        public static void SetDarkBar(IntPtr Handle)
        {
            if (ThemeEditor.UseDarkTopBar && DwmSetWindowAttribute(Handle, 19, new[] { 1 }, 4) != 0)
                DwmSetWindowAttribute(Handle, 20, new[] { 1 }, 4);
        }

        public AccountManager()
        {
            Instance = this;

            ThemeEditor.LoadTheme();

            SetDarkBar(Handle);

            string SettingsPath = Path.Combine(AppContext.BaseDirectory, "RAMSettings.ini");
            IniSettings = File.Exists(SettingsPath) ? new IniFile(SettingsPath) : new IniFile();

            General = IniSettings.Section("General");
            Developer = IniSettings.Section("Developer");
            WebServer = IniSettings.Section("WebServer");
            AccountControl = IniSettings.Section("AccountControl");
            Watcher = IniSettings.Section("Watcher");
            Prompts = IniSettings.Section("Prompts");

            if (!General.Exists("CheckForUpdates")) General.Set("CheckForUpdates", "false");
            if (!General.Exists("AccountJoinDelay")) General.Set("AccountJoinDelay", "8");
            if (!General.Exists("AsyncJoin")) General.Set("AsyncJoin", "false");
            if (!General.Exists("DisableAgingAlert")) General.Set("DisableAgingAlert", "false");
            if (!General.Exists("SavePasswords")) General.Set("SavePasswords", "true");
            if (!General.Exists("ServerRegionFormat")) General.Set("ServerRegionFormat", "<city>, <countryCode>", "Visit http://ip-api.com/json/1.1.1.1 to see available format options");
            if (!General.Exists("MaxRecentGames")) General.Set("MaxRecentGames", "8");
            if (!General.Exists("ShuffleChoosesLowestServer")) General.Set("ShuffleChoosesLowestServer", "false");
            if (!General.Exists("ShufflePageCount")) General.Set("ShufflePageCount", "5");
            if (!General.Exists("IPApiLink")) General.Set("IPApiLink", "http://ip-api.com/json/<ip>");
            if (!General.Exists("WindowScale"))
            {
                General.Set("WindowScale", Screen.PrimaryScreen.Bounds.Height <= Screen.PrimaryScreen.Bounds.Width /*scuffed*/ ? Math.Max(Math.Min(Screen.PrimaryScreen.Bounds.Height / 1080f, 2f), 1f).ToString(".0#", CultureInfo.InvariantCulture) : "1.0");

                if (Program.Scale > 1)
                    if (!Utilities.YesNoPrompt("Roblox Account Manager", "RAM has detected you have a monitor larger than average", $"Would you like to keep the WindowScale setting of {Program.Scale:F2}?", false))
                        General.Set("WindowScale", "1.0");
                    else
                        MessageBox.Show("In case the font scaling is incorrect, open RAMSettings.ini and change \"ScaleFonts=true\" to \"ScaleFonts=false\" without the quotes.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (!General.Exists("ScaleFonts")) General.Set("ScaleFonts", "true");
            if (!General.Exists("AutoCookieRefresh")) General.Set("AutoCookieRefresh", "true");
            if (!General.Exists("AutoCloseLastProcess")) General.Set("AutoCloseLastProcess", "true");
            if (!General.Exists("ShowPresence")) General.Set("ShowPresence", "true");
            if (!General.Exists("PresenceUpdateRate")) General.Set("PresenceUpdateRate", "5");
            if (!General.Exists("EnableLiveStatusPush")) General.Set("EnableLiveStatusPush", "true");
            if (!General.Exists("AutoRejoin")) General.Set("AutoRejoin", "false");
            if (!General.Exists("EnableProcessOptimizer")) General.Set("EnableProcessOptimizer", "false");
            if (!General.Exists("RobloxPriority")) General.Set("RobloxPriority", "BelowNormal");
            if (!General.Exists("RobloxIoPriority")) General.Set("RobloxIoPriority", "Low");
            if (!General.Exists("RobloxAffinityMask")) General.Set("RobloxAffinityMask", "");
            if (!General.Exists("RaiseManagerPriority")) General.Set("RaiseManagerPriority", "true");
            if (!General.Exists("ManagerPriority")) General.Set("ManagerPriority", "AboveNormal");
            if (!General.Exists("ProcessLassoPath")) General.Set("ProcessLassoPath", @"C:\Program Files\Process Lasso\ProcessLasso.exe");
            if (!General.Exists("RAMMapPath")) General.Set("RAMMapPath", @"C:\Sysinternals\RAMMap.exe");
            if (!General.Exists("UnlockFPS")) General.Set("UnlockFPS", "false");
            if (!General.Exists("MaxFPSValue")) General.Set("MaxFPSValue", "120");
            if (!General.Exists("UseCefSharpBrowser")) General.Set("UseCefSharpBrowser", "false");

            if (!Developer.Exists("DevMode")) Developer.Set("DevMode", "false");
            if (!Developer.Exists("EnableWebServer")) Developer.Set("EnableWebServer", "false");

            if (!WebServer.Exists("WebServerPort")) WebServer.Set("WebServerPort", "7963");
            if (!WebServer.Exists("AllowGetCookie")) WebServer.Set("AllowGetCookie", "false");
            if (!WebServer.Exists("AllowGetAccounts")) WebServer.Set("AllowGetAccounts", "false");
            if (!WebServer.Exists("AllowLaunchAccount")) WebServer.Set("AllowLaunchAccount", "false");
            if (!WebServer.Exists("AllowAccountEditing")) WebServer.Set("AllowAccountEditing", "false");
            if (!WebServer.Exists("Password")) WebServer.Set("Password", ""); else WSPassword = WebServer.Get("Password");
            if (!WebServer.Exists("EveryRequestRequiresPassword")) WebServer.Set("EveryRequestRequiresPassword", "false");
            if (!WebServer.Exists("AllowExternalConnections")) WebServer.Set("AllowExternalConnections", "false");

            if (!AccountControl.Exists("AllowExternalConnections")) AccountControl.Set("AllowExternalConnections", "false");
            if (!AccountControl.Exists("RelaunchDelay")) AccountControl.Set("RelaunchDelay", "60");
            if (!AccountControl.Exists("LauncherDelayNumber")) AccountControl.Set("LauncherDelayNumber", "9");
            if (!AccountControl.Exists("NexusPort")) AccountControl.Set("NexusPort", "5242");

            InitializeComponent();
            this.Rescale();
            SetupAccountPidColumn();
            SetupAccountGameColumn();
            SetupAccountLastSeenColumn();

            AccountsList = new List<Account>();
            SelectedAccounts = new List<Account>();

            AccountsView.SetObjects(AccountsList);
            UpdateSelectionStatusText();
            SelectionStatusLabel?.BringToFront();

            if (ThemeEditor.UseDarkTopBar) Icon = Properties.Resources.team_KX4_icon_white; // this has to go after or icon wont actually change

            AccountsView.UnfocusedHighlightBackgroundColor = Color.FromArgb(0, 150, 215);
            AccountsView.UnfocusedHighlightForegroundColor = Color.FromArgb(240, 240, 240);

            SimpleDropSink sink = AccountsView.DropSink as SimpleDropSink;
            sink.CanDropBetween = true;
            sink.CanDropOnItem = true;
            sink.CanDropOnBackground = false;
            sink.CanDropOnSubItem = false;
            sink.CanDrop += Sink_CanDrop;
            sink.Dropped += Sink_Dropped;
            sink.FeedbackColor = Color.FromArgb(33, 33, 33);

            AccountsView.AlwaysGroupByColumn = Group;

            Group.GroupKeyGetter = delegate (object account)
            {
                return ((Account)account).Group;
            };

            Group.GroupKeyToTitleConverter = delegate (object Key)
            {
                string GroupName = Key as string;
                Match match = Regex.Match(GroupName, @"\d{1,3}\s?");

                if (match.Success)
                    return GroupName.Substring(match.Length);
                else
                    return GroupName;
            };

            var VCKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X86");

            if (!Prompts.Exists("VCPrompted") && (VCKey == null || (VCKey is RegistryKey && VCKey.GetValue("Bld") is int VCVersion && VCVersion < 32532)))
                Task.Run(async () => // Make sure the user has the latest 2015-2022 vcredist installed
                {
                    using HttpClient Client = new HttpClient();
                    byte[] bs = await Client.GetByteArrayAsync("https://aka.ms/vs/17/release/vc_redist.x86.exe");
                    string FN = Path.Combine(Path.GetTempPath(), "vcredist.tmp");

                    File.WriteAllBytes(FN, bs);

                    Process.Start(new ProcessStartInfo(FN) { UseShellExecute = false, Arguments = "/q /norestart" }).WaitForExit();

                    Prompts.Set("VCPrompted", "1");
                });
        }

        private static string GetPidText(Account account)
        {
            if (account == null || account.CurrentProcessId <= 0)
                return string.Empty;

            return account.CurrentProcessId.ToString(CultureInfo.InvariantCulture);
        }

        private void SetupAccountPidColumn()
        {
            if (AccountsView.AllColumns.Any(column => string.Equals(column.Text, "PID", StringComparison.OrdinalIgnoreCase)))
                return;

            CurrentPidColumn = new OLVColumn("PID", "CurrentProcessId")
            {
                IsEditable = false,
                Sortable = false,
                Width = (int)(68 * Program.Scale),
                TextAlign = HorizontalAlignment.Center
            };

            CurrentPidColumn.AspectGetter = rowObject => rowObject is Account account ? GetPidText(account) : string.Empty;

            AccountsView.AllColumns.Insert(1, CurrentPidColumn);
            AccountsView.Columns.Insert(1, CurrentPidColumn);
        }

        private void SetupAccountGameColumn()
        {
            if (AccountsView.AllColumns.Any(column => column.AspectName == "CurrentGameName"))
                return;

            CurrentGameColumn = new OLVColumn("Game", "CurrentGameName")
            {
                IsEditable = false,
                Sortable = false,
                Width = (int)(140 * Program.Scale)
            };

            CurrentGameColumn.AspectGetter = rowObject => rowObject is Account account ? account.CurrentGameName : string.Empty;

            int InsertIndex = AccountsView.AllColumns.Any(column => string.Equals(column.Text, "PID", StringComparison.OrdinalIgnoreCase)) ? 2 : 1;

            AccountsView.AllColumns.Insert(InsertIndex, CurrentGameColumn);
            AccountsView.Columns.Insert(InsertIndex, CurrentGameColumn);
        }

        private static string FormatLastSeenAge(DateTime LastSeenUtc)
        {
            if (LastSeenUtc == DateTime.MinValue)
                return string.Empty;

            TimeSpan Age = DateTime.UtcNow - LastSeenUtc;
            if (Age < TimeSpan.Zero)
                Age = TimeSpan.Zero;

            if (Age.TotalSeconds < 60)
                return $"{Math.Max(0, (int)Age.TotalSeconds)}s ago";

            if (Age.TotalMinutes < 60)
                return $"{(int)Age.TotalMinutes}m ago";

            if (Age.TotalHours < 24)
                return $"{(int)Age.TotalHours}h ago";

            return $"{(int)Age.TotalDays}d ago";
        }

        private string GetLastSeenText(Account account)
        {
            if (account == null) return string.Empty;
            if (account.LastLiveStatusUpdateUtc == DateTime.MinValue)
            {
                if (General != null
                    && General.Get<bool>("AutoRejoin")
                    && AutoRejoinStates.TryGetValue(account.UserID, out AutoRejoinState state)
                    && state?.OfflineSinceUtc.HasValue == true)
                {
                    return $"No signal ({FormatLastSeenAge(state.OfflineSinceUtc.Value)})";
                }

                return "No signal";
            }

            return FormatLastSeenAge(account.LastLiveStatusUpdateUtc);
        }

        private void SetupAccountLastSeenColumn()
        {
            if (AccountsView.AllColumns.Any(column => string.Equals(column.Text, "Last Seen", StringComparison.OrdinalIgnoreCase)))
                return;

            LastSeenColumn = new OLVColumn("Last Seen", "LastLiveStatusUpdateUtc")
            {
                IsEditable = false,
                Sortable = false,
                Width = (int)(90 * Program.Scale)
            };

            LastSeenColumn.AspectGetter = rowObject => rowObject is Account account ? GetLastSeenText(account) : string.Empty;

            int InsertIndex = CurrentGameColumn != null ? AccountsView.AllColumns.IndexOf(CurrentGameColumn) + 1 : 2;
            if (InsertIndex < 0)
                InsertIndex = 2;

            AccountsView.AllColumns.Insert(InsertIndex, LastSeenColumn);
            AccountsView.Columns.Insert(InsertIndex, LastSeenColumn);
        }

        private void Sink_CanDrop(object sender, OlvDropEventArgs e)
        {
            if (e.DataObject.GetType() != typeof(OLVDataObject) && e.DragEventArgs.Data.GetDataPresent(DataFormats.Text))
                e.Effect = DragDropEffects.Copy;
        }

        private void Sink_Dropped(object sender, OlvDropEventArgs e)
        {
            if (e.Effect == DragDropEffects.Copy)
            {
                string Text = (string)e.DragEventArgs.Data.GetData(DataFormats.Text);
                Regex RSecRegex = new Regex(@"(_\|WARNING:-DO-NOT-SHARE-THIS\.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items\.\|\w+)");
                MatchCollection RSecMatches = RSecRegex.Matches(Text);

                foreach (Match match in RSecMatches)
                    AddAccount(match.Value);
            }
        }

        private readonly static string DataDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox Account Manager");
        private readonly static string SaveFilePath = ResolveDataFilePath("AccountData.json");
        private readonly static string RecentGamesFilePath = ResolveDataFilePath("RecentGames.json"); // i shouldve combined everything that isnt accountdata into one file but oh well im too lazy : |

        private static string ResolveDataFilePath(string FileName)
        {
            string LegacyPath = Path.Combine(Environment.CurrentDirectory, FileName);

            try
            {
                if (!Directory.Exists(DataDirectoryPath))
                    Directory.CreateDirectory(DataDirectoryPath);

                string GlobalPath = Path.Combine(DataDirectoryPath, FileName);
                bool GlobalExists = File.Exists(GlobalPath);
                bool LegacyExists = File.Exists(LegacyPath);

                if (!GlobalExists && LegacyExists)
                {
                    File.Copy(LegacyPath, GlobalPath, true);
                    return GlobalPath;
                }

                if (GlobalExists && LegacyExists)
                {
                    DateTime LegacyWrite = File.GetLastWriteTimeUtc(LegacyPath);
                    DateTime GlobalWrite = File.GetLastWriteTimeUtc(GlobalPath);

                    if (LegacyWrite > GlobalWrite.AddSeconds(2))
                        File.Copy(LegacyPath, GlobalPath, true);
                }

                return GlobalPath;
            }
            catch
            {
                return LegacyPath;
            }
        }

        private static string GetAccountDistinctKey(Account account)
        {
            if (account == null)
                return string.Empty;

            if (account.UserID > 0)
                return $"id:{account.UserID}";

            return $"name:{(account.Username ?? string.Empty).Trim().ToLowerInvariant()}";
        }

        private static string GenerateBrowserTrackerId()
        {
            int PartA;
            int PartB;

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                byte[] bytes = new byte[8];
                rng.GetBytes(bytes);
                PartA = 100000 + (Math.Abs(BitConverter.ToInt32(bytes, 0)) % 75000);
                PartB = 100000 + (Math.Abs(BitConverter.ToInt32(bytes, 4)) % 800000);
            }

            return $"{PartA}{PartB}";
        }

        private void NormalizeBrowserTrackerIds()
        {
            if (AccountsList == null || AccountsList.Count == 0)
                return;

            HashSet<string> Assigned = new HashSet<string>(StringComparer.Ordinal);
            bool Changed = false;

            foreach (Account account in AccountsList)
            {
                if (account == null)
                    continue;

                string Current = (account.BrowserTrackerID ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(Current) && Assigned.Add(Current))
                    continue;

                string NewId;
                do { NewId = GenerateBrowserTrackerId(); }
                while (!Assigned.Add(NewId));

                if (!string.Equals(Current, NewId, StringComparison.Ordinal))
                {
                    Program.Logger.Info($"[BulkLaunch] Assigned new BrowserTrackerID for {account.Username}: {NewId}");
                    account.BrowserTrackerID = NewId;
                    Changed = true;
                }
            }

            if (Changed)
                SaveAccounts(BypassRateLimit: true, BypassCountCheck: true);
        }

        private static List<Account> DistinctAccounts(IEnumerable<Account> accounts)
        {
            if (accounts == null)
                return new List<Account>();

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<Account> result = new List<Account>();

            foreach (Account account in accounts)
            {
                if (account == null)
                    continue;

                string key = GetAccountDistinctKey(account);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (seen.Add(key))
                    result.Add(account);
            }

            return result;
        }

        private List<Account> GetSelectedAccountsFromView()
        {
            List<Account> byObjects = new List<Account>();
            List<Account> byIndices = new List<Account>();

            try { byObjects = DistinctAccounts(AccountsView.SelectedObjects.Cast<Account>()); } catch { }

            try
            {
                foreach (int selectedIndex in AccountsView.SelectedIndices)
                {
                    if (AccountsView.GetModelObject(selectedIndex) is Account account)
                        byIndices.Add(account);
                }

                byIndices = DistinctAccounts(byIndices);
            }
            catch { }

            if (byIndices.Count > byObjects.Count)
                return byIndices;

            return byObjects;
        }

        private List<Account> GetLaunchTargetsSnapshot()
        {
            List<Account> selectedNow = GetSelectedAccountsFromView();
            if (selectedNow.Count > 0)
                return selectedNow;

            if (SelectedAccounts != null && SelectedAccounts.Count > 0)
                return DistinctAccounts(SelectedAccounts.Where(account => account != null && AccountsList.Contains(account)));

            if (SelectedAccount != null)
                return new List<Account> { SelectedAccount };

            return new List<Account>();
        }

        private void UpdateSelectionStatusText()
        {
            if (SelectionStatusLabel == null || SelectionStatusLabel.IsDisposed)
                return;

            int selectedCount = 0;

            try { selectedCount = GetSelectedAccountsFromView().Count; } catch { }

            if (selectedCount == 0 && SelectedAccounts != null)
                selectedCount = SelectedAccounts.Count;

            int totalCount = AccountsList?.Count ?? 0;
            SelectionStatusLabel.Text = $"Selected: {selectedCount}/{totalCount}";
        }

        private void RefreshView(object obj = null)
        {
            AccountsView.InvokeIfRequired(() =>
            {
                AccountsView.BuildList();
                if (AccountsView.ShowGroups) AccountsView.BuildGroups();

                if (obj != null)
                {
                    AccountsView.RefreshObject(obj);
                    AccountsView.EnsureModelVisible(obj);
                }

                UpdateSelectionStatusText();
            });
        }

        private static ReadOnlyMemory<byte> PasswordHash; // Store the hash after the data is successfully decrypted so we can encrypt again.

        private void LoadAccounts(byte[] Hash = null)
        {
            bool EnteredPassword = false;
            byte[] Data = File.Exists(SaveFilePath) ? File.ReadAllBytes(SaveFilePath) : Array.Empty<byte>();

            if (Data.Length > 0)
            {
                var Header = new ReadOnlySpan<byte>(Data, 0, Cryptography.RAMHeader.Length);

                if (Header.SequenceEqual(Cryptography.RAMHeader))
                {
                    if (Hash == null)
                    {
                        EncryptionSelectionPanel.Visible = false;
                        PasswordSelectionPanel.Visible = false;
                        PasswordLayoutPanel.Visible = true;
                        PasswordPanel.Visible = true;
                        PasswordPanel.BringToFront();
                        PasswordTextBox.Focus();

                        return;
                    }

                    Data = Cryptography.Decrypt(Data, Hash);
                    AccountsList = JsonConvert.DeserializeObject<List<Account>>(Encoding.UTF8.GetString(Data));
                    PasswordHash = new ReadOnlyMemory<byte>(ProtectedData.Protect(Hash, Array.Empty<byte>(), DataProtectionScope.CurrentUser));

                    PasswordPanel.Visible = false;
                    EnteredPassword = true;
                }
                else
                    try { AccountsList = JsonConvert.DeserializeObject<List<Account>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(Data, Entropy, DataProtectionScope.CurrentUser))); }
                    catch (CryptographicException e)
                    {
                        try { AccountsList = JsonConvert.DeserializeObject<List<Account>>(Encoding.UTF8.GetString(Data)); }
                        catch
                        {
                            File.WriteAllBytes(SaveFilePath + ".bak", Data);

                            MessageBox.Show($"Failed to load accounts!\nA backup file was created in case the data can be recovered.\n\n{e.Message}");
                        }
                    }
            }

            AccountsList ??= new List<Account>();

            if (!EnteredPassword && AccountsList.Count == 0 && File.Exists($"{SaveFilePath}.backup") && File.ReadAllBytes($"{SaveFilePath}.backup") is byte[] BackupData && BackupData.Length > 0)
            {
                var Header = new ReadOnlySpan<byte>(BackupData, 0, Cryptography.RAMHeader.Length);

                if (Header.SequenceEqual(Cryptography.RAMHeader) && MessageBox.Show("The existing backup file is password-locked, would you like to attempt to load it?", "Roblox Account Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    if (File.Exists(SaveFilePath))
                    {
                        if (File.Exists($"{SaveFilePath}.old")) File.Delete($"{SaveFilePath}.old");

                        File.Move(SaveFilePath, $"{SaveFilePath}.old");
                    }

                    File.Move($"{SaveFilePath}.backup", SaveFilePath);

                    LoadAccounts();

                    return;
                }

                if (MessageBox.Show("No accounts were loaded but there is a backup file, would you like to load the backup file?", "Roblox Account Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
                {
                    try
                    {
                        string Decoded = Encoding.UTF8.GetString(ProtectedData.Unprotect(BackupData, Entropy, DataProtectionScope.CurrentUser));

                        AccountsList = JsonConvert.DeserializeObject<List<Account>>(Decoded);
                    }
                    catch
                    {
                        try { AccountsList = JsonConvert.DeserializeObject<List<Account>>(Encoding.UTF8.GetString(BackupData)); }
                        catch { MessageBox.Show("Failed to load backup file!", "Roblox Account Manager", MessageBoxButtons.OKCancel, MessageBoxIcon.Error); }
                    }
                }
            }

            AccountsView.SetObjects(AccountsList);
            RefreshView();

            if (AccountsList.Count > 0)
            {
                LastValidAccount = AccountsList[0];

                foreach (Account account in AccountsList)
                    if (account.LastUse > LastValidAccount.LastUse)
                        LastValidAccount = account;
            }
        }

        public static void SaveAccounts(bool BypassRateLimit = false, bool BypassCountCheck = false)
        {
            if ((!BypassRateLimit && (DateTime.Now - startTime).Seconds < 2) || (!BypassCountCheck && AccountsList.Count == 0)) return;

            lock (saveLock)
            {
                byte[] OldInfo = File.Exists(SaveFilePath) ? File.ReadAllBytes(SaveFilePath) : Array.Empty<byte>();
                string SaveData = JsonConvert.SerializeObject(AccountsList);

                FileInfo OldFile = new FileInfo(SaveFilePath);
                FileInfo Backup = new FileInfo($"{SaveFilePath}.backup");

                if (!Backup.Exists || (Backup.Exists && (DateTime.Now - Backup.LastWriteTime).TotalMinutes > 60 * 8))
                    File.WriteAllBytes(Backup.FullName, OldInfo);

                if (!PasswordHash.IsEmpty)
                    File.WriteAllBytes(SaveFilePath, Cryptography.Encrypt(SaveData, ProtectedData.Unprotect(PasswordHash.ToArray(), Array.Empty<byte>(), DataProtectionScope.CurrentUser)));
                else
                {
                    if (File.Exists(Path.Combine(Environment.CurrentDirectory, "NoEncryption.IUnderstandTheRisks.iautamor")))
                        File.WriteAllBytes(SaveFilePath, Encoding.UTF8.GetBytes(SaveData));
                    else
                        File.WriteAllBytes(SaveFilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(SaveData), Entropy, DataProtectionScope.LocalMachine));
                }
            }
        }

        public void ResetEncryption(bool ManualReset = false)
        {
            foreach (var Form in Application.OpenForms.OfType<Form>())
                if (Form != this)
                    Form.Hide();

            IsResettingPassword = true;

            PasswordLayoutPanel.Visible = !PasswordHash.IsEmpty && ManualReset;
            PasswordSelectionPanel.Visible = false;
            EncryptionSelectionPanel.Visible = PasswordHash.IsEmpty || !ManualReset;

            PasswordPanel.Visible = true;
            PasswordPanel.BringToFront();
        }

        private void PasswordTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                UnlockButton.PerformClick();

                e.Handled = true;
            }
        }

        private void Error(string Message)
        {
            Program.Logger.Error(Message);

            throw new Exception(Message);
        }

        private void UnlockButton_Click(object sender, EventArgs e)
        {
            try
            {
                byte[] Hash = CryptoHash.Hash(PasswordTextBox.Text);

                if (PasswordTextBox.Text.Length < 4)
                    Error("Invalid password, your password must contain 4 or more characters");

                if (IsResettingPassword)
                {
                    byte[] Data = File.Exists(SaveFilePath) ? File.ReadAllBytes(SaveFilePath) : Array.Empty<byte>();

                    if (Data.Length > 0)
                    {
                        var Header = new ReadOnlySpan<byte>(Data, 0, Cryptography.RAMHeader.Length);

                        if (Header.SequenceEqual(Cryptography.RAMHeader))
                        {
                            if (Hash == null)
                            {
                                EncryptionSelectionPanel.Visible = false;
                                PasswordSelectionPanel.Visible = false;
                                PasswordLayoutPanel.Visible = true;
                                PasswordPanel.Visible = true;
                                PasswordPanel.BringToFront();
                                PasswordTextBox.Focus();

                                return;
                            }

                            Cryptography.Decrypt(Data, Hash);

                            PasswordLayoutPanel.Visible = false;
                            EncryptionSelectionPanel.Visible = true;
                            IsResettingPassword = false;
                        }
                    }
                }
                else
                    LoadAccounts(Hash);
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Incorrect Password!\n\n{exception.Message}", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { PasswordTextBox.Text = string.Empty; PasswordTextBox.Focus(); }
        }

        private void DefaultEncryptionButton_Click(object sender, EventArgs e)
        {
            PasswordHash = Array.Empty<byte>();
            SaveAccounts(true, true);

            PasswordPanel.Visible = false;
        }

        private void PasswordEncryptionButton_Click(object sender, EventArgs e)
        {
            EncryptionSelectionPanel.Visible = false;
            PasswordLayoutPanel.Visible = false;
            PasswordSelectionPanel.Visible = true;
        }

        private ReadOnlyMemory<byte> LastHash = null;

        private void SetPasswordButton_Click(object sender, EventArgs e)
        {
            if (PasswordSelectionTB.Text.Length < 4)
            {
                MessageBox.Show("Invalid password, your password must contain 4 or more characters", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            byte[] Hash = CryptoHash.Hash(PasswordSelectionTB.Text);

            PasswordHash = new ReadOnlyMemory<byte>(ProtectedData.Protect(Hash, Array.Empty<byte>(), DataProtectionScope.CurrentUser));

            if (LastHash.IsEmpty)
            {
                LastHash = new ReadOnlyMemory<byte>(PasswordHash.ToArray());
                PasswordSelectionTB.Text = string.Empty;
                MessageBox.Show("Please confirm your password.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
            else
            {
                if (ProtectedData.Unprotect(LastHash.ToArray(), Array.Empty<byte>(), DataProtectionScope.CurrentUser).SequenceEqual(Hash.ToArray()))
                {
                    SaveAccounts(true, true);

                    PasswordSelectionTB.Text = string.Empty;
                    PasswordPanel.Visible = false;

                    LastHash = null;
                }
                else
                    MessageBox.Show("You have entered the wrong password, please try again.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        CancellationTokenSource PasswordSelectionCancellationToken;

        private void PasswordSelectionTB_TextChanged(object sender, EventArgs e)
        {
            PasswordSelectionCancellationToken?.Cancel();

            SetPasswordButton.Enabled = false;

            PasswordSelectionCancellationToken = new CancellationTokenSource();
            var Token = PasswordSelectionCancellationToken.Token;

            Task.Run(async () =>
            {
                await Task.Delay(500); // Wait until the user has stopped typing to enable the continue button

                if (Token.IsCancellationRequested)
                    return;

                AccountsView.InvokeIfRequired(() => SetPasswordButton.Enabled = true);
            }, PasswordSelectionCancellationToken.Token);
        }

        private void PasswordPanel_VisibleChanged(object sender, EventArgs e)
        {
            foreach (Control Control in Controls)
                if (Control != PasswordPanel)
                    Control.Enabled = !PasswordPanel.Visible;
        }

        public static bool GetUserID(string Username, out long UserId, out RestResponse response)
        {
            RestRequest request = LastValidAccount?.MakeRequest("v1/usernames/users", Method.Post) ?? new RestRequest("v1/usernames/users", Method.Post);
            request.AddJsonBody(new { usernames = new string[] { Username } });

            response = UsersClient.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK && response.Content.TryParseJson(out JObject UserData) && UserData.ContainsKey("data") && UserData["data"].Count() >= 1)
            {
                UserId = UserData["data"]?[0]?["id"].Value<long>() ?? -1;

                return true;
            }

            UserId = -1;

            return false;
        }

        public void UpdateAccountView(Account account) =>
            AccountsView.InvokeIfRequired(() => AccountsView.UpdateObject(account));

        public static Account AddAccount(string SecurityToken, string Password = "", string AccountJSON = null)
        {
            Account account = new Account(SecurityToken, AccountJSON);

            if (account.Valid)
            {
                account.Password = Password;

                Account exists = AccountsList.AsReadOnly().FirstOrDefault(acc => acc.UserID == account.UserID);

                if (exists != null)
                {
                    account = exists;

                    exists.SecurityToken = SecurityToken;
                    exists.Password = Password;
                    exists.LastUse = DateTime.Now;

                    Instance.RefreshView(exists);
                }
                else
                {
                    AccountsList.Add(account);

                    Instance.RefreshView(account);
                }

                SaveAccounts(true);

                return account;
            }

            return null;
        }

        public static string ShowDialog(string text, string caption, string defaultText = "", bool big = false) // tbh pasted from stackoverflow
        {
            Form prompt = new Form()
            {
                Width = 340,
                Height = big ? 420 : 125,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen
            };

            Label textLabel = new Label() { Left = 15, Top = 10, Text = text, AutoSize = true };
            Control textBox;
            Button confirmation = new Button() { Text = "OK", Left = 15, Width = 100, Top = big ? 350 : 50, DialogResult = DialogResult.OK };

            if (big) textBox = new RichTextBox() { Left = 15, Top = 15 + textLabel.Size.Height, Width = 295, Height = 330 - textLabel.Size.Height, Text = defaultText };
            else textBox = new TextBox() { Left = 15, Top = 25, Width = 295, Text = defaultText };

            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(textLabel);
            if (!big) prompt.AcceptButton = confirmation;

            prompt.Rescale();

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "/UC";
        }

        private void AccountManager_Load(object sender, EventArgs e)
        {
            PasswordPanel.Dock = DockStyle.Fill;

            DirectoryInfo UpdateDir = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "Update"));

            if (UpdateDir.Exists)
                UpdateDir.RecursiveDelete();

            afform = new ArgumentsForm();
            ServerListForm = new ServerList();
            UtilsForm = new AccountUtils();
            ImportAccountsForm = new ImportForm();
            FieldsForm = new AccountFields();
            ThemeForm = new ThemeEditor();
            RGForm = new RecentGamesForm();

            MainClient = new RestClient("https://www.roblox.com/");
            AvatarClient = new RestClient("https://avatar.roblox.com/");
            AuthClient = new RestClient("https://auth.roblox.com/");
            EconClient = new RestClient("https://economy.roblox.com/");
            AccountClient = new RestClient("https://accountsettings.roblox.com/");
            GameJoinClient = new RestClient(new RestClientOptions("https://gamejoin.roblox.com/") { UserAgent = "Roblox/WinInet" });
            UsersClient = new RestClient("https://users.roblox.com");
            FriendsClient = new RestClient("https://friends.roblox.com");
            Web13Client = new RestClient("https://web.roblox.com/");

            if (File.Exists(SaveFilePath))
                LoadAccounts();
            else
                ResetEncryption();

            ApplyTheme();

            RGForm.RecentGameSelected += (sender, e) => { PlaceID.Text = e.Game.Details?.placeId.ToString(); };

            PlaceID.Text = General.Exists("SavedPlaceId") ? General.Get("SavedPlaceId") : "5315046213";
            UserID.Text = General.Exists("SavedFollowUser") ? General.Get("SavedFollowUser") : string.Empty;

            if (!Developer.Get<bool>("DevMode"))
            {
                AccountsStrip.Items.Remove(viewFieldsToolStripMenuItem);
                AccountsStrip.Items.Remove(getAuthenticationTicketToolStripMenuItem);
                AccountsStrip.Items.Remove(copyRbxplayerLinkToolStripMenuItem);
                AccountsStrip.Items.Remove(copySecurityTokenToolStripMenuItem);
                AccountsStrip.Items.Remove(copyAppLinkToolStripMenuItem);
            }
            else
                ArgumentsB.Visible = true;

            if (General.Get<bool>("HideUsernames"))
                HideUsernamesCheckbox.Checked = true;

            Username.Renderer = new AccountRenderer();

            try
            {
                bool EnableWebServer = Developer.Get<bool>("EnableWebServer") || General.Get<bool>("EnableLiveStatusPush");

                if (EnableWebServer)
                {
                    string Port = WebServer.Exists("WebServerPort") ? WebServer.Get("WebServerPort") : "7963";

                    List<string> Prefixes = new List<string>() { $"http://localhost:{Port}/" };

                    if (WebServer.Get<bool>("AllowExternalConnections"))
                        if (Program.Elevated)
                            Prefixes.Add($"http://*:{Port}/");
                        else
                            using (Process proc = new Process() { StartInfo = new ProcessStartInfo(AppDomain.CurrentDomain.FriendlyName, "-adminRequested") { Verb = "runas" } })
                                try
                                {
                                    proc.Start();
                                    Environment.Exit(1);
                                }
                                catch { }


                    AltManagerWS = new WebServer(SendResponse, Prefixes.ToArray());
                    AltManagerWS.Run();
                }
            }
            catch (Exception x) { MessageBox.Show($"Failed to start webserver!\n\n{x}", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error); }

            Task.Run(() =>
            {
                WebClient WC = new WebClient();
                string VersionJSON = WC.DownloadString("https://clientsettings.roblox.com/v1/client-version/WindowsPlayer");

                if (JObject.Parse(VersionJSON).TryGetValue("clientVersionUpload", out JToken token))
                    CurrentVersion = token.Value<string>();
            });

            IniSettings.Save("RAMSettings.ini");

            PlaceID.AutoCompleteCustomSource = new AutoCompleteStringCollection();
            PlaceID.AutoCompleteMode = AutoCompleteMode.Suggest;
            PlaceID.AutoCompleteSource = AutoCompleteSource.CustomSource;

            Task.Run(LoadRecentGames);
            Task.Run(RobloxProcess.UpdateMatches);

            if (General.Get<bool>("ShuffleJobId"))
                ShuffleIcon_Click(null, EventArgs.Empty);

            if (General.Get<bool>("AutoCookieRefresh"))
            {
                AutoCookieRefresh = new System.Timers.Timer(60000 * 5) { Enabled = true };
                AutoCookieRefresh.Elapsed += async (s, e) =>
                {
                    int Count = 0;

                    foreach (var Account in AccountsList)
                    {
                        if (Account.GetField("NoCookieRefresh") != "true" && (DateTime.Now - Account.LastUse).TotalDays > 20 && (DateTime.Now - Account.LastAttemptedRefresh).TotalDays >= 7)
                        {
                            Program.Logger.Info($"Attempting to refresh {Account.Username} | Last Use: {Account.LastUse}");

                            Account.LastAttemptedRefresh = DateTime.Now;

                            if (Account.LogOutOfOtherSessions(true)) Count++;

                            await Task.Delay(5000);
                        }
                    };
                };
            }

            PresenceTimer = new System.Timers.Timer { AutoReset = true, Enabled = true };
            PresenceTimer.Elapsed += async (s, e) => await TryUpdatePresence();
            UpdatePresenceTimerInterval();

            LiveStatusTimer = new System.Timers.Timer(2000) { AutoReset = true, Enabled = true };
            LiveStatusTimer.Elapsed += async (s, e) => await TryUpdateLiveStatus();

            LastSeenTimer = new System.Timers.Timer(1000) { AutoReset = true, Enabled = true };
            LastSeenTimer.Elapsed += (s, e) => RefreshLastSeenColumn();

            ProcessOptimizerTimer = new System.Timers.Timer(2000) { AutoReset = true, Enabled = true };
            ProcessOptimizerTimer.Elapsed += (s, e) => TryApplyProcessOptimization();

            _ = TryUpdateLiveStatus(true);
            _ = TryUpdatePresence();
            TryApplyProcessOptimization();
        }

        public void ApplyTheme()
        {
            BackColor = ThemeEditor.FormsBackground;
            ForeColor = ThemeEditor.FormsForeground;

            if (AccountsView.BackColor != ThemeEditor.AccountBackground || AccountsView.ForeColor != ThemeEditor.AccountForeground)
            {
                AccountsView.BackColor = ThemeEditor.AccountBackground;
                AccountsView.ForeColor = ThemeEditor.AccountForeground;

                RefreshView();
            }

            AccountsView.HeaderStyle = ThemeEditor.ShowHeaders ? (AccountsView.ShowGroups ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.Clickable) : ColumnHeaderStyle.None;
            AccountsView.CellEditActivation = ObjectListView.CellEditActivateMode.DoubleClick;

            Controls.ApplyTheme();

            afform.ApplyTheme();
            ServerListForm.ApplyTheme();
            UtilsForm.ApplyTheme();
            ImportAccountsForm.ApplyTheme();
            FieldsForm.ApplyTheme();
            ThemeForm.ApplyTheme();
            RGForm.ApplyTheme();

            ControlForm?.ApplyTheme();
            SettingsForm?.ApplyTheme();
        }

        private async void LoadRecentGames()
        {
            RecentGames = new List<Game>();

            if (File.Exists(RecentGamesFilePath))
            {
                List<Game> Games = JsonConvert.DeserializeObject<List<Game>>(File.ReadAllText(RecentGamesFilePath));

                RGForm.LoadGames(Games);

                foreach (Game RG in Games)
                    await AddRecentGame(RG, true);
            }
        }

        private async Task AddRecentGame(Game RG, bool Loading = false)
        {
            await RG.WaitForDetails();

            RecentGames.RemoveAll(g => g?.Details?.placeId == RG.Details?.placeId);

            while (RecentGames.Count > General.Get<int>("MaxRecentGames"))
            {
                this.InvokeIfRequired(() => PlaceID.AutoCompleteCustomSource.Remove(RecentGames[0].Details?.filteredName));
                RecentGames.RemoveAt(0);
            }

            RecentGames.Add(RG);

            this.InvokeIfRequired(() => PlaceID.AutoCompleteCustomSource.Add(RG.Details.filteredName));

            if (!Loading)
            {
                this.InvokeIfRequired(() => RecentGameAdded?.Invoke(this, new GameArgs(RG)));

                lock (rgSaveLock)
                    File.WriteAllText(RecentGamesFilePath, JsonConvert.SerializeObject(RecentGames));
            }
        }

        private readonly List<ServerData> AttemptedJoins = new List<ServerData>();

        private string WebServerResponse(object Message, bool Success) => JsonConvert.SerializeObject(new { Success, Message });

        private static bool ParseBoolean(string Value, bool DefaultValue)
        {
            if (string.IsNullOrWhiteSpace(Value))
                return DefaultValue;

            if (bool.TryParse(Value, out bool ParsedBool))
                return ParsedBool;

            if (int.TryParse(Value, out int ParsedInt))
                return ParsedInt != 0;

            switch (Value.Trim().ToLowerInvariant())
            {
                case "yes":
                case "on":
                    return true;
                case "no":
                case "off":
                    return false;
                default:
                    return DefaultValue;
            }
        }

        private static long? ParseNullableLong(string Value)
        {
            if (long.TryParse(Value, out long Parsed) && Parsed > 0)
                return Parsed;

            return null;
        }

        private bool TryGetScriptLiveStatusOverride(Account account, out ScriptLiveStatusOverride Override)
        {
            Override = null;

            if (account == null || !ScriptLiveStatusOverrides.TryGetValue(account.UserID, out Override))
                return false;

            if ((DateTime.UtcNow - Override.UpdatedAtUtc) > ScriptLiveStatusOverrideTtl)
            {
                ScriptLiveStatusOverrides.TryRemove(account.UserID, out _);
                Override = null;
                return false;
            }

            return true;
        }

        private bool ApplyScriptLiveStatusOverride(Account account, ScriptLiveStatusOverride Override, out bool GameChanged)
        {
            GameChanged = false;
            if (account == null || Override == null) return false;
            account.LastLiveStatusUpdateUtc = Override.UpdatedAtUtc;

            bool OverrideIsOnServer = Override.IsOnServer;
            bool OverrideHasResolvedGame = (Override.PlaceId.HasValue && Override.PlaceId.Value > 0)
                || (!string.IsNullOrWhiteSpace(Override.GameName) && !string.Equals(Override.GameName, "Loading...", StringComparison.OrdinalIgnoreCase));

            if (OverrideIsOnServer && !OverrideHasResolvedGame)
                OverrideIsOnServer = false;

            bool StateChanged = account.HasOpenInstance != Override.HasOpenInstance || account.IsOnServer != OverrideIsOnServer;

            if (StateChanged)
            {
                account.HasOpenInstance = Override.HasOpenInstance;
                account.IsOnServer = OverrideIsOnServer;
            }

            long? PlaceId = OverrideIsOnServer ? Override.PlaceId : null;
            string GameName = string.Empty;

            if (OverrideIsOnServer)
            {
                if (!string.IsNullOrWhiteSpace(Override.GameName))
                    GameName = Override.GameName;
                else if (PlaceId.HasValue)
                {
                    if (PlaceNameCache.TryGetValue(PlaceId.Value, out string CachedName) && !string.IsNullOrWhiteSpace(CachedName))
                        GameName = CachedName;
                    else
                    {
                        GameName = PlaceId.Value.ToString();
                        QueuePlaceNameLookup(PlaceId.Value);
                    }
                }
                else
                    GameName = "Loading...";
            }

            if (account.CurrentPlaceId != PlaceId || account.CurrentGameName != GameName)
            {
                account.CurrentPlaceId = PlaceId;
                account.CurrentGameName = GameName;
                GameChanged = true;
            }

            return StateChanged;
        }

        private static bool HasResolvedGame(Account account)
        {
            return account != null
                && (account.CurrentPlaceId.HasValue
                    || (!string.IsNullOrWhiteSpace(account.CurrentGameName) && !string.Equals(account.CurrentGameName, "Loading...", StringComparison.OrdinalIgnoreCase)));
        }

        public static bool HasFreshLiveSignal(Account account, DateTime? nowUtc = null)
        {
            if (account == null || account.LastLiveStatusUpdateUtc == DateTime.MinValue)
                return false;

            DateTime now = nowUtc ?? DateTime.UtcNow;
            TimeSpan age = now - account.LastLiveStatusUpdateUtc;
            if (age < TimeSpan.Zero)
                age = TimeSpan.Zero;

            return age <= AutoRejoinOfflineThreshold;
        }

        private static bool IsAccountInGame(Account account)
        {
            if (account == null) return false;
            if (!HasFreshLiveSignal(account)) return false;

            bool ScriptInGame = account.HasOpenInstance && account.IsOnServer && HasResolvedGame(account);
            bool PresenceInGame = account.Presence?.userPresenceType == UserPresenceType.InGame && GetPresencePlaceId(account).HasValue;

            return ScriptInGame || PresenceInGame;
        }

        private async Task<bool> TryCloseAutoRejoinProcess(Process process)
        {
            if (process == null)
                return false;

            try
            {
                if (process.HasExited)
                    return true;

                process.CloseMainWindow();
                await Task.Delay(250);
                process.CloseMainWindow();
                await Task.Delay(250);

                if (!process.HasExited)
                    process.Kill();

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task CloseAccountProcessForAutoRejoin(Account account)
        {
            if (account == null) return;

            bool ClosedAny = false;

            try
            {
                if (account.CurrentProcessId > 0)
                {
                    try
                    {
                        using (Process pidProcess = Process.GetProcessById(account.CurrentProcessId))
                            ClosedAny = await TryCloseAutoRejoinProcess(pidProcess) || ClosedAny;
                    }
                    catch { }
                }

                if (!ClosedAny && !string.IsNullOrEmpty(account.BrowserTrackerID))
                {
                    foreach (Process proc in Process.GetProcessesByName("RobloxPlayerBeta"))
                    {
                        try
                        {
                            string commandLine = proc.GetCommandLine() ?? string.Empty;
                            Match TrackerMatch = BrowserTrackerRegex.Match(commandLine);
                            string TrackerID = TrackerMatch.Success ? TrackerMatch.Groups[1].Value : string.Empty;

                            if (!string.Equals(TrackerID, account.BrowserTrackerID, StringComparison.Ordinal))
                                continue;

                            if (await TryCloseAutoRejoinProcess(proc))
                                ClosedAny = true;
                        }
                        catch { }
                        finally { proc.Dispose(); }
                    }
                }

                if (ClosedAny)
                    account.CurrentProcessId = 0;
            }
            catch (Exception x)
            {
                Program.Logger.Error($"[AutoRejoin] Failed to close process for {account.Username}: {x}");
            }
        }

        private async Task RelaunchForAutoRejoin(Account account)
        {
            try
            {
                if (Program.Closed || account == null) return;

                Program.Logger.Info($"[AutoRejoin] Relaunching {account.Username} to {AutoRejoinPlaceId} after {AutoRejoinOfflineThreshold.TotalSeconds:0}s offline.");

                await CloseAccountProcessForAutoRejoin(account);

                if (General.Get<bool>("EnableMultiRbx"))
                    UpdateMultiRoblox();

                string Result = await JoinWithFailureRecovery(account, AutoRejoinPlaceId, "", false, false, "AutoRejoin");
                if (!IsJoinSuccess(Result))
                    Program.Logger.Warn($"[AutoRejoin] Relaunch failed for {account.Username}: {Result}");
            }
            catch (Exception x)
            {
                Program.Logger.Error($"[AutoRejoin] Unhandled error while relaunching {account?.Username}: {x}");
            }
            finally
            {
                if (account != null)
                    AutoRejoinInProgress.TryRemove(account.UserID, out _);
            }
        }

        private void UpdateAutoRejoin()
        {
            if (!General.Get<bool>("AutoRejoin"))
            {
                AutoRejoinStates.Clear();
                AutoRejoinInProgress.Clear();
                return;
            }

            if (Interlocked.CompareExchange(ref BulkLaunchInProgress, 0, 0) == 1)
                return;

            DateTime NowUtc = DateTime.UtcNow;

            HashSet<long> ExistingAccounts = new HashSet<long>();

            foreach (Account account in AccountsList)
            {
                if (account == null) continue;

                ExistingAccounts.Add(account.UserID);

                AutoRejoinState State = AutoRejoinStates.GetOrAdd(account.UserID, _ => new AutoRejoinState());
                bool InGame = IsAccountInGame(account);
                DateTime? LastSignalUtc = account.LastLiveStatusUpdateUtc != DateTime.MinValue ? account.LastLiveStatusUpdateUtc : (DateTime?)null;
                bool ManagedAccount = !string.IsNullOrEmpty(account.BrowserTrackerID);
                bool HasKnownProcess = account.CurrentProcessId > 0;
                TimeSpan SignalAge = TimeSpan.MaxValue;
                if (LastSignalUtc.HasValue)
                {
                    SignalAge = NowUtc - LastSignalUtc.Value;
                    if (SignalAge < TimeSpan.Zero)
                        SignalAge = TimeSpan.Zero;
                }

                bool HasFreshSignal = LastSignalUtc.HasValue && SignalAge <= AutoRejoinOfflineThreshold;
                bool SignalStale = LastSignalUtc.HasValue && !HasFreshSignal;
                bool MissingSignal = !LastSignalUtc.HasValue;

                // Auto Rejoin applies to all listed accounts when enabled, even if no signal was seen yet.
                State.HasSeenActiveSession = true;

                // Any fresh signal means the instance is alive; do not run rejoin timer.
                if (HasFreshSignal)
                {
                    State.OfflineSinceUtc = null;
                    continue;
                }

                if (!State.OfflineSinceUtc.HasValue)
                    State.OfflineSinceUtc = LastSignalUtc.HasValue && LastSignalUtc.Value <= NowUtc ? LastSignalUtc.Value : NowUtc;

                if ((NowUtc - State.OfflineSinceUtc.Value) < AutoRejoinOfflineThreshold)
                    continue;

                if ((NowUtc - State.LastAttemptUtc) < AutoRejoinRetryCooldown)
                    continue;

                if (!AutoRejoinInProgress.TryAdd(account.UserID, 0))
                    continue;

                State.LastAttemptUtc = NowUtc;

                Program.Logger.Info($"[AutoRejoin] Watchdog trigger for {account.Username}: lastSignal={(LastSignalUtc.HasValue ? LastSignalUtc.Value.ToString("o") : "none")}, open={account.HasOpenInstance}, inGame={InGame}, stale={SignalStale}, missingSignal={MissingSignal}, managed={ManagedAccount}");
                _ = RelaunchForAutoRejoin(account);
            }

            foreach (long UserId in AutoRejoinStates.Keys)
            {
                if (ExistingAccounts.Contains(UserId))
                    continue;

                AutoRejoinStates.TryRemove(UserId, out _);
                AutoRejoinInProgress.TryRemove(UserId, out _);
            }
        }

        private static string BuildScriptServerClaimKey(long PlaceId, string JobId)
        {
            string NormalizedJobId = (JobId ?? string.Empty).Trim().ToLowerInvariant();
            if (PlaceId <= 0 || string.IsNullOrEmpty(NormalizedJobId))
                return string.Empty;

            return $"{PlaceId}:{NormalizedJobId}";
        }

        private static TimeSpan ClampScriptServerLease(int LeaseSeconds)
        {
            if (LeaseSeconds <= 0)
                return ScriptServerClaimMinLease;

            TimeSpan Lease = TimeSpan.FromSeconds(LeaseSeconds);
            if (Lease < ScriptServerClaimMinLease)
                return ScriptServerClaimMinLease;
            if (Lease > ScriptServerClaimMaxLease)
                return ScriptServerClaimMaxLease;
            return Lease;
        }

        private bool TryClaimScriptServer(long PlaceId, string JobId, string Owner, int LeaseSeconds, out DateTime ExpiresAtUtc, out string ExistingOwner)
        {
            ExpiresAtUtc = DateTime.MinValue;
            ExistingOwner = string.Empty;

            string Key = BuildScriptServerClaimKey(PlaceId, JobId);
            if (string.IsNullOrEmpty(Key))
                return false;

            string NormalizedOwner = string.IsNullOrWhiteSpace(Owner) ? "unknown" : Owner.Trim();
            DateTime NowUtc = DateTime.UtcNow;
            DateTime NewExpiryUtc = NowUtc.Add(ClampScriptServerLease(LeaseSeconds));

            lock (ScriptServerClaimsLock)
            {
                foreach (KeyValuePair<string, ServerClaimState> Pair in ScriptServerClaims.ToArray())
                {
                    if (Pair.Value == null || Pair.Value.ExpiresAtUtc <= NowUtc)
                        ScriptServerClaims.TryRemove(Pair.Key, out _);
                }

                if (ScriptServerClaims.TryGetValue(Key, out ServerClaimState Existing)
                    && Existing != null
                    && Existing.ExpiresAtUtc > NowUtc)
                {
                    if (!string.Equals(Existing.Owner, NormalizedOwner, StringComparison.OrdinalIgnoreCase))
                    {
                        ExpiresAtUtc = Existing.ExpiresAtUtc;
                        ExistingOwner = Existing.Owner;
                        return false;
                    }
                }

                ScriptServerClaims[Key] = new ServerClaimState
                {
                    Owner = NormalizedOwner,
                    ExpiresAtUtc = NewExpiryUtc
                };

                ExpiresAtUtc = NewExpiryUtc;
                ExistingOwner = NormalizedOwner;
                return true;
            }
        }

        private string SendResponse(HttpListenerContext Context)
        {
            HttpListenerRequest request = Context.Request;

            bool V2 = request.Url.AbsolutePath.StartsWith("/v2/");
            string AbsolutePath = V2 ? request.Url.AbsolutePath.Substring(3) : request.Url.AbsolutePath;

            string Reply(string Response, bool Success = false, int Code = -1, string Raw = null)
            {
                Context.Response.StatusCode = Code > 0 ? Code : (Success ? 200 : 400);

                return V2 ? WebServerResponse(Response, Success) : (Raw ?? Response);
            }

            if (!request.IsLocal && !WebServer.Get<bool>("AllowExternalConnections")) return Reply("External connections are not allowed", false, 401, string.Empty);
            if (AbsolutePath == "/favicon.ico") return ""; // always return nothing

            if (AbsolutePath == "/Running") return Reply("Roblox Account Manager is running", true, Raw: "true");

            string Body = new StreamReader(request.InputStream).ReadToEnd();
            string Method = AbsolutePath.Substring(1);
            string Account = request.QueryString["Account"];
            string Password = request.QueryString["Password"];

            if (WebServer.Get<bool>("EveryRequestRequiresPassword") && (WSPassword.Length < 6 || Password != WSPassword)) return Reply("Invalid Password, make sure your password contains 6 or more characters", false, 401, "Invalid Password");

            if ((Method == "GetCookie" || Method == "GetAccounts" || Method == "LaunchAccount" || Method == "FollowUser") && ((WSPassword != null && WSPassword.Length < 6) || (Password != null && Password != WSPassword))) return Reply("Invalid Password, make sure your password contains 6 or more characters", false, 401, "Invalid Password");

            if (!Developer.Get<bool>("EnableWebServer") && Method != "PushLiveStatus" && Method != "ClaimServer")
                return Reply("Method not allowed", false, 401, "Method not allowed");

            if (Method == "GetAccounts")
            {
                if (!WebServer.Get<bool>("AllowGetAccounts")) return Reply("Method `GetAccounts` not allowed", false, 401, "Method not allowed");

                string Names = "";
                string GroupFilter = request.QueryString["Group"];

                foreach (Account acc in AccountsList)
                {
                    if (!string.IsNullOrEmpty(GroupFilter) && acc.Group != GroupFilter) continue;

                    Names += acc.Username + ",";
                }

                return Reply(Names.Remove(Names.Length - 1), true, Raw: Names.Remove(Names.Length - 1));
            }

            if (Method == "GetAccountsJson")
            {
                if (!WebServer.Get<bool>("AllowGetAccounts")) return Reply("Method `GetAccountsJson` not allowed", false, 401, "Method not allowed");

                string GroupFilter = request.QueryString["Group"];
                bool ShowCookies = WSPassword.Length >= 6 && Password != WSPassword && request.QueryString["IncludeCookies"] == "true" && WebServer.Get<bool>("AllowGetCookie");

                List<object> Objects = new List<object>();

                foreach (Account acc in AccountsList)
                {
                    if (!string.IsNullOrEmpty(GroupFilter) && acc.Group != GroupFilter) continue;

                    object AccountObject = new
                    {
                        acc.Username,
                        acc.UserID,
                        acc.Alias,
                        acc.Description,
                        acc.Group,
                        acc.CSRFToken,
                        LastUsed = acc.LastUse.ToRobloxTick(),
                        Cookie = ShowCookies ? acc.SecurityToken : null,
                        acc.Fields,
                    };

                    Objects.Add(AccountObject);
                }

                return Reply(JsonConvert.SerializeObject(Objects), true);
            }

            if (Method == "ImportCookie")
            {
                Account New = AddAccount(request.QueryString["Cookie"]);

                bool Success = New != null;

                return Reply(Success ? "Cookie successfully imported" : "[ImportCookie] An error was encountered importing the cookie", Success, Raw: Success ? "true" : "false");
            }

            if (Method == "PushLiveStatus")
            {
                JObject Payload = null;
                if (!string.IsNullOrWhiteSpace(Body))
                    Body.TryParseJson(out Payload);

                string TargetAccount = request.QueryString["Account"]
                    ?? request.QueryString["Username"]
                    ?? Payload?["Account"]?.Value<string>()
                    ?? Payload?["Username"]?.Value<string>()
                    ?? Payload?["user"]?.Value<string>();

                string TargetUserId = request.QueryString["UserId"]
                    ?? request.QueryString["userid"]
                    ?? Payload?["UserId"]?.ToString()
                    ?? Payload?["userid"]?.ToString();

                Account TargetAccountObj = null;
                if (long.TryParse(TargetUserId, out long UserId) && UserId > 0)
                    TargetAccountObj = AccountsList.FirstOrDefault(x => x.UserID == UserId);

                if (TargetAccountObj == null && !string.IsNullOrWhiteSpace(TargetAccount))
                    TargetAccountObj = AccountsList.FirstOrDefault(x => string.Equals(x.Username, TargetAccount, StringComparison.OrdinalIgnoreCase) || x.UserID.ToString() == TargetAccount);

                if (TargetAccountObj == null)
                    return Reply("Account not found", false, 404, "Account not found");

                string HasOpenInstanceValue = request.QueryString["HasOpenInstance"]
                    ?? request.QueryString["open"]
                    ?? Payload?["HasOpenInstance"]?.ToString()
                    ?? Payload?["hasOpenInstance"]?.ToString();

                string IsOnServerValue = request.QueryString["IsOnServer"]
                    ?? request.QueryString["InGame"]
                    ?? request.QueryString["ingame"]
                    ?? Payload?["IsOnServer"]?.ToString()
                    ?? Payload?["isOnServer"]?.ToString()
                    ?? Payload?["InGame"]?.ToString()
                    ?? Payload?["inGame"]?.ToString();

                string PlaceIdValue = request.QueryString["PlaceId"]
                    ?? request.QueryString["placeid"]
                    ?? Payload?["PlaceId"]?.ToString()
                    ?? Payload?["placeId"]?.ToString();

                string GameName = request.QueryString["GameName"]
                    ?? Payload?["GameName"]?.Value<string>()
                    ?? Payload?["gameName"]?.Value<string>();

                bool HasOpenInstance = ParseBoolean(HasOpenInstanceValue, true);
                bool IsOnServer = ParseBoolean(IsOnServerValue, false);
                long? PlaceId = ParseNullableLong(PlaceIdValue);

                if (!IsOnServer)
                    PlaceId = null;

                ScriptLiveStatusOverride Override = new ScriptLiveStatusOverride
                {
                    UpdatedAtUtc = DateTime.UtcNow,
                    HasOpenInstance = HasOpenInstance,
                    IsOnServer = IsOnServer,
                    PlaceId = PlaceId,
                    GameName = GameName
                };

                ScriptLiveStatusOverrides.AddOrUpdate(TargetAccountObj.UserID, Override, (key, previous) => Override);

                bool GameChanged;
                bool StateChanged = ApplyScriptLiveStatusOverride(TargetAccountObj, Override, out GameChanged);

                if (StateChanged || GameChanged)
                    AccountsView.InvokeIfRequired(() => AccountsView.RefreshObject(TargetAccountObj));

                return Reply("true", true, Raw: "true");
            }

            if (Method == "ClaimServer")
            {
                JObject Payload = null;
                if (!string.IsNullOrWhiteSpace(Body))
                    Body.TryParseJson(out Payload);

                string PlaceIdValue = request.QueryString["PlaceId"]
                    ?? request.QueryString["placeid"]
                    ?? Payload?["PlaceId"]?.ToString()
                    ?? Payload?["placeId"]?.ToString();

                string JobId = request.QueryString["JobId"]
                    ?? request.QueryString["jobid"]
                    ?? Payload?["JobId"]?.ToString()
                    ?? Payload?["jobId"]?.ToString();

                string Owner = request.QueryString["Owner"]
                    ?? request.QueryString["Account"]
                    ?? request.QueryString["Username"]
                    ?? request.QueryString["UserId"]
                    ?? Payload?["Owner"]?.ToString()
                    ?? Payload?["Account"]?.ToString()
                    ?? Payload?["Username"]?.ToString()
                    ?? Payload?["UserId"]?.ToString()
                    ?? Payload?["userid"]?.ToString();

                string LeaseSecondsValue = request.QueryString["LeaseSeconds"]
                    ?? request.QueryString["leaseSeconds"]
                    ?? Payload?["LeaseSeconds"]?.ToString()
                    ?? Payload?["leaseSeconds"]?.ToString();

                if (!long.TryParse(PlaceIdValue, out long PlaceId) || PlaceId <= 0 || string.IsNullOrWhiteSpace(JobId))
                    return Reply(JsonConvert.SerializeObject(new { ok = false, claimed = false, error = "invalid_place_or_job" }), true);

                int LeaseSeconds = 120;
                if (int.TryParse(LeaseSecondsValue, out int ParsedLease) && ParsedLease > 0)
                    LeaseSeconds = ParsedLease;

                bool Claimed = TryClaimScriptServer(PlaceId, JobId, Owner, LeaseSeconds, out DateTime ExpiresAtUtc, out string ExistingOwner);
                string Response = JsonConvert.SerializeObject(new
                {
                    ok = true,
                    claimed = Claimed,
                    placeId = PlaceId,
                    jobId = JobId,
                    owner = ExistingOwner,
                    expiresAtUtc = ExpiresAtUtc == DateTime.MinValue ? string.Empty : ExpiresAtUtc.ToString("o"),
                });

                return Reply(Response, true);
            }

            if (string.IsNullOrEmpty(Account)) return Reply("Empty Account", false);

            Account account = AccountsList.FirstOrDefault(x => x.Username == Account || x.UserID.ToString() == Account);

            if (account == null || !account.GetCSRFToken(out string Token)) return Reply("Invalid Account, the account's cookie may have expired and resulted in the account being logged out", false, Raw: "Invalid Account");

            if (Method == "GetCookie")
            {
                if (!WebServer.Get<bool>("AllowGetCookie")) return Reply("Method `GetCookie` not allowed", false, 401, "Method not allowed");

                return Reply(account.SecurityToken, true);
            }

            if (Method == "LaunchAccount")
            {
                if (!WebServer.Get<bool>("AllowLaunchAccount")) return Reply("Method `LaunchAccount` not allowed", false, 401, "Method not allowed");

                bool ValidPlaceId = long.TryParse(request.QueryString["PlaceId"], out long PlaceId); if (!ValidPlaceId) return Reply("Invalid PlaceId provided", false, Raw: "Invalid PlaceId");

                string JobID = !string.IsNullOrEmpty(request.QueryString["JobId"]) ? request.QueryString["JobId"] : "";
                string FollowUser = request.QueryString["FollowUser"];
                string JoinVIP = request.QueryString["JoinVIP"];

                _ = Task.Run(async () =>
                {
                    string res = await JoinWithFailureRecovery(account, PlaceId, JobID, FollowUser == "true", JoinVIP == "true", "WebLaunchAccount");
                    if (!IsJoinSuccess(res))
                        Program.Logger.Warn($"[WebLaunchAccount] Launch failed for {account.Username}: {res}");
                });

                return Reply($"Launched {Account} to {PlaceId}", true);
            }

            if (Method == "FollowUser") // https://github.com/ic3w0lf22/Roblox-Account-Manager/pull/52
            {
                if (!WebServer.Get<bool>("AllowLaunchAccount")) return Reply("Method `FollowUser` not allowed", false, 401, "Method not allowed");

                string User = request.QueryString["Username"]; if (string.IsNullOrEmpty(User)) return Reply("Invalid Username Parameter", false);

                if (!GetUserID(User, out long UserId, out var Response))
                    return Reply($"[{Response.StatusCode} {Response.StatusDescription}] Failed to get UserId: {Response.Content}", false);

                _ = Task.Run(async () =>
                {
                    string res = await JoinWithFailureRecovery(account, UserId, "", true, false, "WebFollowUser");
                    if (!IsJoinSuccess(res))
                        Program.Logger.Warn($"[WebFollowUser] Follow failed for {account.Username}: {res}");
                });

                return Reply($"Joining {User}'s game on {Account}", true);
            }

            if (Method == "GetCSRFToken") return Reply(Token, true);
            if (Method == "GetAlias") return Reply(account.Alias, true);
            if (Method == "GetDescription") return Reply(account.Description, true);

            if (Method == "BlockUser" && !string.IsNullOrEmpty(request.QueryString["UserId"]))
                try
                {
                    var Res = account.BlockUserId(request.QueryString["UserId"], Context: Context);

                    return Reply(Res.Content, Res.IsSuccessful, (int)Res.StatusCode);
                }
                catch (Exception x) { return Reply(x.Message, false, 500); }
            if (Method == "UnblockUser" && !string.IsNullOrEmpty(request.QueryString["UserId"]))
                try
                {
                    var Res = account.UnblockUserId(request.QueryString["UserId"], Context: Context);

                    return Reply(Res.Content, Res.IsSuccessful, (int)Res.StatusCode);
                }
                catch (Exception x) { return Reply(x.Message, false, 500); }
            if (Method == "GetBlockedList") try
                {
                    var Res = account.GetBlockedList(Context);

                    return Reply(Res.Content, Res.IsSuccessful, (int)Res.StatusCode);
                }
                catch (Exception x) { return Reply(x.Message, false, 500); }
            if (Method == "UnblockEveryone" && account.UnblockEveryone(out string UbRes) is bool UbSuccess) return Reply(UbRes, UbSuccess);

            if (Method == "SetServer" && !string.IsNullOrEmpty(request.QueryString["PlaceId"]) && !string.IsNullOrEmpty(request.QueryString["JobId"]))
            {
                string RSP = account.SetServer(Convert.ToInt64(request.QueryString["PlaceId"]), request.QueryString["JobId"], out bool Success);

                return Reply(RSP, Success);
            }

            if (Method == "SetRecommendedServer")
            {
                int attempts = 0;
                string res = "-1";

                for (int i = RBX_Alt_Manager.ServerList.servers.Count - 1; i > 0; i--)
                {
                    if (attempts > 10)
                        return Reply("Too many failed attempts", false);

                    ServerData server = RBX_Alt_Manager.ServerList.servers[i];

                    if (AttemptedJoins.FirstOrDefault(x => x.id == server.id) != null) continue;
                    if (AttemptedJoins.Count > 100) AttemptedJoins.Clear();

                    AttemptedJoins.Add(server);

                    attempts++;

                    res = account.SetServer(!string.IsNullOrEmpty(request.QueryString["PlaceId"]) ? Convert.ToInt64(request.QueryString["PlaceId"]) : RBX_Alt_Manager.ServerList.CurrentPlaceID, server.id, out bool iSuccess);

                    if (iSuccess)
                        return Reply(res, iSuccess);
                }

                bool Success = !string.IsNullOrEmpty(res);

                return Reply(Success ? "Failed" : res, Success);
            }

            if (Method == "GetField" && !string.IsNullOrEmpty(request.QueryString["Field"])) return Reply(account.GetField(request.QueryString["Field"]), true);

            if (Method == "SetField" && !string.IsNullOrEmpty(request.QueryString["Field"]) && !string.IsNullOrEmpty(request.QueryString["Value"]))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) return Reply("Method `SetField` not allowed", false, 401, "Method not allowed");

                account.SetField(request.QueryString["Field"], request.QueryString["Value"]);

                return Reply($"Set Field {request.QueryString["Field"]} to {request.QueryString["Value"]} for {account.Username}", true);
            }
            if (Method == "RemoveField" && !string.IsNullOrEmpty(request.QueryString["Field"]))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) return Reply("Method `RemoveField` not allowed", false, 401, "Method not allowed");

                account.RemoveField(request.QueryString["Field"]);

                return Reply($"Removed Field {request.QueryString["Field"]} from {account.Username}", true);
            }

            if (Method == "SetAvatar" && Body.TryParseJson(out object _))
            {
                account.SetAvatar(Body);

                return Reply($"Attempting to set avatar of {account.Username} to {Body}", true);
            }

            if (Method == "SetAlias" && !string.IsNullOrEmpty(Body))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) return Reply("Method `SetAlias` not allowed", false, Raw: "Method not allowed");

                account.Alias = Body;
                UpdateAccountView(account);

                return Reply($"Set Alias of {account.Username} to {Body}", true);
            }
            if (Method == "SetDescription" && !string.IsNullOrEmpty(Body))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) Reply("Method `SetDescription` not allowed", false, Raw: "Method not allowed");

                account.Description = Body;
                UpdateAccountView(account);

                return Reply($"Set Description of {account.Username} to {Body}", true);
            }
            if (Method == "AppendDescription" && !string.IsNullOrEmpty(Body))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) return V2 ? WebServerResponse("Method `AppendDescription` not allowed", false) : "Method not allowed";

                account.Description += Body;
                UpdateAccountView(account);

                return Reply($"Appended Description of {account.Username} with {Body}", true);
            }

            return Reply("404 not found", false, 404);
        }

        private void AccountManager_Shown(object sender, EventArgs e)
        {
            _ = Task.Run(() =>
            {
                bool multiOk = UpdateMultiRoblox();
                if (!multiOk && !General.Get<bool>("HideRbxAlert"))
                {
                    this.InvokeIfRequired(() =>
                    {
                        MessageBox.Show("WARNING: Multi Roblox could not lock Roblox's singleton right now.\nRAM will retry automatically for bulk launch.\nIf it still fails, run RAM as admin once and retry.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
            });

            if (General.Get<bool>("CheckForUpdates"))
            {
                _ = Task.Run(async () =>
                {
                    string LatestReleaseTag = await Auto_Update.AutoUpdater.TryGetLatestReleaseTagAsync();
                    if (string.IsNullOrWhiteSpace(LatestReleaseTag))
                        return;

                    string LocalReleaseTag = Auto_Update.AutoUpdater.GetLocalReleaseTag();
                    if (!string.IsNullOrWhiteSpace(LocalReleaseTag)
                        && string.Equals(LocalReleaseTag, LatestReleaseTag, StringComparison.OrdinalIgnoreCase))
                        return;

                    this.InvokeIfRequired(() =>
                    {
                        if (!Utilities.YesNoPrompt("Roblox Account Manager", $"Custom update available ({LatestReleaseTag})", "Would you like to install it now?", false))
                            return;

                        using (Auto_Update.AutoUpdater Updater = new Auto_Update.AutoUpdater())
                            Updater.ShowDialog(this);
                    });
                });
            }

            int Major = Environment.OSVersion.Version.Major, Minor = Environment.OSVersion.Version.Minor;

            PuppeteerSupported = !(Major < 6 || (Major == 6 && Minor <= 1));

            if (General.Get<bool>("UseCefSharpBrowser")) PuppeteerSupported = false;

            if (!PuppeteerSupported)
            {
                AddAccountsStrip.Items.Remove(bulkUserPassToolStripMenuItem);
                AddAccountsStrip.Items.Remove(customURLJSToolStripMenuItem);
                OpenBrowserStrip.Items.Remove(URLJSToolStripMenuItem);
                OpenBrowserStrip.Items.Remove(joinGroupToolStripMenuItem);
            }

            if (PuppeteerSupported && (!Directory.Exists(AccountBrowser.Fetcher.DownloadsFolder) || Directory.GetDirectories(AccountBrowser.Fetcher.DownloadsFolder).Length == 0))
            {
                Add.Visible = false;
                Remove.Visible = false;
                DownloadProgressBar.Visible = true;
                DLChromiumLabel.Visible = true;

                Task.Run(async () =>
                {
                    IsDownloadingChromium = true;

                    void DownloadProgressChanged(object s, DownloadProgressChangedEventArgs e) => DownloadProgressBar.InvokeIfRequired(() => { DownloadProgressBar.Value = e.ProgressPercentage; });

                    AccountBrowser.Fetcher.DownloadProgressChanged += DownloadProgressChanged;
                    await AccountBrowser.Fetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
                    AccountBrowser.Fetcher.DownloadProgressChanged -= DownloadProgressChanged;

                    IsDownloadingChromium = false;

                    this.InvokeIfRequired(() =>
                    {
                        Add.Visible = true;
                        Remove.Visible = true;
                        DownloadProgressBar.Visible = false;
                        DLChromiumLabel.Visible = false;
                    });
                });
            }
            else if (!PuppeteerSupported)
            {
                FileInfo Cef = new FileInfo(Path.Combine(Environment.CurrentDirectory, "x86", "CefSharp.dll"));

                if (Cef.Exists)
                {
                    FileVersionInfo Info = FileVersionInfo.GetVersionInfo(Cef.FullName);

                    if (Info.ProductMajorPart != 109)
                        try { Directory.GetParent(Cef.FullName).RecursiveDelete(); } catch { }
                }

                if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "x86")))
                {
                    var Existing = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "x86"));

                    DLChromiumLabel.Text = "Downloading CefSharp...";

                    Add.Visible = false;
                    Remove.Visible = false;
                    DownloadProgressBar.Visible = true;
                    DLChromiumLabel.Visible = true;

                    Task.Run(async () =>
                    {
                        IsDownloadingChromium = true;

                        using HttpClient client = new HttpClient();

                        string FileName = Path.GetTempFileName(), DownloadUrl = Resources.CefSharpDownload;

                        var TotalDownloadSize = (await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, DownloadUrl))).Content.Headers.ContentLength.Value;
                        Progress<float> progress = new Progress<float>(progress => DownloadProgressBar.InvokeIfRequired(() => DownloadProgressBar.Value = (int)(progress * 100)));

                        using (var file = new FileStream(FileName, FileMode.Create, FileAccess.Write, FileShare.None))
                            await client.DownloadAsync(DownloadUrl, file, progress);

                        if (Existing.Exists) Existing.RecursiveDelete();

                        System.IO.Compression.ZipFile.ExtractToDirectory(FileName, Environment.CurrentDirectory);

                        IsDownloadingChromium = false;

                        this.InvokeIfRequired(() =>
                        {
                            Add.Visible = true;
                            Remove.Visible = true;
                            DownloadProgressBar.Visible = false;
                            DLChromiumLabel.Visible = false;
                        });
                    });
                }
            }

            if (AccountControl.Get<bool>("StartOnLaunch"))
                LaunchNexus.PerformClick();
        }

        public bool UpdateMultiRoblox()
        {
            bool Enabled = General.Get<bool>("EnableMultiRbx");

            if (Enabled)
            {
                if (TryAcquireMultiRobloxMutex())
                    return true;

                if (TryReleaseRobloxSingletonHandles())
                {
                    Thread.Sleep(175);

                    if (TryAcquireMultiRobloxMutex())
                        return true;
                }

                return false;
            }

            if (!Enabled)
            {
                ReleaseMultiRobloxMutex(ref rbxMultiMutex);
                ReleaseMultiRobloxMutex(ref rbxMultiEventNameMutex);
            }

            return true;
        }

        private bool TryAcquireMultiRobloxMutex()
        {
            bool mutexNameOk = TryAcquireNamedMutex("ROBLOX_singletonMutex", ref rbxMultiMutex);
            bool eventNameOk = TryAcquireNamedMutex("ROBLOX_singletonEvent", ref rbxMultiEventNameMutex);
            return mutexNameOk || eventNameOk;
        }

        private static void ReleaseMultiRobloxMutex(ref Mutex mutex)
        {
            if (mutex == null)
                return;

            try { mutex.ReleaseMutex(); } catch { }
            try { mutex.Close(); } catch { }
            try { mutex.Dispose(); } catch { }
            mutex = null;
        }

        private bool TryAcquireNamedMutex(string mutexName, ref Mutex mutex)
        {
            if (mutex != null)
                return true;

            try
            {
                mutex = new Mutex(true, mutexName);

                try
                {
                    if (!mutex.WaitOne(TimeSpan.Zero, true))
                    {
                        mutex.Dispose();
                        mutex = null;
                        return false;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // Another process abandoned this mutex; ownership has been transferred.
                }

                return true;
            }
            catch (Exception x)
            {
                Program.Logger.Warn($"[MultiRbx] Failed acquiring {mutexName}: {x.Message}");

                try { mutex?.Dispose(); } catch { }
                mutex = null;
                return false;
            }
        }

        private static string ReadProcessOutput(Process process)
        {
            string StdOut = string.Empty;
            string StdErr = string.Empty;

            try { StdOut = process?.StandardOutput?.ReadToEnd() ?? string.Empty; } catch { }
            try { StdErr = process?.StandardError?.ReadToEnd() ?? string.Empty; } catch { }

            if (string.IsNullOrEmpty(StdErr))
                return StdOut ?? string.Empty;

            return $"{StdOut}{Environment.NewLine}{StdErr}";
        }

        private bool EnsureHandleToolReady()
        {
            try
            {
                if (!File.Exists(RobloxWatcher.HandlePath))
                    File.WriteAllBytes(RobloxWatcher.HandlePath, Resources.handle);
            }
            catch (Exception x)
            {
                Program.Logger.Warn($"[MultiRbx] Failed writing handle tool: {x.Message}");
                return false;
            }

            if (RobloxWatcher.IsHandleEulaAccepted())
                return true;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Sysinternals\Handle"))
                    key?.SetValue("EulaAccepted", 1, RegistryValueKind.DWord);
            }
            catch (Exception x)
            {
                Program.Logger.Warn($"[MultiRbx] Failed setting Handle EULA value: {x.Message}");
            }

            return RobloxWatcher.IsHandleEulaAccepted();
        }

        private IEnumerable<Process> GetMultiRobloxHandleCandidates()
        {
            List<Process> candidates = new List<Process>();

            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        string processName = process.ProcessName ?? string.Empty;
                        if (processName.IndexOf("Roblox", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            process.Dispose();
                            continue;
                        }

                        candidates.Add(process);
                    }
                    catch
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }
            catch { }

            return candidates;
        }

        private bool TryReleaseRobloxSingletonHandles()
        {
            if (!EnsureHandleToolReady())
                return false;

            bool ClosedAny = false;

            foreach (Process process in GetMultiRobloxHandleCandidates())
            {
                int pid = -1;

                try
                {
                    pid = process.Id;
                    if (process.HasExited) continue;

                    ProcessStartInfo scan = new ProcessStartInfo(RobloxWatcher.HandlePath)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        Arguments = $"-a -nobanner -p {pid} ROBLOX_singleton"
                    };

                    using (Process scanProc = Process.Start(scan))
                    {
                        if (scanProc == null) continue;

                        if (!scanProc.WaitForExit(6000))
                        {
                            try { scanProc.Kill(); } catch { }
                            continue;
                        }

                        string scanOutput = ReadProcessOutput(scanProc);
                        MatchCollection matches = MultiRbxHandleRegex.Matches(scanOutput ?? string.Empty);

                        foreach (Match match in matches)
                        {
                            if (!match.Success || match.Groups.Count < 2) continue;

                            string handleId = match.Groups[1].Value;
                            if (string.IsNullOrWhiteSpace(handleId)) continue;

                            ProcessStartInfo close = new ProcessStartInfo(RobloxWatcher.HandlePath)
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                Arguments = $"-c {handleId} -p {pid} -y -nobanner"
                            };

                            using (Process closeProc = Process.Start(close))
                            {
                                if (closeProc == null) continue;

                                if (!closeProc.WaitForExit(6000))
                                {
                                    try { closeProc.Kill(); } catch { }
                                    continue;
                                }

                                string closeOutput = ReadProcessOutput(closeProc);
                                bool closed = closeProc.ExitCode == 0 && closeOutput.IndexOf("Error", StringComparison.OrdinalIgnoreCase) < 0;

                                if (closed)
                                {
                                    ClosedAny = true;
                                    Program.Logger.Info($"[MultiRbx] Closed singleton handle {handleId} for PID {pid}");
                                }
                            }
                        }
                    }
                }
                catch (Exception x)
                {
                    Program.Logger.Warn($"[MultiRbx] Failed singleton handle cleanup for PID {pid}: {x.Message}");
                }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            }

            return ClosedAny;
        }

        private bool EnsureMultiRobloxForBulkLaunch()
        {
            if (!General.Get<bool>("EnableMultiRbx"))
            {
                General.Set("EnableMultiRbx", "true");
                IniSettings.Save("RAMSettings.ini");
            }

            if (Process.GetProcessesByName("RobloxPlayerBeta").Length > 0)
                TryReleaseRobloxSingletonHandles();

            if (UpdateMultiRoblox())
                return true;

            Program.Logger.Warn("[BulkLaunch] Initial multi-Roblox prep failed. Forcing Roblox process reset and retrying prep.");
            ForceStopAllRobloxProcessesForMultiPrep();
            Thread.Sleep(700);

            if (UpdateMultiRoblox())
            {
                Program.Logger.Info("[BulkLaunch] Multi-Roblox prep recovered after full Roblox reset.");
                return true;
            }

            MessageBox.Show("Multi Roblox prep failed. RAM will still attempt bulk launch in best-effort mode.\nIf instances still do not open in parallel, close all Roblox processes and retry.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void ForceStopAllRobloxProcessesForMultiPrep()
        {
            foreach (Process Process in GetAllRobloxClientProcessesForRecovery())
            {
                try
                {
                    if (Process.HasExited)
                        continue;

                    Process.CloseMainWindow();
                    if (!Process.WaitForExit(700))
                    {
                        try { Process.Kill(); } catch { }
                        try { Process.WaitForExit(1200); } catch { }
                    }
                }
                catch { }
                finally
                {
                    try { Process.Dispose(); } catch { }
                }
            }

            try
            {
                RobloxWatcher.Instances.Clear();
                RobloxWatcher.Seen.Clear();
            }
            catch { }
        }

        private bool PrepareMultiRobloxForAccountLaunch(int attempts = 3)
        {
            if (!General.Get<bool>("EnableMultiRbx"))
                return true;

            int retries = Math.Max(1, attempts);

            for (int i = 0; i < retries; i++)
            {
                TryReleaseRobloxSingletonHandles();
                if (UpdateMultiRoblox())
                    return true;

                Thread.Sleep(120);
            }

            return false;
        }

        private static bool IsJoinSuccess(string Result) =>
            !string.IsNullOrWhiteSpace(Result) && Result.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;

        private static int GetRobloxPlayerProcessCount()
        {
            try { return Process.GetProcessesByName("RobloxPlayerBeta").Length; }
            catch { return 0; }
        }

        private static bool IsProcessAlive(int processId)
        {
            if (processId <= 0)
                return false;

            try
            {
                using (Process process = Process.GetProcessById(processId))
                    return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private int FindRobloxProcessIdByTracker(string browserTrackerId)
        {
            if (string.IsNullOrWhiteSpace(browserTrackerId))
                return 0;

            try
            {
                foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    try
                    {
                        if (process.HasExited)
                            continue;

                        string commandLine = process.GetCommandLine() ?? string.Empty;
                        Match trackerMatch = BrowserTrackerRegex.Match(commandLine);
                        string trackerId = trackerMatch.Success ? trackerMatch.Groups[1].Value : string.Empty;

                        if (string.Equals(trackerId, browserTrackerId, StringComparison.Ordinal))
                            return process.Id;
                    }
                    catch { }
                    finally { try { process.Dispose(); } catch { } }
                }
            }
            catch { }

            return 0;
        }

        private async Task<bool> WaitForLaunchEvidence(Account account, int previousProcessId, int baselineProcessCount, int timeoutMs = 12000)
        {
            string trackerId = account?.BrowserTrackerID;
            Stopwatch timer = Stopwatch.StartNew();

            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                int trackerProcessId = FindRobloxProcessIdByTracker(trackerId);
                if (trackerProcessId > 0 && trackerProcessId != previousProcessId && IsProcessAlive(trackerProcessId))
                    return true;

                if (account != null)
                {
                    int pid = account.CurrentProcessId;
                    if (pid > 0 && pid != previousProcessId && IsProcessAlive(pid))
                        return true;
                }

                if (GetRobloxPlayerProcessCount() > baselineProcessCount)
                    return true;

                await Task.Delay(200);
            }

            return false;
        }

        private static IEnumerable<Process> GetAllRobloxClientProcessesForRecovery()
        {
            string[] Names = new[]
            {
                "RobloxPlayerBeta",
                "RobloxPlayerLauncher",
                "RobloxPlayerInstaller",
                "RobloxCrashHandler",
                "RobloxCrashHandler64",
                "RobloxGameLauncher"
            };

            HashSet<int> SeenPids = new HashSet<int>();
            List<Process> Result = new List<Process>();

            foreach (string Name in Names)
            {
                Process[] Processes;
                try { Processes = Process.GetProcessesByName(Name); }
                catch { continue; }

                foreach (Process Process in Processes)
                {
                    bool KeepProcess = false;

                    try
                    {
                        KeepProcess = SeenPids.Add(Process.Id);
                    }
                    catch { }

                    if (KeepProcess)
                    {
                        Result.Add(Process);
                    }
                    else
                    {
                        try { Process.Dispose(); } catch { }
                    }
                }
            }

            return Result;
        }

        private async Task CloseAllRobloxInstancesForRetry()
        {
            try
            {
                foreach (Process Process in GetAllRobloxClientProcessesForRecovery())
                {
                    try
                    {
                        if (Process.HasExited)
                            continue;

                        Process.CloseMainWindow();
                        await Task.Delay(150);
                        Process.CloseMainWindow();
                        await Task.Delay(150);

                        if (!Process.HasExited)
                            Process.Kill();
                    }
                    catch { }
                    finally
                    {
                        try { Process.Dispose(); } catch { }
                    }
                }
            }
            catch { }

            try
            {
                RobloxWatcher.Instances.Clear();
                RobloxWatcher.Seen.Clear();
            }
            catch { }

            List<Account> Updates = new List<Account>();

            foreach (Account Account in AccountsList)
            {
                if (Account == null) continue;

                bool Changed = false;

                if (Account.HasOpenInstance)
                {
                    Account.HasOpenInstance = false;
                    Changed = true;
                }

                if (Account.IsOnServer)
                {
                    Account.IsOnServer = false;
                    Changed = true;
                }

                if (Account.CurrentProcessId != 0)
                {
                    Account.CurrentProcessId = 0;
                    Changed = true;
                }

                if (!string.IsNullOrEmpty(Account.CurrentGameName))
                {
                    Account.CurrentGameName = string.Empty;
                    Account.CurrentPlaceId = null;
                    Changed = true;
                }

                if (Changed)
                    Updates.Add(Account);
            }

            if (Updates.Count > 0)
                AccountsView.InvokeIfRequired(() => AccountsView.RefreshObjects(Updates));
        }

        private async Task<string> JoinWithFailureRecovery(Account account, long placeId, string jobId, bool followUser, bool vipServer, string contextTag, bool allowGlobalReset = true, bool requireNewProcess = false)
        {
            if (account == null)
                return "ERROR: Account is null";

            int baselineProcessCount = requireNewProcess ? GetRobloxPlayerProcessCount() : 0;
            int previousProcessId = account.CurrentProcessId;

            string result = await account.JoinServer(placeId, jobId, followUser, vipServer);
            if (IsJoinSuccess(result) && requireNewProcess)
            {
                bool launched = await WaitForLaunchEvidence(account, previousProcessId, baselineProcessCount);
                if (!launched)
                {
                    Program.Logger.Warn($"[{contextTag}] Join call for {account.Username} returned but no new process was detected.");
                    result = "ERROR: Join returned but no new Roblox process was detected for this account.";
                }
            }

            if (IsJoinSuccess(result))
                return result;

            if (!allowGlobalReset)
            {
                Program.Logger.Warn($"[{contextTag}] Initial launch failed for {account.Username}: {result}. Retrying once without global reset.");

                if (General.Get<bool>("EnableMultiRbx"))
                    PrepareMultiRobloxForAccountLaunch();

                await Task.Delay(600);

                int retryBaselineCount = requireNewProcess ? GetRobloxPlayerProcessCount() : 0;
                int retryPreviousPid = account.CurrentProcessId;
                string retryNoReset = await account.JoinServer(placeId, jobId, followUser, vipServer);

                if (IsJoinSuccess(retryNoReset) && requireNewProcess)
                {
                    bool launched = await WaitForLaunchEvidence(account, retryPreviousPid, retryBaselineCount);
                    if (!launched)
                    {
                        Program.Logger.Warn($"[{contextTag}] Retry join for {account.Username} returned but still no new process was detected.");
                        retryNoReset = "ERROR: Join returned but no new Roblox process was detected for this account.";
                    }
                }

                if (!IsJoinSuccess(retryNoReset))
                    Program.Logger.Warn($"[{contextTag}] Retry failed for {account.Username}: {retryNoReset}");

                return retryNoReset;
            }

            Program.Logger.Warn($"[{contextTag}] Initial launch failed for {account.Username}: {result}. Closing all Roblox instances and retrying once.");

            await CloseAllRobloxInstancesForRetry();

            if (General.Get<bool>("EnableMultiRbx"))
                PrepareMultiRobloxForAccountLaunch();

            await Task.Delay(800);

            int retryProcessCount = requireNewProcess ? GetRobloxPlayerProcessCount() : 0;
            int retryProcessId = account.CurrentProcessId;
            string retryResult = await account.JoinServer(placeId, jobId, followUser, vipServer);

            if (IsJoinSuccess(retryResult) && requireNewProcess)
            {
                bool launched = await WaitForLaunchEvidence(account, retryProcessId, retryProcessCount);
                if (!launched)
                {
                    Program.Logger.Warn($"[{contextTag}] Retry join for {account.Username} returned but no new process was detected.");
                    retryResult = "ERROR: Join returned but no new Roblox process was detected for this account.";
                }
            }

            if (!IsJoinSuccess(retryResult))
                Program.Logger.Warn($"[{contextTag}] Retry failed for {account.Username}: {retryResult}");

            return retryResult;
        }

        private void Remove_Click(object sender, EventArgs e)
        {
            if (AccountsView.SelectedObjects.Count > 1)
            {
                DialogResult result = MessageBox.Show($"Are you sure you want to remove {AccountsView.SelectedObjects.Count} accounts?", "Remove Accounts", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    foreach (Account acc in AccountsView.SelectedObjects)
                        AccountsList.Remove(acc);

                    RefreshView();

                    SaveAccounts();
                }
            }
            else if (SelectedAccount != null)
            {
                DialogResult result = MessageBox.Show($"Are you sure you want to remove {SelectedAccount.Username}?", "Remove Account", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    AccountsList.RemoveAll(x => x == SelectedAccount);

                    RefreshView();

                    SaveAccounts();
                }
            }
        }

        private async void Add_Click(object sender, EventArgs e)
        {
            if (PuppeteerSupported)
            {
                Add.Enabled = false;

                try { await new AccountBrowser().Login(); }
                catch (Exception x)
                {
                    Program.Logger.Error($"[Add_Click] An error was encountered attempting to login: {x}");

                    if (Utilities.YesNoPrompt($"An error was encountered attempting to login", "You may have a corrupted chromium installation", "Would you like to re-install chromium?", false))
                    {
                        MessageBox.Show("Roblox Account Manager will now close since it can't delete the folder while it's in use.", "", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        if (Directory.GetFiles(AccountBrowser.Fetcher.DownloadsFolder).Length <= 1 && Directory.GetDirectories(AccountBrowser.Fetcher.DownloadsFolder).Length <= 1)
                            Process.Start("cmd.exe", $"/c rmdir /s /q \"{AccountBrowser.Fetcher.DownloadsFolder}\"");
                        else
                            Process.Start("explorer.exe", "/select, " + AccountBrowser.Fetcher.DownloadsFolder);

                        Environment.Exit(0);
                    }
                }

                Add.Enabled = true;
            }
            else
                CefBrowser.Instance.Login();
        }

        private void DownloadProgressBar_Click(object sender, EventArgs e)
        {
            static void ShowManualInstallInstructions()
            {
                string Temp = Path.Combine(Path.GetTempPath(), "manual install instructions.html");

                string DownloadLink = PuppeteerSupported ? (string)typeof(BrowserFetcher).GetMethod("GetDownloadURL", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { AccountBrowser.Fetcher.Product, AccountBrowser.Fetcher.Platform, AccountBrowser.Fetcher.DownloadHost, BrowserFetcher.DefaultChromiumRevision }) : Resources.CefSharpDownload;
                string Directory = PuppeteerSupported ? Path.Combine(AccountBrowser.Fetcher.DownloadsFolder, $"{AccountBrowser.Fetcher.Platform}-{BrowserFetcher.DefaultChromiumRevision}") : Path.Combine(Environment.CurrentDirectory);

                File.WriteAllText(Temp, string.Format(Resources.ManualInstallHTML, PuppeteerSupported ? "Chromium" : "CefSharp", DownloadLink, PuppeteerSupported ? "chrome-win" : "x86", Directory));

                Process.Start(new ProcessStartInfo(Temp) { UseShellExecute = true });
                Process.Start(new ProcessStartInfo("cmd") { Arguments = $"/c mkdir \"{Directory}\"", CreateNoWindow = true });
            }

            if (TaskDialog.IsPlatformSupported)
            {
                TaskDialog Dialog = new TaskDialog()
                {
                    Caption = "Add Account",
                    InstructionText = $"{(PuppeteerSupported ? "Chromium" : "CefSharp")} is still being downloaded",
                    Text = "If this is not working for you, you can choose to manually install",
                    Icon = TaskDialogStandardIcon.Information
                };

                TaskDialogButton Manual = new TaskDialogButton("Manual", "Download Manually");
                TaskDialogButton Wait = new TaskDialogButton("Wait", "Wait");

                Wait.Click += (s, e) => Dialog.Close();
                Manual.Click += (s, e) =>
                {
                    Dialog.Close();

                    ShowManualInstallInstructions();
                };

                Dialog.Controls.Add(Manual);
                Dialog.Controls.Add(Wait);
                Wait.Default = true;

                Dialog.Show();
            }
            else if (MessageBox.Show($"{(PuppeteerSupported ? "Chromium" : "CefSharp")} is still downloading, you may have to wait a while before adding an account.\n\nNot working? You can choose to manually install by pressing \"Yes\"", "Roblox Account Manager", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) == DialogResult.Yes)
                ShowManualInstallInstructions();
        }

        private void DLChromiumLabel_Click(object sender, EventArgs e) => DownloadProgressBar_Click(sender, e);

        private void manualToolStripMenuItem_Click(object sender, EventArgs e) => Add.PerformClick();

        private void addAccountsToolStripMenuItem_Click(object sender, EventArgs e) => Add.PerformClick();

        private void byCookieToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ImportAccountsForm.Show();
            ImportAccountsForm.WindowState = FormWindowState.Normal;
            ImportAccountsForm.BringToFront();
        }

        private async void bulkUserPassToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string Combos = ShowDialog("Separate the accounts with new lines\nMust be in user:pass form", "Import by User:Pass", big: true);

            if (Combos == "/UC") return;

            List<string> ComboList = new List<string>(Combos.Split('\n'));

            var Size = new System.Numerics.Vector2(455, 485);
            AccountBrowser.CreateGrid(Size);

            for (int i = 0; i < ComboList.Count; i++)
            {
                string Combo = ComboList[i];

                if (!Combo.Contains(':')) continue;

                var LoginTask = new AccountBrowser() { Index = i, Size = Size }.Login(Combo.Substring(0, Combo.IndexOf(':')), Combo.Substring(Combo.IndexOf(":") + 1));

                if ((i + 1) % 2 == 0) await LoginTask;
            }
        }

        private void AccountsView_SelectedIndexChanged(object sender, EventArgs e)
        {
            List<Account> selectedNow = GetSelectedAccountsFromView();
            SelectedAccounts = selectedNow;
            UpdateSelectionStatusText();

            if (selectedNow.Count != 1)
            {
                SelectedAccount = null;
                SelectedAccountItem = null;

                return;
            }

            SelectedAccount = selectedNow[0];
            SelectedAccountItem = AccountsView.SelectedItem;

            if (SelectedAccount == null) return;

            AccountsView.HideSelection = false;

            Alias.Text = SelectedAccount.Alias;
            DescriptionBox.Text = SelectedAccount.Description;

            if (!string.IsNullOrEmpty(SelectedAccount.GetField("SavedPlaceId"))) PlaceID.Text = SelectedAccount.GetField("SavedPlaceId");
            if (!string.IsNullOrEmpty(SelectedAccount.GetField("SavedJobId"))) JobID.Text = SelectedAccount.GetField("SavedJobId");
        }

        private void SetAlias_Click(object sender, EventArgs e)
        {
            foreach (Account account in AccountsView.SelectedObjects)
                account.Alias = Alias.Text;

            RefreshView();
        }

        private void SetDescription_Click(object sender, EventArgs e)
        {
            foreach (Account account in AccountsView.SelectedObjects)
                account.Description = DescriptionBox.Text;

            RefreshView();
        }

        private void JoinServer_Click(object sender, EventArgs e)
        {
            Match IDMatch = Regex.Match(PlaceID.Text, @"\/games\/(\d+)[\/|\?]?"); // idiotproofing

            if (PlaceID.Text.Contains("privateServerLinkCode") && IDMatch.Success)
                JobID.Text = PlaceID.Text;

            Game G = RecentGames.FirstOrDefault(RG => RG.Details.filteredName == PlaceID.Text);

            if (G != null)
                PlaceID.Text = G.Details.placeId.ToString();

            PlaceID.Text = IDMatch.Success ? IDMatch.Groups[1].Value : Regex.Replace(PlaceID.Text, "[^0-9]", "");

            bool VIPServer = JobID.TextLength > 4 && JobID.Text.Substring(0, 4) == "VIP:";

            if (!long.TryParse(PlaceID.Text, out long PlaceId)) return;

            if (!PlaceTimer.Enabled)
                _ = Task.Run(() => AddRecentGame(new Game(PlaceId)));

            CancelLaunching();

            List<Account> LaunchTargets = GetLaunchTargetsSnapshot();
            if (LaunchTargets.Count == 0)
            {
                MessageBox.Show("Select at least one account to launch.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool LaunchMultiple = LaunchTargets.Count > 1;
            Account SingleTarget = LaunchTargets.Count == 1 ? LaunchTargets[0] : null;
            string JoinJobId = VIPServer ? JobID.Text.Substring(4) : JobID.Text;

            new Thread(async () => // finally fixing an ancient bug in a dumb way, p.s. i do not condone this.
            {
                if (LaunchMultiple)
                {
                    if (!EnsureMultiRobloxForBulkLaunch())
                        Program.Logger.Warn("[BulkLaunch] Multi Roblox prep failed in pre-check; continuing with best-effort launch.");

                    LauncherToken = new CancellationTokenSource();

                    await LaunchAccounts(LaunchTargets, PlaceId, JoinJobId, false, VIPServer);
                }
                else if (SingleTarget != null)
                {
                    string res = await JoinWithFailureRecovery(SingleTarget, PlaceId, JoinJobId, false, VIPServer, "JoinServer");

                    if (!IsJoinSuccess(res))
                        MessageBox.Show(res);
                }
            }).Start();
        }

        private async void Follow_Click(object sender, EventArgs e)
        {
            if (!GetUserID(UserID.Text, out long UserId, out var Response))
            {
                MessageBox.Show($"[{Response.StatusCode} {Response.StatusDescription}] Failed to get UserId: {Response.Content}");
                return;
            }
    
            if (!(await Presence.GetPresenceSingular(UserId) is UserPresence Status && Status.userPresenceType == UserPresenceType.InGame && Status.placeId is long FollowPlaceID && FollowPlaceID > 0) &&
                !Utilities.YesNoPrompt("Warning", "The user you are trying to follow is not in game or has their joins off", "Do you want to attempt to join anyways?")) return;

            CancelLaunching();

            List<Account> LaunchTargets = GetLaunchTargetsSnapshot();
            if (LaunchTargets.Count == 0)
            {
                MessageBox.Show("Select at least one account to launch.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool LaunchMultiple = LaunchTargets.Count > 1;
            Account SingleTarget = LaunchTargets.Count == 1 ? LaunchTargets[0] : null;

            if (LaunchMultiple)
            {
                LauncherToken = new CancellationTokenSource();

                await Task.Run(async () =>
                {
                    if (!EnsureMultiRobloxForBulkLaunch())
                        Program.Logger.Warn("[FollowLaunch] Multi Roblox prep failed in pre-check; continuing with best-effort launch.");

                    await LaunchAccounts(LaunchTargets, UserId, "", true);
                });
            }
            else if (SingleTarget != null)
            {
                string res = await JoinWithFailureRecovery(SingleTarget, UserId, "", true, false, "FollowJoin");

                if (!IsJoinSuccess(res))
                    MessageBox.Show(res);
            }
        }

        private void ServerList_Click(object sender, EventArgs e)
        {
            if (AccountsList.Count == 0 || LastValidAccount == null)
                MessageBox.Show("Some features may not work unless there is a valid account", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            if (ServerListForm.Visible)
            {
                ServerListForm.WindowState = FormWindowState.Normal;
                ServerListForm.BringToFront();
            }
            else
                ServerListForm.Show();

            ServerListForm.Busy = false; // incase it somehow bugs out

            ServerListForm.StartPosition = FormStartPosition.Manual;
            ServerListForm.Top = Top;
            ServerListForm.Left = Right;
        }

        private void HideUsernamesCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            General.Set("HideUsernames", HideUsernamesCheckbox.Checked ? "true" : "false");

            AccountsView.BeginUpdate();

            Username.Width = HideUsernamesCheckbox.Checked ? 0 : (int)(120 * Program.Scale);

            AccountsView.EndUpdate();
        }

        private void removeAccountToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (AccountsView.SelectedObjects.Count > 1)
            {
                DialogResult result = MessageBox.Show($"Are you sure you want to remove {AccountsView.SelectedObjects.Count} accounts?", "Remove Accounts", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    foreach (Account acc in AccountsView.SelectedObjects)
                        AccountsList.Remove(acc);

                    RefreshView();
                    SaveAccounts();
                }
            }
            else if (SelectedAccount != null)
            {
                DialogResult result = MessageBox.Show($"Are you sure you want to remove {SelectedAccount.Username}?", "Remove Account", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    AccountsList.Remove(SelectedAccount);

                    RefreshView();
                    SaveAccounts();
                }
            }
        }

        private void AccountManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (IsDownloadingChromium && !Utilities.YesNoPrompt("Roblox Account Manager", $"{(PuppeteerSupported ? "Chromium" : "CefSharp")} is still being downloaded, exiting may corrupt your chromium installation and prevent account manager from working", "Exit anyways?", false))
            {
                e.Cancel = true;

                return;
            }

            AltManagerWS?.Stop();
            LastSeenTimer?.Stop();
            LastSeenTimer?.Dispose();
            LiveStatusTimer?.Stop();
            LiveStatusTimer?.Dispose();
            PresenceTimer?.Stop();
            PresenceTimer?.Dispose();
            ProcessOptimizerTimer?.Stop();
            ProcessOptimizerTimer?.Dispose();

            if (PlaceID == null || string.IsNullOrEmpty(PlaceID.Text)) return;

            General.Set("SavedPlaceId", PlaceID.Text);
            General.Set("SavedFollowUser", UserID.Text);
            IniSettings.Save("RAMSettings.ini");
        }

        private void BrowserButton_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("No Account Selected!");
                return;
            }

            UtilsForm.Show();
            UtilsForm.WindowState = FormWindowState.Normal;
            UtilsForm.BringToFront();
        }

        private void getAuthenticationTicketToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount != null)
            {
                if (SelectedAccount.GetAuthTicket(out string STicket))
                    Clipboard.SetText(STicket);

                return;
            }

            if (SelectedAccounts.Count < 1) return;

            List<string> Tickets = new List<string>();

            foreach (Account acc in SelectedAccounts)
            {
                if (acc.GetAuthTicket(out string Ticket))
                    Tickets.Add($"{acc.Username}:{Ticket}");
            }

            if (Tickets.Count > 0)
                Clipboard.SetText(string.Join("\n", Tickets));
        }

        private void copyRbxplayerLinkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null) return;

            if (SelectedAccount.GetAuthTicket(out string Ticket))
            {
                bool HasJobId = string.IsNullOrEmpty(JobID.Text);
                double LaunchTime = Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds * 1000);

                Random r = new Random();
                Clipboard.SetText(string.Format("<roblox-player://1/1+launchmode:play+gameinfo:{0}+launchtime:{4}+browsertrackerid:{5}+placelauncherurl:https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame{3}&placeId={1}{2}+robloxLocale:en_us+gameLocale:en_us>", Ticket, PlaceID.Text, HasJobId ? "" : ("&gameId=" + JobID.Text), HasJobId ? "" : "Job", LaunchTime, r.Next(100000, 130000).ToString() + r.Next(100000, 900000).ToString()));
            }
        }

        private void ArgumentsB_Click(object sender, EventArgs e)
        {
            if (afform != null)
                if (afform.Visible)
                    afform.HideForm();
                else
                    afform.ShowForm();
        }

        private void copySecurityTokenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Tokens = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Tokens.Add(account.SecurityToken);

            Clipboard.SetText(string.Join("\n", Tokens));
        }

        private void copyUsernameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Usernames = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Usernames.Add(account.Username);

            Clipboard.SetText(string.Join("\n", Usernames));
        }

        private void copyPasswordToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Passwords = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Passwords.Add($"{account.Password}");

            Clipboard.SetText(string.Join("\n", Passwords));
        }

        private void copyUserPassComboToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Combos = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Combos.Add($"{account.Username}:{account.Password}");

            Clipboard.SetText(string.Join("\n", Combos));
        }

        private void copyUserIdToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> UserIds = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                UserIds.Add(account.UserID.ToString());

            Clipboard.SetText(string.Join("\n", UserIds));
        }

        private void PlaceID_TextChanged(object sender, EventArgs e)
        {
            if (PlaceTimer.Enabled) PlaceTimer.Stop();

            PlaceTimer.Start();
        }

        private async void PlaceTimer_Tick(object sender, EventArgs e)
        {
            PlaceTimer.Stop();

            if (PlaceID != null)
            {
                General.Set("SavedPlaceId", PlaceID.Text);
                IniSettings.Save("RAMSettings.ini");
            }

            if (EconClient == null) return;

            RestRequest request = new RestRequest($"v2/assets/{PlaceID.Text}/details", Method.Get);
            request.AddHeader("Accept", "application/json");
            RestResponse response = await EconClient.ExecuteAsync(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK && response.Content.StartsWith("{") && response.Content.EndsWith("}"))
            {
                ProductInfo placeInfo = JsonConvert.DeserializeObject<ProductInfo>(response.Content);

                Utilities.InvokeIfRequired(this, () => CurrentPlace.Text = placeInfo.Name);
            }
        }

        private void moveToToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (AccountsView.SelectedObjects.Count == 0) return;

            string GroupName = ShowDialog("Group Name", "Move Account to Group", SelectedAccount != null ? SelectedAccount.Group : string.Empty);

            if (GroupName == "/UC") return; // User Cancelled
            if (string.IsNullOrEmpty(GroupName)) GroupName = "Default";

            foreach (Account acc in AccountsView.SelectedObjects)
                acc.Group = GroupName;

            RefreshView();
            SaveAccounts();
        }

        private void copyGroupToolStripMenuItem_Click(object sender, EventArgs e) => Clipboard.SetText(SelectedAccount?.Group ?? "No Account Selected");

        private void copyAppLinkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null) return;

            if (SelectedAccount.GetAuthTicket(out string Ticket))
            {
                double LaunchTime = Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds * 1000);

                Random r = new Random();
                Clipboard.SetText(string.Format("<roblox-player://1/1+launchmode:app+gameinfo:{0}+launchtime:{1}+browsertrackerid:{2}+robloxLocale:en_us+gameLocale:en_us>", Ticket, LaunchTime, r.Next(500000, 600000).ToString() + r.Next(10000, 90000).ToString()));
            }
        }

        private void JoinDiscord_Click(object sender, EventArgs e) => Process.Start("https://discord.gg/MsEH7smXY8");

        private void OpenBrowser_Click(object sender, EventArgs e)
        {
            if (PuppeteerSupported)
                foreach (Account account in AccountsView.SelectedObjects)
                    new AccountBrowser(account);
            else if (!PuppeteerSupported && SelectedAccount != null)
                CefBrowser.Instance.EnterBrowserMode(SelectedAccount);
        }

        private void customURLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Uri.TryCreate(ShowDialog("URL", "Open Browser"), UriKind.Absolute, out Uri Link))
                if (PuppeteerSupported)
                    foreach (Account account in AccountsView.SelectedObjects)
                        new AccountBrowser(account, Link.ToString(), string.Empty);
                else if (!PuppeteerSupported && SelectedAccount != null)
                    CefBrowser.Instance.EnterBrowserMode(SelectedAccount, Link.ToString());
        }

        private void URLJSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Utilities.YesNoPrompt("Warning", "Your accounts may be at risk using this feature", "Do not paste in javascript unless you know what it does, your account's cookies can easily be logged through javascript.\n\nPress Yes to continue", true)) return;

            if (Uri.TryCreate(ShowDialog("URL", "Open Browser"), UriKind.Absolute, out Uri Link))
            {
                string Script = ShowDialog("Javascript", "Open Browser", big: true);

                foreach (Account account in AccountsView.SelectedObjects)
                    new AccountBrowser(account, Link.ToString(), Script);
            }
        }

        private void joinGroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Uri.TryCreate(ShowDialog("Group Link", "Open Browser"), UriKind.Absolute, out Uri Link))
            {
                foreach (Account account in AccountsView.SelectedObjects)
                    new AccountBrowser(account, Link.ToString(), PostNavigation: async (page) =>
                    {
                        await (await page.WaitForSelectorAsync("#group-join-button", new WaitForSelectorOptions() { Timeout = 12000 })).ClickAsync();
                    });
            }
        }

        private void customURLJSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int Count = 1;

            if (ModifierKeys == Keys.Shift)
                int.TryParse(ShowDialog("Amount (Limited to 15)", "Launch Browser", "1"), out Count);

            if (Uri.TryCreate(ShowDialog("URL", "Launch Browser", "https://roblox.com/"), UriKind.Absolute, out Uri Link))
            {
                string Script = ShowDialog("Javascript", "Launch Browser", big: true);

                var Size = new System.Numerics.Vector2(550, 440);
                AccountBrowser.CreateGrid(Size);

                for (int i = 0; i < Math.Min(Count, 15); i++) {
                    var Browser = new AccountBrowser() { Size = Size, Index = i };

                    _ = Browser.LaunchBrowser(Url: Link.ToString(), Script: Script, PostNavigation: async (p) => await Browser.LoginTask(p));
                }
            }
        }

        private void copyProfileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Profiles = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Profiles.Add($"https://www.roblox.com/users/{account.UserID}/profile");

            Clipboard.SetText(string.Join("\n", Profiles));
        }

        private void viewFieldsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null) return;

            FieldsForm.View(SelectedAccount);
        }

        private void SaveToAccount_Click(object sender, EventArgs e)
        {
            if (ModifierKeys == Keys.Shift)
            {
                List<Account> HasSaved = new List<Account>();

                foreach (Account account in AccountsList)
                    if (account.Fields.ContainsKey("SavedPlaceId") || account.Fields.ContainsKey("SavedJobId"))
                        HasSaved.Add(account);

                if (HasSaved.Count > 0 && MessageBox.Show($"Are you sure you want to remove {HasSaved.Count} saved Place Ids?", "Roblox Account Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.OK)
                    foreach (Account account in HasSaved)
                    {
                        account.RemoveField("SavedPlaceId");
                        account.RemoveField("SavedJobId");
                    }
            }

            foreach (Account account in AccountsView.SelectedObjects)
            {
                if (string.IsNullOrEmpty(PlaceID.Text) && string.IsNullOrEmpty(JobID.Text))
                {
                    account.RemoveField("SavedPlaceId");
                    account.RemoveField("SavedJobId");

                    return;
                }

                string PlaceId = CurrentPlaceId;

                if (JobID.Text.Contains("privateServerLinkCode") && Regex.IsMatch(JobID.Text, @"\/games\/(\d+)\/"))
                    PlaceId = Regex.Match(CurrentJobId, @"\/games\/(\d+)\/").Groups[1].Value;

                account.SetField("SavedPlaceId", PlaceId);
                account.SetField("SavedJobId", JobID.Text);
            }
        }

        private void AccountsView_ModelCanDrop(object sender, ModelDropEventArgs e)
        {
            if (e.SourceModels[0] != null && e.SourceModels[0] is Account) e.Effect = DragDropEffects.Move;
        }

        private void AccountsView_ModelDropped(object sender, ModelDropEventArgs e)
        {
            if (e.TargetModel == null || e.SourceModels.Count == 0) return;

            Account droppedOn = e.TargetModel as Account;

            int Index = e.DropTargetIndex;

            for (int i = e.SourceModels.Count; i > 0; i--)
            {
                if (!(e.SourceModels[i - 1] is Account dragged)) continue;

                dragged.Group = droppedOn.Group;

                AccountsList.Remove(dragged);
                AccountsList.Insert(Index, dragged);
            }

            RefreshView(e.SourceModels[e.SourceModels.Count - 1]);
            SaveAccounts();
        }

        private void sortAlphabeticallyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show($"Are you sure you want to sort every account alphabetically?", "Roblox Account Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                AccountsList = AccountsList.OrderByDescending(x => x.Username.All(char.IsDigit)).ThenByDescending(x => x.Username.Any(char.IsLetter)).ThenBy(x => x.Username).ToList();

                AccountsView.SetObjects(AccountsList);
                AccountsView.BuildGroups();
            }
        }

        private async void quickLogInToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null) return;

            if (!Utilities.YesNoPrompt("Quick Log In", "Only enter codes that you requested\nNever enter another user's code", $"Do you understand?", SaveIfNo: false))
                return;

            if (Clipboard.ContainsText() && Clipboard.GetText() is string ClipCode && ClipCode.Length == 6 && await SelectedAccount.QuickLogIn(ClipCode))
                return;

            string Code = ShowDialog("Code", "Quick Log In");

            if (Code.Length != 6) { MessageBox.Show("Quick Log In codes requires 6 characters", "Quick Log In", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            await SelectedAccount.QuickLogIn(Code);
        }

        private void toggleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AccountsView.ShowGroups = !AccountsView.ShowGroups;

            if (AccountsView.HeaderStyle != ColumnHeaderStyle.None) AccountsView.HeaderStyle = AccountsView.ShowGroups ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.Clickable;

            AccountsView.BuildGroups();
        }

        private void EditTheme_Click(object sender, EventArgs e)
        {
            if (ThemeForm != null && ThemeForm.Visible)
            {
                ThemeForm.Hide();
                return;
            }

            ThemeForm.Show();
        }

        private void LaunchNexus_Click(object sender, EventArgs e)
        {
            if (ControlForm != null)
            {
                ControlForm.Top = Bottom;
                ControlForm.Left = Left;
                ControlForm.Show();
                ControlForm.BringToFront();
            }
            else
            {
                ControlForm = new AccountControl
                {
                    StartPosition = FormStartPosition.Manual,
                    Top = Bottom,
                    Left = Left
                };
                ControlForm.Show();
                ControlForm.ApplyTheme();
            }
        }

        private async Task LaunchAccounts(List<Account> Accounts, long PlaceID, string JobID, bool FollowUser = false, bool VIPServer = false)
        {
            int Delay = General.Exists("AccountJoinDelay") ? General.Get<int>("AccountJoinDelay") : 8;

            bool AsyncJoin = General.Get<bool>("AsyncJoin");
            CancellationTokenSource Token = LauncherToken;
            List<string> Failures = new List<string>();
            bool MultiLaunch = Accounts != null && Accounts.Count > 1;

            if (MultiLaunch)
                AsyncJoin = false; // Always run selected multi-launch as strict queue.

            Interlocked.Exchange(ref BulkLaunchInProgress, 1);
            NormalizeBrowserTrackerIds();

            try
            {
                int total = Accounts?.Count ?? 0;
                int index = 0;

                foreach (Account account in Accounts ?? Enumerable.Empty<Account>())
                {
                    index++;

                    if (Token != null && Token.IsCancellationRequested)
                        break;

                    Program.Logger.Info($"[BulkLaunch] Launching {index}/{total}: {account?.Username ?? "unknown"}");

                    long PlaceId = PlaceID;
                    string JobId = JobID;

                    if (!FollowUser)
                    {
                        if (!string.IsNullOrEmpty(account.GetField("SavedPlaceId")) && long.TryParse(account.GetField("SavedPlaceId"), out long PID)) PlaceId = PID;
                        if (!string.IsNullOrEmpty(account.GetField("SavedJobId"))) JobId = account.GetField("SavedJobId");
                    }

                    try
                    {
                        string Result = await JoinWithFailureRecovery(account, PlaceId, JobId, FollowUser, VIPServer, "BulkLaunch", allowGlobalReset: false, requireNewProcess: true);
                        if (!IsJoinSuccess(Result))
                        {
                            Program.Logger.Warn($"[BulkLaunch] Failed launching {account.Username}: {Result}");
                            Failures.Add($"{account.Username}: {Result.Split('\n').FirstOrDefault() ?? Result}");
                        }
                        else
                        {
                            Program.Logger.Info($"[BulkLaunch] Success launching {index}/{total}: {account.Username}");
                        }
                    }
                    catch (Exception x)
                    {
                        Program.Logger.Error($"[BulkLaunch] Exception launching {account?.Username ?? "unknown"}: {x}");
                        Failures.Add($"{account?.Username ?? "unknown"}: {x.Message}");
                    }

                    if (AsyncJoin)
                    {
                        while (!LaunchNext)
                            await Task.Delay(50);
                    }
                    else
                        await Task.Delay(Delay * 1000);

                    LaunchNext = false;
                }
            }
            finally
            {
                LaunchNext = false;

                if (Token != null)
                {
                    try { Token.Cancel(); } catch { }
                    try { Token.Dispose(); } catch { }
                }

                Interlocked.Exchange(ref BulkLaunchInProgress, 0);
            }

            if (Failures.Count > 0)
                this.InvokeIfRequired(() => MessageBox.Show($"Some accounts failed to launch:\n\n{string.Join("\n", Failures)}", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        }

        public void NextAccount() => LaunchNext = true;
        public void CancelLaunching()
        {
            if (LauncherToken != null && !LauncherToken.IsCancellationRequested)
                LauncherToken.Cancel();
        }

        private void infoToolStripMenuItem1_Click(object sender, EventArgs e) =>
            MessageBox.Show("Roblox Account Manager created by ic3w0lf under the GNU GPLv3 license.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

        private void groupsToolStripMenuItem_Click(object sender, EventArgs e) =>
            MessageBox.Show("Groups can be sorted by naming them a number then whatever you want.\nFor example: You can put Group Apple on top by naming it '001 Apple' or '1Apple'.\nThe numbers will be hidden from the name but will be correctly sorted depending on the number.\nAccounts can also be dragged into groups.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

        private void DonateButton_Click(object sender, EventArgs e) =>
            Process.Start("https://ic3w0lf22.github.io/donate.html");

        private void ConfigButton_Click(object sender, EventArgs e)
        {
            SettingsForm ??= new SettingsForm();

            if (SettingsForm.Visible)
            {
                SettingsForm.WindowState = FormWindowState.Normal;
                SettingsForm.BringToFront();
            }
            else
                SettingsForm.Show();

            SettingsForm.StartPosition = FormStartPosition.Manual;
            SettingsForm.Top = Top;
            SettingsForm.Left = Right;
        }

        private void HistoryIcon_MouseHover(object sender, EventArgs e) => RGForm.ShowForm();

        private void ShuffleIcon_Click(object sender, EventArgs e)
        {
            ShuffleJobID = !ShuffleJobID;

            if (sender != null)
            {
                General.Set("ShuffleJobId", ShuffleJobID ? "true" : "false");
                IniSettings.Save("RAMSettings.ini");
            }

            if (ShuffleJobID)
                if (ThemeEditor.LightImages)
                    ShuffleIcon.ColorImage(87, 245, 102);
                else
                    ShuffleIcon.ColorImage(57, 152, 22);
            else
            {
                if (BackColor.GetBrightness() < 0.5)
                    ShuffleIcon.ColorImage(255, 255, 255);
                else
                    ShuffleIcon.ColorImage(0, 0, 0);
            }
        }

        private void ShowDetailsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "AccountDumps")))
                Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "AccountDumps"));

            foreach (Account Account in AccountsView.SelectedObjects)
            {
                Task.Run(async () =>
                {
                    var UserInfo = await Account.GetUserInfo();
                    double AccountAge = -1;

                    if (DateTime.TryParse(UserInfo["created"].Value<string>(), out DateTime CreationTime))
                        AccountAge = (DateTime.UtcNow - CreationTime).TotalDays;

                    StringBuilder builder = new StringBuilder();

                    builder.AppendLine($"Username: {Account.Username}");
                    builder.AppendLine($"UserId: {Account.UserID}");
                    builder.AppendLine($"Robux: {await Account.GetRobux()}");
                    builder.AppendLine($"Account Age: {(AccountAge >= 0 ? $"{AccountAge:F1}" : "UNKNOWN")}");
                    builder.AppendLine($"Email Status: {await Account.GetEmailJSON()}");
                    builder.AppendLine($"User Info: {UserInfo}");
                    builder.AppendLine($"Other: {await Account.GetMobileInfo()}");
                    builder.AppendLine($"Fields: {JsonConvert.SerializeObject(Account.Fields)}");

                    string FileName = Path.Combine(Environment.CurrentDirectory, "AccountDumps", Account.Username + ".txt");

                    File.WriteAllText(FileName, builder.ToString());

                    Process.Start(FileName);
                });
            }
        }

        private static bool TryParsePriorityClass(string value, out ProcessPriorityClass priorityClass)
        {
            priorityClass = ProcessPriorityClass.Normal;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "idle":
                    priorityClass = ProcessPriorityClass.Idle;
                    return true;
                case "belownormal":
                    priorityClass = ProcessPriorityClass.BelowNormal;
                    return true;
                case "normal":
                    priorityClass = ProcessPriorityClass.Normal;
                    return true;
                case "abovenormal":
                    priorityClass = ProcessPriorityClass.AboveNormal;
                    return true;
                case "high":
                    priorityClass = ProcessPriorityClass.High;
                    return true;
                case "realtime":
                    priorityClass = ProcessPriorityClass.RealTime;
                    return true;
                default:
                    return false;
            }
        }

        private static int ParseIoPriorityHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return -1;

            switch (value.Trim().ToLowerInvariant())
            {
                case "verylow":
                    return 0;
                case "low":
                    return 1;
                case "normal":
                    return 2;
                default:
                    return -1;
            }
        }

        private static bool TryParseAffinityMask(string value, out IntPtr affinity)
        {
            affinity = IntPtr.Zero;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string raw = value.Trim();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring(2);

            if (!ulong.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed) || parsed == 0)
                return false;

            affinity = IntPtr.Size == 8 ? new IntPtr(unchecked((long)parsed)) : new IntPtr(unchecked((int)parsed));
            return true;
        }

        private static void TrySetIoPriority(Process process, string ioPrioritySetting)
        {
            int hint = ParseIoPriorityHint(ioPrioritySetting);
            if (hint < 0)
                return;

            try
            {
                NtSetInformationProcess(process.Handle, ProcessIoPriorityClass, ref hint, sizeof(int));
            }
            catch { }
        }

        private void ApplyManagerPriority()
        {
            if (!General.Get<bool>("RaiseManagerPriority"))
                return;

            if (!TryParsePriorityClass(General.Get("ManagerPriority"), out ProcessPriorityClass managerPriority))
                managerPriority = ProcessPriorityClass.AboveNormal;

            using (Process current = Process.GetCurrentProcess())
            {
                try
                {
                    if (current.PriorityClass != managerPriority)
                        current.PriorityClass = managerPriority;
                }
                catch { }
            }
        }

        public void ApplyRobloxProcessOptimization(Process process)
        {
            if (process == null || !General.Get<bool>("EnableProcessOptimizer"))
                return;

            try
            {
                if (process.HasExited)
                    return;
            }
            catch
            {
                return;
            }

            try { process.PriorityBoostEnabled = false; } catch { }

            if (TryParsePriorityClass(General.Get("RobloxPriority"), out ProcessPriorityClass robloxPriority))
            {
                try
                {
                    if (process.PriorityClass != robloxPriority)
                        process.PriorityClass = robloxPriority;
                }
                catch { }
            }

            TrySetIoPriority(process, General.Get("RobloxIoPriority"));

            if (TryParseAffinityMask(General.Get("RobloxAffinityMask"), out IntPtr affinity))
            {
                try
                {
                    process.ProcessorAffinity = affinity;
                }
                catch { }
            }

            try
            {
                EmptyWorkingSet(process.Handle);
            }
            catch { }
        }

        private void TryApplyProcessOptimization()
        {
            if (Interlocked.Exchange(ref ProcessOptimizerInProgress, 1) == 1)
                return;

            try
            {
                ApplyManagerPriority();

                if (!General.Get<bool>("EnableProcessOptimizer"))
                    return;

                foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    try
                    {
                        ApplyRobloxProcessOptimization(process);
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            finally
            {
                Interlocked.Exchange(ref ProcessOptimizerInProgress, 0);
            }
        }

        public void ApplyProcessOptimizationNow()
        {
            Task.Run(() => TryApplyProcessOptimization());
        }

        private CancellationTokenSource PresenceCancellationToken;

        public void UpdatePresenceTimerInterval()
        {
            if (PresenceTimer == null) return;

            double Minutes = 2;
            try { Minutes = Math.Max(General.Get<double>("PresenceUpdateRate"), 1d); } catch { }

            PresenceTimer.Interval = Minutes * 60000d;
        }

        private async Task TryUpdatePresence()
        {
            if (!General.Get<bool>("ShowPresence")) return;
            if (Interlocked.Exchange(ref PresenceUpdateInProgress, 1) == 1) return;

            try { await UpdatePresence(); }
            finally { Interlocked.Exchange(ref PresenceUpdateInProgress, 0); }
        }

        private async Task TryUpdateLiveStatus(bool ForcePresenceRefresh = false)
        {
            if (Interlocked.Exchange(ref LiveStatusUpdateInProgress, 1) == 1) return;

            try { await UpdateLiveStatus(ForcePresenceRefresh); }
            finally { Interlocked.Exchange(ref LiveStatusUpdateInProgress, 0); }
        }

        private static string GetBrowserTrackerID(string CommandLine)
        {
            if (string.IsNullOrEmpty(CommandLine)) return string.Empty;

            Match trackerMatch = BrowserTrackerRegex.Match(CommandLine);
            return trackerMatch.Success && trackerMatch.Groups.Count >= 2 ? trackerMatch.Groups[1].Value : string.Empty;
        }

        private Dictionary<string, OpenInstanceState> GetOpenInstanceStates()
        {
            Dictionary<string, OpenInstanceState> States = new Dictionary<string, OpenInstanceState>();

            try
            {
                foreach (RobloxProcess Instance in RobloxWatcher.Instances.ToArray())
                {
                    if (Instance == null || string.IsNullOrEmpty(Instance.BrowserTrackerID)) continue;
                    States[Instance.BrowserTrackerID] = new OpenInstanceState
                    {
                        IsConnectedToServer = Instance.IsConnectedToServer,
                        ProcessId = Math.Max(0, Instance.ProcessId)
                    };
                }
            }
            catch { }

            foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                try
                {
                    string TrackerID = GetBrowserTrackerID(process.GetCommandLine());

                    if (string.IsNullOrEmpty(TrackerID) || States.ContainsKey(TrackerID))
                        continue;

                    States.Add(TrackerID, new OpenInstanceState
                    {
                        IsConnectedToServer = null,
                        ProcessId = process.Id
                    });
                }
                catch { }
                finally { process.Dispose(); }
            }

            return States;
        }

        private List<Account> GetVisibleAccounts()
        {
            List<Account> VisibleAccounts = new List<Account>();

            var Bounds = AccountsView.ClientRectangle;
            int Padding = (int)(AccountsView.HeaderStyle == ColumnHeaderStyle.None ? 4f * Program.Scale : 20f * Program.Scale);

            for (int Y = Padding; Y < Bounds.Height - (Padding / 2); Y += (int)(6f * Program.Scale))
            {
                var Item = AccountsView.GetItemAt(4, Y);

                if (Item != null && AccountsView.GetModelObject(Item.Index) is Account account && !VisibleAccounts.Contains(account))
                    VisibleAccounts.Add(account);
            }

            return VisibleAccounts;
        }

        private void RefreshLastSeenColumn()
        {
            if (AccountsView == null || AccountsView.IsDisposed || !IsHandleCreated)
                return;

            AccountsView.InvokeIfRequired(() =>
            {
                if (AccountsView.IsDisposed)
                    return;

                List<Account> Visible = GetVisibleAccounts();
                if (Visible.Count > 0)
                    AccountsView.RefreshObjects(Visible);
            });
        }

        private static long? GetPresencePlaceId(Account account)
        {
            if (account?.Presence == null) return null;

            if (account.Presence.placeId is long PlaceId && PlaceId > 0)
                return PlaceId;

            if (account.Presence.rootPlaceId is long RootPlaceId && RootPlaceId > 0)
                return RootPlaceId;

            return null;
        }

        private void QueuePlaceNameLookup(long PlaceId)
        {
            if (PlaceId <= 0 || EconClient == null) return;
            if (PlaceNameCache.ContainsKey(PlaceId)) return;
            if (!PlaceNameLookups.TryAdd(PlaceId, 0)) return;

            Task.Run(async () =>
            {
                try
                {
                    RestRequest request = new RestRequest($"v2/assets/{PlaceId}/details", Method.Get);
                    request.AddHeader("Accept", "application/json");

                    RestResponse response = await EconClient.ExecuteAsync(request);

                    if (!response.IsSuccessful || response.StatusCode != HttpStatusCode.OK || !response.Content.TryParseJson(out JObject Details))
                        return;

                    string Name = Details["Name"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(Name)) return;

                    PlaceNameCache[PlaceId] = Name;

                    List<Account> Updated = AccountsList?.Where(account => account.CurrentPlaceId == PlaceId).ToList() ?? new List<Account>();
                    if (Updated.Count == 0) return;

                    foreach (Account account in Updated)
                        account.CurrentGameName = Name;

                    AccountsView.InvokeIfRequired(() => AccountsView.RefreshObjects(Updated));
                }
                catch (Exception x)
                {
                    Program.Logger.Error($"Failed to load game name for PlaceId {PlaceId}: {x.Message}");
                }
                finally
                {
                    PlaceNameLookups.TryRemove(PlaceId, out _);
                }
            });
        }

        private List<Account> UpdateAccountGameNames(IEnumerable<Account> TargetAccounts)
        {
            List<Account> Accounts = (TargetAccounts ?? Enumerable.Empty<Account>()).Where(account => account != null).Distinct().ToList();
            List<Account> Changed = new List<Account>();
            List<Account> PresenceNeeded = new List<Account>();

            foreach (Account account in Accounts)
            {
                long? PresencePlaceId = GetPresencePlaceId(account);
                long? PlaceId = PresencePlaceId ?? account.CurrentPlaceId;
                bool PresenceInGame = account.Presence?.userPresenceType == UserPresenceType.InGame && PresencePlaceId.HasValue;
                bool ScriptInGame = account.HasOpenInstance && account.IsOnServer && HasResolvedGame(account);
                bool InGame = ScriptInGame || PresenceInGame;
                string GameName = string.Empty;

                if (InGame)
                {
                    if (PlaceId.HasValue)
                    {
                        if (PlaceNameCache.TryGetValue(PlaceId.Value, out string CachedName) && !string.IsNullOrWhiteSpace(CachedName))
                            GameName = CachedName;
                        else
                        {
                            GameName = PlaceId.Value.ToString();
                            QueuePlaceNameLookup(PlaceId.Value);
                        }
                    }
                    else
                    {
                        GameName = "Loading...";
                        PresenceNeeded.Add(account);
                    }
                }

                if (account.CurrentPlaceId != PlaceId || account.CurrentGameName != GameName)
                {
                    account.CurrentPlaceId = PlaceId;
                    account.CurrentGameName = GameName;
                    Changed.Add(account);
                }
            }

            if (Changed.Count > 0)
                AccountsView.InvokeIfRequired(() => AccountsView.RefreshObjects(Changed));

            return PresenceNeeded;
        }

        private async Task UpdateLiveStatus(bool ForcePresenceRefresh = false)
        {
            if (AccountsList == null || AccountsList.Count == 0) return;

            Dictionary<string, OpenInstanceState> States = GetOpenInstanceStates();
            List<Account> Changed = new List<Account>();
            List<Account> GameNameChanged = new List<Account>();
            List<Account> PresenceFallback = new List<Account>();
            HashSet<Account> OverrideAccounts = new HashSet<Account>();

            foreach (Account account in AccountsList)
            {
                OpenInstanceState InstanceState = null;
                int CurrentPid = 0;

                if (!string.IsNullOrEmpty(account.BrowserTrackerID) && States.TryGetValue(account.BrowserTrackerID, out OpenInstanceState TrackerState))
                {
                    InstanceState = TrackerState;
                    CurrentPid = Math.Max(0, TrackerState.ProcessId);
                }

                if (TryGetScriptLiveStatusOverride(account, out ScriptLiveStatusOverride Override))
                {
                    bool GameChanged;
                    bool StateChanged = ApplyScriptLiveStatusOverride(account, Override, out GameChanged);

                    if (account.CurrentProcessId != CurrentPid)
                    {
                        account.CurrentProcessId = CurrentPid;
                        StateChanged = true;
                    }

                    if (StateChanged)
                        Changed.Add(account);

                    if (GameChanged)
                        GameNameChanged.Add(account);

                    OverrideAccounts.Add(account);

                    continue;
                }

                bool HasOpenInstance = false;
                bool IsOnServer = false;

                if (InstanceState != null)
                {
                    HasOpenInstance = true;

                    bool? ServerState = InstanceState.IsConnectedToServer;

                    if (ServerState.HasValue)
                    {
                        long? PresencePlaceId = GetPresencePlaceId(account);
                        IsOnServer = ServerState.Value && PresencePlaceId.HasValue;

                        if (!PresencePlaceId.HasValue)
                            PresenceFallback.Add(account);
                    }
                    else
                    {
                        long? PresencePlaceId = GetPresencePlaceId(account);
                        IsOnServer = account.Presence?.userPresenceType == UserPresenceType.InGame && PresencePlaceId.HasValue;
                        PresenceFallback.Add(account);
                    }
                }

                if (account.HasOpenInstance != HasOpenInstance || account.IsOnServer != IsOnServer || account.CurrentProcessId != CurrentPid)
                {
                    account.HasOpenInstance = HasOpenInstance;
                    account.IsOnServer = IsOnServer;
                    account.CurrentProcessId = CurrentPid;
                    Changed.Add(account);
                }
            }

            if (Changed.Count > 0)
                AccountsView.InvokeIfRequired(() => AccountsView.RefreshObjects(Changed));

            if (GameNameChanged.Count > 0)
                AccountsView.InvokeIfRequired(() => AccountsView.RefreshObjects(GameNameChanged));

            IEnumerable<Account> GameNameTargets = OverrideAccounts.Count > 0
                ? AccountsList.Where(account => !OverrideAccounts.Contains(account))
                : AccountsList;

            List<Account> PresenceRefreshTargets = new List<Account>(PresenceFallback);
            PresenceRefreshTargets.AddRange(UpdateAccountGameNames(GameNameTargets));
            PresenceRefreshTargets = PresenceRefreshTargets.Distinct().ToList();
            UpdateAutoRejoin();

            if (PresenceRefreshTargets.Count == 0) return;
            if (!ForcePresenceRefresh && (DateTime.Now - LastOpenInstancePresenceUpdate).TotalSeconds < 6) return;

            try
            {
                await Presence.UpdatePresence(PresenceRefreshTargets.Select(account => account.UserID).Distinct().ToArray());
                LastOpenInstancePresenceUpdate = DateTime.Now;
            }
            catch { }

            List<Account> PresenceUpdates = new List<Account>();

            foreach (Account account in PresenceRefreshTargets)
            {
                bool IsOnServer = account.Presence?.userPresenceType == UserPresenceType.InGame && GetPresencePlaceId(account).HasValue;

                if (account.HasOpenInstance && account.IsOnServer != IsOnServer)
                {
                    account.IsOnServer = IsOnServer;
                    PresenceUpdates.Add(account);
                }
            }

            if (PresenceUpdates.Count > 0)
                AccountsView.InvokeIfRequired(() => AccountsView.RefreshObjects(PresenceUpdates));

            UpdateAccountGameNames(PresenceRefreshTargets);
        }

        private void AccountsView_Scroll(object sender, ScrollEventArgs e)
        {
            PresenceCancellationToken?.Cancel();
            if (!General.Get<bool>("ShowPresence")) return;

            PresenceCancellationToken = new CancellationTokenSource();
            var Token = PresenceCancellationToken.Token;

            Task.Run(async () =>
            {
                await Task.Delay(3500); // Wait until the user has stopped scrolling before updating account presence

                if (Token.IsCancellationRequested)
                    return;

                await TryUpdatePresence();
            }, PresenceCancellationToken.Token);
        }

        private async Task UpdatePresence(IEnumerable<Account> TargetAccounts = null)
        {
            if (!General.Get<bool>("ShowPresence")) return;

            List<Account> Accounts = (TargetAccounts ?? GetVisibleAccounts()).Where(account => account != null).Distinct().ToList();
            if (Accounts.Count == 0) return;

            try { await Presence.UpdatePresence(Accounts.Select(account => account.UserID).ToArray()); } catch { }
            UpdateAccountGameNames(Accounts);
        }

        private void JobID_Click( object sender, EventArgs e )
        {
            JobID.SelectAll(); // Allows quick replacing of the JobID with a click and ctrl-v.
        }

        private void PlaceID_Click( object sender, EventArgs e )
        {
            PlaceID.SelectAll(); // Allows quick replacing of the PlaceID with a click and ctrl-v.
        }
    }
}
