using HarmonyLib;

namespace Andromeda.Mod.Patches.Gameplay
{
    [HarmonyPatch(typeof(AndromedaServer), "EndGame")]
    public static class PreventSinglePlayerEndGame
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (DedicatedServerStartup.OnePlayerMode)
            {
                return false; // Skip the EndGame method
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AndromedaServer), "CheckAlive")]
    public static class ForceCheckAliveTrue
    {
        [HarmonyPostfix]
        public static void Postfix(AndromedaServer __instance, ref bool __result)
        {
            if (DedicatedServerStartup.OnePlayerMode)
            {
                if (__instance.isRoundEnded)
                {
                    __instance.isRoundEnded = false;
                }
                __result = true;
            }
        }
    }
}
