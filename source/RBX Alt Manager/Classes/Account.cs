using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RBX_Alt_Manager.Classes;
using RBX_Alt_Manager.Forms;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

namespace RBX_Alt_Manager
{
    public class Account : IComparable<Account>
    {
        public bool Valid;
        public string SecurityToken;
        public string Username;
        public DateTime LastUse;
        private string _Alias = "";
        private string _Description = "";
        private string _Password = "";
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string Group { get; set; } = "Default";
        public long UserID;
        public Dictionary<string, string> Fields = new Dictionary<string, string>();
        public DateTime LastAttemptedRefresh;
        [JsonIgnore] public DateTime PinUnlocked;
        [JsonIgnore] public DateTime TokenSet;
        [JsonIgnore] public DateTime LastAppLaunch;
        [JsonIgnore] public string CSRFToken;
        [JsonIgnore] public UserPresence Presence;
        [JsonIgnore] public bool HasOpenInstance;
        [JsonIgnore] public bool IsOnServer;
        [JsonIgnore] public long? CurrentPlaceId;
        [JsonIgnore] public string CurrentGameName = string.Empty;
        [JsonIgnore] public DateTime LastLiveStatusUpdateUtc = DateTime.MinValue;
        [JsonIgnore] public int CurrentProcessId;
        [JsonIgnore] private int LastTinyLaunchSlot = -1;
        [JsonIgnore] private int PendingTinyLaunchIgnoredProcessId = 0;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("psapi.dll", SetLastError = true)]
        static extern bool EmptyWorkingSet(IntPtr hProcess);

        private const int TinyLaunchPosX = 0;
        private const int TinyLaunchPosY = 0;
        private const int TinyLaunchWidth = 96;
        private const int TinyLaunchHeight = 54;
        private const int TinyLaunchGapX = 2;
        private const int TinyLaunchGapY = 2;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly object TinyLaunchSlotLock = new object();
        private static readonly HashSet<int> PendingTinyLaunchSlots = new HashSet<int>();

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static int GetWindowStyle(IntPtr handle)
        {
            if (IntPtr.Size == 8)
                return (int)GetWindowLongPtr64(handle, GWL_STYLE).ToInt64();

            return GetWindowLong32(handle, GWL_STYLE);
        }

        private static void SetWindowStyle(IntPtr handle, int style)
        {
            if (IntPtr.Size == 8)
                _ = SetWindowLongPtr64(handle, GWL_STYLE, new IntPtr(style));
            else
                _ = SetWindowLong32(handle, GWL_STYLE, style);
        }

