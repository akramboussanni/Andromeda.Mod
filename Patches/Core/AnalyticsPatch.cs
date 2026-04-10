using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch]
    public static class AnalyticsPatch
    {
        [HarmonyPatch(typeof(DeltaDNA.DDNA), "StartSDK", new System.Type[] { typeof(DeltaDNA.Configuration), typeof(string) })]
        [HarmonyPrefix]
        public static bool Prefix()
        {
            MelonLogger.Msg("[ANALYTICS] Blocked DeltaDNA SDK startup to prevent network spam.");
            return false;
        }

        [HarmonyPatch(typeof(DeltaDNA.Logger), "LogWarning")]
        [HarmonyPrefix]
        public static bool FilterLogs(string msg)
        {
            if (msg != null && msg.Contains("deltadna")) return false;
            return true;
        }
    }
}
