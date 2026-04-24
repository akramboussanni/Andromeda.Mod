using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Patches.Lobby
{
    [HarmonyPatch(typeof(LobbyListItem), "Set")]
    public static class LobbyListItemSetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LobbyListItem __instance, ApiClient.PartyListResponseData party)
        {
            // Prefer authoritative API maxPlayers; only fallback when backend value is invalid.
            if (party != null)
            {
                var playersTextField = typeof(LobbyListItem).GetField("playersText", BindingFlags.NonPublic | BindingFlags.Instance);
                var text = playersTextField?.GetValue(__instance) as TMPro.TMP_Text;
                if (text != null)
                {
                    int effectiveMax = party.maxPlayers > 0 ? party.maxPlayers : DedicatedServerStartup.MaxPlayers;
                    text.text = $"{party.currentPlayers}/{effectiveMax}";
                }
            }
        }
    }

    [HarmonyPatch(typeof(ProgramClient), "Connect")]
    public static class ProgramClientConnectPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref ApiShared.JoinData data)
        {
            // If the client joins and we're in special server mode, ensure the join response object itself is patched
            // though usually this is handled via response.maxPlayers in the Task return.
        }
    }

    [HarmonyPatch(typeof(AndromedaServerTransitionPatch))]
    public static class AndromedaServerTransitionPatch
    {
        private static readonly HashSet<int> ObjectivesEntered = new HashSet<int>();

        private static (int loadedCount, int totalPlayers) ReadLoadState(AndromedaServer instance)
        {
            var loadedField = typeof(AndromedaServer).GetField("playerLoaded", BindingFlags.NonPublic | BindingFlags.Instance);
            var playersField = typeof(AndromedaServer).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
            if (instance == null || loadedField == null || playersField == null) return (0, 0);

            var loaded = loadedField.GetValue(instance) as Dictionary<PlayerId, bool>;
            var players = playersField.GetValue(instance) as Dictionary<PlayerId, AndromedaServer.Player>;
            return (loaded?.Count(kv => kv.Value) ?? 0, players?.Count ?? 0);
        }

        private static System.Collections.IEnumerator SetupWatchdog(AndromedaServer instance)
        {
            int id = instance.GetInstanceID();
            const float tickSeconds = 10f; // Slower tick for performance
            const float timeoutSeconds = 40f;
            float elapsed = 0f;

            while (elapsed < timeoutSeconds)
            {
                yield return new WaitForSeconds(tickSeconds);
                elapsed += tickSeconds;
                if ((UnityEngine.Object)instance == (UnityEngine.Object)null || ObjectivesEntered.Contains(id)) yield break;
            }

            if ((UnityEngine.Object)instance != (UnityEngine.Object)null && !ObjectivesEntered.Contains(id))
            {
                var finalState = ReadLoadState(instance);
                if (finalState.totalPlayers > 0)
                {
                    MethodInfo objectivesMethod = typeof(AndromedaServer).GetMethod("Objectives", BindingFlags.NonPublic | BindingFlags.Instance);
                    objectivesMethod?.Invoke(instance, null);
                }
            }
        }

        [HarmonyPatch(typeof(AndromedaServer), "Setup")]
        [HarmonyPostfix]
        public static void PostfixSetup(AndromedaServer __instance)
        {
            if (!DedicatedServerStartup.IsServer || __instance == null) return;
            ObjectivesEntered.Remove(__instance.GetInstanceID());
            MelonCoroutines.Start(SetupWatchdog(__instance));
        }

        [HarmonyPatch(typeof(AndromedaServer), "Objectives")]
        [HarmonyPrefix]
        public static void PrefixObjectives(AndromedaServer __instance)
        {
            if (!DedicatedServerStartup.IsServer || __instance == null) return;
            ObjectivesEntered.Add(__instance.GetInstanceID());
        }
    }
}
