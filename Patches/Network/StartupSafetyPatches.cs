using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches.Network
{
    [HarmonyPatch(typeof(ProgramServer), "Quit", new[] { typeof(string) })]
    public static class ProgramServerQuitSafetyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(string reason)
        {
            if (!DedicatedServerStartup.IsServer && !Andromeda.Mod.Patches.EnvironmentPatch.IsHost()) return true;

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
            if (!DedicatedServerStartup.IsServer && !Andromeda.Mod.Patches.EnvironmentPatch.IsHost()) return;
            if (!DedicatedServerStartup.OnePlayerMode) return;

            // Keep the server alive indefinitely in OnePlayerMode by resetting the
            // empty-room timer and the max-run-time timer before ProgramServer.Update
            // can read them. Direct field access via publicized assembly — no reflection.
            __instance.emptyTime = 0f;
            __instance.activeTime = 0f;
        }
    }
}
