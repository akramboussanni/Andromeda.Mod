using System;
using HarmonyLib;
using UnityEngine;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch]
    public static class UnlockableButtonSpawnerPatch
    {
        [HarmonyPatch(typeof(UnlockableButtonSpawner), "InitializePerkButton")]
        [HarmonyPrefix]
        public static void Prefix(string guid, ref int tier)
        {
            try
            {
                if (string.IsNullOrEmpty(guid) || ApiData.Profile == null || ApiData.Profile.characters == null)
                    return;

                foreach (var playerChar in ApiData.Profile.characters)
                {
                    if (playerChar.perks == null)
                        continue;

                    foreach (var pGuid in playerChar.perks)
                    {
                        if (pGuid != guid)
                            continue;

                        if (playerChar.ascension >= 6) tier = 2;
                        else tier = 0;
                        return;
                    }
                }
            }
            catch { }
        }
    }

    [HarmonyPatch]
    public static class WhatsNewCoordinatorPatch
    {
        [HarmonyPatch(typeof(WhatsNewCoordinator), "OnEnable")]
        [HarmonyPrefix]
        public static bool Prefix() => false;
    }

    [HarmonyPatch]
    public static class AppQuitPatch
    {
        [HarmonyPatch(typeof(Application), "Quit", new Type[0])]
        [HarmonyPrefix]
        public static void PrefixQuit()
        {
            if (DedicatedServerStartup.IsServer && !string.IsNullOrEmpty(DedicatedServerStartup.SessionId))
            {
                string reason = "Application.Quit() called.";
                try { reason = System.Environment.StackTrace; } catch { }
                try { RestApi.SendShutdown(DedicatedServerStartup.SessionId, reason); } catch { }
            }
        }

        [HarmonyPatch(typeof(Application), "Quit", new Type[] { typeof(int) })]
        [HarmonyPrefix]
        public static void PrefixQuitExitCode(int exitCode)
        {
            if (DedicatedServerStartup.IsServer && !string.IsNullOrEmpty(DedicatedServerStartup.SessionId))
            {
                string reason = "Application.Quit(" + exitCode + ") called.";
                try { reason = System.Environment.StackTrace; } catch { }
                try { RestApi.SendShutdown(DedicatedServerStartup.SessionId, reason); } catch { }
            }
        }
    }

    [HarmonyPatch]
    public static class ReturnToLobbyRedirectPatch
    {
        // Cached once — avoids per-call reflection. With publicized assembly the field is accessible
        // via __instance.displaying, but FieldInfo caching is safe here since this fires at most once
        // per round-end screen dismissal.
        private static readonly System.Reflection.FieldInfo _displayingField =
            typeof(RoundEndScreen).GetField("displaying", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        [HarmonyPatch(typeof(RoundEndScreen), "LeaveGame")]
        [HarmonyPrefix]
        public static bool PrefixRoundEndLeave(RoundEndScreen __instance)
        {
            try { _displayingField?.SetValue(__instance, false); } catch { }
            MainMenu.Instance.LeaveGame();
            return false;
        }

        [HarmonyPatch(typeof(AndromedaClient), "QuitToMenu")]
        [HarmonyPrefix]
        public static bool PrefixQuitToMenu()
        {
            MainMenu.Instance.LeaveGame();
            return false;
        }
    }
}