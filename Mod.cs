using System;
using System.Reflection;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Andromeda.Mod.Patches;
using Andromeda.Mod.Features;

namespace Andromeda.Mod
{
    public class Mod : MelonMod
    {
        public static Mod Instance;

        public override void OnInitializeMelon()
        {
            Instance = this;
            MelonLogger.Msg($"[BOOT] Andromeda version {BuildInfo.Version}");

            string[] args = System.Environment.GetCommandLineArgs();
            DedicatedServerStartup.Check(args);

            if (DedicatedServerStartup.IsServer)
            {
                MelonLogger.Msg("--------------------------------------------------");
                MelonLogger.Msg("   ANDROMEDA DEDICATED SERVER BOOTSTRAP   ");
                MelonLogger.Msg("--------------------------------------------------");

                // Signal to our patches to disable the empty-server shutdown timer
                System.Environment.SetEnvironmentVariable("ANDROMEDA_DISABLE_EMPTY_TIMEOUT", "1");
            }

            // Load settings before any patches
            NetworkDebugger.LoadSettingsEarly();

            PatchServiceAddress();

            var harmony = new HarmonyLib.Harmony("com.moul7anout.andromeda");
            ApplyManualPatches(harmony);

            ApiData.OnReload += CryonautModelFix.ApplyToApiData;
            CryonautModelFix.ApplyToApiData();
        }

        [Obsolete]
        public override void OnApplicationStart()
        {
            if (DedicatedServerStartup.IsServer)
            {
                MelonLogger.Msg("[INIT] Late app initialization starting...");
            }

            TransitionTrace.Log($"[BOOT] PID={System.Diagnostics.Process.GetCurrentProcess().Id} IsServer={DedicatedServerStartup.IsServer} Args='{string.Join(" ", System.Environment.GetCommandLineArgs())}'");

            NetworkDebugger.Initialize();

            if (!DedicatedServerStartup.IsServer)
                Features.UpdateChecker.CheckAsync();
        }

        private void PatchServiceAddress()
        {
            try
            {
                var apiType = typeof(ApiShared);
                var urlField = apiType.GetField("SERVICE_ADDRESS", BindingFlags.Public | BindingFlags.Static);
                if (urlField != null)
                {
                    urlField.SetValue(null, RestApi.API_URL);
                    MelonLogger.Msg($"[INIT] SERVICE_ADDRESS -> {RestApi.API_URL}");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[INIT-ERROR] Failed to patch SERVICE_ADDRESS: {e}");
            }
        }

        private void ApplyManualPatches(HarmonyLib.Harmony harmony)
        {
            MelonLogger.Msg("--------------------------------------------------");
            MelonLogger.Msg("[BOOT] INITIALIZING HARD-TARGET PATCHES...");
            MelonLogger.Msg("--------------------------------------------------");

            // 1. HARD-TARGET: ProgramClient.Awake Bypass (Fixes Screen.resolutions crash) 
            try {
                var clientAwake = AccessTools.Method(typeof(ProgramClient), "Awake");
                var prefix = AccessTools.Method(typeof(ProgramClientProbePatch), "PrefixClientAwakeStub");
                if (clientAwake != null && prefix != null) {
                    harmony.Patch(clientAwake, new HarmonyMethod(prefix));
                    MelonLogger.Msg("[PATCH] ProgramClient.Awake Crash Bypass - HARD-LINK OK");
                }
            } catch (Exception ex) { MelonLogger.Error($"[PATCH] Client Awake link failed: {ex.Message}"); }

            // 2. HARD-TARGET: Private Steam Methods (Fixes Harmony skipping private Awake/Update)
            try {
                var steamAwake = AccessTools.Method(typeof(Steam), "Awake");
                var awakePrefix = AccessTools.Method(typeof(SteamSdkBypass), "PrefixSteamAwake");
                if (steamAwake != null && awakePrefix != null) {
                    harmony.Patch(steamAwake, new HarmonyMethod(awakePrefix));
                    MelonLogger.Msg("[PATCH] Steam.Awake Server Redirect - HARD-LINK OK");
                }

                var steamUpdate = AccessTools.Method(typeof(Steam), "Update");
                var updatePrefix = AccessTools.Method(typeof(SteamSdkBypass), "PrefixSteamUpdate");
                if (steamUpdate != null && updatePrefix != null) {
                    harmony.Patch(steamUpdate, new HarmonyMethod(updatePrefix));
                }

                var steamDisable = AccessTools.Method(typeof(Steam), "OnDisable");
                var disablePrefix = AccessTools.Method(typeof(SteamSdkBypass), "PrefixSteamDisable");
                if (steamDisable != null && disablePrefix != null) {
                    harmony.Patch(steamDisable, new HarmonyMethod(disablePrefix));
                }
            } catch (Exception ex) { MelonLogger.Error($"[PATCH] Steam Hard-Links failed: {ex.Message}"); }

            // 3. Register the rest of the patch groups
            ManualPatch(harmony, typeof(LogWriterRedirectPatch), "Log Redirection");
            ManualPatch(harmony, typeof(AnalyticsPatch), "Analytics Skip");
            ManualPatch(harmony, typeof(SteamSdkBypass), "Steam SDK Group"); // This will still patch the properties (Id, Username, IsOffline)
            ManualPatch(harmony, typeof(GameliftPatch), "Gamelift SDK Bypass");
            ManualPatch(harmony, typeof(ProgramServerPatch), "Server Logic");
            ManualPatch(harmony, typeof(LobbyServerPatch), "Lobby Logic");
            ManualPatch(harmony, typeof(NetworkListenPatch), "Socket Monitor");
            ManualPatch(harmony, typeof(EnvironmentPatch), "Env Identity");
            ManualPatch(harmony, typeof(AndromedaServerTransitionPatch), "Andromeda Server Transition");
            ManualPatch(harmony, typeof(AppQuitPatch), "Quit Interceptor");

            // Final fallback
            try { 
                harmony.PatchAll(Assembly.GetExecutingAssembly()); 
                MelonLogger.Msg("[INIT] PatchAll Fallback - OK");
            } catch { }

            // Manual transpiler patch
            AndromedaServerMinPlayerPatch.Apply(harmony);

            MelonLogger.Msg("--------------------------------------------------");
            MelonLogger.Msg("[BOOT] PATCH REGISTRATION COMPLETE.");
            MelonLogger.Msg("--------------------------------------------------");
        }

        private void ManualPatch(HarmonyLib.Harmony harmony, Type patchType, string name)
        {
            try
            {
                var processor = harmony.CreateClassProcessor(patchType);
                var patchedMethods = processor.Patch();
                if (patchedMethods != null && patchedMethods.Count > 0)
                    MelonLogger.Msg($"[PATCH] {name} - OK ({patchedMethods.Count} methods)");
                else
                    MelonLogger.Warning($"[PATCH] {name} - NO METHODS FOUND");
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[PATCH] {name} - FAILED: {e.Message}");
            }
        }

        public override void OnUpdate()
        {
            NetworkDebugger.Update();
            DedicatedServerStartup.Update();
        }

        public override void OnGUI()
        {
            NetworkDebugger.OnGUI();
            Features.UpdateChecker.OnGUI();
        }

        public override void OnApplicationQuit()
        {
            if (DedicatedServerStartup.IsServer && !string.IsNullOrEmpty(DedicatedServerStartup.SessionId))
            {
                // Note: This might not finish if the process is killed abruptly,
                // but we have the heartbeat timeout as a fallback.
                RestApi.SendShutdown(DedicatedServerStartup.SessionId);
            }
        }
    }
}
