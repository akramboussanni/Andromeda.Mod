using System;
using System.Collections;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Andromeda.Mod.Features
{
    /// <summary>
    /// Polls /client/commands every 5 seconds and reacts to admin-issued
    /// broadcast messages and force-exit commands.
    /// </summary>
    public static class AdminCommandPoller
    {
        private static int _lastBroadcastVersion = -1;
        private static int _lastForceExitVersion = -1;

        // Overlay state
        private static string _broadcastMessage = null;
        private static float _broadcastShowUntil = 0f;

        private const float POLL_INTERVAL = 5f;
        private const float BROADCAST_DISPLAY_SECONDS = 10f;

        public static void Start()
        {
            MelonCoroutines.Start(PollLoop());
            MelonLogger.Msg("[AdminPoller] Started — polling /client/commands every 5s");
        }

        private static IEnumerator PollLoop()
        {
            // Warm-up: wait a few seconds after startup before first poll
            yield return new WaitForSeconds(10f);

            while (true)
            {
                yield return PollOnce();
                yield return new WaitForSeconds(POLL_INTERVAL);
            }
        }

        private static IEnumerator PollOnce()
        {
            string url = RestApi.API_URL + "/client/commands";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 5;
                yield return req.SendWebRequest();

                if (req.isNetworkError || req.isHttpError)
                    yield break;

                CommandResponse resp = null;
                try { resp = JsonConvert.DeserializeObject<CommandResponse>(req.downloadHandler.text); }
                catch { yield break; }

                if (resp == null) yield break;

                // ── Force Exit ──
                if (resp.force_exit != null && resp.force_exit.version > _lastForceExitVersion)
                {
                    _lastForceExitVersion = resp.force_exit.version;
                    if (_lastForceExitVersion > 0) // ignore version 0 (startup default)
                    {
                        MelonLogger.Warning("[AdminPoller] FORCE EXIT received from server admin — quitting.");
                        Application.Quit();
                    }
                }

                // ── Broadcast ──
                if (resp.broadcast != null && resp.broadcast.version > _lastBroadcastVersion)
                {
                    _lastBroadcastVersion = resp.broadcast.version;
                    if (_lastBroadcastVersion > 0 && !string.IsNullOrEmpty(resp.broadcast.message))
                    {
                        MelonLogger.Msg($"[AdminPoller] BROADCAST: {resp.broadcast.message}");
                        _broadcastMessage = resp.broadcast.message;
                        _broadcastShowUntil = Time.realtimeSinceStartup + BROADCAST_DISPLAY_SECONDS;
                    }
                }
            }
        }

        /// <summary>
        /// Call from Mod.OnGUI() to render the broadcast overlay.
        /// </summary>
        public static void OnGUI()
        {
            if (_broadcastMessage == null) return;
            if (Time.realtimeSinceStartup > _broadcastShowUntil)
            {
                _broadcastMessage = null;
                return;
            }

            float remaining = _broadcastShowUntil - Time.realtimeSinceStartup;
            float alpha = Mathf.Clamp01(remaining); // fade last second

            int sw = Screen.width;
            int sh = Screen.height;

            // Background banner
            var oldColor = GUI.color;
            GUI.color = new Color(0.05f, 0.05f, 0.15f, 0.92f * alpha);
            GUI.DrawTexture(new Rect(0, sh - 120, sw, 110), Texture2D.whiteTexture);

            // Title
            GUI.color = new Color(0.6f, 0.7f, 1f, alpha);
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            GUI.Label(new Rect(0, sh - 115, sw, 28), "📢  SERVER ANNOUNCEMENT", titleStyle);

            // Message
            GUI.color = new Color(1f, 1f, 1f, alpha);
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
        private class VersionedCommand
        {
            public int version;
            public string message;
        }

        [Serializable]
        private class CommandResponse
        {
            public VersionedCommand broadcast;
            public VersionedCommand force_exit;
        }
    }
}
