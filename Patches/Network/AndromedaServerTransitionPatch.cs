using Andromeda.Mod.Features;
using HarmonyLib;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch]
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

        private static IEnumerator SetupWatchdog(AndromedaServer instance)
        {
            int id = instance.GetInstanceID();
            const float tickSeconds = 10f;
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
            Features.ForceStartFeature.OnGameServerSetup();
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