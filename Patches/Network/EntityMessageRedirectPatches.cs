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

            var type = entity.GetType();
            string typeName = type.Name ?? string.Empty;

            // Dedicated runtime should only rewrite server-side entity components.
            // Client/proxy/shared components should keep original behavior.
            return typeName.EndsWith("Server", StringComparison.Ordinal);
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendReliable")]
    public static class EntityBaseSendReliablePatch
    {
        private static NetServer _cachedServer;

        [HarmonyPrefix]
        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

                var msg = new Entity.Message
                {
                    id = __instance.id,
                    componentType = __instance.ComponentType,
                    Body = body
                };
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
            if (!DedicatedServerStartup.IsServer) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

                var msg = new Entity.Message
                {
                    id = __instance.id,
                    componentType = __instance.ComponentType,
                    Body = body
                };
                // Note: Dedicated server treats room-broadcast as global-broadcast 
                // for entities that call this.
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
            if (!DedicatedServerStartup.IsServer) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

                var msg = new Entity.Message
                {
                    id = __instance.id,
                    componentType = __instance.ComponentType,
                    Body = body
                };
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
            if (!DedicatedServerStartup.IsServer) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

                var msg = new Entity.Message
                {
                    id = __instance.id,
                    componentType = __instance.ComponentType,
                    Body = body
                };
                _cachedServer.SendAllUnreliable(msg);
                return false;
            }
            catch { return true; }
        }
    }
    [HarmonyPatch(typeof(EntityManagerClient), "SendReliable")]
    public static class EntityManagerClientSendReliablePatch
    {
        private static NetServer _cachedServer;

        [HarmonyPrefix]
        public static bool Prefix(Entity.Base from, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;

                var msg = new Entity.Message
                {
                    id = from.id,
                    componentType = from.ComponentType,
                    Body = body
                };
                _cachedServer.SendAllReliable(msg);
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(EntityManagerClient), "SendUnreliable")]
    public static class EntityManagerClientSendUnreliablePatch
    {
        private static NetServer _cachedServer;

        [HarmonyPrefix]
        public static bool Prefix(Entity.Base from, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try
            {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;

                var msg = new Entity.Message
                {
                    id = from.id,
                    componentType = from.ComponentType,
                    Body = body
                };
                _cachedServer.SendAllUnreliable(msg);
                return false;
            }
            catch { return true; }
        }
    }
}