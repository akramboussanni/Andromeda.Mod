using HarmonyLib;
using MelonLoader;
using System.Reflection;
using System.Collections.Generic;
using System;
using System.IO;
using System.Diagnostics;
using UnityEngine;
using UniRx.Async;
using Windwalk.Net;
using System.Linq;

namespace Andromeda.Mod.Patches
{
    // Nuclear Fix: Redirect ALL entity messaging from NetClient to NetServer when running as a server.
    // Optimized with cached Singleton lookup to minimize per-frame overhead.
    [HarmonyPatch(typeof(Entity.Base), "SendReliable")]
    public static class EntityBaseSendReliablePatch
    {
        private static NetServer _cachedServer;
        private static readonly Entity.Message _cachedMsg = new Entity.Message();

        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;

                _cachedMsg.id = __instance.id;
                _cachedMsg.componentType = __instance.ComponentType;
                _cachedMsg.Body = body;
                _cachedServer.SendAllReliable(_cachedMsg);
                return false;
            } catch { return true; }
        }
    }

    [HarmonyPatch]
    public static class ProgramServerPatch
    {
        [HarmonyPatch(typeof(ProgramServer), "Host")]
        [HarmonyPrefix]
        public static void PrefixHost(string region, string sessionId, string name, GamemodeList.Key gamemodeKey)
        {
            NetworkDebugger.LogLobbyEvent($"[SERVER-STARTED] Hosting: {name} (Mode: {gamemodeKey})");
        }

        [HarmonyPatch(typeof(ProgramServer), "OnJoin")]
        [HarmonyPrefix]
        public static void PrefixOnJoin(ProgramServer __instance, PlayerId playerId, ProgramShared.JoinRequest request)
        {
            if (!DedicatedServerStartup.IsServer) return;

            // Handshake version bypass
            if (request != null)
            {
                var versionField = typeof(ProgramShared.JoinRequest).GetField("version", BindingFlags.Public | BindingFlags.Instance);
                if (versionField != null)
                {
                    versionField.SetValue(request, Version.Value);
                }
            }
        }

        [HarmonyPatch(typeof(ProgramServer), "OnLeave")]
        [HarmonyPrefix]
        public static void PrefixOnLeave(ProgramServer __instance, PlayerId playerId)
        {
            if (!DedicatedServerStartup.IsServer) return;
            NetworkDebugger.LogLobbyEvent($"[SERVER] Player Left: {playerId}");
        }

        public static bool PrefixClientAwakeStub()
        {
            if (DedicatedServerStartup.IsServer)
            {
                MelonLogger.Msg("[SERVER-BOOT] Skipping ProgramClient.Awake via Hard-Link (resolution crash fixed).");
                return false;
            }
            return true;
        }
    }

    // Guard: prevent AndromedaClient from processing any messages on the dedicated server.
    // NetServer.SendAllReliable loops back to server-local entities, so without this guard
    // the server's own AndromedaClient instance fires OnPlayerList, OnNotify, etc. — causing
    // every event to be processed (and logged) twice.
    [HarmonyPatch(typeof(AndromedaClient), "Setup")]
    public static class AndromedaClientServerGuardPatch
    {
        [HarmonyPrefix]
        public static bool BlockOnServer() => !DedicatedServerStartup.IsServer;
    }

    [HarmonyPatch]
    public static class GameliftPatch
    {
        [HarmonyPatch(typeof(Gamelift), "Initialize")]
        [HarmonyPrefix]
        public static bool PrefixInitialize() => false; // Skip AWS init

        [HarmonyPatch(typeof(Gamelift), "ValidatePlayerSession")]
        [HarmonyPrefix]
        public static bool PrefixValidate(ref bool __result)
        {
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(Gamelift), "RemovePlayerSession")]
        [HarmonyPrefix]
        public static bool PrefixRemove() => false;

        [HarmonyPatch(typeof(Gamelift), "End")]
        [HarmonyPrefix]
        public static bool PrefixEnd() => false;
    }

    [HarmonyPatch]
    public static class ApiClientPartyJoinPatch
    {
        [HarmonyPatch(typeof(ApiShared), "GamesCustomNew")]
        [HarmonyPrefix]
        public static void PrefixGamesCustomNew(string region, GamemodeList.Key gamemodeKey)
        {
            MelonLogger.Msg($"[REST] Registering server: {gamemodeKey} in {region}");
        }
    }

    [HarmonyPatch]
    public static class AndromedaPhaseClockDesyncPatch
    {
        private static AndromedaShared.RoundPhase _lastPhase = AndromedaShared.RoundPhase.None;
        private static float _lastAllowedPhaseTimeAt = -999f;

        [HarmonyPatch(typeof(AndromedaClient), "OnLoadLevel")]
        [HarmonyPrefix]
        public static void ResetPhaseTimeGate()
        {
            _lastPhase = AndromedaShared.RoundPhase.None;
            _lastAllowedPhaseTimeAt = -999f;
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnPhaseTime")]
        [HarmonyPrefix]
        public static bool PrefixOnPhaseTime(AndromedaClient __instance)
        {
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null) return true;

            var phase = __instance.Phase;
            bool samePhase = phase == _lastPhase;
            bool timerAlreadyActive = __instance.PhaseEndTime > Time.time + 1f;
            bool rapidRepeat = Time.time - _lastAllowedPhaseTimeAt < 3f;

            if (samePhase && timerAlreadyActive && rapidRepeat) return false;

            _lastPhase = phase;
            _lastAllowedPhaseTimeAt = Time.time;
            return true;
        }

        // The ReadyRoom PhaseTime (symbiont selection countdown) fires before any real
        // game phase but still calls SetEndTime with phaseIndex=-1, which auto-increments
        // currentPhase from null to 0 and pushes every subsequent indicator 1 slot ahead.
        // Suppress the increment for that phase only — the timer still runs, it just
        // doesn't advance the phase dots.
        [HarmonyPatch(typeof(PhaseClock), "SetEndTime")]
        [HarmonyPrefix]
        public static void SkipReadyRoomPhaseAdvance(ref bool trackPhase)
        {
            var client = AndromedaClient.Instance;
            if (client == null) return;
            if (client.Phase == AndromedaShared.RoundPhase.ReadyRoom)
                trackPhase = false;
        }
    }

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