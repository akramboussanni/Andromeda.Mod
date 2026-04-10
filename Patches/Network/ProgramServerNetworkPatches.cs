using Andromeda.Mod.Features;
using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Patches
{
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
            if (!DedicatedServerStartup.IsServer && !Andromeda.Mod.Patches.EnvironmentPatch.IsHost()) return;

            if (request != null)
            {
                var versionField = typeof(ProgramShared.JoinRequest).GetField("version", BindingFlags.Public | BindingFlags.Instance);
                if (versionField != null)
                {
                    versionField.SetValue(request, Version.Value);
                }
            }

            try
            {
                var gamemodeField = typeof(ProgramServer).GetField("gamemode", BindingFlags.NonPublic | BindingFlags.Instance);
                var gamemode = gamemodeField?.GetValue(__instance);
                if (gamemode != null)
                {
                    var maxPlayersField = gamemode.GetType().GetField("maxPlayers", BindingFlags.NonPublic | BindingFlags.Instance);
                    maxPlayersField?.SetValue(gamemode, DedicatedServerStartup.MaxPlayers);
                }
            }
            catch { }

            ForceStartFeature.OnPlayerCountChanged(GetProgramServerPlayerCount(__instance));
        }

        [HarmonyPatch(typeof(ProgramServer), "OnLeave")]
        [HarmonyPrefix]
        public static void PrefixOnLeave(ProgramServer __instance, PlayerId playerId)
        {
            if (DedicatedServerStartup.IsServer)
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
            catch
            {
                return 0;
            }
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
}