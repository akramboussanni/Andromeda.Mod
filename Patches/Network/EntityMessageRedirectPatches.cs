using HarmonyLib;
using System;
using Windwalk.Net;
using UnityEngine;

namespace Andromeda.Mod.Patches
{
    internal static class EntityRedirectFilter
    {
        public static bool ShouldRedirect(Entity.Base entity)
        {
            if (entity == null) return false;
            // Only rewrite sends from server-side entity components.
            // Client/proxy/shared components keep their original behavior.
            return entity.GetType().Name.EndsWith("Server", StringComparison.Ordinal);
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendReliable")]
    public static class EntityBaseSendReliablePatch
    {
        private static NetServer _cachedServer;

        [HarmonyPrefix]
        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer && !EnvironmentPatch.IsHost()) return true;
            if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                var msg = new Entity.Message { id = __instance.id, componentType = __instance.ComponentType, Body = body };
                _cachedServer.SendAllReliable(msg);
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendReliableToRoom")]
    public static class EntityBaseSendReliableToRoomPatch
    {
        private static NetServer _cachedServer;

        [HarmonyPrefix]
        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer && !EnvironmentPatch.IsHost()) return true;
            if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                var msg = new Entity.Message { id = __instance.id, componentType = __instance.ComponentType, Body = body };
                _cachedServer.SendAllReliable(msg);
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendUnreliable")]
    public static class EntityBaseSendUnreliablePatch
    {
        private static NetServer _cachedServer;

        [HarmonyPrefix]
        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer && !EnvironmentPatch.IsHost()) return true;
            if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                var msg = new Entity.Message { id = __instance.id, componentType = __instance.ComponentType, Body = body };
                _cachedServer.SendAllUnreliable(msg);
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendUnreliableToRoom")]
    public static class EntityBaseSendUnreliableToRoomPatch
    {
        private static NetServer _cachedServer;

        [HarmonyPrefix]
        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer && !EnvironmentPatch.IsHost()) return true;
            if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                var msg = new Entity.Message { id = __instance.id, componentType = __instance.ComponentType, Body = body };
                _cachedServer.SendAllUnreliable(msg);
                return false;
            }
            catch { return true; }
        }
    }
    /// <summary>
    /// On the dedicated server, EntityManagerClient.SendReliable/SendUnreliable are called by
    /// client-side entity components (e.g. AbilitiesClient, PlayerClient controls).
    /// These are CLIENT→SERVER messages. Redirecting them via SendAllReliable broadcasts them
    /// to all clients, causing "no handlers registered" on proxy entities that don't handle those
    /// message types. The correct behavior is to drop them on the server entirely.
    /// (This patch was not present in 0.11.1 — its addition in 0.12.0 reintroduced the bug.)
    /// </summary>
    [HarmonyPatch(typeof(EntityManagerClient), "SendReliable")]
    public static class EntityManagerClientSendReliablePatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !(DedicatedServerStartup.IsServer || EnvironmentPatch.IsHost()); // drop on server/host
    }

    [HarmonyPatch(typeof(EntityManagerClient), "SendUnreliable")]
    public static class EntityManagerClientSendUnreliablePatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !(DedicatedServerStartup.IsServer || EnvironmentPatch.IsHost()); // drop on server/host
    }
}