using System;
using HarmonyLib;
using MelonLoader;
using UniRx.Async;
using UnityEngine.Networking;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch(typeof(ApiShared), "ParseResponse")]
    public static class ApiWarmupMessagePatch
    {
        private static DateTime _nextPromptUtc = DateTime.MinValue;

        [HarmonyPostfix]
        public static void Postfix(UnityWebRequest request, ref ApiShared.Response __result)
        {
            try
            {
                if (request == null || __result == null)
                    return;

                if (!IsMatchmakingEndpoint(request.url))
                    return;

                if (!LooksLikeWarmupState(__result.status, __result.message))
                    return;

                var now = DateTime.UtcNow;
                if (now < _nextPromptUtc)
                    return;

                _nextPromptUtc = now.AddSeconds(20);
                Dialog.Prompt("Please wait a little, servers are booting.").Forget<bool>();
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

        private static bool LooksLikeWarmupState(int status, string message)
        {
            if (status == 200)
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
            bool isTransientStatus = status == 425 || status == 429 || status == 503 || status == 504;
            return hasWarmupHint || isTransientStatus;
        }
    }
}
