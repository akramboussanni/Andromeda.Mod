using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches.Gameplay
{
    /// <summary>
    /// Patches for OnePlayerMode (Single-Player / Sandbox).
    /// Prevents the game from automatically ending due to win/loss conditions,
    /// while still allowing manual EndGame calls (e.g. from chat commands).
    /// </summary>

    [HarmonyPatch(typeof(AndromedaServer), "CheckAlive")]
    public static class ForceCheckAliveTrue
    {
        [HarmonyPrefix]
        public static bool Prefix(AndromedaServer __instance, ref bool __result)
        {
            if (DedicatedServerStartup.OnePlayerMode)
            {
                // In OnePlayerMode, we ignore automatic win/loss conditions.
                // We keep isRoundEnded false and return true to keep the game loop running.
                __instance.GetType().GetField("isRoundEnded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(__instance, false);
                
                __result = true;
                return false; // Skip the original CheckAlive logic
            }
            return true;
        }
    }
}
