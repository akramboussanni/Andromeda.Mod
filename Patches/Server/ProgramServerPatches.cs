using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Patches.Server
{
    [HarmonyPatch]
    public static class ProgramServerPatch
    {
        [HarmonyPatch(typeof(ProgramServer), "Host")]
        [HarmonyPrefix]
        public static void PrefixHost(string region, string sessionId, string name, GamemodeList.Key gamemodeKey)
        {
            NetworkDebugger.LogLobbyEvent($"[SERVER-STARTED] Hosting: {name} (Mode: {gamemodeKey})");
        }

        [HarmonyPatch(typeof(ProgramServer), "OnJoin")]
        [HarmonyPrefix]
        public static void PrefixOnJoin(ProgramServer __instance, PlayerId playerId, ProgramShared.JoinRequest request)
        {
            if (!DedicatedServerStartup.IsServer) return;

            // Handshake version bypass
            if (request != null)
            {
                var versionField = typeof(ProgramShared.JoinRequest).GetField("version", BindingFlags.Public | BindingFlags.Instance);
                if (versionField != null)
                {
                    versionField.SetValue(request, Version.Value);
                }
            }
        }

        [HarmonyPatch(typeof(ProgramServer), "OnLeave")]
        [HarmonyPrefix]
        public static void PrefixOnLeave(ProgramServer __instance, PlayerId playerId)
        {
            if (!DedicatedServerStartup.IsServer) return;
            NetworkDebugger.LogLobbyEvent($"[SERVER] Player Left: {playerId}");
        }

        public static bool PrefixClientAwakeStub()
        {
            if (DedicatedServerStartup.IsServer && Application.isBatchMode)
            {
                MelonLogger.Msg("[SERVER-BOOT] Skipping ProgramClient.Awake via Hard-Link (resolution crash fixed).");
                return false;
            }

            if (DedicatedServerStartup.IsServer && !Application.isBatchMode)
            {
                MelonLogger.Warning("[SERVER-BOOT] IsServer=true but Application.isBatchMode=false; allowing ProgramClient.Awake to avoid client-side null state.");
            }

            return true;
        }
    }

    // Guard: prevent AndromedaClient from processing any messages on the dedicated server.
    // NetServer.SendAllReliable loops back to server-local entities, so without this guard
    // the server's own AndromedaClient instance fires OnPlayerList, OnNotify, etc. — causing
    // every event to be processed (and logged) twice.
    [HarmonyPatch(typeof(AndromedaClient), "Setup")]
    public static class AndromedaClientServerGuardPatch
    {
        [HarmonyPrefix]
        public static bool BlockOnServer() => !(DedicatedServerStartup.IsServer && Application.isBatchMode);
    }
}
