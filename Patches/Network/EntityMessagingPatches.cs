using HarmonyLib;
using Windwalk.Net;

namespace Andromeda.Mod.Patches.Network
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
            return typeName.EndsWith("Server", System.StringComparison.Ordinal);
        }
    }

    // Nuclear Fix: Redirect ALL entity messaging from NetClient to NetServer when running as a server.
    // Optimized with cached Singleton lookup to minimize per-frame overhead.
    [HarmonyPatch(typeof(Entity.Base), "SendReliable")]
    public static class EntityBaseSendReliablePatch
    {
        private static NetServer _cachedServer;
        private static readonly Entity.Message _cachedMsg = new Entity.Message();

        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

                _cachedMsg.id = __instance.id;
                _cachedMsg.componentType = __instance.ComponentType;
                _cachedMsg.Body = body;
                _cachedServer.SendAllReliable(_cachedMsg);
                return false;
            } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendReliableToRoom")]
    public static class EntityBaseSendReliableToRoomPatch
    {
        private static NetServer _cachedServer;
        private static readonly Entity.Message _cachedMsg = new Entity.Message();

        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

                _cachedMsg.id = __instance.id;
                _cachedMsg.componentType = __instance.ComponentType;
                _cachedMsg.Body = body;
                _cachedServer.SendAllReliable(_cachedMsg);
                return false;
            } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendUnreliable")]
    public static class EntityBaseSendUnreliablePatch
    {
        private static NetServer _cachedServer;
        private static readonly Entity.Message _cachedMsg = new Entity.Message();

        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

                _cachedMsg.id = __instance.id;
                _cachedMsg.componentType = __instance.ComponentType;
                _cachedMsg.Body = body;
                _cachedServer.SendAllUnreliable(_cachedMsg);
                return false;
            } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Entity.Base), "SendUnreliableToRoom")]
    public static class EntityBaseSendUnreliableToRoomPatch
    {
        private static NetServer _cachedServer;
        private static readonly Entity.Message _cachedMsg = new Entity.Message();

        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;

            try {
                if (_cachedServer == null) _cachedServer = Singleton.Get<NetServer>();
                if (_cachedServer == null) return true;
                if (!EntityRedirectFilter.ShouldRedirect(__instance)) return true;

                _cachedMsg.id = __instance.id;
                _cachedMsg.componentType = __instance.ComponentType;
                _cachedMsg.Body = body;
                // Treat room broadcast as global broadcast for dedicated server
                _cachedServer.SendAllUnreliable(_cachedMsg);
                return false;
            } catch { return true; }
        }
    }
}
