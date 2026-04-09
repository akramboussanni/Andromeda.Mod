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
    [HarmonyPatch(typeof(Scenes), "Load", new[] { typeof(string[]) })]
    public static class ScenesLoadPathSafetyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string[] paths)
        {
            if (paths == null) return;

            int originalCount = paths.Length;
            paths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

            if (paths.Length != originalCount)
            {
                MelonLogger.Warning($"[SCENES] Removed {originalCount - paths.Length} invalid scene path(s) before load.");
            }
        }
    }

    [HarmonyPatch(typeof(PurchaseFundsModal), "Start")]
    public static class PurchaseFundsModalStartSafetyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PurchaseFundsModal __instance)
        {
            SafeStart(__instance);
            return false;
        }

        private static async void SafeStart(PurchaseFundsModal modal)
        {
            await UniTask.WaitUntil(() => ApiData.IsLoaded);

            var tr = Traverse.Create(modal);
            var offerRoot = tr.Field("offerRoot").GetValue<Transform>();
            var offerPrefab = tr.Field("fundOfferPrefab").GetValue<FundOfferListItem>();
            var offerDisplayData = tr.Field("offerDisplayData").GetValue<PurchaseFundsModal.FundOfferDisplayData[]>();

            if (offerRoot == null || offerPrefab == null || offerDisplayData == null)
            {
                MelonLogger.Warning("[FUNDS] PurchaseFundsModal missing references; skipping offer render.");
                return;
            }

            foreach (Transform child in offerRoot.Cast<Transform>().ToList())
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            var offers = ApiData.FundOffers;
            if (offers == null || offers.Count == 0)
            {
                MelonLogger.Warning("[FUNDS] No fund offers available from API; purchase modal will be empty.");
                return;
            }

            var baselineOffer = offers.Values
                .Where(o => o != null && o.cost > 0)
                .OrderBy(o => o.amount / o.cost)
                .FirstOrDefault();

            if (baselineOffer == null)
            {
                MelonLogger.Warning("[FUNDS] No valid fund offers with positive cost; purchase modal will be empty.");
                return;
            }

            float baselineRatio = baselineOffer.amount / baselineOffer.cost;
            if (baselineRatio <= 0f) baselineRatio = 1f;

            foreach (var display in offerDisplayData)
            {
                ApiClient.FundOfferData offer;
                if (!offers.TryGetValue(display.guid, out offer) || offer == null) continue;
                if (offer.cost <= 0) continue;

                float ratio = offer.amount / offer.cost;
                float discount = ratio / baselineRatio - 1f;

                UnityEngine.Object.Instantiate(offerPrefab, offerRoot).Initialize(
                    display.guid,
                    offer.description,
                    offer.cost,
                    discount,
                    display.icon,
                    () => tr.Method("InitiatePurchase", offer).GetValue()
                );
            }
        }
    }

    internal static class EntityRedirectFilter
    {
        public static bool ShouldRedirect(Entity.Base entity)
        {
            if (entity == null) return false;

            var type = entity.GetType();
            string typeName = type.Name ?? string.Empty;

            // Dedicated runtime should only rewrite server-side entity components.
            // Client/proxy/shared components should keep original behavior.
            return typeName.EndsWith("Server", StringComparison.Ordinal);
        }
    }

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
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

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
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

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
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

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
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

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
            if (DedicatedServerStartup.IsServer && Application.isBatchMode)
            {
                MelonLogger.Msg("[SERVER-BOOT] Skipping ProgramClient.Awake via Hard-Link (resolution crash fixed).");
                return false;
            }

            if (DedicatedServerStartup.IsServer && !Application.isBatchMode)
            {
                MelonLogger.Warning("[SERVER-BOOT] IsServer=true but Application.isBatchMode=false; allowing ProgramClient.Awake to avoid client-side null state.");
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
        public static bool BlockOnServer() => !(DedicatedServerStartup.IsServer && Application.isBatchMode);
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
        private static bool _initialPhaseAdvanceApplied;

        [HarmonyPatch(typeof(AndromedaClient), "OnLoadLevel")]
        [HarmonyPrefix]
        public static void ResetPhaseGate()
        {
            _lastPhaseHandled = AndromedaShared.RoundPhase.None;
            _lastPhaseTimeReceivedAt = -999f;
            _initialPhaseAdvanceApplied = false;
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

            // ReadyRoom can fire multiple SetEndTime updates.
            // Apply the first phase advance once at match start, then suppress repeats.
            if (client.Phase == AndromedaShared.RoundPhase.ReadyRoom)
            {
                if (!_initialPhaseAdvanceApplied)
                {
                    _initialPhaseAdvanceApplied = true;
                    trackPhase = true;
                    return;
                }

                trackPhase = false;
            }
        }
    }

    [HarmonyPatch]
    public static class EarlyArmoryItemGuard
    {
        private static float _warningUntil;
        private static GUIStyle _warningStyle;
        private static bool _armoryReachedThisRound;
        private static bool _cheatLockActiveThisRound;
        private static bool _alertPlayedThisRound;

        private static readonly HashSet<ItemSpawnList.Key> EarlyArmoryRestrictedItems = new HashSet<ItemSpawnList.Key>
        {
            ItemSpawnList.Key.Wrench_charged,
            ItemSpawnList.Key.Wrench_uncharged,
            ItemSpawnList.Key.Sledgehammer_charged,
            ItemSpawnList.Key.Sledgehammer_uncharged,
            ItemSpawnList.Key.ThrowingAxe_charged,
            ItemSpawnList.Key.ThrowingAxe_uncharged,
            ItemSpawnList.Key.Knife_charged,
            ItemSpawnList.Key.Knife_uncharged,
        };

        private static bool ShouldBlock(ItemSpawnList.Key key)
        {
            var client = AndromedaClient.Instance;
            if (client == null) return false;
            if (!EarlyArmoryRestrictedItems.Contains(key)) return false;

            if (client.Phase == AndromedaShared.RoundPhase.Armory)
                _armoryReachedThisRound = true;

            return !_armoryReachedThisRound;
        }

        private static void TriggerWarning()
        {
            _cheatLockActiveThisRound = true;
            _warningUntil = Time.time + 99999f;

            if (_alertPlayedThisRound)
                return;

            _alertPlayedThisRound = true;
            try
            {
                var client = AndromedaClient.Instance;
                if ((UnityEngine.Object)client == (UnityEngine.Object)null)
                    return;

                var tr = Traverse.Create(client);
                var clip = tr.Field("RoundStartVoiceLine").GetValue<AudioClip>()
                    ?? tr.Field("RoundStartAudio").GetValue<AudioClip>();
                var mixer = tr.Field("mixerGroup").GetValue<UnityEngine.Audio.AudioMixerGroup>();
                if ((UnityEngine.Object)clip != (UnityEngine.Object)null && (UnityEngine.Object)OneShotAudioPlayer.Instance != (UnityEngine.Object)null)
                {
                    // Stack the same alert 3x to make the punishment cue much louder.
                    OneShotAudioPlayer.Instance.PlayNonSpatialized(clip, mixer, 1f, 0.75f);
                    OneShotAudioPlayer.Instance.PlayNonSpatialized(clip, mixer, 1f, 0.8f);
                    OneShotAudioPlayer.Instance.PlayNonSpatialized(clip, mixer, 1f, 0.7f);
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(WorldItem), "Interact")]
        [HarmonyPrefix]
        public static bool PrefixWorldItemInteract(WorldItem __instance)
        {
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null) return true;
            if (!ShouldBlock(__instance.Key)) return true;

            TriggerWarning();
            return false;
        }

        [HarmonyPatch(typeof(ToolbeltServer), "PickupItem")]
        [HarmonyPrefix]
        public static bool PrefixToolbeltPickup(ItemSpawnList.Key key)
        {
            if (!ShouldBlock(key)) return true;

            TriggerWarning();
            return false;
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnLoadLevel")]
        [HarmonyPrefix]
        public static void ResetRoundArmoryState()
        {
            _armoryReachedThisRound = false;
            _cheatLockActiveThisRound = false;
            _alertPlayedThisRound = false;
            _warningUntil = -1f;
            GameInput.IsBlockedCinematic = false;
        }

        [HarmonyPatch(typeof(AndromedaClient), "OnPhaseTime")]
        [HarmonyPrefix]
        public static void TrackArmoryReached(AndromedaClient __instance)
        {
            if ((UnityEngine.Object)__instance == (UnityEngine.Object)null) return;
            if (__instance.Phase == AndromedaShared.RoundPhase.Armory)
                _armoryReachedThisRound = true;
        }

        public static void DrawWarningOverlay()
        {
            if (_cheatLockActiveThisRound)
            {
                // Keep gameplay input blocked for the current round as anti-cheat punishment.
                GameInput.IsBlockedCinematic = true;
            }

            if (Time.time > _warningUntil) return;

            if (_warningStyle == null)
            {
                _warningStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 72,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.2f, 0.2f, 1f) }
                };
            }

            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.Label(full, "PISS YOURSELF", _warningStyle);
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