
import re
import difflib

v_code = """
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

                _cachedMsg.id = __instance.id;
                _cachedMsg.componentType = __instance.ComponentType;
                _cachedMsg.Body = body;
                _cachedServer.SendAllReliable(_cachedMsg);
                return false;
            } catch { return true; }
        }
    }
"""

h_code = """
    [HarmonyPatch(typeof(Entity.Base), "SendReliable")]
    public static class EntityBaseSendReliablePatch
    {
        private static NetServer _cachedServer;

        [HarmonyPrefix]
        public static bool Prefix(Entity.Base __instance, BaseMessage body)
        {
            if (!DedicatedServerStartup.IsServer) return true;
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
"""

print("DIFF: EntityBaseSendReliablePatch")
diff = difflib.unified_diff(v_code.splitlines(), h_code.splitlines(), fromfile='0.11.1', tofile='HEAD', lineterm='')
for line in diff:
    print(line)
