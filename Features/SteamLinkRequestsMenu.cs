using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Andromeda.Mod.Features
{
    public static class SteamLinkRequestsMenu
    {
        private class LinkRequestEntry
        {
            public string DiscordUserId;
            public string DiscordUsername;
        }

        private static bool _checkingLinkRequests;
        private static bool _showOverlay;
        private static string _header = "Steam link requests";
        private static string _subtitle = "Press F9 any time to refresh. Use /link_steam in Discord.";
        private static string _status = string.Empty;
        private static float _statusUntil;
        private static readonly List<LinkRequestEntry> _pendingRequests = new List<LinkRequestEntry>();
        private static int _selectedIndex;
        private static bool _actionInProgress;
        private static Vector2 _requestScroll;

        public static void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                _showOverlay = true;
                SetStatus("Refreshing link requests...");
                CheckLinkRequestsAsync();
                MelonLogger.Msg("F9 pressed. Opening Steam link request menu and refreshing.");
            }

            if (_showOverlay && Input.GetKeyDown(KeyCode.Escape))
            {
                _showOverlay = false;
            }
        }

        public static void OnGUI()
        {
            DrawOverlay();
        }

        public static void CheckLinkRequestsAsync()
        {
            if (_checkingLinkRequests)
                return;

            MelonCoroutines.Start(CheckLinkRequestsCoro());
        }

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

        private static void DrawOverlay()
        {
            if (!_showOverlay) return;

            GUI.depth = -10000;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float boxW = Mathf.Min(780f, Screen.width - 40f);
            float boxH = Mathf.Min(520f, Screen.height - 40f);
            float boxX = (Screen.width - boxW) / 2f;
            float boxY = (Screen.height - boxH) / 2f;

            GUI.color = new Color(0.08f, 0.1f, 0.14f, 0.98f);
            GUI.DrawTexture(new Rect(boxX, boxY, boxW, boxH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.95f, 1f, 1f) }
            };
            var subtleStyle = new GUIStyle(bodyStyle)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.74f, 0.8f, 0.9f, 1f) }
            };
            var itemStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                wordWrap = true
            };

            GUI.Label(new Rect(boxX + 20f, boxY + 16f, boxW - 40f, 36f), _header, titleStyle);
            GUI.Label(new Rect(boxX + 20f, boxY + 52f, boxW - 40f, 22f), _subtitle, subtleStyle);

            float toolbarY = boxY + 80f;
            float contentY = toolbarY + 44f;

            string currentSteamId = GetCurrentSteamId();
            string steamContext = string.IsNullOrEmpty(currentSteamId)
                ? "Current SteamID: unavailable"
                : $"Current SteamID: {currentSteamId}";

            float toolbarBtnX = boxX + 20f;
            float toolbarBtnY = toolbarY;
            float toolbarBtnW = 108f;
            float toolbarBtnH = 28f;

            GUI.enabled = !_checkingLinkRequests && !_actionInProgress;
            if (GUI.Button(new Rect(toolbarBtnX, toolbarBtnY, toolbarBtnW, toolbarBtnH), _checkingLinkRequests ? "Refreshing..." : "Refresh"))
            {
                SetStatus("Refreshing link requests...");
                CheckLinkRequestsAsync();
            }

            toolbarBtnX += toolbarBtnW + 8f;
            GUI.enabled = !string.IsNullOrEmpty(currentSteamId);
            if (GUI.Button(new Rect(toolbarBtnX, toolbarBtnY, toolbarBtnW, toolbarBtnH), "Copy SteamID") && !string.IsNullOrEmpty(currentSteamId))
            {
                GUIUtility.systemCopyBuffer = currentSteamId;
                SetStatus("Copied SteamID to clipboard.");
            }

            toolbarBtnX += toolbarBtnW + 8f;
            GUI.enabled = !_actionInProgress && !string.IsNullOrEmpty(currentSteamId);
            if (GUI.Button(new Rect(toolbarBtnX, toolbarBtnY, toolbarBtnW, toolbarBtnH), "Unlink") && !string.IsNullOrEmpty(currentSteamId))
            {
                MelonCoroutines.Start(HandleUnlinkCurrentAccountCoro());
            }

            toolbarBtnX += toolbarBtnW + 8f;
            GUI.enabled = true;
            if (GUI.Button(new Rect(toolbarBtnX, toolbarBtnY, toolbarBtnW, toolbarBtnH), "Close"))
            {
                _showOverlay = false;
            }

            GUI.Label(new Rect(boxX + 20f, contentY, boxW - 40f, 20f), steamContext, subtleStyle);
            contentY += 22f;

            if (!string.IsNullOrEmpty(_status) && Time.realtimeSinceStartup <= _statusUntil)
            {
                GUI.Label(new Rect(boxX + 20f, contentY, boxW - 40f, 22f), _status, bodyStyle);
                contentY += 24f;
            }

            bool hasSelection = _pendingRequests.Count > 0 &&
                                _selectedIndex >= 0 &&
                                _selectedIndex < _pendingRequests.Count;

            float listX = boxX + 20f;
            float listY = contentY;
            float listW = boxW - 40f;
            float listH = 260f;

            GUI.color = new Color(0.12f, 0.16f, 0.22f, 0.96f);
            GUI.DrawTexture(new Rect(listX, listY, listW, listH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (_pendingRequests.Count == 0)
            {
                GUI.Label(
                    new Rect(listX + 12f, listY + 12f, listW - 24f, 50f),
                    _checkingLinkRequests ? "Loading pending requests..." : "No pending requests right now.",
                    bodyStyle
                );
            }
            else
            {
                float itemH = 36f;
                float contentH = _pendingRequests.Count * (itemH + 4f);
                var viewRect = new Rect(listX + 10f, listY + 10f, listW - 20f, listH - 20f);
                var scrollRect = new Rect(0f, 0f, viewRect.width - 20f, Mathf.Max(viewRect.height, contentH));

                _requestScroll = GUI.BeginScrollView(viewRect, _requestScroll, scrollRect);
                float itemY = 0f;
                for (int i = 0; i < _pendingRequests.Count; i++)
                {
                    var request = _pendingRequests[i];
                    bool selected = i == _selectedIndex;
                    string username = CleanDiscordUsername(request.DiscordUsername);
                    string label = $"{username}\n{request.DiscordUserId}";

                    GUI.color = selected ? new Color(0.2f, 0.35f, 0.55f, 1f) : Color.white;
                    if (GUI.Button(new Rect(0f, itemY, scrollRect.width, itemH), label, itemStyle))
                    {
                        _selectedIndex = i;
                    }
                    GUI.color = Color.white;
                    itemY += itemH + 4f;
                }
                GUI.EndScrollView();
            }

            float detailY = listY + listH + 10f;
            if (hasSelection)
            {
                var selectedRequest = _pendingRequests[_selectedIndex];
                string selectedUsername = CleanDiscordUsername(selectedRequest.DiscordUsername);

                GUI.color = new Color(0.14f, 0.18f, 0.24f, 0.96f);
                GUI.DrawTexture(new Rect(boxX + 20f, detailY, boxW - 40f, 64f), Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUI.Label(new Rect(boxX + 32f, detailY + 8f, boxW - 64f, 20f), "Selected request", subtleStyle);
                GUI.Label(new Rect(boxX + 32f, detailY + 28f, boxW - 64f, 22f), $"{selectedUsername} ({selectedRequest.DiscordUserId})", bodyStyle);
            }
            else
            {
                GUI.Label(new Rect(boxX + 20f, detailY + 22f, boxW - 40f, 22f), "Select a request to take action.", subtleStyle);
            }

            float btnY = boxY + boxH - 42f;
            float btnW = (boxW - 60f) / 3f;
            float btnX = boxX + 20f;

            GUI.enabled = !_actionInProgress && hasSelection;

            if (GUI.Button(new Rect(btnX, btnY, btnW, 30f), "Accept") && hasSelection)
            {
                var request = _pendingRequests[_selectedIndex];
                SetStatus($"Sending accept for {CleanDiscordUsername(request.DiscordUsername)}...");
                MelonCoroutines.Start(HandleLinkActionCoro("accept", request));
            }

            btnX += btnW + 10f;
            if (GUI.Button(new Rect(btnX, btnY, btnW, 30f), "Block 24h") && hasSelection)
            {
                var request = _pendingRequests[_selectedIndex];
                SetStatus($"Sending 24h block for {CleanDiscordUsername(request.DiscordUsername)}...");
                MelonCoroutines.Start(HandleLinkActionCoro("block24h", request));
            }

            btnX += btnW + 10f;
            if (GUI.Button(new Rect(btnX, btnY, btnW, 30f), "Block Forever") && hasSelection)
            {
                var request = _pendingRequests[_selectedIndex];
                SetStatus($"Sending permanent block for {CleanDiscordUsername(request.DiscordUsername)}...");
                MelonCoroutines.Start(HandleLinkActionCoro("blockForever", request));
            }

            GUI.enabled = true;
        }

        private static void SetOverlay(string header, string subtitle = null)
        {
            _header = header;
            _subtitle = string.IsNullOrWhiteSpace(subtitle)
                ? "Press F9 any time to refresh. Use /link_steam in Discord."
                : subtitle;
            _showOverlay = true;
        }

        private static void SetStatus(string message)
        {
            _status = message ?? string.Empty;
            _statusUntil = Time.realtimeSinceStartup + 8f;
        }

        private static IEnumerator CheckLinkRequestsCoro()
        {
            _showOverlay = true;
            _checkingLinkRequests = true;

            try
            {
                if (string.IsNullOrEmpty(Steam.AuthToken))
                {
                    _pendingRequests.Clear();
                    SetOverlay("Steam link requests", "No Steam auth token is available yet.");
                    NetworkDebugger.LogLobbyEvent("[LINK-CHECK] No Steam auth token available.", "Error");
                    yield break;
                }

                string url = RestApi.API_URL + "/players/link/requests";
                using (var req = UnityWebRequest.Get(url))
                {
                    req.timeout = 10;
                    req.SetRequestHeader("Authorization", Steam.AuthToken);
                    yield return req.SendWebRequest();

                    if (req.isNetworkError || req.isHttpError)
                    {
                        string errorLine = $"Core request failed: {req.error} ({req.responseCode})";
                        _pendingRequests.Clear();
                        SetOverlay("Steam link requests", errorLine);
                        NetworkDebugger.LogLobbyEvent($"[LINK-CHECK] {errorLine}", "Error");
                        yield break;
                    }

                    try
                    {
                        var json = JObject.Parse(req.downloadHandler.text);
                        var data = json["data"] as JObject;
                        if (data == null)
                        {
                            _pendingRequests.Clear();
                            SetOverlay("Steam link requests", "No data returned from Core.");
                            yield break;
                        }

                        int count = data["count"]?.Value<int>() ?? 0;
                        var requests = data["requests"] as JArray;
                        _pendingRequests.Clear();

                        if (requests != null)
                        {
                            foreach (var item in requests)
                            {
                                string discordUserId = item["discord_user_id"]?.ToString() ?? item["discordUserId"]?.ToString() ?? "?";
                                string username = item["discord_username"]?.ToString() ?? item["discordUsername"]?.ToString() ?? "Unknown user";
                                _pendingRequests.Add(new LinkRequestEntry
                                {
                                    DiscordUserId = discordUserId,
                                    DiscordUsername = username,
                                });
                            }
                        }

                        if (_selectedIndex < 0 || _selectedIndex >= _pendingRequests.Count)
                            _selectedIndex = 0;

                        string header = count > 0
                            ? "Steam link requests"
                            : "Steam link requests";

                        string subtitle = count > 0
                            ? $"{count} pending request(s). Select one and choose an action."
                            : "No pending requests right now. Press F9 to refresh.";

                        SetOverlay(header, subtitle);
                        NetworkDebugger.LogLobbyEvent($"[LINK-CHECK] {count} pending link request(s).", "Info");
                    }
                    catch (Exception ex)
                    {
                        string errorLine = $"Failed to parse link requests: {ex.Message}";
                        _pendingRequests.Clear();
                        SetOverlay("Steam link requests", errorLine);
                        NetworkDebugger.LogLobbyEvent($"[LINK-CHECK] {errorLine}", "Error");
                    }
                }
            }
            finally
            {
                _checkingLinkRequests = false;
            }
        }

        private static IEnumerator HandleLinkActionCoro(string action, LinkRequestEntry request)
        {
            if (_actionInProgress || request == null)
                yield break;

            if (string.IsNullOrEmpty(Steam.AuthToken))
            {
                SetStatus("No Steam auth token available.");
                yield break;
            }

            _actionInProgress = true;

            try
            {
                string currentSteamId = GetCurrentSteamId();

                string actionLabel;
                string url;
                if (action == "accept")
                {
                    actionLabel = "accept";
                    url = RestApi.API_URL + "/players/link/" + request.DiscordUserId + "/accept";
                }
                else if (action == "blockForever")
                {
                    actionLabel = "block forever";
                    url = RestApi.API_URL + "/players/link/" + request.DiscordUserId + "/block?block_duration=forever";
                }
                else
                {
                    actionLabel = "block for 24h";
                    url = RestApi.API_URL + "/players/link/" + request.DiscordUserId + "/block?block_duration=24h";
                }

                MelonLogger.Msg($"[LINK-ACTION] {actionLabel} -> {url}");

                using (var req = new UnityWebRequest(url, "POST"))
                {
                    req.timeout = 10;
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Authorization", Steam.AuthToken);
                    yield return req.SendWebRequest();

                    string responseText = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
                    string targetName = CleanDiscordUsername(request.DiscordUsername);
                    string statusLine = $"HTTP {req.responseCode} {req.error}".Trim();
                    MelonLogger.Msg($"[LINK-ACTION] Response {actionLabel}: {statusLine} Body='{responseText}'");

                    if (req.isNetworkError || req.responseCode != 200)
                    {
                        string message = $"Failed to {actionLabel} for {targetName}: {statusLine}{(string.IsNullOrWhiteSpace(responseText) ? string.Empty : $" - {responseText}")}";
                        MelonLogger.Error("[LINK-ACTION] " + message);
                        SetStatus(message);
                        NetworkDebugger.LogLobbyEvent("[LINK-ACTION] " + message, "Error");
                        yield break;
                    }

                    if (!string.IsNullOrWhiteSpace(responseText))
                    {
                        NetworkDebugger.LogLobbyEvent($"[LINK-ACTION] {actionLabel} response: {responseText}", "Info");
                    }

                    SetStatus($"{actionLabel} sent for {targetName} on SteamID {currentSteamId}. {statusLine}");
                    NetworkDebugger.LogLobbyEvent($"[LINK-ACTION] {actionLabel} for {request.DiscordUserId}", "Info");
                }

                CheckLinkRequestsAsync();
            }
            finally
            {
                _actionInProgress = false;
            }
        }

        private static IEnumerator HandleUnlinkCurrentAccountCoro()
        {
            if (_actionInProgress)
                yield break;

            if (string.IsNullOrEmpty(Steam.AuthToken))
            {
                SetStatus("No Steam auth token available.");
                yield break;
            }

            _actionInProgress = true;

            try
            {
                using (var req = new UnityWebRequest(RestApi.API_URL + "/players/link/unlink", "POST"))
                {
                    req.timeout = 10;
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Authorization", Steam.AuthToken);
                    yield return req.SendWebRequest();

                    string responseText = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
                    string statusLine = $"HTTP {req.responseCode} {req.error}".Trim();
                    MelonLogger.Msg($"[LINK-ACTION] Response unlink: {statusLine} Body='{responseText}'");

                    if (req.isNetworkError || req.responseCode != 200)
                    {
                        string message = $"Failed to unlink current account: {statusLine}{(string.IsNullOrWhiteSpace(responseText) ? string.Empty : $" - {responseText}")}";
                        MelonLogger.Error("[LINK-ACTION] " + message);
                        SetStatus(message);
                        NetworkDebugger.LogLobbyEvent("[LINK-ACTION] " + message, "Error");
                        yield break;
                    }

                    SetStatus("Current Steam account unlinked.");
                    NetworkDebugger.LogLobbyEvent("[LINK-ACTION] Current account unlinked.", "Info");
                }

                CheckLinkRequestsAsync();
            }
            finally
            {
                _actionInProgress = false;
            }
        }
    }
}
