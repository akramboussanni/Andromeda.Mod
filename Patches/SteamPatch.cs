using HarmonyLib;
using MelonLoader;
using System.Reflection;
using System;

namespace Andromeda.Mod.Patches
{
    // Implementation of Proper Steam Server SDK (No Steam Client Required)
    [HarmonyPatch]
    public static class SteamSdkBypass
    {
        [HarmonyPatch(typeof(Steam), "get_Id")]
        [HarmonyPrefix]
        public static bool PrefixSteamId(ref string __result)
        {
            if (DedicatedServerStartup.IsServer)
            {
                __result = "76561197960287930"; // Dedicated Server dummy ID
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Steam), "get_Username")]
        [HarmonyPrefix]
        public static bool PrefixSteamUsername(ref string __result)
        {
            if (DedicatedServerStartup.IsServer)
            {
                __result = "DedicatedServer";
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Steam), "get_IsOffline")]
        [HarmonyPrefix]
        public static bool PrefixSteamIsOffline(ref bool __result)
        {
            if (DedicatedServerStartup.IsServer)
            {
                __result = false; // We are always "online" thanks to SteamServer
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Steam), "Awake")]
        [HarmonyPrefix]
        public static bool PrefixSteamAwake(Steam __instance)
        {
            if (DedicatedServerStartup.IsServer)
            {
                if (Steamworks.SteamServer.IsValid)
                {
                    return false; // Skip if already initialized
                }

                MelonLogger.Msg("[STEAM-SERVER] Initializing proper SteamServer (no client required)...");
                try
                {
                    // Steam does not actually bind the GamePort, it just advertises it.
                    // We safely retrieve it via reflection to avoid compiler ambiguity with Unity's Environment.
                    ushort gamePort = 7777;
                    try
                    {
                        var portField = typeof(global::Environment).GetField("port", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        if (portField != null && portField.GetValue(null) is int envPort && envPort > 0)
                        {
                            gamePort = (ushort)envPort;
                        }
                    } 
                    catch { }

                    var init = new Steamworks.SteamServerInit("Enemy On Board", "Enemy On Board")
                    {
                        GamePort = gamePort,
                        QueryPort = 0, // 0 tells Steam to auto-assign a random ephemeral port so multiple servers don't collide
                        Secure = false,
                        DedicatedServer = true
                    };
                    Steamworks.SteamServer.Init(999860, init, true);
                    Steamworks.SteamServer.LogOnAnonymous();
                    
                    // Set authToken so Steam.IsLoaded returns true and passes all gatekeeping checks
                    typeof(Steam).GetField("authToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(__instance, "DEDICATED_SERVER_TOKEN");
                    
                    MelonLogger.Msg("[STEAM-SERVER] Successfully logged on anonymously to Steam backend.");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[STEAM-SERVER] Failed to initialize: {ex.Message}");
                }
                
                return false; // Skip the original SteamClient.Init from the game
            }
            return true;
        }

        [HarmonyPatch(typeof(Steam), "Update")]
        [HarmonyPrefix]
        public static bool PrefixSteamUpdate()
        {
            if (DedicatedServerStartup.IsServer)
            {
                if (Steamworks.SteamServer.IsValid)
                {
                    Steamworks.SteamServer.RunCallbacks();
                }
                return false; // Skip SteamClient.RunCallbacks
            }
            return true;
        }

        [HarmonyPatch(typeof(Steam), "OnDisable")]
        [HarmonyPrefix]
        public static bool PrefixSteamDisable()
        {
            if (DedicatedServerStartup.IsServer)
            {
                if (Steamworks.SteamServer.IsValid)
                {
                    Steamworks.SteamServer.Shutdown();
                }
                return false; // Skip SteamClient.Shutdown
            }
            return true;
        }
    }

}
