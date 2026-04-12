using Andromeda.Mod.Features;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UniRx.Async;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch(typeof(Scenes), "Load", new[] { typeof(string[]) })]
    public static class ScenesLoadPathSafetyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string[] paths)
        {
            if (paths == null) return;

            // Remove null/whitespace paths — LoadSceneAsync returns null for empty names,
            // which crashes Level.LoadServer with 'Value cannot be null (asyncOperation)'.
            paths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
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

    // Guard: prevent AndromedaClient from processing any messages on the dedicated server.
    // NetServer.SendAllReliable loops back to server-local entities, so without this guard
    // the server's own AndromedaClient instance fires OnPlayerList, OnNotify, etc. - causing
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
        public static bool PrefixInitialize() => false;

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
                __result = 0f;
                return null;
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
            // Intentionally left as compatibility hook.
        }
    }

    [HarmonyPatch]
    public static class ProgramServerPatch
    {
        /// <summary>
        /// Skips ProgramClient.Awake entirely on the dedicated server.
        /// In headless/batch mode Screen.resolutions returns an empty array, so the
        /// vanilla Awake crashes immediately at:
        ///   Screen.SetResolution(Screen.resolutions[LocalUserData.ResolutionIndex].width, ...)
        /// This stub is registered manually from Mod.cs before PatchAll runs.
        /// </summary>
        public static bool PrefixClientAwakeStub()
        {
            // Return false = skip original Awake; only skip in server mode.
            return !(DedicatedServerStartup.IsServer || Andromeda.Mod.Patches.EnvironmentPatch.IsHost());
        }

        [HarmonyPatch(typeof(ProgramServer), "Host")]
        [HarmonyPrefix]
        public static void PrefixHost(string name, GamemodeList.Key gamemodeKey)
        {
            NetworkDebugger.LogLobbyEvent($"[SERVER-STARTED] Hosting: {name} (Mode: {gamemodeKey})");
        }

        [HarmonyPatch(typeof(ProgramServer), "OnJoin")]
        [HarmonyPrefix]
        public static void PrefixOnJoin(ProgramServer __instance, PlayerId playerId, ProgramShared.JoinRequest request)
        {
            if (!DedicatedServerStartup.IsServer && !Andromeda.Mod.Patches.EnvironmentPatch.IsHost()) return;

            // Handshake version bypass - ensures clients can always connect to the modded server
            if (request != null)
            {
                var versionField = typeof(ProgramShared.JoinRequest).GetField("version", BindingFlags.Public | BindingFlags.Instance);
                if (versionField != null)
                {
                    versionField.SetValue(request, Version.Value);
                }
            }

            // Restored from 0.11.1 ProgramServerJoinPatch:
            // Override the gamemode's max players right before the join check so that
            // ProgramServer doesn't reject players beyond the hardcoded gamemode cap.
            try
            {
                var gamemode = Traverse.Create(__instance).Field("gamemode").GetValue();
                if (gamemode != null)
                {
                    Traverse.Create(gamemode).Field("maxPlayers").SetValue(DedicatedServerStartup.MaxPlayers);
                }
            }
            catch { }

            ForceStartFeature.OnPlayerCountChanged(GetProgramServerPlayerCount(__instance));
        }

        [HarmonyPatch(typeof(ProgramServer), "OnLeave")]
        [HarmonyPrefix]
        public static void PrefixOnLeave(ProgramServer __instance, PlayerId playerId)
        {
            if (!DedicatedServerStartup.IsServer && !Andromeda.Mod.Patches.EnvironmentPatch.IsHost()) return;
            NetworkDebugger.LogLobbyEvent($"[SERVER] Player Left: {playerId}");
            ForceStartFeature.OnPlayerCountChanged(GetProgramServerPlayerCount(__instance) - 1);
        }

        private static int GetProgramServerPlayerCount(ProgramServer instance)
        {
            if (instance == null) return 0;
            try
            {
                var playersField = typeof(ProgramServer).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
                var players = playersField?.GetValue(instance) as System.Collections.IDictionary;
                return players?.Count ?? 0;
            }
            catch { return 0; }
        }
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
                        codes[i].opcode = CodeInstruction.Call(typeof(DedicatedServerStartup), "get_MaxPlayers").opcode;
                        codes[i].operand = CodeInstruction.Call(typeof(DedicatedServerStartup), "get_MaxPlayers").operand;
                        patchedCount++;
                    }
                }
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
                        codes[i].opcode = CodeInstruction.Call(typeof(DedicatedServerStartup), "get_MaxPlayers").opcode;
                        codes[i].operand = CodeInstruction.Call(typeof(DedicatedServerStartup), "get_MaxPlayers").operand;
                        patchedCount++;
                    }
                }
                if (patchedCount > 0)
                    MelonLogger.Msg($"[PATCH] ApiShared.GamesNew maxPlayers (12 -> dynamic) - Patched {patchedCount} occurrence(s).");
                return codes;
            }
        }
    }
}