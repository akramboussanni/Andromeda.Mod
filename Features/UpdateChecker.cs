using System;
using System.Collections;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Andromeda.Mod.Features
{
    internal static class UpdateChecker
    {
        // Stable releases: main Andromeda repo (contains Andromeda.Mod.dll as an asset)
        private const string StableApiUrl = "https://api.github.com/repos/akramboussanni/andromeda/releases/latest";
        // Bleeding-edge: mod repo releases
        private const string BleedingEdgeApiUrl = "https://api.github.com/repos/akramboussanni/Andromeda.Mod/releases/latest";

        private static bool _blocked;
        private static string _blockedVersion;
        private static string _blockedUrl;

        // Called from Mod.OnGUI — draws a fullscreen block if an update is required
        public static void OnGUI()
        {
            if (!_blocked) return;

            // Fullscreen dark overlay
            GUI.depth = -10000;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float boxW = 520f;
            float boxH = 200f;
            float boxX = (Screen.width - boxW) / 2f;
            float boxY = (Screen.height - boxH) / 2f;

            GUIStyle box = new GUIStyle(GUI.skin.box);
            box.fontSize = 15;
            box.normal.textColor = Color.white;
            box.alignment = TextAnchor.MiddleCenter;
            box.wordWrap = true;

            GUI.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            GUI.Box(new Rect(boxX, boxY, boxW, boxH), GUIContent.none);
            GUI.color = Color.white;

            GUIStyle label = new GUIStyle(GUI.skin.label);
            label.alignment = TextAnchor.MiddleCenter;
            label.wordWrap = true;
            label.normal.textColor = Color.white;

            GUIStyle title = new GUIStyle(label);
            title.fontSize = 20;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = new Color(1f, 0.35f, 0.35f);

            float pad = 20f;
            float inner = boxW - pad * 2;

            GUI.Label(new Rect(boxX + pad, boxY + pad, inner, 36f), "Update Required", title);

            label.fontSize = 14;
            GUI.Label(new Rect(boxX + pad, boxY + 64f, inner, 24f),
                $"Your version:  v{BuildInfo.Version}    →    Latest:  v{_blockedVersion}", label);

            label.fontSize = 12;
            label.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            GUI.Label(new Rect(boxX + pad, boxY + 94f, inner, 20f),
                "You must update before playing on this server.", label);

            label.normal.textColor = new Color(0.55f, 0.85f, 1f);
            GUI.Label(new Rect(boxX + pad, boxY + 118f, inner, 20f), _blockedUrl, label);

            GUIStyle btn = new GUIStyle(GUI.skin.button);
            btn.fontSize = 13;
            if (GUI.Button(new Rect(boxX + pad, boxY + boxH - 48f, inner, 30f), "Quit Game", btn))
                Application.Quit();
        }

        public static void CheckAsync()
        {
            MelonCoroutines.Start(CheckSequentialCoro());
        }

        private static IEnumerator CheckSequentialCoro()
        {
            // Wait for the main menu to be ready before showing any dialog
            yield return new WaitForSeconds(10f);

            // Check stable first; only proceed to bleeding-edge if stable is up to date
            bool stableOutdated = false;
            yield return CheckCoro(StableApiUrl, "stable", true, (result) => stableOutdated = result);

            if (!stableOutdated)
                yield return CheckCoro(BleedingEdgeApiUrl, "bleeding-edge", false, null);
        }

        private static IEnumerator CheckCoro(string apiUrl, string channel, bool isStable, Action<bool> onResult)
        {
            bool wasOutdated = false;

            var req = UnityWebRequest.Get(apiUrl);
            req.SetRequestHeader("User-Agent", "Andromeda.Mod/" + BuildInfo.Version);
            yield return req.SendWebRequest();

            if (req.isNetworkError || req.isHttpError)
            {
                MelonLogger.Warning($"[UPDATE] Could not check {channel} releases: {req.error}");
                onResult?.Invoke(wasOutdated);
                yield break;
            }

            ReleaseInfo release;
            try
            {
                release = JsonConvert.DeserializeObject<ReleaseInfo>(req.downloadHandler.text);
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[UPDATE] Failed to parse {channel} release JSON: {e.Message}");
                onResult?.Invoke(wasOutdated);
                yield break;
            }

            if (release == null || string.IsNullOrEmpty(release.tag_name))
            {
                onResult?.Invoke(wasOutdated);
                yield break;
            }

            string remoteTag = release.tag_name.TrimStart('v');
            if (!System.Version.TryParse(remoteTag, out System.Version remote) ||
                !System.Version.TryParse(BuildInfo.Version, out System.Version local))
            {
                MelonLogger.Warning($"[UPDATE] Could not compare versions: local={BuildInfo.Version} remote={release.tag_name} channel={channel}");
                onResult?.Invoke(wasOutdated);
                yield break;
            }

            if (remote > local)
            {
                wasOutdated = true;
                MelonLogger.Msg($"[UPDATE] {channel} update required: v{remoteTag} (you have v{BuildInfo.Version}) — {release.html_url}");

                if (isStable)
                {
                    // Hard block: engage the overlay so the player cannot play
                    _blockedVersion = remoteTag;
                    _blockedUrl = release.html_url;
                    _blocked = true;
                }
                else
                {
                    // Bleeding-edge: informational only (dev channel)
                    MelonLogger.Msg($"[UPDATE] bleeding-edge v{remoteTag} available — {release.html_url}");
                }
            }
            else
            {
                MelonLogger.Msg($"[UPDATE] {channel}: up to date (v{BuildInfo.Version})");
            }
            
            onResult?.Invoke(wasOutdated);
        }

        private class ReleaseInfo
        {
#pragma warning disable 0649
            public string tag_name;
            public string html_url;
#pragma warning restore 0649
        }
    }
}
