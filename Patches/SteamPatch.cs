using HarmonyLib;
using MelonLoader;
using System.Reflection;
using System;

namespace Andromeda.Mod.Patches
{
    public static class SteamPatch
    {
        [HarmonyPatch(typeof(Steam), "get_AuthToken")]
        [HarmonyPrefix]
        public static bool PrefixAuthToken(ref string __result)
        {
            if (DedicatedServerStartup.IsServer)
            {
                __result = "DEDICATED_SERVER_TOKEN";
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Steam), "Awake")]
        [HarmonyPrefix]
        public static bool PrefixAwake(Steam __instance)
        {
            if (DedicatedServerStartup.IsServer)
            {
                MelonLogger.Msg("[STEAM] Bypassing SteamClient.Init for Dedicated Server.");
                var field = typeof(Steam).GetField("authToken", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) field.SetValue(__instance, "DEDICATED_SERVER_TOKEN");
                return false;
            }
            return true;
        }
    }

    public static class ApiSharedPatch
    {
        [HarmonyPatch(typeof(ApiShared), "AuthToken", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool PrefixAuthToken(ref string __result)
        {
            if (DedicatedServerStartup.IsServer)
            {
                __result = "DEDICATED_SERVER_TOKEN";
                return false;
            }
            return true;
        }
    }
}