        private static bool TryGetProcessMainWindowHandle(Process process, out IntPtr mainWindowHandle)
        {
            mainWindowHandle = IntPtr.Zero;

            if (process == null)
                return false;

            try
            {
                if (process.HasExited)
                    return false;

                mainWindowHandle = process.MainWindowHandle;
                return mainWindowHandle != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetProcessStartTimeUtc(Process process, out DateTime startTimeUtc)
        {
            startTimeUtc = DateTime.MinValue;

            if (process == null)
                return false;

            try
            {
                startTimeUtc = process.StartTime.ToUniversalTime();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static (Rectangle WorkingArea, int CellWidth, int CellHeight, int Columns, int Rows, int TotalSlots) GetTinyLaunchGridLayout()
        {
            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea
                ?? Screen.AllScreens.FirstOrDefault()?.WorkingArea
                ?? new Rectangle(TinyLaunchPosX, TinyLaunchPosY, TinyLaunchWidth, TinyLaunchHeight);

            int cellWidth = TinyLaunchWidth + TinyLaunchGapX;
            int cellHeight = TinyLaunchHeight + TinyLaunchGapY;
            int columns = Math.Max(1, (Math.Max(workingArea.Width, TinyLaunchWidth) + TinyLaunchGapX) / cellWidth);
            int rows = Math.Max(1, (Math.Max(workingArea.Height, TinyLaunchHeight) + TinyLaunchGapY) / cellHeight);
            int totalSlots = Math.Max(1, columns * rows);

            return (workingArea, cellWidth, cellHeight, columns, rows, totalSlots);
        }

        private static bool TryGetTinyLaunchSlotFromRect(RECT rect, out int slot)
        {
            slot = -1;

            Rectangle bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var layout = GetTinyLaunchGridLayout();
            int originX = layout.WorkingArea.Left + TinyLaunchPosX;
            int originY = layout.WorkingArea.Top + TinyLaunchPosY;
            double relativeX = bounds.Left - originX;
            double relativeY = bounds.Top - originY;
            int column = (int)Math.Round(relativeX / Math.Max(1d, layout.CellWidth));
            int row = (int)Math.Round(relativeY / Math.Max(1d, layout.CellHeight));

            if (column < 0 || column >= layout.Columns || row < 0 || row >= layout.Rows)
                return false;

            int expectedX = originX + (column * layout.CellWidth);
            int expectedY = originY + (row * layout.CellHeight);
            int toleranceX = Math.Max(8, TinyLaunchGapX + 6);
            int toleranceY = Math.Max(8, TinyLaunchGapY + 6);

            if (Math.Abs(bounds.Left - expectedX) > toleranceX || Math.Abs(bounds.Top - expectedY) > toleranceY)
                return false;

            slot = (row * layout.Columns) + column;
            return slot >= 0 && slot < layout.TotalSlots;
        }

        private static int AcquireTinyLaunchSlot(int ignoredProcessId = 0, int preferredSlot = -1)
        {
            lock (TinyLaunchSlotLock)
            {
                var layout = GetTinyLaunchGridLayout();
                HashSet<int> occupiedSlots = new HashSet<int>(PendingTinyLaunchSlots);
                Process[] processSnapshot;

                try { processSnapshot = Process.GetProcessesByName("RobloxPlayerBeta"); }
                catch { processSnapshot = Array.Empty<Process>(); }

                foreach (Process process in processSnapshot)
                {
                    try
                    {
                        int processId = -1;
                        try { processId = process.Id; } catch { }
                        if (ignoredProcessId > 0 && processId == ignoredProcessId)
                            continue;

                        if (!TryGetProcessMainWindowHandle(process, out IntPtr mainWindowHandle))
                            continue;

                        if (GetWindowRect(mainWindowHandle, out RECT rect) && TryGetTinyLaunchSlotFromRect(rect, out int liveSlot))
                            occupiedSlots.Add(liveSlot);
                    }
                    catch { }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }

                if (preferredSlot >= 0 && preferredSlot < layout.TotalSlots && !occupiedSlots.Contains(preferredSlot))
                {
                    PendingTinyLaunchSlots.Add(preferredSlot);
                    return preferredSlot;
                }

                for (int slot = 0; slot < layout.TotalSlots; slot++)
                {
                    if (occupiedSlots.Contains(slot))
                        continue;

                    PendingTinyLaunchSlots.Add(slot);
                    return slot;
                }

                int fallbackSlot = 0;
                PendingTinyLaunchSlots.Add(fallbackSlot);
                return fallbackSlot;
            }
        }

        private static void ReleaseTinyLaunchSlot(int slot)
        {
            lock (TinyLaunchSlotLock)
            {
                PendingTinyLaunchSlots.Remove(slot);
            }
        }

        private static (int PosX, int PosY) GetTinyLaunchGridPosition(int slot)
        {
            var layout = GetTinyLaunchGridLayout();
            int normalizedSlot = slot % layout.TotalSlots;
            int column = normalizedSlot % layout.Columns;
            int row = normalizedSlot / layout.Columns;
            int posX = layout.WorkingArea.Left + TinyLaunchPosX + (column * layout.CellWidth);
            int posY = layout.WorkingArea.Top + TinyLaunchPosY + (row * layout.CellHeight);

            return (posX, posY);
        }

        private int FindTrackedRobloxProcessId()
        {
            if (string.IsNullOrWhiteSpace(BrowserTrackerID))
                return 0;

            try
            {
                foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    try
                    {
                        string commandLine = process.GetCommandLine() ?? string.Empty;
                        if (commandLine.IndexOf(BrowserTrackerID, StringComparison.OrdinalIgnoreCase) >= 0)
                            return process.Id;
                    }
                    catch { }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }
            catch { }

            return 0;
        }

        public void RememberTinyLaunchSlotFromProcess(int processId = 0)
        {
            int targetPid = processId > 0 ? processId : CurrentProcessId;
            if (targetPid <= 0)
                return;

            try
            {
                using (Process process = Process.GetProcessById(targetPid))
                {
                    if (!TryGetProcessMainWindowHandle(process, out IntPtr mainWindowHandle))
                        return;

                    if (GetWindowRect(mainWindowHandle, out RECT rect) && TryGetTinyLaunchSlotFromRect(rect, out int liveSlot))
                        LastTinyLaunchSlot = liveSlot;
                }
            }
            catch { }
        }

        public void SetPendingTinyLaunchIgnoredProcessId(int processId)
        {
            PendingTinyLaunchIgnoredProcessId = processId > 0 ? processId : 0;
        }

        private int GetTinyLaunchIgnoredProcessId()
        {
            if (CurrentProcessId > 0)
                return CurrentProcessId;

            if (PendingTinyLaunchIgnoredProcessId > 0)
                return PendingTinyLaunchIgnoredProcessId;

            return FindTrackedRobloxProcessId();
        }

        private void ClearPendingTinyLaunchIgnoredProcessId(int processId)
        {
            if (processId > 0 && PendingTinyLaunchIgnoredProcessId == processId)
                PendingTinyLaunchIgnoredProcessId = 0;
        }

        private bool HasFreshPlaceJoinSignal(long expectedPlaceId, int matchedProcessId, int previousProcessId)
        {
            if (expectedPlaceId <= 0)
                return true;

            if (!CurrentPlaceId.HasValue || CurrentPlaceId.Value != expectedPlaceId)
                return false;

            if (LastAppLaunch == DateTime.MinValue)
                return true;

            if (CurrentProcessId > 0)
            {
                if (matchedProcessId > 0 && CurrentProcessId == matchedProcessId)
                    return true;

                if (previousProcessId > 0 && CurrentProcessId != previousProcessId)
                    return true;
            }

            if (LastLiveStatusUpdateUtc == DateTime.MinValue)
                return false;

            return LastLiveStatusUpdateUtc >= LastAppLaunch.AddSeconds(-1);
        }

        private static int GetSecureRandomNumber(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
                return minInclusive;

            byte[] bytes = new byte[4];
            uint range = (uint)(maxExclusive - minInclusive);
            uint limit = uint.MaxValue - (uint.MaxValue % range);
            uint value;

            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                do
                {
                    generator.GetBytes(bytes);
                    value = BitConverter.ToUInt32(bytes, 0);
                } while (value >= limit);
            }

            return (int)(minInclusive + (value % range));
        }

        public int CompareTo(Account compareTo)
        {
            if (compareTo == null)
                return 1;
            else
                return Group.CompareTo(compareTo.Group);
        }

        public string BrowserTrackerID;

        public string Alias
        {
            get => _Alias;
            set
            {
                if (value == null || value.Length > 50)
                    return;

                _Alias = value;
                AccountManager.SaveAccounts();
            }
        }
        public string Description
        {
            get => _Description;
            set
            {
                if (value == null || value.Length > 5000)
                    return;

                _Description = value;
                AccountManager.SaveAccounts();
            }
        }
        public string Password
        {
            get => _Password;
            set
            {
                if (value == null || value.Length > 5000)
                    return;

                _Password = value;
                AccountManager.SaveAccounts();
            }
        }

        public Account() { }

        public Account(string Cookie, string AccountJSON = null)
        {
            SecurityToken = Cookie;
            
            AccountJSON ??= AccountManager.MainClient.Execute(MakeRequest("my/account/json", Method.Get)).Content;

            if (!string.IsNullOrEmpty(AccountJSON) && Utilities.TryParseJson(AccountJSON, out AccountJson Data))
            {
                Username = Data.Name;
                UserID = Data.UserId;

                Valid = true;

                LastUse = DateTime.Now;

                AccountManager.LastValidAccount = this;
            }
        }

        public RestRequest MakeRequest(string url, Method method = Method.Get)
        {
            return new RestRequest(url, method).AddCookie(".ROBLOSECURITY", SecurityToken, "/", ".roblox.com");
        }

        public bool GetAuthTicket(out string Ticket, string CSRFToken = null)
        {
            Ticket = string.Empty;

            string Token = CSRFToken;

            if (string.IsNullOrEmpty(Token) && !GetCSRFToken(out Token))
                return false;

            RestRequest request = MakeRequest("/v1/authentication-ticket/", Method.Post)
                .AddHeader("x-csrf-token", Token)
                .AddHeader("Referer", "https://www.roblox.com/games/2753915549/Blox-Fruits")
                .AddJsonBody(string.Empty);

            RestResponse response = AccountManager.AuthClient.Execute(request);

            Parameter TicketHeader = response.Headers.FirstOrDefault(x => string.Equals((x.Name ?? string.Empty).ToString(), "rbx-authentication-ticket", StringComparison.OrdinalIgnoreCase));

            if (TicketHeader == null)
                return false;

            Ticket = (string)TicketHeader.Value;
            LastUse = DateTime.Now;

            return true;
        }

        public bool GetCSRFToken(out string Result)
        {
            RestRequest request = MakeRequest("/v1/authentication-ticket/", Method.Post).AddHeader("Referer", "https://www.roblox.com/games/2753915549/Blox-Fruits");

            RestResponse response = AccountManager.AuthClient.Execute(request);

            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                Result = $"[{(int)response.StatusCode} {response.StatusCode}] {response.Content}";
                return false;
            }

            Parameter result = response.Headers.FirstOrDefault(x => x.Name == "x-csrf-token");

            string Token = string.Empty;

            if (result != null)
            {
                Token = (string)result.Value;
                LastUse = DateTime.Now;

                AccountManager.LastValidAccount = this;
                AccountManager.SaveAccounts();
            }

            CSRFToken = Token;
            TokenSet = DateTime.Now;
            Result = Token;

            return !string.IsNullOrEmpty(Result);
        }

        public bool CheckPin(bool Internal = false)
        {
            if (!GetCSRFToken(out _))
            {
                if (!Internal) MessageBox.Show("Invalid Account Session!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return false;
            }

            if (DateTime.Now < PinUnlocked)
                return true;

            RestRequest request = MakeRequest("v1/account/pin/", Method.Get).AddHeader("Referer", "https://www.roblox.com/");

            RestResponse response = AccountManager.AuthClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                JObject pinInfo = JObject.Parse(response.Content);

                if (!pinInfo["isEnabled"].Value<bool>() || (pinInfo["unlockedUntil"].Type != JTokenType.Null && pinInfo["unlockedUntil"].Value<int>() > 0)) return true;
            }

            if (!Internal) MessageBox.Show("Pin required!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }

        public bool UnlockPin(string Pin)
        {
            if (Pin.Length != 4) return false;
            if (CheckPin(true)) return true;

            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("v1/account/pin/unlock", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded")
                .AddParameter("pin", Pin);

            RestResponse response = AccountManager.AuthClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                JObject pinInfo = JObject.Parse(response.Content);

                if (pinInfo["isEnabled"].Value<bool>() && pinInfo["unlockedUntil"].Value<int>() > 0)
                    PinUnlocked = DateTime.Now.AddSeconds(pinInfo["unlockedUntil"].Value<int>());

                if (PinUnlocked > DateTime.Now)
                {
                    MessageBox.Show("Pin unlocked for 5 minutes", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    return true;
                }
            }

            return false;
        }

        public async Task<string> GetEmailJSON()
        {
            RestRequest DataRequest = MakeRequest("v1/email", Method.Get);

            RestResponse response = await AccountManager.AccountClient.ExecuteAsync(DataRequest);

            return response.Content;
        }

        public async Task<JToken> GetMobileInfo()
        {
            RestRequest DataRequest = MakeRequest("mobileapi/userinfo", Method.Get);

            RestResponse response = await AccountManager.MainClient.ExecuteAsync(DataRequest);

            if (response.StatusCode == HttpStatusCode.OK && Utilities.TryParseJson(response.Content, out JToken Data))
                return Data;

            return null;
        }

        public async Task<JToken> GetUserInfo()
        {
            RestRequest DataRequest = MakeRequest($"v1/users/{UserID}", Method.Get);

            RestResponse response = await AccountManager.UsersClient.ExecuteAsync(DataRequest);

            if (response.StatusCode == HttpStatusCode.OK && Utilities.TryParseJson(response.Content, out JToken Data))
                return Data;

            return null;
        }

        public async Task<long> GetRobux() => (await GetMobileInfo())?["RobuxBalance"]?.Value<long>() ?? 0;

        public bool SetFollowPrivacy(int Privacy)
        {
            if (!CheckPin()) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("account/settings/follow-me-privacy", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/my/account")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded");

            switch (Privacy)
            {
                case 0:
                    request.AddParameter("FollowMePrivacy", "All");
                    break;
                case 1:
                    request.AddParameter("FollowMePrivacy", "Followers");
                    break;
                case 2:
                    request.AddParameter("FollowMePrivacy", "Following");
                    break;
                case 3:
                    request.AddParameter("FollowMePrivacy", "Friends");
                    break;
                case 4:
                    request.AddParameter("FollowMePrivacy", "NoOne");
                    break;
            }

            RestResponse response = AccountManager.MainClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK) return true;

            return false;
        }

        public bool ChangePassword(string Current, string New)
        {
            if (!CheckPin()) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("v2/user/passwords/change", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded")
                .AddParameter("currentPassword", Current)
                .AddParameter("newPassword", New);

            RestResponse response = AccountManager.AuthClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                Password = New;

                var SToken = response.Cookies[".ROBLOSECURITY"];

                if (SToken != null)
                {
                    SecurityToken = SToken.Value;
                    AccountManager.SaveAccounts();
                }
                else
                    MessageBox.Show("An error occured while changing passwords, you will need to re-login with your new password!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

                MessageBox.Show("Password changed!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }

            MessageBox.Show("Failed to change password!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }

        public bool ChangeEmail(string Password, string NewEmail)
        {
            if (!CheckPin()) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("v1/email", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded")
                .AddParameter("password", Password)
                .AddParameter("emailAddress", NewEmail);

            RestResponse response = AccountManager.AccountClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                MessageBox.Show("Email changed!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }

            MessageBox.Show("Failed to change email, maybe your password is incorrect!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }

        public bool LogOutOfOtherSessions(bool Internal = false)
        {
            if (!CheckPin(Internal)) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("authentication/signoutfromallsessionsandreauthenticate", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded");

            RestResponse response = AccountManager.MainClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                var SToken = response.Cookies[".ROBLOSECURITY"];

                if (SToken != null)
                {
                    SecurityToken = SToken.Value;
                    AccountManager.SaveAccounts(true);
                }
                else if (!Internal)
                    MessageBox.Show("An error occured, you will need to re-login!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

                if (!Internal) MessageBox.Show("Signed out of all other sessions!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }

            if (!Internal) MessageBox.Show("Failed to log out of other sessions!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }

        public bool TogglePlayerBlocked(string Username, ref bool Unblocked)
        {
            if (!CheckPin()) throw new Exception("Pin is Locked!");
            if (!AccountManager.GetUserID(Username, out long BlockeeID, out _)) throw new Exception($"Failed to obtain UserId of {Username}!");

            RestResponse BlockedResponse = GetBlockedList();

            if (!BlockedResponse.IsSuccessful) throw new Exception("Failed to obtain blocked users list!");

            string BlockedUsers = BlockedResponse.Content;

            if (!Regex.IsMatch(BlockedUsers, $"\\b{BlockeeID}\\b"))
                return BlockUserId($"{BlockeeID}").IsSuccessful;

            Unblocked = true;

            return BlockUserId($"{BlockeeID}", Unblock: true).IsSuccessful;
        }

        public RestResponse BlockUserId(string UserID, bool SkipPinCheck = false, HttpListenerContext Context = null, bool Unblock = false)
        {
            if (Context != null) Context.Response.StatusCode = 401;
            if (!SkipPinCheck && !CheckPin(true)) throw new Exception("Pin Locked");
            if (!GetCSRFToken(out string Token)) throw new Exception("Invalid X-CSRF-Token");

            RestRequest blockReq = MakeRequest($"v1/users/{UserID}/{(Unblock ? "unblock" : "block")}", Method.Post).AddHeader("X-CSRF-TOKEN", Token);

            RestResponse blockRes = AccountManager.AccountClient.Execute(blockReq);

            Program.Logger.Info($"Block Response for {UserID} | Unblocking: {Unblock}: [{blockRes.StatusCode}] {blockRes.Content}");

            if (Context != null)
                Context.Response.StatusCode = (int)blockRes.StatusCode;

            return blockRes;
        }

        public RestResponse UnblockUserId(string UserID, bool SkipPinCheck = false, HttpListenerContext Context = null) => BlockUserId(UserID, SkipPinCheck, Context, true);

        public bool UnblockEveryone(out string Response)
        {
            if (!CheckPin(true)) { Response = "Pin is Locked"; return false; }

            RestResponse response = GetBlockedList();

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                Task.Run(async () =>
                {
                    JObject List = JObject.Parse(response.Content);

                    if (List.ContainsKey("blockedUsers"))
                    {
                        foreach (var User in List["blockedUsers"])
                        {
                            if (!UnblockUserId(User["userId"].Value<string>(), true).IsSuccessful)
                            {
                                await Task.Delay(20000);

                                UnblockUserId(User["userId"].Value<string>(), true);

                                if (!CheckPin(true))
                                    break;
                            }
                        }
                    }
                });

                Response = "Unblocking Everyone";

                return true; 
            }

            Response = "Failed to unblock everyone";

            return false;
        }

        public RestResponse GetBlockedList(HttpListenerContext Context = null)
        {
            if (Context != null) Context.Response.StatusCode = 401;

            if (!CheckPin(true)) throw new Exception("Pin is Locked");

            RestRequest request = MakeRequest($"v1/users/get-detailed-blocked-users", Method.Get);

            RestResponse response = AccountManager.AccountClient.Execute(request);

            if (Context != null) Context.Response.StatusCode = (int)response.StatusCode;

            return response;
        }

        public bool ParseAccessCode(RestResponse response, out string Code)
        {
            Code = "";

            Match match = Regex.Match(response.Content, "Roblox.GameLauncher.joinPrivateGame\\(\\d+\\,\\s*'(\\w+\\-\\w+\\-\\w+\\-\\w+\\-\\w+)'");

            if (match.Success && match.Groups.Count == 2)
            {
                Code = match.Groups[1]?.Value ?? string.Empty;

                return true;
            }

            return false;
        }

        private string BuildLaunchRequestUrl(long placeId, string jobId, bool followUser, bool joinVIP, string accessCode, string linkCode)
        {
            if (joinVIP)
                return $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId={placeId}&accessCode={accessCode}&linkCode={linkCode}";

            if (followUser)
                return $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser&userId={placeId}";

            return $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame{(string.IsNullOrEmpty(jobId) ? "" : "Job")}&browserTrackerId={BrowserTrackerID}&placeId={placeId}{(string.IsNullOrEmpty(jobId) ? "" : ("&gameId=" + jobId))}&isPlayTogetherGame=false{(AccountManager.IsTeleport ? "&isTeleport=true" : "")}";
        }

        private static string ResolveRobloxPlayerPath(string preferredPath = null, bool allowFallback = true)
        {
            return Utilities.ResolveRobloxPlayerExecutablePath(preferredPath, allowFallback);
        }

        private static void LaunchRobloxPlayerExecutable(string executablePath, string ticket, string launchRequestUrl, string browserTrackerId = "")
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new FileNotFoundException("Failed to find ROBLOX executable.");

            ProcessStartInfo roblox = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                Arguments = $"--app -t {ticket}{(string.IsNullOrWhiteSpace(browserTrackerId) ? string.Empty : $" -b {browserTrackerId}")} -j \"{launchRequestUrl}\""
            };

            Process launchedProcess = Process.Start(roblox);
            if (launchedProcess == null)
                throw new InvalidOperationException("RobloxPlayerBeta.exe failed to start.");

            try { launchedProcess.Dispose(); } catch { }
        }

        public async Task<string> JoinServer(long PlaceID, string JobID = "", bool FollowUser = false, bool JoinVIP = false, bool Internal = false) // oh god i am not refactoring everything to be async im sorry
        {
            if (string.IsNullOrEmpty(BrowserTrackerID))
            {
                string trackerId = string.Empty;
                int attempts = 0;

                do
                {
                    trackerId = GetSecureRandomNumber(100000, 175000).ToString() + GetSecureRandomNumber(100000, 900000).ToString();
                    attempts++;
                } while (attempts < 5 && AccountManager.AccountsList?.Any(account => account != this && string.Equals(account.BrowserTrackerID, trackerId, StringComparison.Ordinal)) == true);

                BrowserTrackerID = trackerId; // Persisted through normal SaveAccounts flow later.
            }

            int ignoredProcessId = GetTinyLaunchIgnoredProcessId();

            try { ClientSettingsPatcher.PatchSettings(); } catch (Exception Ex) { Program.Logger.Error($"Failed to patch ClientAppSettings: {Ex}"); }

            if (!GetCSRFToken(out string Token)) return $"ERROR: Account Session Expired, re-add the account or try again. (Invalid X-CSRF-Token)\n{Token}";

            if (AccountManager.ShuffleJobID && string.IsNullOrEmpty(JobID))
                JobID = await Utilities.GetRandomJobId(PlaceID);

            if (GetAuthTicket(out string Ticket))
            {
                if (AccountManager.General.Get<bool>("AutoCloseLastProcess"))
                {
                    try
                    {
                        foreach(Process proc in Process.GetProcessesByName("RobloxPlayerBeta"))
                        {
                            string CommandLine = proc.GetCommandLine() ?? string.Empty;
                            var TrackerMatch = Regex.Match(CommandLine, @"\-b (\d+)");
                            string TrackerID = TrackerMatch.Success ? TrackerMatch.Groups[1].Value : string.Empty;

                            if (TrackerID == BrowserTrackerID)
                            {
                                try // ignore ObjectDisposedExceptions
                                {
                                    proc.CloseMainWindow();
                                    await Task.Delay(250);
                                    proc.CloseMainWindow(); // Allows Roblox to disconnect from the server so we don't get the "Same account launched" error
                                    await Task.Delay(250);
                                    proc.Kill();
                                }
                                catch { }
                            }
                        }
                    }
                    catch (Exception x) { Program.Logger.Error($"An error occured attempting to close {Username}'s last process(es): {x}"); }
                }

                string LinkCode = string.IsNullOrEmpty(JobID) ? string.Empty : Regex.Match(JobID, "privateServerLinkCode=(.+)")?.Groups[1]?.Value;
                string AccessCode = JobID;

                if (!string.IsNullOrEmpty(LinkCode))
                {
                    RestRequest request = MakeRequest(string.Format("/games/{0}?privateServerLinkCode={1}", PlaceID, LinkCode), Method.Get).AddHeader("X-CSRF-TOKEN", Token).AddHeader("Referer", "https://www.roblox.com/games/4924922222/Brookhaven-RP");

                    RestResponse response = await AccountManager.MainClient.ExecuteAsync(request);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (ParseAccessCode(response, out string Code))
                        {
                            JoinVIP = true;
                            AccessCode = Code;
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Redirect) // thx wally (p.s. i hate wally)
                    {
                        request = MakeRequest(string.Format("/games/{0}?privateServerLinkCode={1}", PlaceID, LinkCode), Method.Get).AddHeader("X-CSRF-TOKEN", Token).AddHeader("Referer", "https://www.roblox.com/games/4924922222/Brookhaven-RP");

                        RestResponse result = await AccountManager.Web13Client.ExecuteAsync(request);

                        if (result.StatusCode == HttpStatusCode.OK)
                        {
                            if (ParseAccessCode(response, out string Code))
                            {
                                JoinVIP = true;
                                AccessCode = Code;
                            }
                        }
                    }
                }

                if (JoinVIP)
                {
                    var request = MakeRequest("/account/settings/private-server-invite-privacy").AddHeader("X-CSRF-TOKEN", Token).AddHeader("Referer", "https://www.roblox.com/my/account");

                    RestResponse result = await AccountManager.MainClient.ExecuteAsync(request);

                    if (result.IsSuccessful && !result.Content.Contains("\"AllUsers\""))
                    {
                        AccountManager.Instance.InvokeIfRequired(() =>
                        {
                            if (Utilities.YesNoPrompt("Roblox Account Manager", "Account Manager has detected your account's privacy settings do not allow you to join private servers.", "Would you like to change this setting to Everyone now?"))
                            {
                                if (!CheckPin(true)) return;

                                var setRequest = MakeRequest("/account/settings/private-server-invite-privacy", Method.Post);

                                setRequest.AddHeader("X-CSRF-TOKEN", Token);
                                setRequest.AddHeader("Referer", "https://www.roblox.com/my/account");
                                setRequest.AddHeader("Content-Type", "application/x-www-form-urlencoded");

                                setRequest.AddParameter("PrivateServerInvitePrivacy", "AllUsers");

                                AccountManager.MainClient.Execute(setRequest);
                            }
                        });
                    }
                }

                double LaunchTime = Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds * 1000);

                string launchRequestUrl = BuildLaunchRequestUrl(PlaceID, JobID, FollowUser, JoinVIP, AccessCode, LinkCode);
                string configuredRobloxPath = Utilities.GetConfiguredRobloxInstallPath();
                bool useCustomRobloxPath = !string.IsNullOrWhiteSpace(configuredRobloxPath);

                if (AccountManager.UseOldJoin)
                {
                    string robloxPlayerPath = ResolveRobloxPlayerPath(
                        useCustomRobloxPath ? configuredRobloxPath : null,
                        allowFallback: !useCustomRobloxPath);
                    if (string.IsNullOrEmpty(robloxPlayerPath))
                        return useCustomRobloxPath
                            ? "ERROR: Failed to find ROBLOX executable in the configured custom Roblox path."
                            : "ERROR: Failed to find ROBLOX executable";

                    if (!Internal)
                        AccountManager.Instance.NextAccount();

                    LastAppLaunch = DateTime.UtcNow;

                    await Task.Run(() => LaunchRobloxPlayerExecutable(robloxPlayerPath, Ticket, launchRequestUrl, BrowserTrackerID));

                    _ = Task.Run(() => AdjustWindowPosition(ignoredProcessId, LastTinyLaunchSlot, PlaceID));

                    return "Success";
                }
                else
                {
                    Exception LaunchException = null;

                    await Task.Run(() => // prevents roblox launcher hanging our main process
                    {
                        try
                        {
                            LastAppLaunch = DateTime.UtcNow;

                            if (useCustomRobloxPath)
                            {
                                string robloxPlayerPath = ResolveRobloxPlayerPath(configuredRobloxPath, allowFallback: false);
                                if (string.IsNullOrWhiteSpace(robloxPlayerPath))
                                    throw new FileNotFoundException("Failed to find ROBLOX executable in the configured custom Roblox path.");

                                LaunchRobloxPlayerExecutable(robloxPlayerPath, Ticket, launchRequestUrl, BrowserTrackerID);
                            }
                            else
                            {
                                string protocolLaunchUrl = $"roblox-player:1+launchmode:play+gameinfo:{Ticket}+launchtime:{LaunchTime}+placelauncherurl:{HttpUtility.UrlEncode(launchRequestUrl)}+browsertrackerid:{BrowserTrackerID}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";

                                try
                                {
                                    ProcessStartInfo LaunchInfo = new ProcessStartInfo { FileName = protocolLaunchUrl };
                                    using (Process Launcher = Process.Start(LaunchInfo))
                                    {
                                        if (Launcher == null)
                                            throw new InvalidOperationException("Roblox launcher process failed to start.");

                                        // Keep launch queue highly responsive during bulk launch.
                                        if (!Launcher.WaitForExit(300))
                                            Program.Logger.Warn($"[JoinServer] Launcher wait timeout for {Username}; continuing launch queue.");
                                    }
                                }
                                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1155 || ex.Message.IndexOf("Application not found", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    LaunchRobloxPlayerExecutable(ResolveRobloxPlayerPath(), Ticket, launchRequestUrl, BrowserTrackerID);
                                }
                            }

                            if (!Internal)
                                AccountManager.Instance.NextAccount();

                            _ = Task.Run(() => AdjustWindowPosition(ignoredProcessId, LastTinyLaunchSlot, PlaceID));
                        }
                        catch (Exception x)
                        {
                            LaunchException = x;
                            Program.Logger.Error($"[JoinServer] Failed launching {Username}: {x}");
                        }
                    });

                    if (LaunchException != null)
                        return $"ERROR: Failed to launch Roblox! Try re-installing Roblox.\n\n{LaunchException.Message}";

                    return "Success";
                }
            }
            else
                return "ERROR: Invalid Authentication Ticket, re-add the account or try again\n(Failed to get Authentication Ticket, Roblox has probably signed you out)";
        }

        public async void AdjustWindowPosition(int ignoredProcessId = 0, int preferredSlot = -1, long expectedPlaceId = 0)
        {
            try
            {
                bool shouldMoveWindow = false;
                int posX = 0;
                int posY = 0;
                int width = 0;
                int height = 0;

                if (RobloxWatcher.RememberWindowPositions)
                    shouldMoveWindow = int.TryParse(GetField("Window_Position_X"), out posX)
                        && int.TryParse(GetField("Window_Position_Y"), out posY)
                        && int.TryParse(GetField("Window_Width"), out width)
                        && int.TryParse(GetField("Window_Height"), out height);

                DateTime searchEnds = DateTime.Now.AddSeconds(45);

                while (DateTime.Now <= searchEnds)
                {
                    await Task.Delay(350);

                    IntPtr windowHandle = FindTrackedRobloxWindowHandle(ignoredProcessId);
                    if (windowHandle == IntPtr.Zero)
                        continue;

                    try { ShowWindow(windowHandle, SW_SHOWNOACTIVATE); } catch { }

                    if (shouldMoveWindow)
                    {
                        try { MoveWindow(windowHandle, posX, posY, width, height, true); } catch { }
                    }

                    ClearPendingTinyLaunchIgnoredProcessId(ignoredProcessId);
                    LastTinyLaunchSlot = -1;
                    return;
                }
            }
            catch (Exception ex)
            {
                Program.Logger.Error($"[AdjustWindowPosition] Failed for {Username}: {ex}");
            }
            finally
            {
                ClearPendingTinyLaunchIgnoredProcessId(ignoredProcessId);
            }
        }

        private IntPtr FindTrackedRobloxWindowHandle(int ignoredProcessId = 0)
        {
            IntPtr fallbackHandle = IntPtr.Zero;
            DateTime fallbackStartUtc = DateTime.MinValue;
            DateTime lastLaunchUtc = LastAppLaunch == DateTime.MinValue ? DateTime.MinValue : LastAppLaunch.AddSeconds(-3);

            try
            {
                foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta").Reverse())
                {
                    try
                    {
                        int processId = 0;
                        try { processId = process.Id; } catch { }
                        if (ignoredProcessId > 0 && processId == ignoredProcessId)
                            continue;

                        if (!TryGetProcessMainWindowHandle(process, out IntPtr mainWindowHandle))
                            continue;

                        string commandLine = string.Empty;
                        try { commandLine = process.GetCommandLine() ?? string.Empty; } catch { }

                        bool trackerMatched = false;
                        if (!string.IsNullOrWhiteSpace(BrowserTrackerID) && !string.IsNullOrWhiteSpace(commandLine))
                        {
                            if (commandLine.IndexOf(BrowserTrackerID, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                trackerMatched = true;
                            }
                            else
                            {
                                Match trackerMatch = Regex.Match(commandLine, @"(?:\s-b\s+|browsertrackerid[:=\s]+)(\d+)", RegexOptions.IgnoreCase);
                                string trackerId = trackerMatch.Success ? trackerMatch.Groups[1].Value : string.Empty;
                                trackerMatched = string.Equals(trackerId, BrowserTrackerID, StringComparison.Ordinal);
                            }
                        }

                        if (!trackerMatched && CurrentProcessId > 0 && processId == CurrentProcessId)
                            trackerMatched = true;

                        if (trackerMatched)
                            return mainWindowHandle;

                        if (lastLaunchUtc != DateTime.MinValue && TryGetProcessStartTimeUtc(process, out DateTime processStartUtc) && processStartUtc >= lastLaunchUtc && processStartUtc >= fallbackStartUtc)
                        {
                            fallbackStartUtc = processStartUtc;
                            fallbackHandle = mainWindowHandle;
                        }
                    }
                    catch (Exception x)
                    {
                        Program.Logger.Warn($"[AdjustWindowPosition] Ignoring process scan error for {Username}: {x.Message}");
                    }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception x)
            {
                Program.Logger.Warn($"[AdjustWindowPosition] Failed enumerating Roblox windows for {Username}: {x.Message}");
            }

            return fallbackHandle;
        }

        public string SetServer(long PlaceID, string JobID, out bool Successful)
        {
            Successful = false;

            if (!GetCSRFToken(out string Token)) return $"ERROR: Account Session Expired, re-add the account or try again. (Invalid X-CSRF-Token)\n{Token}";

            if (string.IsNullOrEmpty(Token))
                return "ERROR: Account Session Expired, re-add the account or try again. (Invalid X-CSRF-Token)";

            RestRequest request = MakeRequest("v1/join-game-instance", Method.Post).AddHeader("Content-Type", "application/json").AddJsonBody(new { gameId = JobID, placeId = PlaceID });

            RestResponse response = AccountManager.GameJoinClient.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                Successful = true;
                return Regex.IsMatch(response.Content, "\"joinScriptUrl\":[%s+]?null") ? response.Content : "Success";
            }
            else
                return $"Failed {response.StatusCode}: {response.Content} {response.ErrorMessage}";
        }

        public bool SendFriendRequest(string Username)
        {
            if (!AccountManager.GetUserID(Username, out long UserId, out _)) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest friendRequest = MakeRequest($"/v1/users/{UserId}/request-friendship", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddHeader("Content-Type", "application/json");

            RestResponse friendResponse = AccountManager.FriendsClient.Execute(friendRequest);

            return friendResponse.IsSuccessful && friendResponse.StatusCode == HttpStatusCode.OK;
        }

        public void SetDisplayName(string DisplayName)
        {
            if (!GetCSRFToken(out string Token)) return;

            RestRequest dpRequest = MakeRequest($"/v1/users/{UserID}/display-names", Method.Patch).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(new { newDisplayName = DisplayName });

            RestResponse dpResponse = AccountManager.UsersClient.Execute(dpRequest);

            if (dpResponse.StatusCode != HttpStatusCode.OK)
                throw new Exception(JObject.Parse(dpResponse.Content)?["errors"]?[0]?["message"].Value<string>() ?? $"Something went wrong\n{dpResponse.StatusCode}: {dpResponse.Content}");
        }

        public void SetAvatar(string AvatarJSONData)
        {
            if (string.IsNullOrEmpty(AvatarJSONData)) return;
            if (!AvatarJSONData.TryParseJson(out JObject Avatar)) return;
            if (Avatar == null) return;
            if (!GetCSRFToken(out string Token)) return;

            RestRequest request;

            if (Avatar.ContainsKey("playerAvatarType"))
            {
                request = MakeRequest("v1/avatar/set-player-avatar-type", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(new { playerAvatarType = Avatar["playerAvatarType"].Value<string>() });

                AccountManager.AvatarClient.Execute(request);
            }

            JToken ScaleObject = Avatar.ContainsKey("scales") ? Avatar["scales"] : (Avatar.ContainsKey("scale") ? Avatar["scale"] : null);

            if (ScaleObject != null)
            {
                request = MakeRequest("v1/avatar/set-scales", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(ScaleObject.ToString());

                AccountManager.AvatarClient.Execute(request);
            }

            if (Avatar.ContainsKey("bodyColors"))
            {
                request = MakeRequest("v1/avatar/set-body-colors", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(Avatar["bodyColors"].ToString());

                AccountManager.AvatarClient.Execute(request);
            }

            if (Avatar.ContainsKey("assets"))
            {
                request = MakeRequest("v2/avatar/set-wearing-assets", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody($"{{\"assets\":{Avatar["assets"]}}}");

                RestResponse Response = AccountManager.AvatarClient.Execute(request);

                if (Response.IsSuccessful)
                {
                    var ResponseJson = JObject.Parse(Response.Content);

                    if (ResponseJson.ContainsKey("invalidAssetIds"))
                        AccountManager.Instance.InvokeIfRequired(() => new MissingAssets(this, ResponseJson["invalidAssetIds"].Select(asset => asset.Value<long>()).ToArray()).Show());
                }
            }
        }

        public async Task<bool> QuickLogIn(string Code)
        {
            if (string.IsNullOrEmpty(Code) || Code.Length != 6) return false;
            if (!GetCSRFToken(out string Token)) return false;

            using var API = new RestClient("https://apis.roblox.com/");
            var Response = await API.PostAsync(MakeRequest("auth-token-service/v1/login/enterCode").AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(new { code = Code }));

            if (Response.IsSuccessful && Response.Content.TryParseJson(out dynamic Info))
                if (Utilities.YesNoPrompt("Quick Log In", "Please confirm you are logging in with this device", $"Device: {Info?.deviceInfo ?? "Unknown"}\nLocation: {Info?.location ?? "Unknown"}"))
                    return (await API.PostAsync(MakeRequest("auth-token-service/v1/login/validateCode").AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(new { code = Code }))).IsSuccessful;

            return false;
        }

        public string GetField(string Name) => Fields.ContainsKey(Name) ? Fields[Name] : string.Empty;
        public void SetField(string Name, string Value) { Fields[Name] = Value; AccountManager.SaveAccounts(); }
        public void RemoveField(string Name) { Fields.Remove(Name); AccountManager.SaveAccounts(); }
    }

    public class AccountJson
    {
        public long UserId { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string UserEmail { get; set; }
        public bool IsEmailVerified { get; set; }
        public int AgeBracket { get; set; }
        public bool UserAbove13 { get; set; }
    }
}
