using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Andromeda.Mod.Features;
using MelonLoader;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod
{
    public static partial class NetworkDebugger
    {
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

            if (GUILayout.Button("CHECK LINK REQUESTS (F9)", GUILayout.Width(210)))
            {
                SteamLinkRequestsMenu.CheckLinkRequestsAsync();
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
            GUILayout.Label("API Presets:");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Main Server", GUILayout.Height(30)))
            {
                _apiUrlInput = MainApiPreset;
                ApplyApiUrl();
            }
            if (GUILayout.Button("Test Server", GUILayout.Height(30)))
            {
                _apiUrlInput = BetaApiPreset;
                ApplyApiUrl();
            }
            if (GUILayout.Button("Local Server", GUILayout.Height(30)))
            {
                _apiUrlInput = LocalApiPreset;
                ApplyApiUrl();
            }
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

            // Limit display to last 30 to keep GUILayout fast
            int startIdx = Math.Max(0, Requests.Count - 30);
            for (int i = Requests.Count - 1; i >= startIdx; i--)
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
    }
}
