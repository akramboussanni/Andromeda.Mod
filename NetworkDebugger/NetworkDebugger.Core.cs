using System;
using System.Collections.Generic;
using System.Text;
using Andromeda.Mod.Features;
using Andromeda.Mod.Settings;
using MelonLoader;
using UnityEngine;
using LobbySettings = Andromeda.Mod.Settings.AndromedaSettings;
using ClientSettings = Andromeda.Mod.Settings.AndromedaClientSettings;

namespace Andromeda.Mod
{
    public static partial class NetworkDebugger
    {
        // Data Structure for Requests
        public class NetworkRequest
        {
            public string Url;
            public string Method;
            public string RequestBody;
            public string ResponseBody;
            public int ResponseCode;
            public string Status; // Pending, Success, Error
            public string ResolvedType; // E.g. "AbilitiesGetRequest"
            public string ResolvedResponseType; // E.g. "AbilitiesGetResponse"
            public float Timestamp;
            public string Error;
        }

        public static List<NetworkRequest> Requests = new List<NetworkRequest>();
        public static Dictionary<string, Type> EndpointTypeMap = new Dictionary<string, Type>();
        private const int MaxRequests = 50;
        private static bool StoreApiRequests => ClientSettings.NetworkDebuggerRequestCaptureEnabled.Value;
        public static bool VerboseLoggingEnabled => ClientSettings.VerboseDebugLoggingEnabled.Value;

        // API Settings
        private static string _apiUrlInput = RestApi.API_URL;
        private const string MainApiPreset = "https://andromeda.kimotherapy.dev";
        private const string BetaApiPreset = "https://andromeda-beta.kimotherapy.dev";
        private const string LocalApiPreset = "http://localhost:8000";
        private static string _logIpInput = "127.0.0.1";
        private static string _logPortInput = "9090";

        /// <summary>Hostname used by LogRedirectPatch (and LogLobbyEvent TCP sender).</summary>
        public static string LogHost => _logIpInput;
        private static string _lobbySizeInput = "12";
        private static bool _upnpEnabled = false; // Disabled by default
        private static bool _firstPersonEnabled = false;
        private static float _firstPersonYawSensitivity = 0.35f;
        private static string _publicIp = "Fetching...";
        private static bool _showPublicIp = false; // Hidden by default as requested

        // GUI State
        private static bool _showGui = false;
        private static Rect _windowRect = new Rect(20, 20, 1000, 600);
        private static Vector2 _scrollPositionList = Vector2.zero;
        private static Vector2 _scrollPositionDetails = Vector2.zero;
        private static NetworkRequest _selectedRequest = null;

        // Tabs
        private enum DebugTab { Network, Data, Settings, Guide }
        private static DebugTab _currentTab = DebugTab.Guide;

        // Data Explorer State
        private static string _selectedCategory = "Characters";
        private static object _selectedDataItem = null;
        private static Vector2 _scrollPosDataList = Vector2.zero;
        private static Vector2 _scrollPosDataDetails = Vector2.zero;

        private static string GetCurrentSteamId()
        {
            try
            {
                return Steam.Id;
            }
            catch
            {
                return null;
            }
        }

        private static string CleanDiscordUsername(string username)
        {
            username = (username ?? string.Empty).Trim();
            if (username.EndsWith("#0"))
            {
                return username.Substring(0, username.Length - 2);
            }

            return string.IsNullOrEmpty(username) ? "Unknown user" : username;
        }

        public static bool IsUpnpEnabled => ClientSettings.UpnpEnabled.Value;
        public static bool IsFirstPersonEnabled => LobbySettings.FirstPersonEnabled.Value;
        public static float FirstPersonYawSensitivity => ClientSettings.FirstPersonYawSensitivity.Value;
        public static int[] AllowedLobbySizes => LobbySettings.AllowedLobbySizes;

        public static int GetLobbySize()
        {
            LobbySettings.Initialize();
            return LobbySettings.LobbySize.Value;
        }

        public static bool SetLobbySize(int maxPlayers, bool persist = true)
        {
            bool ok = LobbySettings.SetLobbySize(maxPlayers, persist);
            _lobbySizeInput = LobbySettings.LobbySize.Value.ToString();
            return ok;
        }

        public static void SetFirstPersonEnabled(bool enabled, bool persist = true)
        {
            LobbySettings.SetFirstPersonEnabled(enabled, persist);
            _firstPersonEnabled = LobbySettings.FirstPersonEnabled.Value;
        }

        public static void SetUpnpEnabled(bool enabled)
        {
            _upnpEnabled = enabled;
            ClientSettings.SetUpnpEnabled(enabled, persist: false);
        }

        public static void LogLobbyEvent(string info, string status = "Info")
        {
            bool isError = string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase);
            if (!VerboseLoggingEnabled && !isError)
                return;

            if (isError) MelonLogger.Error($"[DEBUG-LOG] {info}");
            else MelonLogger.Msg($"[DEBUG-LOG] {info}");

