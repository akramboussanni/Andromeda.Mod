using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine.Networking;

namespace Andromeda.Mod.Patches
{
    public static class ApiWarmupMessagePatch
    {
        private static DateTime _nextPromptUtc = DateTime.MinValue;

        public static void Postfix(UnityWebRequest request)
        {
            try
            {
                if (request == null)
                    return;

                if (!IsMatchmakingEndpoint(request.url))
                    return;

                int statusCode = (int)request.responseCode;
                string message = request.downloadHandler?.text ?? string.Empty;

                if (!LooksLikeWarmupState(statusCode, message))
                    return;

                var now = DateTime.UtcNow;
                if (now < _nextPromptUtc)
                    return;

                _nextPromptUtc = now.AddSeconds(20);

                // Avoid generic async helper calls here: Harmony dynamic IL emit can fail on some generic imports.
                _ = Dialog.Prompt("Please wait a little, servers are booting.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[WARMUP-PATCH] Failed to show warmup message: {ex.Message}");
            }
        }

        private static bool IsMatchmakingEndpoint(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.IndexOf("/games/new", StringComparison.OrdinalIgnoreCase) >= 0
                   || url.IndexOf("/games/join", StringComparison.OrdinalIgnoreCase) >= 0
                   || url.IndexOf("/party/create", StringComparison.OrdinalIgnoreCase) >= 0
                   || url.IndexOf("/party/join", StringComparison.OrdinalIgnoreCase) >= 0
                   || url.IndexOf("/match/start", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeWarmupState(int statusCode, string message)
        {
            if (statusCode == 200)
                return false;

            string text = (message ?? string.Empty).Trim();
            if (text.Length == 0)
                return false;

            string lower = text.ToLowerInvariant();
            bool hasWarmupHint = lower.Contains("boot")
                                 || lower.Contains("starting")
                                 || lower.Contains("warming")
                                 || lower.Contains("provision")
                                 || lower.Contains("initializ")
                                 || lower.Contains("please wait")
                                 || lower.Contains("try again");

            // Only trigger for expected transient states.
            bool isTransientStatus = statusCode == 425 || statusCode == 429 || statusCode == 503 || statusCode == 504;
            return hasWarmupHint || isTransientStatus;
        }
    }
}
