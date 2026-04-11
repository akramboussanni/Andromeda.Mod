using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Andromeda.Mod.Patches.Network
{
    [HarmonyPatch(typeof(ProgramServer), "Quit", new[] { typeof(string) })]
    public static class ProgramServerQuitSafetyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(string reason)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            // Prevent server from quitting during startup if we are in OnePlayerMode.
            // AndromedaServer.Setup() normally quits if count < 2.
            if (DedicatedServerStartup.OnePlayerMode && reason == "player failed to connect")
            {
                MelonLogger.Msg("[STARTUP] Suppressing shutdown for single-player connection requirement.");
                return false;
            }

            // Also prevent empty timeout in OnePlayerMode/Debug mode for easier testing.
            if (DedicatedServerStartup.OnePlayerMode && reason == "server shutting down: empty timeout exceeded")
            {
                MelonLogger.Msg("[STARTUP] Suppressing empty-room shutdown in OnePlayerMode.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ProgramServer), "Update")]
    public static class ProgramServerUpdateSafetyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ProgramServer __instance)
        {
            if (!DedicatedServerStartup.IsServer) return;

            // If in OnePlayerMode, reset the empty timer frequently to keep server alive 
            // even if no one is in yet (useful for sandbox startup).
            if (DedicatedServerStartup.OnePlayerMode)
            {
                Traverse.Create(__instance).Field("emptyTime").SetValue(0f);
                Traverse.Create(__instance).Field("activeTime").SetValue(0f); // Disable max run time too
            }
        }
    }
}