            string steamId = null;
            try { steamId = Steam.Id; } catch { }
            string prefix = steamId != null ? $"[SteamID:{steamId}] " : "";

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                SendToLogServer($"{prefix}[{status}] {info}");
            });
        }

        private static void SendToLogServer(string message)
        {
            try
            {
                if (!int.TryParse(_logPortInput, out int port)) return;
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect(_logIpInput, port, null, null);
                    if (result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(300)))
                    {
                        using (var stream = client.GetStream())
                        {
                            byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                            stream.Write(data, 0, data.Length);
                        }
                    }
                }
            }
            catch { /* fire-and-forget — never crash the game */ }
        }

        public static void Initialize()
        {
            MelonLogger.Msg("Andromeda Mod Debugger Loaded. Initializing...");
            FetchPublicIp();
            BuildEndpointMap();
            LoadSettings();
        }

        private static void FetchPublicIp()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var wc = new System.Net.WebClient())
                    {
                        _publicIp = wc.DownloadString("https://api.ipify.org").Trim();
                    }
                }
                catch
                {
                    _publicIp = "Failed to fetch (Check connection)";
                }
            });
        }

        public static void LoadSettingsEarly()
        {
            // One-time defaults migration for updated patch behavior.
            if (PlayerPrefs.GetInt("Andromeda_Version", 0) < 12)
            {
                PlayerPrefs.SetString("Andromeda_ApiUrl", "https://andromeda.kimotherapy.dev");
                PlayerPrefs.SetString("Andromeda_LogIp", "log.andromeda.kimotherapy.dev");
                PlayerPrefs.SetString("Andromeda_LogPort", "9090");
                if (!PlayerPrefs.HasKey("Andromeda_LobbySize"))
                    PlayerPrefs.SetString("Andromeda_LobbySize", "12");
                PlayerPrefs.SetInt("Andromeda_Version", 12);
                PlayerPrefs.Save();
            }

            _apiUrlInput = PlayerPrefs.GetString("Andromeda_ApiUrl", RestApi.API_URL);
            LobbySettings.Initialize();
            ClientSettings.Initialize();
            _lobbySizeInput = LobbySettings.LobbySize.Value.ToString();
            _upnpEnabled = ClientSettings.UpnpEnabled.Value;
            _firstPersonEnabled = LobbySettings.FirstPersonEnabled.Value;
            _firstPersonYawSensitivity = ClientSettings.FirstPersonYawSensitivity.Value;
            _showPublicIp = PlayerPrefs.GetInt("Andromeda_ShowPublicIp", 0) == 1;

            if (DedicatedServerStartup.IsServer && !string.IsNullOrWhiteSpace(DedicatedServerStartup.ForcedApiUrl))
                _apiUrlInput = DedicatedServerStartup.ForcedApiUrl;

            _apiUrlInput = FixUrl(_apiUrlInput);
            RestApi.API_URL = _apiUrlInput;

            // Default log host to the community server
            _logIpInput = PlayerPrefs.GetString("Andromeda_LogIp", "log.andromeda.kimotherapy.dev");
            _logPortInput = PlayerPrefs.GetString("Andromeda_LogPort", "9090");

            if (string.IsNullOrEmpty(_logIpInput))
            {
                // Fallback to the API host if no log server is set at all.
                try
                {
                    Uri uri = new Uri(_apiUrlInput);
                    _logIpInput = uri.Host;
                }
                catch { _logIpInput = "log.andromeda.kimotherapy.dev"; }
            }
        }

        private static string FixUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "https://andromeda.kimotherapy.dev";

            string sanitized = url.Trim().TrimEnd('/');

            // Default to http if no protocol is provided
            if (!sanitized.StartsWith("http://") && !sanitized.StartsWith("https://"))
            {
                sanitized = "http://" + sanitized;
            }

            // Check for port removed as requested by user

            return sanitized;
        }

        private static void LoadSettings()
        {
            // Settings are already loaded in LoadSettingsEarly, just ensure UI fields are set
            _apiUrlInput = RestApi.API_URL;
        }

        private static void SaveSettings()
        {
            PlayerPrefs.SetString("Andromeda_ApiUrl", _apiUrlInput);
            PlayerPrefs.SetString("Andromeda_LogIp", _logIpInput);
            PlayerPrefs.SetString("Andromeda_LogPort", _logPortInput);
            LobbySettings.SetLobbySize(int.TryParse(_lobbySizeInput, out int parsed) ? parsed : LobbySettings.LobbySize.Value, persist: false);
            ClientSettings.SetUpnpEnabled(_upnpEnabled, persist: false);
            LobbySettings.SetFirstPersonEnabled(_firstPersonEnabled, persist: false);
            ClientSettings.SetFirstPersonYawSensitivity(_firstPersonYawSensitivity, persist: false);
            PlayerPrefs.SetInt("Andromeda_ShowPublicIp", _showPublicIp ? 1 : 0);
            LobbySettings.SaveAll();
            ClientSettings.SaveAll();
            PlayerPrefs.Save();

            _lobbySizeInput = LobbySettings.LobbySize.Value.ToString();
            _firstPersonEnabled = LobbySettings.FirstPersonEnabled.Value;
            _firstPersonYawSensitivity = ClientSettings.FirstPersonYawSensitivity.Value;
        }

        public static void Update()
        {
            // F10 to toggle menu
            if (Input.GetKeyDown(KeyCode.F10))
            {
                _showGui = !_showGui;
                if (!_showGui)
                {
                    // Clean up heavy data when closing to free memory/CPU
                    _selectedRequest = null;
                    _selectedDataItem = null;
                }
                MelonLogger.Msg($"F10 Pressed. Toggle GUI: {_showGui}");
            }

            SteamLinkRequestsMenu.Update();
        }
    }
}
