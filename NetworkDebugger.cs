using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Windwalk.Net;

namespace Andromeda.Mod
{
    public static class NetworkDebugger
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

        // API Settings
        private static string _apiUrlInput = RestApi.API_URL;
        private static string _logIpInput = "127.0.0.1";
        private static string _logPortInput = "9090";
        private static string _lobbySizeInput = "8";
        private static bool _upnpEnabled = false; // Disabled by default
        private static string _publicIp = "Fetching...";
        private static bool _showPublicIp = false; // Hidden by default as requested

        public static bool IsUpnpEnabled => _upnpEnabled;

        public static void SetUpnpEnabled(bool enabled)
        {
            _upnpEnabled = enabled;
        }

        public static void LogLobbyEvent(string info, string status = "Info")
        {
            if (status == "Error") MelonLogger.Error($"[DEBUG-LOG] {info}");
            else MelonLogger.Msg($"[DEBUG-LOG] {info}");

            // Send to Python Log Server (Fire and Forget)
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                SendToLogServer($"[{status}] {info}");
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
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100)); // fast timeout
                    if (success)
                    {
                        using (var stream = client.GetStream())
                        {
                            byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                            stream.Write(data, 0, data.Length);
                        }
                    }
                }
            }
            catch { /* Ignore logging errors to prevent spam/crash */ }
        }

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
            // Force override on first launch of this patch version (incremented to version 3)
            if (PlayerPrefs.GetInt("Andromeda_Version", 0) < 9)
            {
                PlayerPrefs.SetString("Andromeda_ApiUrl", "https://andromeda.kimotherapy.dev");
                PlayerPrefs.SetInt("Andromeda_Version", 9);
                PlayerPrefs.Save();
            }

            _apiUrlInput = PlayerPrefs.GetString("Andromeda_ApiUrl", RestApi.API_URL);
            _logIpInput = PlayerPrefs.GetString("Andromeda_LogIp", "127.0.0.1");
            _logPortInput = PlayerPrefs.GetString("Andromeda_LogPort", "9090");
            _lobbySizeInput = PlayerPrefs.GetString("Andromeda_LobbySize", "8");
            _upnpEnabled = PlayerPrefs.GetInt("Andromeda_UpnpEnabled", 0) == 1;
            _showPublicIp = PlayerPrefs.GetInt("Andromeda_ShowPublicIp", 0) == 1;

            _apiUrlInput = FixUrl(_apiUrlInput);
            RestApi.API_URL = _apiUrlInput;
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
            PlayerPrefs.SetString("Andromeda_LobbySize", _lobbySizeInput);
            PlayerPrefs.SetInt("Andromeda_UpnpEnabled", _upnpEnabled ? 1 : 0);
            PlayerPrefs.SetInt("Andromeda_ShowPublicIp", _showPublicIp ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void Update()
        {
            // F10 to toggle menu (switched from F11)
            if (Input.GetKeyDown(KeyCode.F10))
            {
                _showGui = !_showGui;
                MelonLogger.Msg($"F10 Pressed. Toggle GUI: {_showGui}");
            }
        }

        public static void OnGUI()
        {
            if (!_showGui) return;

            GUI.depth = -9999;
            GUI.skin.window.normal.background = Texture2D.whiteTexture;

            _windowRect = GUI.Window(1001, _windowRect, DrawWindow, "Andromeda Debugger (F10)");
        }

        private static void DrawWindow(int windowID)
        {
            // Toolbar
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Guide", _currentTab == DebugTab.Guide ? GUI.skin.button : GUI.skin.box)) _currentTab = DebugTab.Guide;
            if (GUILayout.Button("Network", _currentTab == DebugTab.Network ? GUI.skin.button : GUI.skin.box)) _currentTab = DebugTab.Network;
            if (GUILayout.Button("Data Explorer", _currentTab == DebugTab.Data ? GUI.skin.button : GUI.skin.box)) _currentTab = DebugTab.Data;
            if (GUILayout.Button("Settings", _currentTab == DebugTab.Settings ? GUI.skin.button : GUI.skin.box)) _currentTab = DebugTab.Settings;

            GUILayout.FlexibleSpace();

            // "this button" (Force Start)
            GUI.color = Color.green;
            if (GUILayout.Button("FORCE START GAME", GUILayout.Width(150)))
            {
                ForceStart();
            }
            GUI.color = Color.white;

            if (GUILayout.Button("DUMP DATA", GUILayout.Width(100)))
            {
                DumpData();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            if (_currentTab == DebugTab.Guide) DrawGuideTab();
            else if (_currentTab == DebugTab.Network) DrawNetworkTab();
            else if (_currentTab == DebugTab.Data) DrawDataTab();
            else if (_currentTab == DebugTab.Settings) DrawSettingsTab();

            GUI.DragWindow();
        }

        private static void DrawGuideTab()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Port Forwarding & Hosting Guide", GUI.skin.box);
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            string displayIp = _showPublicIp ? _publicIp : "••••.••••.••••.••••";
            GUILayout.Label($"<b>Your Public IP:</b> <color=cyan>{displayIp}</color>", GUI.skin.box);
            
            if (_showPublicIp)
            {
                if (GUILayout.Button("Copy", GUILayout.Width(60)))
                {
                    GUIUtility.systemCopyBuffer = _publicIp;
                }
                if (GUILayout.Button("Hide", GUILayout.Width(60)))
                {
                    _showPublicIp = false;
                }
            }
            else
            {
                if (GUILayout.Button("Show", GUILayout.Width(60)))
                {
                    _showPublicIp = true;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.Label("To host a dedicated server on the internet, you need to open the following ports on your router (Access your router via 192.168.1.1 or similar, and find 'Port Forwarding' / 'Virtual Servers'):");
            GUILayout.Space(10);

            GUILayout.Label("<b>1. Game Server Ports (Required)</b>");
            GUILayout.Label("- Protocol: TCP & UDP");
            GUILayout.Label("- Range: <b>7777 to 7877</b>");
            GUILayout.Label("<i>(Each game lobby spawns a new instance on an incremented port starting at 7777. The voice server naturally opens on +1 of the game port.)</i>");

            GUILayout.Space(15);

            GUILayout.Label("<b>2. Python API Backend (Required)</b>");
            GUILayout.Label("- Protocol: TCP");
            GUILayout.Label("- Port: <b>8000</b> (unless you use a reverse proxy to route 80/443)");
            GUILayout.Label("<i>(Clients must point their Settings API URL to your Public IP above—or your domain like Andromeda2.mrie.dev—so they can authenticate, browse lobbies, and load catalogs.)</i>");

            GUILayout.Space(15);

            GUILayout.Label("<b>3. Python Log Server (Optional)</b>");
            GUILayout.Label("- Protocol: TCP");
            GUILayout.Label("- Port: <b>9090</b>");
            GUILayout.Label("<i>(Used for remote debugging and server console logs. Do not expose this publicly unless necessary.)</i>");

            GUILayout.Space(20);
            GUILayout.Label("<b>UPnP (Automatic Port Forwarding)</b>");
            GUILayout.Label("If your router supports UPnP, you can check 'Enable UPnP' in the Settings tab. The dedicated server will automatically attempt to open its dynamic port when it launches, bypassing manual router config.");

            GUILayout.EndVertical();
        }

        private static void DrawSettingsTab()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Mod & API Settings", GUI.skin.box);

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Python API URL:", GUILayout.Width(120));
            _apiUrlInput = GUILayout.TextField(_apiUrlInput);
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            if (GUILayout.Button("APPLY NEW API URL", GUILayout.Height(40)))
            {
                ApplyApiUrl();
            }

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label("New Lobby Size:", GUILayout.Width(120));
            _lobbySizeInput = GUILayout.TextField(_lobbySizeInput);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            _upnpEnabled = GUILayout.Toggle(_upnpEnabled, "Enable UPnP Port Forwarding");
            GUILayout.EndHorizontal();

            if (GUILayout.Button("SAVE SETTINGS", GUILayout.Height(30)))
            {
                SaveSettings();
                MelonLogger.Msg("[DEBUG-UI] Settings saved.");
            }

            GUILayout.Space(20);
            GUILayout.Label("Python Log Server Settings", GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Log Server IP:", GUILayout.Width(120));
            _logIpInput = GUILayout.TextField(_logIpInput);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Log Server Port:", GUILayout.Width(120));
            _logPortInput = GUILayout.TextField(_logPortInput);
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            if (GUILayout.Button("FORCE API LOADED (SET TRUE)", GUILayout.Height(30)))
            {
                ForceApiLoaded();
            }

            GUILayout.Space(20);
            GUILayout.Label("Current Status:", GUI.skin.box);
            GUILayout.Label($"<b>REST Endpoint:</b> {RestApi.API_URL}");
            GUILayout.Label($"<b>Log Server:</b> {_logIpInput}:{_logPortInput}");
            GUILayout.Label($"<b>Game Engine Endpoint:</b> {ApiShared.SERVICE_ADDRESS}");
            GUILayout.Label($"<b>ApiData.IsLoaded:</b> {(ApiData.IsLoaded ? "<color=green>TRUE</color>" : "<color=red>FALSE</color>")}");

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string currentMode = "None";
            try
            {
                var server = Singleton.Existing<ProgramServer>();
                if (server != null)
                {
                    var modeField = typeof(ProgramServer).GetField("gamemode", BindingFlags.NonPublic | BindingFlags.Instance);
                    var mode = modeField?.GetValue(server);
                    if (mode != null) currentMode = mode.GetType().Name;
                }
            }
            catch { }

            GUILayout.Label($"<b>Active Scene:</b> <color=cyan>{sceneName}</color>");
            GUILayout.Label($"<b>Active Gamemode:</b> <color=cyan>{currentMode}</color>");

            GUILayout.EndVertical();
        }

        private static void ForceApiLoaded()
        {
            try
            {
                var prop = typeof(ApiData).GetProperty("IsLoaded", BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    prop.SetValue(null, true);
                    MelonLogger.Msg("[DEBUG-UI] Forced ApiData.IsLoaded to TRUE.");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[DEBUG-UI] Failed to force ApiData.IsLoaded: {e.Message}");
            }
        }

        private static void ApplyApiUrl()
        {
            _apiUrlInput = FixUrl(_apiUrlInput);
            RestApi.API_URL = _apiUrlInput;

            // Re-patch ApiShared.SERVICE_ADDRESS
            try
            {
                var apiType = typeof(ApiShared);
                var urlField = apiType.GetField("SERVICE_ADDRESS", BindingFlags.Public | BindingFlags.Static);
                if (urlField != null)
                {
                    urlField.SetValue(null, RestApi.API_URL);
                    MelonLogger.Msg($"[DEBUG-UI] Updated SERVICE_ADDRESS -> {RestApi.API_URL}");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[DEBUG-UI] Failed to re-patch SERVICE_ADDRESS: {e.Message}");
            }

            SaveSettings();
        }

        public static void ForceStart()
        {
            var lobby = UnityEngine.Object.FindObjectOfType<LobbyServer>();
            if (lobby != null)
            {
                // Set all players to ready
                var playersField = typeof(LobbyShared).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
                if (playersField != null)
                {
                    var players = playersField.GetValue(lobby) as Dictionary<PlayerId, LobbyShared.Player>;
                    if (players != null)
                    {
                        foreach (var key in players.Keys.ToList())
                        {
                            var p = players[key];
                            p.isReady = true;
                            players[key] = p;
                        }
                    }
                }
                // Send update
                var sendUpdate = typeof(LobbyServer).GetMethod("SendUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                sendUpdate?.Invoke(lobby, null);
                MelonLogger.Msg("[DEBUG-UI] Force Start triggered: All players set to READY.");
            }
            else
            {
                MelonLogger.Warning("[DEBUG-UI] Could not find LobbyServer to force start.");
            }
        }

        private static void DrawNetworkTab()
        {
            GUILayout.BeginHorizontal();

            // LEFT PANEL: List
            GUILayout.BeginVertical(GUILayout.Width(350));
            GUILayout.Label("Requests", GUI.skin.box);

            _scrollPositionList = GUILayout.BeginScrollView(_scrollPositionList, GUI.skin.box);

            for (int i = Requests.Count - 1; i >= 0; i--)
            {
                var req = Requests[i];
                GUI.color = GetStatusColor(req.Status);
                string label = $"[{req.Method}] {GetShortUrl(req.Url)}\n{req.ResolvedType ?? "Unknown"}";

                if (GUILayout.Button(label, GUILayout.Height(40)))
                {
                    _selectedRequest = req;
                }
                GUI.color = Color.white;
            }

            GUILayout.EndScrollView();

            if (GUILayout.Button("Clear", GUILayout.Height(30)))
            {
                Requests.Clear();
                _selectedRequest = null;
            }
            GUILayout.EndVertical();

            // RIGHT PANEL: Details
            GUILayout.BeginVertical();
            GUILayout.Label("Details", GUI.skin.box);

            _scrollPositionDetails = GUILayout.BeginScrollView(_scrollPositionDetails, GUI.skin.box);

            if (_selectedRequest != null)
            {
                GUILayout.Label($"<b>URL:</b> {_selectedRequest.Url}");
                GUILayout.Label($"<b>Method:</b> {_selectedRequest.Method}");
                GUILayout.Label($"<b>Type:</b> <b><color=cyan>{_selectedRequest.ResolvedType ?? "N/A"}</color></b> -> <b><color=cyan>{_selectedRequest.ResolvedResponseType ?? "N/A"}</color></b>");
                GUILayout.Label($"<b>Status:</b> {_selectedRequest.ResponseCode} ({_selectedRequest.Status})");
                if (!string.IsNullOrEmpty(_selectedRequest.Error))
                {
                    GUI.color = Color.red;
                    GUILayout.Label($"<b>Error:</b> {_selectedRequest.Error}");
                    GUI.color = Color.white;
                }

                GUILayout.Space(10);
                GUILayout.Label("<b>Request Body:</b>");
                GUILayout.TextArea(_selectedRequest.RequestBody ?? "", GUILayout.Height(100));

                GUILayout.Space(10);
                GUILayout.Label("<b>Response Body:</b>");
                GUILayout.TextArea(_selectedRequest.ResponseBody ?? "", GUILayout.ExpandHeight(true));
            }
            else
            {
                GUILayout.Label("Select a request to view details.");
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private static void DrawDataTab()
        {
            GUILayout.BeginHorizontal();

            // 1. Categories
            GUILayout.BeginVertical(GUILayout.Width(150));
            GUILayout.Label("Categories", GUI.skin.box);
            if (GUILayout.Button("Characters")) { _selectedCategory = "Characters"; _selectedDataItem = null; }
            if (GUILayout.Button("Items")) { _selectedCategory = "Items"; _selectedDataItem = null; }
            if (GUILayout.Button("Abilities")) { _selectedCategory = "Abilities"; _selectedDataItem = null; }
            if (GUILayout.Button("Perks")) { _selectedCategory = "Perks"; _selectedDataItem = null; }
            if (GUILayout.Button("Skins")) { _selectedCategory = "Skins"; _selectedDataItem = null; }
            GUILayout.EndVertical();

            // 2. Items List
            GUILayout.BeginVertical(GUILayout.Width(250));
            GUILayout.Label($"{_selectedCategory} List", GUI.skin.box);
            _scrollPosDataList = GUILayout.BeginScrollView(_scrollPosDataList, GUI.skin.box);

            var list = GetDataList(_selectedCategory);
            foreach (var item in list)
            {
                string name = GetItemName(item);
                if (GUILayout.Button(name))
                {
                    _selectedDataItem = item;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // 3. Inspector
            GUILayout.BeginVertical();
            GUILayout.Label("Inspector", GUI.skin.box);
            _scrollPosDataDetails = GUILayout.BeginScrollView(_scrollPosDataDetails, GUI.skin.box);

            if (_selectedDataItem != null)
            {
                DrawObjectInspector(_selectedDataItem);
            }
            else
            {
                GUILayout.Label("Select an item to inspect.");
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private static IEnumerable<object> GetDataList(string category)
        {
            object instance = null;
            switch (category)
            {
                case "Characters": instance = CharacterSpawnList.Instance; break;
                case "Items": instance = ItemSpawnList.Instance; break;
                case "Abilities": instance = AbilitySpawnList.Instance; break;
                case "Perks": instance = PerkSpawnList.Instance; break;
                case "Skins":
                    try
                    {
                        Patches.SkinGuidRuntimeFix.NormalizeAllSkins();
                    }
                    catch { }
                    instance = SkinSpawnList.Instance;
                    break;
            }

            if (instance == null) return new List<object>();

            var getEntries = instance.GetType().GetMethod("GetEntries");
            if (getEntries == null) return new List<object>();

            var entries = getEntries.Invoke(instance, null) as System.Collections.IEnumerable;
            if (entries == null) return new List<object>();

            var result = new List<object>();
            foreach (var e in entries) result.Add(e);
            return result;
        }

        private static string GetItemName(object entry)
        {
            if (entry == null) return "null";
            var type = entry.GetType();
            var keyField = type.GetField("key");
            if (keyField != null)
            {
                var val = keyField.GetValue(entry);
                return val?.ToString() ?? "null";
            }
            return entry.ToString();
        }

        private static void DrawObjectInspector(object obj)
        {
            if (obj == null) return;
            var type = obj.GetType();
            GUILayout.Label($"<b>Type:</b> {type.Name}");

            var keyField = type.GetField("key");
            var valueField = type.GetField("value");

            if (keyField != null)
            {
                GUILayout.Label($"<b>Key (Enum):</b> {keyField.GetValue(obj)}");
            }

            if (valueField != null)
            {
                var settingsObj = valueField.GetValue(obj);
                GUILayout.Space(5);
                GUILayout.Label("<b>Settings Object:</b>");
                if (settingsObj != null)
                {
                    var keyObj = keyField != null ? keyField.GetValue(obj) : null;
                    var resolvedGuid = ResolveGuidWithFallback(settingsObj, keyObj);
                    if (!string.IsNullOrEmpty(resolvedGuid))
                    {
                        var currentGuid = GetStringMember(settingsObj, "guid");
                        if (string.IsNullOrWhiteSpace(currentGuid))
                            SetStringMember(settingsObj, "guid", resolvedGuid);
                    }

                    DrawReflectedFields(settingsObj);

                    if (!string.IsNullOrEmpty(resolvedGuid))
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("<b>resolvedGuid:</b> ", GUILayout.Width(150));
                        GUILayout.Label(resolvedGuid);
                        GUILayout.EndHorizontal();
                    }
                }
                else
                {
                    GUILayout.Label("null");
                }
            }
            else
            {
                DrawReflectedFields(obj);
            }
        }

        private static void DrawReflectedFields(object obj)
        {
            if (obj == null) return;
            var type = obj.GetType();

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var val = field.GetValue(obj);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{field.Name}:</b> ", GUILayout.Width(150));
                GUILayout.Label(val?.ToString() ?? "null");
                GUILayout.EndHorizontal();
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                object val = null;
                try { val = prop.GetValue(obj, null); } catch { val = "Error"; }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{prop.Name}:</b> ", GUILayout.Width(150));
                GUILayout.Label(val?.ToString() ?? "null");
                GUILayout.EndHorizontal();
            }
        }

        private static string GetStringMember(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return null;

            var type = obj.GetType();
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(string))
                return field.GetValue(obj) as string;

            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanRead && prop.PropertyType == typeof(string))
                return prop.GetValue(obj, null) as string;

            return null;
        }

        private static bool SetStringMember(object obj, string memberName, string value)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return false;

            var type = obj.GetType();
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(string))
            {
                field.SetValue(obj, value);
                return true;
            }

            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string))
            {
                prop.SetValue(obj, value, null);
                return true;
            }

            return false;
        }

        private static string ToSnakeCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            bool wroteUnderscore = false;

            foreach (char c in value.Trim())
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (char.IsUpper(c) && sb.Length > 0 && !wroteUnderscore)
                        sb.Append('_');

                    sb.Append(char.ToLowerInvariant(c));
                    wroteUnderscore = false;
                }
                else if (sb.Length > 0 && !wroteUnderscore)
                {
                    sb.Append('_');
                    wroteUnderscore = true;
                }
            }

            string snake = sb.ToString().Trim('_');
            if (snake.EndsWith("_skin_settings", StringComparison.OrdinalIgnoreCase))
                snake = snake.Substring(0, snake.Length - "_skin_settings".Length);
            else if (snake.EndsWith("_settings", StringComparison.OrdinalIgnoreCase))
                snake = snake.Substring(0, snake.Length - "_settings".Length);

            return snake.Trim('_');
        }

        private static string ResolveGuidWithFallback(object settingsObj, object keyObj)
        {
            string guid = GetStringMember(settingsObj, "guid");
            if (!string.IsNullOrWhiteSpace(guid))
                return guid;

            string skinName = GetStringMember(settingsObj, "skinName");
            string name = GetStringMember(settingsObj, "name");
            string keyName = keyObj?.ToString();

            if (!string.IsNullOrWhiteSpace(name)) return ToSnakeCase(name);
            if (!string.IsNullOrWhiteSpace(keyName)) return ToSnakeCase(keyName);
            if (!string.IsNullOrWhiteSpace(skinName)) return ToSnakeCase(skinName);

            return null;
        }

        private static Color GetStatusColor(string status)
        {
            if (status == "Pending") return Color.yellow;
            if (status == "Error") return Color.red;
            return Color.green;
        }

        private static string GetShortUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.AbsolutePath;
            }
            catch { return url; }
        }

        // --- Reflection Logic ---
        private static void BuildEndpointMap()
        {
            try
            {
                MelonLogger.Msg("Mapping ApiShared endpoints...");
                var apiType = typeof(ApiShared);
                var nestedTypes = apiType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

                foreach (var type in nestedTypes)
                {
                    if (type.IsSubclassOf(typeof(ApiShared.Request)))
                    {
                        try
                        {
                            var instance = Activator.CreateInstance(type) as ApiShared.Request;
                            if (instance != null)
                            {
                                string endpoint = instance.Endpoint;
                                if (!string.IsNullOrEmpty(endpoint))
                                {
                                    if (!endpoint.StartsWith("/")) endpoint = "/" + endpoint;
                                    EndpointTypeMap[endpoint] = type;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Failed to build endpoint map: {e}");
            }
        }

        private static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            try
            {
                object parsedJson = JsonConvert.DeserializeObject(json);
                return JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }

        // --- Harmony Patches ---

        [HarmonyPatch(typeof(UnityWebRequest), "SendWebRequest")]
        public static class WebRequestPatch
        {
            public static void Prefix(UnityWebRequest __instance)
            {
                try
                {
                    string url = __instance.url;
                    if (string.IsNullOrEmpty(url) || !url.StartsWith("http")) return;

                    string body = "";
                    if (__instance.uploadHandler != null && __instance.uploadHandler.data != null)
                    {
                        body = Encoding.UTF8.GetString(__instance.uploadHandler.data);
                        
                        // Inject Lobby Size into Create requests
                        if (url.Contains("/party/create") || url.Contains("/games/new") || url.Contains("/games/custom/new"))
                        {
                            try
                            {
                                var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
                                if (dict != null && int.TryParse(_lobbySizeInput, out int lobbySize))
                                {
                                    dict["maxPlayers"] = lobbySize;
                                    string newBody = JsonConvert.SerializeObject(dict);
                                    byte[] newBodyData = Encoding.UTF8.GetBytes(newBody);
                                    
                                    // Replace the upload handler with the modified body
                                    __instance.uploadHandler = new UploadHandlerRaw(newBodyData);
                                    __instance.SetRequestHeader("Content-Type", "application/json");
                                    body = newBody; // Update local body for the inspector
                                }
                            }
                            catch { }
                        }
                    }

                    string path = "Unknown";
                    string typeName = "Unknown/Custom";
                    string typeResponseName = "";
                    try
                    {
                        var uri = new Uri(url);
                        path = uri.AbsolutePath;
                        if (EndpointTypeMap.TryGetValue(path, out Type t))
                        {
                            typeName = t.Name;
                            if (typeName.EndsWith("Request"))
                            {
                                string baseName = typeName.Substring(0, typeName.Length - 7);
                                typeResponseName = baseName + "Response";
                            }
                        }
                    }
                    catch { }

                    var req = new NetworkRequest
                    {
                        Url = url,
                        Method = __instance.method,
                        RequestBody = FormatJson(body),
                        Status = "Pending",
                        ResolvedType = typeName,
                        ResolvedResponseType = typeResponseName,
                        Timestamp = Time.time
                    };

                    Requests.Add(req);
                }
                catch (Exception e) { MelonLogger.Error($"Prefix Error: {e}"); }
            }

            public static void Postfix(UnityWebRequest __instance, UnityWebRequestAsyncOperation __result)
            {
                if (__result == null) return;

                var req = Requests.LastOrDefault(r => r.Url == __instance.url && r.Status == "Pending");

                __result.completed += (operation) =>
                {
                    try
                    {
                        if (req == null) return;

                        req.ResponseCode = (int)__instance.responseCode;
                        req.Status = (__instance.isNetworkError || __instance.isHttpError) ? "Error" : "Success";
                        req.Error = __instance.error;

                        if (__instance.downloadHandler != null)
                        {
                            req.ResponseBody = FormatJson(__instance.downloadHandler.text);
                        }
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Error($"Async Callback Error: {e}");
                    }
                };
            }
        }

        public static void DumpData()
        {
            MelonLogger.Msg("Starting Data Dump...");

            string dumpDir = Path.Combine(Directory.GetCurrentDirectory(), "constant_data");
            if (!Directory.Exists(dumpDir))
            {
                Directory.CreateDirectory(dumpDir);
            }

            DumpFile(dumpDir, "characters.json", ExtractCharacters());
            DumpFile(dumpDir, "items.json", ExtractItems());
            DumpFile(dumpDir, "abilities.json", ExtractAbilities());
            DumpFile(dumpDir, "perks.json", ExtractPerks());
            DumpFile(dumpDir, "skins.json", ExtractSkins());
            DumpFile(dumpDir, "progression.json", ExtractProgression());

            MelonLogger.Msg($"Data dumped to {dumpDir}");
        }

        private static void DumpFile(string dir, string filename, object data)
        {
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(Path.Combine(dir, filename), json);
            MelonLogger.Msg($"Exported {filename}");
        }

        private static object ExtractCharacters() => ExtractEnumMapping(CharacterSpawnList.Instance);
        private static object ExtractItems() => ExtractEnumMapping(ItemSpawnList.Instance);
        private static object ExtractAbilities() => ExtractEnumMapping(AbilitySpawnList.Instance);
        private static object ExtractPerks() => ExtractEnumMapping(PerkSpawnList.Instance);
        private static object ExtractSkins() => ExtractEnumMapping(SkinSpawnList.Instance);

        private static object ExtractProgression()
        {
            var progressionData = new Dictionary<string, object>();
            var charProgress = new Dictionary<CharacterSpawnList.Key, (AbilitySpawnList.Key baseAbility, PerkSpawnList.Key[] uniquePerks)>
            {
                { CharacterSpawnList.Key.Medic, (AbilitySpawnList.Key.Heal_1, new[] { PerkSpawnList.Key.Bedside_Manner_1, PerkSpawnList.Key.Remote_Diagnostics_1 }) },
                { CharacterSpawnList.Key.Spy, (AbilitySpawnList.Key.Scan_1, new[] { PerkSpawnList.Key.Ninja_1, PerkSpawnList.Key.Advanced_Optics_1 }) },
                { CharacterSpawnList.Key.Scientist, (AbilitySpawnList.Key.TeleportDeploy_1, new[] { PerkSpawnList.Key.Critical_Failure_1, PerkSpawnList.Key.Fusion_Cell_1 }) },
                { CharacterSpawnList.Key.Commando, (AbilitySpawnList.Key.BattleRage_1, new[] { PerkSpawnList.Key.Knuckle_Dusters_1, PerkSpawnList.Key.Second_Wind_1 }) },
                { CharacterSpawnList.Key.Captain, (AbilitySpawnList.Key.Shield_1, new[] { PerkSpawnList.Key.Dead_Shot_1, PerkSpawnList.Key.Mercenary_1 }) },
                { CharacterSpawnList.Key.SpaceMonkey, (AbilitySpawnList.Key.CombatRoll_1, new[] { PerkSpawnList.Key.Lean_Build_1, PerkSpawnList.Key.Scrappy_1 }) },
                { CharacterSpawnList.Key.Grifter, (AbilitySpawnList.Key.SpellSteal_1, new[] { PerkSpawnList.Key.Channeler_1, PerkSpawnList.Key.Alien_Affinity_1 }) },
                { CharacterSpawnList.Key.Officer, (AbilitySpawnList.Key.LockDoor_2, new[] { PerkSpawnList.Key.Utility_Belt_1, PerkSpawnList.Key.Bullet_Proof_Vest_1 }) },
                { CharacterSpawnList.Key.Assassin, (AbilitySpawnList.Key.ShadowSneak_1, new[] { PerkSpawnList.Key.Concealed_Blade_1, PerkSpawnList.Key.Backstab_1 }) }
            };

            var charactersDict = new Dictionary<string, object>();

            foreach (var kvp in charProgress)
            {
                var charKey = kvp.Key;
                var charSettings = CharacterSpawnList.Instance.Get(charKey);
                if (charSettings == null) continue;

                var abilityKey = kvp.Value.baseAbility;
                var perkKeys = kvp.Value.uniquePerks;

                var abilityData = new Dictionary<string, object>();
                var abilityBaseInfo = AbilitySpawnList.Instance.Get(abilityKey);
                if (abilityBaseInfo != null)
                {
                    var abilityTiers = new List<string>();
                    string baseName = abilityKey.ToString();
                    string rootName = baseName.EndsWith("_1") ? baseName.Substring(0, baseName.Length - 2) : baseName;

                    foreach (var entry in AbilitySpawnList.Instance.GetEntries())
                    {
                        string entryName = entry.key.ToString();
                        if (entryName == rootName || entryName.StartsWith(rootName + "_"))
                        {
                            if (entry.value != null) abilityTiers.Add(entry.value.guid);
                        }
                    }

                    abilityData["base_guid"] = abilityBaseInfo.guid;
                    abilityData["tiers"] = abilityTiers;
                }

                var perksList = new List<object>();
                foreach (var pKey in perkKeys)
                {
                    var pSettings = PerkSpawnList.Instance.Get(pKey);
                    if (pSettings != null)
                    {
                        var perkTiers = new List<string>();
                        string pBaseName = pKey.ToString();
                        string pRootName = pBaseName.EndsWith("_1") ? pBaseName.Substring(0, pBaseName.Length - 2) : pBaseName;

                        foreach (var entry in PerkSpawnList.Instance.GetEntries())
                        {
                            string entryName = entry.key.ToString();
                            if (entryName == pRootName || entryName.StartsWith(pRootName + "_"))
                            {
                                if (entry.value != null) perkTiers.Add(entry.value.guid);
                            }
                        }

                        perksList.Add(new
                        {
                            base_guid = pSettings.guid,
                            tiers = perkTiers
                        });
                    }
                }

                charactersDict[charSettings.guid] = new
                {
                    ability = abilityData,
                    perks = perksList
                };
            }

            progressionData["characters"] = charactersDict;
            return progressionData;
        }

        private static object ExtractEnumMapping(object instance)
        {
            if (instance == null) return null;
            var type = instance.GetType();
            var getEntries = type.GetMethod("GetEntries");
            if (getEntries == null) return null;

            var entries = getEntries.Invoke(instance, null) as System.Collections.IEnumerable;
            if (entries == null) return null;

            var list = new List<object>();
            foreach (var entry in entries)
            {
                var entryType = entry.GetType();
                var keyField = entryType.GetField("key");
                var valueField = entryType.GetField("value");
                if (valueField == null) continue;

                var settings = valueField.GetValue(entry);
                if (settings == null) continue;

                var settingsType = settings.GetType();
                var guidField = settingsType.GetField("guid") ?? settingsType.GetProperty("guid") as MemberInfo;

                string guidVal = null;
                if (guidField is FieldInfo fi) guidVal = fi.GetValue(settings) as string;
                else if (guidField is PropertyInfo pi) guidVal = pi.GetValue(settings, null) as string;

                var nameField = settingsType.GetField("name") ?? settingsType.GetProperty("name") as MemberInfo;
                object nameVal = null;
                if (nameField is FieldInfo ni) nameVal = ni.GetValue(settings);
                else if (nameField is PropertyInfo npi) nameVal = npi.GetValue(settings, null);

                if (string.IsNullOrEmpty(guidVal) && keyField != null)
                {
                    var keyVal = keyField.GetValue(entry);
                    if (keyVal != null) guidVal = keyVal.ToString();
                }

                if (!string.IsNullOrEmpty(guidVal))
                {
                    list.Add(new
                    {
                        guid = guidVal,
                        name = nameVal?.ToString() ?? guidVal,
                        purchasable = true,
                        cost = 0
                    });
                }
            }
            return list;
        }
    }
}
