using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches.Server
{
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
