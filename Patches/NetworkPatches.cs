using HarmonyLib;
using MelonLoader;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System;
using System.IO;
using System.Diagnostics;
using UnityEngine;
using UniRx.Async;
using Windwalk.Net;
using System.Linq;
using System.Reflection.Emit;

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

    [HarmonyPatch(typeof(Entity.Base), "SendReliableToRoom")]
    public static class EntityBaseSendReliableToRoomPatch
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

    [HarmonyPatch(typeof(Entity.Base), "SendUnreliable")]
    public static class EntityBaseSendUnreliablePatch
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
                _cachedServer.SendAllUnreliable(_cachedMsg);
                return false;
            } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendUnreliableToRoom")]
    public static class EntityBaseSendUnreliableToRoomPatch
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
                // Treat room broadcast as global broadcast for dedicated server
                _cachedServer.SendAllUnreliable(_cachedMsg);
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
        private static bool MatchLdc12(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_S && (instruction.operand is sbyte s && s == 12)) return true;
            if (instruction.opcode == OpCodes.Ldc_I4 && (instruction.operand is int i && i == 12)) return true;
            return false;
        }

        [HarmonyPatch]
        public static class GamesCustomNewTranspiler
        {
            public static MethodBase TargetMethod()
            {
                Type type = typeof(ApiShared).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(t => t.Name.Contains("GamesCustomNew") && t.Name.Contains("d__"));
                return type?.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                int patchedCount = 0;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (MatchLdc12(codes[i]))
                    {
                        codes[i].opcode = OpCodes.Call;
                        codes[i].operand = AccessTools.PropertyGetter(typeof(DedicatedServerStartup), nameof(DedicatedServerStartup.MaxPlayers));
                        patchedCount++;
                    }
                }
                // Only log if we actually found something, but don't warn if it fails here 
                // because PatchAll might pick this up and we want to avoid double-processing logs
                if (patchedCount > 0) 
                    MelonLogger.Msg($"[PATCH] ApiShared.GamesCustomNew maxPlayers (12 -> dynamic) - Patched {patchedCount} occurrence(s).");
                return codes;
            }
        }

        [HarmonyPatch]
        public static class GamesNewTranspiler
        {
            public static MethodBase TargetMethod()
            {
                Type type = typeof(ApiShared).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(t => t.Name.Contains("GamesNew") && t.Name.Contains("d__"));
                return type?.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                int patchedCount = 0;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (MatchLdc12(codes[i]))
                    {
                        codes[i].opcode = OpCodes.Call;
                        codes[i].operand = AccessTools.PropertyGetter(typeof(DedicatedServerStartup), nameof(DedicatedServerStartup.MaxPlayers));
                        patchedCount++;
                    }
                }
                if (patchedCount > 0) 
                    MelonLogger.Msg($"[PATCH] ApiShared.GamesNew maxPlayers (12 -> dynamic) - Patched {patchedCount} occurrence(s).");
                return codes;
            }
        }
    }

    [HarmonyPatch]
    public static class AndromedaPhaseClockDesyncPatch
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
            // Some server transitions send redundant PhaseTime packets
            var currentPhase = __instance.Phase;
            bool samePhase = currentPhase == _lastPhaseHandled;
            bool recentUpdate = Time.time - _lastPhaseTimeReceivedAt < 2.0f;

            if (samePhase && recentUpdate) 
            {
                // We already handled a PhaseTime message for this phase recently, skip UI re-advance
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

            // CRITICAL: The 10s "Ready Room" countdown (symbiont selection) 
            // uses phaseIndex=-1 which auto-increments the dots. 
            // We suppress that increment here so the FIRST dot only 
            // lights up when the actual match phases start.
            if (client.Phase == AndromedaShared.RoundPhase.ReadyRoom)
            {
                trackPhase = false;
            }
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
                // Rate-limit selection updates to prevent "Key already added" crashes in UI
                // caused by redundant network packets or double-initialization.
                return false; 
            }
            _lastSelectionsReceivedAt = now;
            return true;
        }
    }

    [HarmonyPatch]
    public static class VoiceUIHUDSafetyPatch
    {
        private static FieldInfo[] _cachedFields;
        public static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VoiceUIHUD"), "Initialize");

        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                // Optimization: Cache reflection fields to prevent performance hits if Initialize() is called repeatedly.
                if (_cachedFields == null)
                {
                    _cachedFields = __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                foreach (var field in _cachedFields)
                {
                    if (typeof(System.Collections.IDictionary).IsAssignableFrom(field.FieldType))
                    {
                        var dict = field.GetValue(__instance) as System.Collections.IDictionary;
                        dict?.Clear();
                    }
                }
            }
            catch { }
        }
    }

    [HarmonyPatch]
    public static class VoiceClientSafetyPatch
    {
        public static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VoiceClient"), "GetPlayerVolume");

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, ref float __result)
        {
            if (__exception != null)
            {
                // Safety: Silence NREs in VoiceClient during UI rendering loops.
                // These usually occur when the Sidebar UI is trying to render player volume data
                // that hasn't arrived over the network yet.
                __result = 0f;
                return null; // Suppress the exception
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LobbyListItem), "Set")]
    public static class LobbyListItemSetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LobbyListItem __instance, ApiClient.PartyListResponseData party)
        {
            // If we are looking at an Andromeda server with a custom lobby size, force the UI to show it correctly
            if (party != null && (DedicatedServerStartup.IsServer || Andromeda.Mod.Patches.EnvironmentPatch.IsHost()))
            {
                var playersTextField = typeof(LobbyListItem).GetField("playersText", BindingFlags.NonPublic | BindingFlags.Instance);
                var text = playersTextField?.GetValue(__instance) as TMPro.TMP_Text;
                if (text != null)
                {
                    text.text = $"{party.currentPlayers}/{DedicatedServerStartup.MaxPlayers}";
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