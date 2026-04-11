using HarmonyLib;
using System;
using UnityEngine;

namespace Andromeda.Mod.Patches.Network
{
    /// <summary>
    /// Restored from proper_net.cs.
    /// Handles synchronization logic for phase clocks and selection updates.
    /// </summary>
    [HarmonyPatch]
    public static class AndromedaSyncPatches
    {
        private static AndromedaShared.RoundPhase _lastPhaseHandled = AndromedaShared.RoundPhase.None;
        private static float _lastPhaseTimeReceivedAt = -999f;

        [HarmonyPatch(typeof(AndromedaClient), "OnLoadLevel")]
        [HarmonyPrefix]
        public static void ResetPhaseGate()
        {
            _lastPhaseHandled = AndromedaShared.RoundPhase.None;
            _lastPhaseTimeReceivedAt = -999f;
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnPhaseTime")]
        [HarmonyPrefix]
        public static bool PrefixOnPhaseTime(AndromedaClient __instance)
        {
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null) return true;

            // Rate-limit identical phase updates to prevent "double-step" bugs
            var currentPhase = __instance.Phase;
            bool samePhase = currentPhase == _lastPhaseHandled;
            bool recentUpdate = Time.time - _lastPhaseTimeReceivedAt < 2.0f;

            if (samePhase && recentUpdate)
            {
                return false;
            }

            _lastPhaseHandled = currentPhase;
            _lastPhaseTimeReceivedAt = Time.time;
            return true;
        }

        [HarmonyPatch(typeof(PhaseClock), "SetEndTime")]
        [HarmonyPrefix]
        public static void RefinedPhaseAdvanceSync(ref bool trackPhase)
        {
            var client = AndromedaClient.Instance;
            if (client == null) return;

            // Suppress auto-increment during ReadyRoom (symbiont selection)
            if (client.Phase == AndromedaShared.RoundPhase.ReadyRoom)
            {
                trackPhase = false;
            }
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnSetPlayerSelections")]
        public static class AndromedaSelectionDesyncPatch
        {
            private static float _lastSelectionsReceivedAt = -10f;

            [HarmonyPrefix]
            public static bool Prefix()
            {
                float now = Time.time;
                if (now - _lastSelectionsReceivedAt < 1.0f)
                {
                    // Rate-limit selection updates to prevent "Key already added" crashes
                    return false;
                }
                _lastSelectionsReceivedAt = now;
                return true;
            }
        }
    }
}
