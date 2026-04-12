/*using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Reflection;
using Windwalk.Net;

namespace Andromeda.Mod.Patches.Network
{
    /// <summary>
    /// Guards against "no handlers registered" log spam.
    /// This happens when the server broadcasts entity messages (e.g. EvolutionPoints, Abilities)
    /// to all clients, but some clients spawned that entity as a Proxy which doesn't register
    /// those specific handlers. The server sends to everyone — proxies legitimately skip them.
    ///
    /// These are NOT crashes and do NOT swallow exceptions. They return false (skip the original)
    /// only when the message type has no registered handler, which is the correct behavior.
    /// </summary>
    [HarmonyPatch]
    public static class EntityMessageSafetyPatches
    {
        [HarmonyPatch(typeof(EvolutionPointsShared), "HandleMessage")]
        [HarmonyPrefix]
        public static bool PrefixEP(EvolutionPointsShared __instance, Entity.Message msg)
        {
            var handlersField = typeof(EvolutionPointsShared).GetField("handlers",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var handlers = handlersField?.GetValue(__instance) as System.Collections.IDictionary;
            if (handlers == null) return true;

            short key = msg.PayloadReader.ReadInt16();
            msg.PayloadReader.SeekZero();

            foreach (var k in handlers.Keys)
                if (Convert.ToInt16(k) == key) return true;

            return false; // no handler registered — skip silently
        }

        [HarmonyPatch(typeof(AbilitiesShared), "HandleMessage")]
        [HarmonyPrefix]
        public static bool PrefixAbilities(AbilitiesShared __instance, Entity.Message msg)
        {
            var handlersField = typeof(AbilitiesShared).GetField("handlers",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var handlers = handlersField?.GetValue(__instance) as System.Collections.IDictionary;
            if (handlers == null) return true;

            short key = msg.PayloadReader.ReadInt16();
            msg.PayloadReader.SeekZero();

            foreach (var k in handlers.Keys)
                if (Convert.ToInt16(k) == key) return true;

            return false; // no handler registered — skip silently
        }

        /// <summary>
        /// Silences "no handlers registered" log spam for PlayerClient proxy instances.
        /// PlayerProxy entities receive server broadcasts intended for PlayerClient but only
        /// register a subset of handlers — the rest are legitimately absent.
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
            public static bool Prefix(object __instance, Entity.Message entityMsg)
            {
                if (__instance == null) return true;

                var handlersField = __instance.GetType().GetField("handlers",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                var handlers = handlersField?.GetValue(__instance) as System.Collections.IDictionary;
                if (handlers == null || handlers.Count == 0) return false;

                short key = entityMsg.PayloadReader.ReadInt16();
                entityMsg.PayloadReader.SeekZero();

                foreach (var k in handlers.Keys)
                    if (Convert.ToInt16(k) == key) return true;

                return false; // no handler registered — skip silently
            }
        }
    }
}
*/