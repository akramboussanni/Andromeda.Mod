using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Andromeda.Mod.Patches.Ui
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
                    if (playerChar.perks != null)
                    {
                        foreach (var pGuid in playerChar.perks)
                        {
                            if (pGuid == guid)
                            {
                                if (playerChar.ascension >= 6) tier = 2; // Gold
                                else tier = 0; // Blue
                                return;
                            }
                        }
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
                RestApi.SendShutdown(DedicatedServerStartup.SessionId, reason);
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
                RestApi.SendShutdown(DedicatedServerStartup.SessionId, reason);
            }
        }
    }

    [HarmonyPatch]
    public static class ReturnToLobbyRedirectPatch
    {
        [HarmonyPatch(typeof(RoundEndScreen), "LeaveGame")]
        [HarmonyPrefix]
        public static bool PrefixRoundEndLeave(RoundEndScreen __instance)
        {
            try
            {
                var displayingField = typeof(RoundEndScreen).GetField("displaying", BindingFlags.NonPublic | BindingFlags.Instance);
                if (displayingField != null)
                    displayingField.SetValue(__instance, false);
            }
            catch { }

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
