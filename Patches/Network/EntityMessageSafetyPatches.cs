using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Patches.Network
{
    /// <summary>
    /// Suppresses "no handlers registered" noise on the client.
    /// This noise occurs when the server broadcasts entity messages (e.g. EvolutionPoints Update)
    /// to all clients, but some clients have spawned that entity as a Proxy which doesn't
    /// need those specific handlers. This was always happening but became visible once
    /// engine logs were redirected to the dashboard.
    /// </summary>
    [HarmonyPatch]
    public static class EntityMessageSafetyPatches
    {
        [HarmonyPatch(typeof(EvolutionPointsShared), "HandleMessage")]
        [HarmonyPrefix]
        public static bool PrefixEP(EvolutionPointsShared __instance, Entity.Message msg)
        {
            try
            {
                var handlersField = typeof(EvolutionPointsShared).GetField("handlers", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                var handlers = handlersField?.GetValue(__instance) as System.Collections.IDictionary;
                if (handlers == null) return true;

                // Game reads short for MsgType in HandleMessage
                short key = msg.PayloadReader.ReadInt16();
                msg.PayloadReader.SeekZero(); 

                // Check if the enum-keyed dictionary contains this value
                // Since it's IDictionary, we need to be careful with the key type.
                bool hasHandler = false;
                foreach (var k in handlers.Keys)
                {
                    if (Convert.ToInt16(k) == key)
                    {
                        hasHandler = true;
                        break;
                    }
                }

                if (!hasHandler) return false;
            }
            catch { }
            return true;
        }

        [HarmonyPatch(typeof(AbilitiesShared), "HandleMessage")]
        [HarmonyPrefix]
        public static bool PrefixAbilities(AbilitiesShared __instance, Entity.Message msg)
        {
            try
            {
                var handlersField = typeof(AbilitiesShared).GetField("handlers", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                var handlers = handlersField?.GetValue(__instance) as System.Collections.IDictionary;
                if (handlers == null) return true;

                short key = msg.PayloadReader.ReadInt16();
                msg.PayloadReader.SeekZero();

                bool hasHandler = false;
                foreach (var k in handlers.Keys)
                {
                    if (Convert.ToInt16(k) == key)
                    {
                        hasHandler = true;
                        break;
                    }
                }

                if (!hasHandler) return false;
            }
            catch { }
            return true;
        }

        /// <summary>
        /// Silences "no handlers registered" spam for PlayerClient.
        /// This fires after PlayerClient.Setup() crashes (KeyNotFoundException) and
        /// leaves the entity's handlers dict empty, causing every subsequent server
        /// broadcast for msg type 105 to log a warning.
        /// </summary>
        [HarmonyPatch]
        public static class PlayerClientHandleMessageSafetyPatch
        {
            public static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("PlayerClient");
                return type == null ? null : AccessTools.Method(type, "HandleMessage");
            }

            [HarmonyPrefix]
            public static bool Prefix(object __instance, Entity.Message msg)
            {
                try
                {
                    if (__instance == null) return false;
                    var handlersField = __instance.GetType().GetField("handlers",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                    var handlers = handlersField?.GetValue(__instance) as System.Collections.IDictionary;
                    if (handlers == null || handlers.Count == 0) return false;

                    short key = msg.PayloadReader.ReadInt16();
                    msg.PayloadReader.SeekZero();

                    bool hasHandler = false;
                    foreach (var k in handlers.Keys)
                    {
                        if (Convert.ToInt16(k) == key) { hasHandler = true; break; }
                    }
                    if (!hasHandler) return false;
                }
                catch { }
                return true;
            }
        }

        /// <summary>
        /// Absorbs the KeyNotFoundException that crashes PlayerClient.Setup() during
        /// OnEntitySpawn when the entity's internal component dictionary is not yet
        /// populated (race condition after 10+ games / 87 s session).
        /// </summary>
        [HarmonyPatch]
        public static class PlayerClientSetupSafetyPatch
        {
            public static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("PlayerClient");
                return type == null ? null : AccessTools.Method(type, "Setup");
            }

            [HarmonyFinalizer]
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception is KeyNotFoundException)
                {
                    MelonLogger.Warning("[ENTITY] PlayerClient.Setup() KeyNotFoundException suppressed — " +
                                       "entity component dict not ready during spawn (msg 105).");
                    return null; // swallow
                }
                return __exception; // re-throw anything else
            }
        }
    }
}
