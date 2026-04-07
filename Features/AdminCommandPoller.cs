using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace Andromeda.Mod.Features
{
    /// <summary>
    /// Connects to /client/events (Server-Sent Events) and reacts to admin-issued
    /// broadcast messages and force-exit commands in real time.
    /// No polling, no TTL, no version tracking needed — events are fire-and-forget.
    /// New connections only receive commands issued after they connect.
    /// </summary>
    public static class AdminCommandPoller
    {
        // Overlay state — written from background thread, read from main thread
        private static volatile string _broadcastMessage = null;
        private static float _broadcastShowUntil = 0f;
        private const float BROADCAST_DISPLAY_SECONDS = 8f;

        // Actions that must run on Unity's main thread (e.g. Application.Quit, Time.realtimeSinceStartup)
        private static readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();

        private static CancellationTokenSource _cts;
        // Force-Exit state
        private static bool _isForcedExitPending = false;
        private static float _forceExitTime = 0f;
        private const float FORCE_EXIT_COUNTDOWN = 6.0f; // 5 seconds display + safety buffer

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        public static void Start()
        {
            _cts = new CancellationTokenSource();
            _isForcedExitPending = false;
            Task.Run(() => ConnectLoop(_cts.Token));
            MelonLogger.Msg("[AdminPoller] Started — connected to /client/events (SSE)");
        }

        public static void Stop()
        {
            _cts?.Cancel();
        }

        /// <summary>Call from Mod.OnUpdate() to drain main-thread actions.</summary>
        public static void OnUpdate()
        {
            while (_mainThread.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { MelonLogger.Warning($"[AdminPoller] Main-thread action failed: {ex.Message}"); }
            }

            if (_isForcedExitPending && Time.realtimeSinceStartup >= _forceExitTime)
            {
                MelonLogger.Msg("[AdminPoller] Countdown finished. Quitting...");
                Application.Quit();
            }
        }

        // ── Background thread: persistent SSE connection with auto-reconnect ──

        private static async Task ConnectLoop(CancellationToken ct)
        {
            await Task.Delay(5000, ct).ConfigureAwait(false); // short warm-up after startup

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ConnectOnce(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[AdminPoller] SSE disconnected: {ex.Message} — reconnecting in 10s");
                    try { await Task.Delay(10_000, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private static async Task ConnectOnce(CancellationToken ct)
        {
            string url = RestApi.API_URL + "/client/events";
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                                            .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            string pendingEvent = null;

            while (!ct.IsCancellationRequested)
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break; // server closed stream

                if (line.StartsWith("event:"))
                {
                    pendingEvent = line.Substring(6).Trim();
                }
                else if (line.StartsWith("data:"))
                {
                    string data = line.Substring(5).Trim();
                    if (pendingEvent != null)
                    {
                        HandleEvent(pendingEvent, data);
                    }
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    // \n\n received - reset pending event
                    pendingEvent = null;
                }
            }
        }

        // ── Event handlers (called from background thread → dispatch to main thread as needed) ──

        private static void HandleEvent(string type, string json)
        {
            try
            {
                switch (type)
                {
                    case "broadcast":
                        var bc = JsonConvert.DeserializeObject<BroadcastPayload>(json);
                        if (bc != null && !string.IsNullOrEmpty(bc.message))
                        {
                            MelonLogger.Msg($"[AdminPoller] BROADCAST: {bc.message}");
                            string msg = bc.message;
                            _mainThread.Enqueue(() =>
                            {
                                _broadcastMessage = msg;
                                _broadcastShowUntil = Time.realtimeSinceStartup + BROADCAST_DISPLAY_SECONDS;
                            });
                        }
                        break;

                    case "force_exit":
                        MelonLogger.Warning("[AdminPoller] FORCE EXIT received from server admin — starting countdown.");
                        _mainThread.Enqueue(() =>
                        {
                            if (_isForcedExitPending) return;
                            _isForcedExitPending = true;
                            _forceExitTime = Time.realtimeSinceStartup + FORCE_EXIT_COUNTDOWN;
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AdminPoller] Failed to handle event '{type}': {ex.Message}");
            }
        }

        // ── OnGUI — render overlays (call from Mod.OnGUI) ──

        public static void OnGUI()
        {
            int sw = Screen.width;
            int sh = Screen.height;
            var oldColor = GUI.color;

            // 1. Force Exit Countdown (Top center)
            if (_isForcedExitPending)
            {
                float remaining = _forceExitTime - Time.realtimeSinceStartup;
                int seconds = Mathf.Max(0, Mathf.FloorToInt(remaining - 1));

                // Red banner at the top
                GUI.color = new Color(0.8f, 0f, 0f, 0.95f);
                GUI.DrawTexture(new Rect(0, 0, sw, 60), Texture2D.whiteTexture);

                GUI.color = Color.white;
                var quitStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                GUI.Label(new Rect(0, 0, sw, 60), $"⚠️ SERVER CLOSING IN {seconds}s...", quitStyle);
                GUI.color = oldColor;
            }

            // 2. Broadcast Overlay (Bottom center)
            if (_broadcastMessage == null) return;
            if (Time.realtimeSinceStartup > _broadcastShowUntil)
            {
                _broadcastMessage = null;
                return;
            }

            float bRemaining = _broadcastShowUntil - Time.realtimeSinceStartup;
            float bAlpha = Mathf.Clamp01(bRemaining); // fade last second

            // Background banner
            GUI.color = new Color(0.05f, 0.05f, 0.15f, 0.92f * bAlpha);
            GUI.DrawTexture(new Rect(0, sh - 120, sw, 110), Texture2D.whiteTexture);

            // Title
            GUI.color = new Color(0.6f, 0.7f, 1f, bAlpha);
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            GUI.Label(new Rect(0, sh - 115, sw, 28), "SERVER ANNOUNCEMENT", titleStyle);

            // Message
            GUI.color = new Color(1f, 1f, 1f, bAlpha);
            var msgStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            GUI.Label(new Rect(40, sh - 88, sw - 80, 60), _broadcastMessage, msgStyle);

            GUI.color = oldColor;
        }

        // ── JSON models ──

        [Serializable]
        private class BroadcastPayload
        {
            public string message = null;
        }
    }
}
