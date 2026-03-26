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

        public override void OnApplicationStart()
        {
            Instance = this;
            MelonLogger.Msg($"[BOOT] Andromeda version {BuildInfo.Version}");
            
            string[] args = System.Environment.GetCommandLineArgs();
            DedicatedServerStartup.Check(args);
            TransitionTrace.Log($"[BOOT] PID={System.Diagnostics.Process.GetCurrentProcess().Id} IsServer={DedicatedServerStartup.IsServer} Args='{string.Join(" ", args)}'");

            if (DedicatedServerStartup.IsServer)
            {
                MelonLogger.Msg("******************************************");
                MelonLogger.Msg("*        Andromeda DEDICATED SERVER       *");
                MelonLogger.Msg("******************************************");
            }

            // Load settings before any patches
            NetworkDebugger.LoadSettingsEarly();

            PatchServiceAddress();
            NetworkDebugger.Initialize();

            var harmony = new HarmonyLib.Harmony("com.moul7anout.Andromeda");
            ApplyManualPatches(harmony);

            ApiData.OnReload += CryonautModelFix.ApplyToApiData;
            CryonautModelFix.ApplyToApiData();

            //PerkFeatures.SwapPerkSprites();
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
            MelonLogger.Msg("[INIT] Registering patches...");
            
            ManualPatch(harmony, typeof(SkinGuidBackfillPatch), "Skin Guid Backfill");
            ManualPatch(harmony, typeof(SkinGuidAliasPatch), "Skin Guid Alias");
            ManualPatch(harmony, typeof(SkinKeyAliasPatch), "Skin Key Alias");
            ManualPatch(harmony, typeof(CryonautSkinOwnershipAliasPatch), "Cryonaut Skin Ownership Alias");
            ManualPatch(harmony, typeof(CryonautProgressionUiPatch), "Cryonaut Progression UI Fix");
            ManualPatch(harmony, typeof(CryonautClassTabPatch), "Cryonaut Class Tab Fix");
            ManualPatch(harmony, typeof(CryonautShowcasePatch), "Cryonaut Showcase Fix");
            ManualPatch(harmony, typeof(UnlockableButtonSpawnerPatch), "UI Perks");
            ManualPatch(harmony, typeof(WhatsNewCoordinatorPatch), "Tutorial Disable");
            ManualPatch(harmony, typeof(LogWriterRedirectPatch), "Log Redirection");
            ManualPatch(harmony, typeof(AnalyticsPatch), "Analytics Skip");
            ManualPatch(harmony, typeof(SteamPatch), "Steam Bypass");
            ManualPatch(harmony, typeof(ApiSharedPatch), "API Auth Bypass");
            ManualPatch(harmony, typeof(ApiWarmupMessagePatch), "Server Warmup UX Message");
            ManualPatch(harmony, typeof(UserStorePatch), "Profile Storage");
            ManualPatch(harmony, typeof(ProgramServerPatch), "Server Logic");
            ManualPatch(harmony, typeof(LobbyServerPatch), "Lobby Logic");
            ManualPatch(harmony, typeof(NetworkListenPatch), "Socket Monitor");
            ManualPatch(harmony, typeof(EnvironmentPatch), "Env Identity");
            ManualPatch(harmony, typeof(ApiClientPartyJoinPatch), "REST Trace");
            ManualPatch(harmony, typeof(ProgramClientConnectPatch), "Connection Logic");
            ManualPatch(harmony, typeof(ProgramClientProbePatch), "Client Boot/Join Probe");
            ManualPatch(harmony, typeof(MainMenuTransitionProbePatch), "MainMenu JoinMatch Probe");
            ManualPatch(harmony, typeof(EntityBaseSendReliablePatch), "Base Msg Reroute (Reliable)");
            ManualPatch(harmony, typeof(EntityBaseSendUnreliablePatch), "Base Msg Reroute (Unreliable)");
            ManualPatch(harmony, typeof(CustomPartyClientJoinGamePatch), "Client JoinGame Trace");
            ManualPatch(harmony, typeof(AndromedaClientTransitionPatch), "Andromeda Client Transition");
            ManualPatch(harmony, typeof(AndromedaPhaseClockDesyncPatch), "Andromeda Phase Clock Desync");
            ManualPatch(harmony, typeof(AndromedaServerTransitionPatch), "Andromeda Server Transition");
            ManualPatch(harmony, typeof(GamemodeBeginGatePatch), "Gamemode Begin Gate Checks");
            ManualPatch(harmony, typeof(EntitySpawnGatePatch), "Entity Spawn Gate Checks");
            ManualPatch(harmony, typeof(AppQuitPatch), "Quit Interceptor");
            ManualPatch(harmony, typeof(ReturnToLobbyRedirectPatch), "Return-To-Menu Redirect");
            ManualPatch(harmony, typeof(CustomPartyServerSetupPatch), "CustomParty Lobby Log");
            ManualPatch(harmony, typeof(JoinGameMsgLoggingPatch), "JoinGame Broadcast Log");

            // Manual transpiler patch — must be called after harmony instance is ready
            AndromedaServerMinPlayerPatch.Apply(harmony);
            
            MelonLogger.Msg("[INIT] Patches registration complete.");
        }

        private void ManualPatch(HarmonyLib.Harmony harmony, Type patchType, string name)
        {
            try
            {
                var processor = harmony.CreateClassProcessor(patchType);
                processor.Patch();
                MelonLogger.Msg($"[PATCH] {name} - OK");
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

        public override void OnGUI() { NetworkDebugger.OnGUI(); }

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
