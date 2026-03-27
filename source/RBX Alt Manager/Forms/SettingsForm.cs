using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace RBX_Alt_Manager.Forms
{
    public partial class SettingsForm : Form
    {
        private bool SettingsLoaded = false;
        private RegistryKey StartupKey;
        private CheckBox AutoRejoinCB;
        private CheckBox EnableProcessOptimizerCB;
        private Label RobloxPriorityLabel;
        private ComboBox RobloxPriorityCombo;
        private Label RobloxIoPriorityLabel;
        private ComboBox RobloxIoPriorityCombo;
        private Label RobloxAffinityMaskLabel;
        private TextBox RobloxAffinityMaskTB;
        private CheckBox RaiseManagerPriorityCB;
        private Label ManagerPriorityLabel;
        private ComboBox ManagerPriorityCombo;
        private Label ProcessLassoPathLabel;
        private TextBox ProcessLassoPathTB;
        private Button OpenProcessLassoButton;
        private Label RAMMapPathLabel;
        private TextBox RAMMapPathTB;
        private Button OpenRAMMapButton;
        private Label CustomRobloxPathLabel;
        private TextBox CustomRobloxPathTB;
        private Button BrowseCustomRobloxPathButton;
        private Button ClearCustomRobloxPathButton;
        private Label MemoryMonitorLabel;
        private Timer PerformanceMonitorTimer;
        private const long AutoRejoinPlaceId = 109983668079237;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public SettingsForm()
        {
            AccountManager.SetDarkBar(Handle);

            InitializeComponent();
            InitializeAutoRejoinControl();
            InitializePerformanceControls();
            this.Rescale();
        }

        private void InitializeAutoRejoinControl()
        {
            AutoRejoinCB = new CheckBox
            {
                AutoSize = true,
                Name = "AutoRejoinCB",
                Text = "Auto Rejoin"
            };

            Helper.SetToolTip(AutoRejoinCB, $"If an active account has no in-game signal for 1m 25s, RAM relaunches it to place {AutoRejoinPlaceId}.");
            AutoRejoinCB.CheckedChanged += AutoRejoinCB_CheckedChanged;

            int InsertAfter = SettingsLayoutPanel.Controls.IndexOf(AutoCookieRefreshCB);

            SettingsLayoutPanel.Controls.Add(AutoRejoinCB);
            SettingsLayoutPanel.SetFlowBreak(AutoRejoinCB, true);

            if (InsertAfter >= 0)
                SettingsLayoutPanel.Controls.SetChildIndex(AutoRejoinCB, InsertAfter + 1);
        }

        private void InitializePerformanceControls()
        {
            MiscellaneousFlowPanel.AutoScroll = true;

            EnableProcessOptimizerCB = new CheckBox
            {
                AutoSize = true,
                Name = "EnableProcessOptimizerCB",
                Text = "Enable Process Optimizer"
            };
            EnableProcessOptimizerCB.CheckedChanged += EnableProcessOptimizerCB_CheckedChanged;

            RobloxPriorityLabel = new Label
            {
                AutoSize = true,
                Text = "Roblox Priority"
            };

            RobloxPriorityCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 120,
                Name = "RobloxPriorityCombo"
            };
            RobloxPriorityCombo.Items.AddRange(new object[] { "Idle", "BelowNormal", "Normal", "AboveNormal", "High" });
            RobloxPriorityCombo.SelectedIndexChanged += RobloxPriorityCombo_SelectedIndexChanged;

            RobloxIoPriorityLabel = new Label
            {
                AutoSize = true,
                Text = "Roblox I/O Priority"
            };

            RobloxIoPriorityCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 120,
                Name = "RobloxIoPriorityCombo"
            };
            RobloxIoPriorityCombo.Items.AddRange(new object[] { "VeryLow", "Low", "Normal" });
            RobloxIoPriorityCombo.SelectedIndexChanged += RobloxIoPriorityCombo_SelectedIndexChanged;

            RobloxAffinityMaskLabel = new Label
            {
                AutoSize = true,
                Text = "Roblox Affinity Mask (hex)"
            };

            RobloxAffinityMaskTB = new TextBox
            {
                Name = "RobloxAffinityMaskTB",
                Width = 269,
                Text = ""
            };
            RobloxAffinityMaskTB.TextChanged += RobloxAffinityMaskTB_TextChanged;

            RaiseManagerPriorityCB = new CheckBox
            {
                AutoSize = true,
                Name = "RaiseManagerPriorityCB",
                Text = "Keep RAMV2 Priority Elevated"
            };
            RaiseManagerPriorityCB.CheckedChanged += RaiseManagerPriorityCB_CheckedChanged;

            ManagerPriorityLabel = new Label
            {
                AutoSize = true,
                Text = "RAMV2 Priority"
            };

            ManagerPriorityCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 120,
                Name = "ManagerPriorityCombo"
            };
            ManagerPriorityCombo.Items.AddRange(new object[] { "Normal", "AboveNormal", "High" });
            ManagerPriorityCombo.SelectedIndexChanged += ManagerPriorityCombo_SelectedIndexChanged;

            ProcessLassoPathLabel = new Label
            {
                AutoSize = true,
                Text = "Process Lasso Path"
            };

            ProcessLassoPathTB = new TextBox
            {
                Name = "ProcessLassoPathTB",
                Width = 269
            };
            ProcessLassoPathTB.TextChanged += ProcessLassoPathTB_TextChanged;

            OpenProcessLassoButton = new Button
            {
                Name = "OpenProcessLassoButton",
                Text = "Open Process Lasso",
                Width = 269
            };
            OpenProcessLassoButton.Click += OpenProcessLassoButton_Click;

            RAMMapPathLabel = new Label
            {
                AutoSize = true,
                Text = "RAMMap Path"
            };

            RAMMapPathTB = new TextBox
            {
                Name = "RAMMapPathTB",
                Width = 269
            };
            RAMMapPathTB.TextChanged += RAMMapPathTB_TextChanged;

            OpenRAMMapButton = new Button
            {
                Name = "OpenRAMMapButton",
                Text = "Open RAMMap",
                Width = 269
            };
            OpenRAMMapButton.Click += OpenRAMMapButton_Click;

            CustomRobloxPathLabel = new Label
            {
                AutoSize = true,
                Text = "Custom Roblox Install Path"
            };

            CustomRobloxPathTB = new TextBox
            {
                Name = "CustomRobloxPathTB",
                Width = 269
            };
            CustomRobloxPathTB.TextChanged += CustomRobloxPathTB_TextChanged;

            BrowseCustomRobloxPathButton = new Button
            {
                Name = "BrowseCustomRobloxPathButton",
                Text = "Browse Roblox Folder",
                Width = 131
            };
            BrowseCustomRobloxPathButton.Click += BrowseCustomRobloxPathButton_Click;

            ClearCustomRobloxPathButton = new Button
            {
                Name = "ClearCustomRobloxPathButton",
                Text = "Use Default Path",
                Width = 131
            };
            ClearCustomRobloxPathButton.Click += ClearCustomRobloxPathButton_Click;

            MemoryMonitorLabel = new Label
            {
                AutoSize = true,
                Name = "MemoryMonitorLabel",
                Width = 269
            };

            PerformanceMonitorTimer = new Timer { Interval = 2000 };
            PerformanceMonitorTimer.Tick += (s, e) => UpdateMemoryMonitor();

            Helper.SetToolTip(EnableProcessOptimizerCB, "Applies selected priority, I/O priority and affinity mask to RobloxPlayerBeta processes.");
            Helper.SetToolTip(RobloxAffinityMaskTB, "Example: FF (cores 1-8), F0, 0x3F. Leave empty to use all cores.");
            Helper.SetToolTip(OpenProcessLassoButton, "Open configured Process Lasso path, or its official site if missing.");
            Helper.SetToolTip(OpenRAMMapButton, "Open configured RAMMap path, or Microsoft docs if missing.");
            Helper.SetToolTip(CustomRobloxPathLabel, "Optional. Point RAMV2 at a custom Roblox version folder, Roblox root folder, or paste RobloxPlayerBeta.exe into the box below.");
            Helper.SetToolTip(CustomRobloxPathTB, "Examples: C:\\Users\\you\\AppData\\Local\\Roblox\\Versions\\version-xxx or a direct RobloxPlayerBeta.exe path.");
            Helper.SetToolTip(BrowseCustomRobloxPathButton, "Choose a Roblox install folder. RAMV2 will launch from this path instead of the default protocol handler.");
            Helper.SetToolTip(ClearCustomRobloxPathButton, "Clear the custom path and go back to the normal Roblox install lookup.");

            MiscellaneousFlowPanel.Controls.Remove(ForceUpdateButton);

            MiscellaneousFlowPanel.Controls.Add(EnableProcessOptimizerCB);
            MiscellaneousFlowPanel.SetFlowBreak(EnableProcessOptimizerCB, true);
            MiscellaneousFlowPanel.Controls.Add(RobloxPriorityLabel);
            MiscellaneousFlowPanel.Controls.Add(RobloxPriorityCombo);
            MiscellaneousFlowPanel.SetFlowBreak(RobloxPriorityCombo, true);
            MiscellaneousFlowPanel.Controls.Add(RobloxIoPriorityLabel);
            MiscellaneousFlowPanel.Controls.Add(RobloxIoPriorityCombo);
            MiscellaneousFlowPanel.SetFlowBreak(RobloxIoPriorityCombo, true);
            MiscellaneousFlowPanel.Controls.Add(RobloxAffinityMaskLabel);
            MiscellaneousFlowPanel.SetFlowBreak(RobloxAffinityMaskLabel, true);
            MiscellaneousFlowPanel.Controls.Add(RobloxAffinityMaskTB);
            MiscellaneousFlowPanel.SetFlowBreak(RobloxAffinityMaskTB, true);
            MiscellaneousFlowPanel.Controls.Add(RaiseManagerPriorityCB);
            MiscellaneousFlowPanel.SetFlowBreak(RaiseManagerPriorityCB, true);
            MiscellaneousFlowPanel.Controls.Add(ManagerPriorityLabel);
            MiscellaneousFlowPanel.Controls.Add(ManagerPriorityCombo);
            MiscellaneousFlowPanel.SetFlowBreak(ManagerPriorityCombo, true);
            MiscellaneousFlowPanel.Controls.Add(ProcessLassoPathLabel);
            MiscellaneousFlowPanel.SetFlowBreak(ProcessLassoPathLabel, true);
            MiscellaneousFlowPanel.Controls.Add(ProcessLassoPathTB);
            MiscellaneousFlowPanel.SetFlowBreak(ProcessLassoPathTB, true);
            MiscellaneousFlowPanel.Controls.Add(OpenProcessLassoButton);
            MiscellaneousFlowPanel.SetFlowBreak(OpenProcessLassoButton, true);
            MiscellaneousFlowPanel.Controls.Add(RAMMapPathLabel);
            MiscellaneousFlowPanel.SetFlowBreak(RAMMapPathLabel, true);
            MiscellaneousFlowPanel.Controls.Add(RAMMapPathTB);
            MiscellaneousFlowPanel.SetFlowBreak(RAMMapPathTB, true);
            MiscellaneousFlowPanel.Controls.Add(OpenRAMMapButton);
            MiscellaneousFlowPanel.SetFlowBreak(OpenRAMMapButton, true);
            MiscellaneousFlowPanel.Controls.Add(CustomRobloxPathLabel);
            MiscellaneousFlowPanel.SetFlowBreak(CustomRobloxPathLabel, true);
            MiscellaneousFlowPanel.Controls.Add(CustomRobloxPathTB);
            MiscellaneousFlowPanel.SetFlowBreak(CustomRobloxPathTB, true);
            MiscellaneousFlowPanel.Controls.Add(BrowseCustomRobloxPathButton);
            MiscellaneousFlowPanel.Controls.Add(ClearCustomRobloxPathButton);
            MiscellaneousFlowPanel.SetFlowBreak(ClearCustomRobloxPathButton, true);
            MiscellaneousFlowPanel.Controls.Add(MemoryMonitorLabel);
            MiscellaneousFlowPanel.SetFlowBreak(MemoryMonitorLabel, true);

            MiscellaneousFlowPanel.Controls.Add(ForceUpdateButton);
            MiscellaneousFlowPanel.SetFlowBreak(ForceUpdateButton, true);
        }

        private static void SetComboValue(ComboBox comboBox, string value, string fallback)
        {
            string target = string.IsNullOrWhiteSpace(value) ? fallback : value;

            foreach (var item in comboBox.Items)
            {
                if (string.Equals(item?.ToString(), target, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        private static string FormatMb(long bytes) => $"{bytes / (1024d * 1024d):N0} MB";

        private void UpdateMemoryMonitor()
        {
            try
            {
                int robloxCount = 0;
                long robloxWorkingSet = 0;
                long robloxPrivate = 0;

                foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    try
                    {
                        robloxCount++;
                        robloxWorkingSet += process.WorkingSet64;
                        robloxPrivate += process.PrivateMemorySize64;
                    }
                    catch { }
                    finally { process.Dispose(); }
                }

                long managerWorkingSet = 0;
                using (var current = Process.GetCurrentProcess())
                    managerWorkingSet = current.WorkingSet64;

                MEMORYSTATUSEX memoryStatus = new MEMORYSTATUSEX
                {
                    dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX))
                };

                ulong total = 0;
                ulong available = 0;
                uint loadPercent = 0;
                if (GlobalMemoryStatusEx(ref memoryStatus))
                {
                    total = memoryStatus.ullTotalPhys;
                    available = memoryStatus.ullAvailPhys;
                    loadPercent = memoryStatus.dwMemoryLoad;
                }

                MemoryMonitorLabel.Text =
                    $"Roblox Instances: {robloxCount}\n" +
                    $"Roblox Working Set: {FormatMb(robloxWorkingSet)}\n" +
                    $"Roblox Private: {FormatMb(robloxPrivate)}\n" +
                    $"RAMV2 Working Set: {FormatMb(managerWorkingSet)}\n" +
                    $"System RAM: {FormatMb((long)available)} free / {FormatMb((long)total)} total ({loadPercent}% used)";
            }
            catch
            {
                MemoryMonitorLabel.Text = "Memory monitor unavailable.";
            }
        }

        private static void OpenToolOrWebsite(string configuredPath, string fallbackUrl)
        {
            string path = (configuredPath ?? string.Empty).Trim().Trim('"');

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            Process.Start(fallbackUrl);
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            AutoUpdateCB.Checked = AccountManager.General.Get<bool>("CheckForUpdates");
            AutoUpdateCB.Enabled = true;
            ForceUpdateButton.Enabled = true;
            ForceUpdateButton.Visible = true;
            ForceUpdateButton.Text = "Update From Custom Release";
            AsyncJoinCB.Checked = AccountManager.General.Get<bool>("AsyncJoin");
            LaunchDelayNumber.Value = AccountManager.General.Get<decimal>("AccountJoinDelay");
            SavePasswordCB.Checked = AccountManager.General.Get<bool>("SavePasswords");
            DisableAgingAlertCB.Checked = AccountManager.General.Get<bool>("DisableAgingAlert");
            HideMRobloxCB.Checked = AccountManager.General.Get<bool>("HideRbxAlert");
            DisableImagesCB.Checked = AccountManager.General.Get<bool>("DisableImages");
            ShuffleLowestServerCB.Checked = AccountManager.General.Get<bool>("ShuffleChoosesLowestServer");
            MultiRobloxCB.Checked = AccountManager.General.Get<bool>("EnableMultiRbx");
            RegionFormatTB.Text = AccountManager.General.Get<string>("ServerRegionFormat");
            MaxRecentGamesNumber.Value = AccountManager.General.Get<int>("MaxRecentGames");
            AutoRejoinCB.Checked = AccountManager.General.Get<bool>("AutoRejoin");

            EnableDMCB.Checked = AccountManager.Developer.Get<bool>("DevMode");
            EnableWSCB.Checked = AccountManager.Developer.Get<bool>("EnableWebServer");
            ERRPCB.Checked = AccountManager.WebServer.Get<bool>("EveryRequestRequiresPassword");
            AllowGCCB.Checked = AccountManager.WebServer.Get<bool>("AllowGetCookie");
            AllowGACB.Checked = AccountManager.WebServer.Get<bool>("AllowGetAccounts");
            AllowLACB.Checked = AccountManager.WebServer.Get<bool>("AllowLaunchAccount");
            AllowAECB.Checked = AccountManager.WebServer.Get<bool>("AllowAccountEditing");
            AllowExternalConnectionsCB.Checked = AccountManager.WebServer.Get<bool>("AllowExternalConnections");
            PasswordTextBox.Text = AccountManager.WebServer.Get("Password");
            PortNumber.Value = AccountManager.WebServer.Get<decimal>("WebServerPort");

            PresenceCB.Checked = AccountManager.General.Get<bool>("ShowPresence");
            PresenceUpdateRateNum .Value = AccountManager.General.Get<int>("PresenceUpdateRate");
            UnlockFPSCB.Checked = AccountManager.General.Get<bool>("UnlockFPS");
            MaxFPSValue.Value = AccountManager.General.Get<int>("MaxFPSValue");
            EnableProcessOptimizerCB.Checked = AccountManager.General.Get<bool>("EnableProcessOptimizer");
            SetComboValue(RobloxPriorityCombo, AccountManager.General.Get("RobloxPriority"), "BelowNormal");
            SetComboValue(RobloxIoPriorityCombo, AccountManager.General.Get("RobloxIoPriority"), "Low");
            RobloxAffinityMaskTB.Text = AccountManager.General.Get("RobloxAffinityMask");
            RaiseManagerPriorityCB.Checked = AccountManager.General.Get<bool>("RaiseManagerPriority");
            SetComboValue(ManagerPriorityCombo, AccountManager.General.Get("ManagerPriority"), "AboveNormal");
            ProcessLassoPathTB.Text = AccountManager.General.Get("ProcessLassoPath");
            RAMMapPathTB.Text = AccountManager.General.Get("RAMMapPath");
            CustomRobloxPathTB.Text = AccountManager.General.Get("CustomRobloxInstallPath");
            UpdateMemoryMonitor();
            PerformanceMonitorTimer.Start();

            if (AccountManager.General.Exists("CustomClientSettings") && File.Exists(AccountManager.General.Get<string>("CustomClientSettings")))
            {
                OverrideWithCustomCB.Checked = true;
                UnlockFPSCB.Enabled = false;
            }

            try { StartupKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true); } catch { }

            if (StartupKey != null && StartupKey.GetValue(Application.ProductName) is string ExistingPath)
            {
                if (ExistingPath != Application.ExecutablePath) // fix the path if moved
                    StartupKey.SetValue(Application.ProductName, Application.ExecutablePath);

                StartOnPCStartup.Checked = true;
            }

            SettingsLoaded = true;

            ApplyTheme();
        }

        private void SettingsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        #region General

        private void AutoUpdateCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("CheckForUpdates", AutoUpdateCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void AsyncJoinCB_CheckedChanged(object sender, EventArgs e)
        {
            LaunchDelayNumber.Enabled = !AsyncJoinCB.Checked;

            if (!SettingsLoaded) return;

            AccountManager.General.Set("AsyncJoin", AsyncJoinCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void LaunchDelayNumber_ValueChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("AccountJoinDelay", LaunchDelayNumber.Value.ToString());
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void MaxRecentGamesNumber_ValueChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("MaxRecentGames", MaxRecentGamesNumber.Value.ToString());
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void SavePasswordCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("SavePasswords", SavePasswordCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void DisableAgingAlertCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("DisableAgingAlert", DisableAgingAlertCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void HideMRobloxCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("HideRbxAlert", HideMRobloxCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void RegionFormatTB_TextChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("ServerRegionFormat", RegionFormatTB.Text, "Visit http://ip-api.com/json to see available format options");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void DisableImagesCB_CheckedChanged(object sender, EventArgs e)
        {
            AccountManager.General.Set("DisableImages", DisableImagesCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void ShuffleLowestServerCB_CheckedChanged(object sender, EventArgs e)
        {
            AccountManager.General.Set("ShuffleChoosesLowestServer", ShuffleLowestServerCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void MultiRobloxCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("EnableMultiRbx", MultiRobloxCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");

            if (!AccountManager.Instance.UpdateMultiRoblox())
                MessageBox.Show("Multi Roblox could not be enabled right now.\nRAM will retry automatically when you launch multiple accounts.\nIf it keeps failing, run RAM as admin once and toggle this setting again.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void AutoCookieRefreshCB_CheckedChanged(object sender, EventArgs e)
        {
            AccountManager.General.Set("AutoCookieRefresh", AutoCookieRefreshCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");

            if (AccountManager.Instance.AutoCookieRefresh != null)
                AccountManager.Instance.AutoCookieRefresh.Enabled = AutoCookieRefreshCB.Checked;
        }

        private void AutoRejoinCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("AutoRejoin", AutoRejoinCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void StartOnPCStartup_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            if (StartOnPCStartup.Checked)
                StartupKey?.SetValue(Application.ProductName, Application.ExecutablePath);
            else
                StartupKey?.DeleteValue(Application.ProductName);
        }

        private void EncryptionSelectionButton_Click(object sender, EventArgs e)
        {
            if (Utilities.YesNoPrompt("Settings", "Change Encryption Method", "Are you sure you want to change how your data is encrypted?", false))
                AccountManager.Instance.ResetEncryption(true);
        }

        #endregion

        #region Developer

        private void EnableDMCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.Developer.Set("DevMode", EnableDMCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void EnableWSCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.Developer.Set("EnableWebServer", EnableWSCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");

            MessageBox.Show("Roblox Account Manager must be restarted to enable this setting", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ERRPCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.WebServer.Set("EveryRequestRequiresPassword", ERRPCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void AllowGCCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.WebServer.Set("AllowGetCookie", AllowGCCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void AllowGACB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.WebServer.Set("AllowGetAccounts", AllowGACB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void AllowLACB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.WebServer.Set("AllowLaunchAccount", AllowLACB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void AllowAECB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.WebServer.Set("AllowAccountEditing", AllowAECB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void AllowExternalConnectionsCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.WebServer.Set("AllowExternalConnections", AllowExternalConnectionsCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");

            MessageBox.Show("Roblox Account Manager must be restarted to enable this setting\n\nThis setting requires admin privileges", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void PortNumber_ValueChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.WebServer.Set("WebServerPort", PortNumber.Value.ToString());
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void PasswordTextBox_TextChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            PasswordTextBox.Text = Regex.Replace(PasswordTextBox.Text, "[^0-9a-zA-Z ]", "");

            AccountManager.WebServer.Set("Password", PasswordTextBox.Text);
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        #endregion

        #region Miscellaneous

        private void PresenceCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("ShowPresence", PresenceCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void PresenceUpdateRateNum_ValueChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("PresenceUpdateRate", PresenceUpdateRateNum.Value.ToString());
            AccountManager.IniSettings.Save("RAMSettings.ini");
            AccountManager.Instance?.UpdatePresenceTimerInterval();
        }

        private void UnlockFPSCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("UnlockFPS", UnlockFPSCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void MaxFPSValue_ValueChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("MaxFPSValue", MaxFPSValue.Value.ToString());
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void EnableProcessOptimizerCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("EnableProcessOptimizer", EnableProcessOptimizerCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
            AccountManager.Instance?.ApplyProcessOptimizationNow();
        }

        private void RobloxPriorityCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("RobloxPriority", RobloxPriorityCombo.SelectedItem?.ToString() ?? "BelowNormal");
            AccountManager.IniSettings.Save("RAMSettings.ini");
            AccountManager.Instance?.ApplyProcessOptimizationNow();
        }

        private void RobloxIoPriorityCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("RobloxIoPriority", RobloxIoPriorityCombo.SelectedItem?.ToString() ?? "Low");
            AccountManager.IniSettings.Save("RAMSettings.ini");
            AccountManager.Instance?.ApplyProcessOptimizationNow();
        }

        private void RobloxAffinityMaskTB_TextChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("RobloxAffinityMask", (RobloxAffinityMaskTB.Text ?? string.Empty).Trim());
            AccountManager.IniSettings.Save("RAMSettings.ini");
            AccountManager.Instance?.ApplyProcessOptimizationNow();
        }

        private void RaiseManagerPriorityCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("RaiseManagerPriority", RaiseManagerPriorityCB.Checked ? "true" : "false");
            AccountManager.IniSettings.Save("RAMSettings.ini");
            AccountManager.Instance?.ApplyProcessOptimizationNow();
        }

        private void ManagerPriorityCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("ManagerPriority", ManagerPriorityCombo.SelectedItem?.ToString() ?? "AboveNormal");
            AccountManager.IniSettings.Save("RAMSettings.ini");
            AccountManager.Instance?.ApplyProcessOptimizationNow();
        }

        private void ProcessLassoPathTB_TextChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("ProcessLassoPath", ProcessLassoPathTB.Text ?? string.Empty);
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void RAMMapPathTB_TextChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("RAMMapPath", RAMMapPathTB.Text ?? string.Empty);
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void CustomRobloxPathTB_TextChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            AccountManager.General.Set("CustomRobloxInstallPath", (CustomRobloxPathTB.Text ?? string.Empty).Trim());
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void OpenProcessLassoButton_Click(object sender, EventArgs e)
        {
            OpenToolOrWebsite(ProcessLassoPathTB.Text, "https://bitsum.com/");
        }

        private void OpenRAMMapButton_Click(object sender, EventArgs e)
        {
            OpenToolOrWebsite(RAMMapPathTB.Text, "https://learn.microsoft.com/en-us/sysinternals/downloads/rammap");
        }

        private void BrowseCustomRobloxPathButton_Click(object sender, EventArgs e)
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "Select the Roblox version folder or Roblox install root."
            };

            string currentPath = (CustomRobloxPathTB.Text ?? string.Empty).Trim().Trim('"');
            if (File.Exists(currentPath))
                currentPath = Path.GetDirectoryName(currentPath) ?? string.Empty;

            if (Directory.Exists(currentPath))
                dialog.SelectedPath = currentPath;

            if (dialog.ShowDialog(this) == DialogResult.OK)
                CustomRobloxPathTB.Text = dialog.SelectedPath;
        }

        private void ClearCustomRobloxPathButton_Click(object sender, EventArgs e)
        {
            CustomRobloxPathTB.Text = string.Empty;
        }

        private void OverrideWithCustomCB_CheckedChanged(object sender, EventArgs e)
        {
            if (!SettingsLoaded) return;

            UnlockFPSCB.Enabled = !OverrideWithCustomCB.Checked;

            void Remove()
            {
                AccountManager.General.RemoveProperty("CustomClientSettings");
                OverrideWithCustomCB.Checked = false;
            }

            if (OverrideWithCustomCB.Checked)
            {
                if (CustomClientSettingsDialog.ShowDialog() == DialogResult.OK)
                {
                    if (File.Exists(CustomClientSettingsDialog.FileName) && File.ReadAllText(CustomClientSettingsDialog.FileName).TryParseJson<object>(out _))
                    {
                        string FileName = Path.Combine(Environment.CurrentDirectory, "CustomClientAppSettings.json");

                        File.Copy(CustomClientSettingsDialog.FileName, FileName);
                        AccountManager.General.Set("CustomClientSettings", FileName);
                    }
                    else
                        MessageBox.Show("Invalid file selected, make sure it contains valid JSON", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                    Remove();
            }
            else
                Remove();
            
            AccountManager.IniSettings.Save("RAMSettings.ini");
        }

        private void ForceUpdateButton_Click(object sender, EventArgs e)
        {
            try
            {
                using (Auto_Update.AutoUpdater Updater = new Auto_Update.AutoUpdater())
                    Updater.ShowDialog(this);
            }
            catch (Exception x)
            {
                MessageBox.Show($"Failed to launch updater.\n\n{x.Message}", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Themes

        public void ApplyTheme()
        {
            BackColor = ThemeEditor.FormsBackground;
            ForeColor = ThemeEditor.FormsForeground;

            ApplyTheme(Controls);
        }

        public void ApplyTheme(Control.ControlCollection _Controls)
        {
            foreach (Control control in _Controls)
            {
                if (control is Button || control is CheckBox)
                {
                    if (control is Button)
                    {
                        Button b = control as Button;
                        b.FlatStyle = ThemeEditor.ButtonStyle;
                        b.FlatAppearance.BorderColor = ThemeEditor.ButtonsBorder;
                    }

                    if (!(control is CheckBox)) control.BackColor = ThemeEditor.ButtonsBackground;
                    control.ForeColor = ThemeEditor.ButtonsForeground;
                }
                else if (control is TextBox || control is RichTextBox)
                {
                    if (control is Classes.BorderedTextBox)
                    {
                        Classes.BorderedTextBox b = control as Classes.BorderedTextBox;
                        b.BorderColor = ThemeEditor.TextBoxesBorder;
                    }

                    if (control is Classes.BorderedRichTextBox)
                    {
                        Classes.BorderedRichTextBox b = control as Classes.BorderedRichTextBox;
                        b.BorderColor = ThemeEditor.TextBoxesBorder;
                    }

                    control.BackColor = ThemeEditor.TextBoxesBackground;
                    control.ForeColor = ThemeEditor.TextBoxesForeground;
                }
                else if (control is Label)
                {
                    control.BackColor = ThemeEditor.LabelTransparent ? Color.Transparent : ThemeEditor.LabelBackground;
                    control.ForeColor = ThemeEditor.LabelForeground;
                }
                else if (control is ListBox)
                {
                    control.BackColor = ThemeEditor.ButtonsBackground;
                    control.ForeColor = ThemeEditor.ButtonsForeground;
                }
                else if (control is TabPage)
                {
                    ApplyTheme(control.Controls);

                    control.BackColor = ThemeEditor.ButtonsBackground;
                    control.ForeColor = ThemeEditor.ButtonsForeground;
                }
                else if (control is FastColoredTextBoxNS.FastColoredTextBox)
                    control.ForeColor = Color.Black;
                else if (control is FlowLayoutPanel || control is Panel || control is TabControl)
                    ApplyTheme(control.Controls);
            }
        }

        #endregion
    }
}
