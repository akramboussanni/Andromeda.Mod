using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Andromeda.Mod.Patches
{
    // Using global:: to ensure we hit the game's Environment class and not System.Environment
    [HarmonyPatch(typeof(global::Environment))]
    public static class EnvironmentPatch
    {
        [HarmonyPatch("get_IsClient")]
        [HarmonyPrefix]
        public static bool PrefixIsClient(ref bool __result)
        {
            __result = !DedicatedServerStartup.IsServer;
            return false;
        }

        [HarmonyPatch("get_IsServer")]
        [HarmonyPrefix]
        public static bool PrefixIsServer(ref bool __result)
        {
            __result = DedicatedServerStartup.IsServer;
            return false;
        }

        public static bool IsHost()
        {
            if (DedicatedServerStartup.IsServer)
                return true;

            if (Singleton.Existing<ProgramServer>() != null)
                return true;

            return Object.FindObjectOfType<ProgramServer>() != null;
        }
    }
}
