using System;
using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches
{
    public static class SkinGuidAliasPatch
    {
        [HarmonyPatch(typeof(SkinSpawnList), "GetByGuid")]
        [HarmonyPrefix]
        public static void Prefix(ref string guid)
        {
            guid = SkinGuidRuntimeFix.ResolveAlias(guid);

            if (string.Equals(guid, "cryonaut", StringComparison.OrdinalIgnoreCase)
                || string.Equals(guid, "finn", StringComparison.OrdinalIgnoreCase))
            {
                string replacementGuid = CryonautModelFix.GetReplacementSkinGuid();
                MelonLogger.Msg($"[CRYONAUT-DEBUG] SkinSpawnList.GetByGuid remap cryonaut -> {replacementGuid}");
                guid = replacementGuid;
            }
        }
    }

    public static class SkinGuidBackfillPatch
    {
        [HarmonyPatch(typeof(SkinSpawnList), "Get", new Type[] { typeof(SkinSpawnList.Key) })]
        [HarmonyPostfix]
        public static void Postfix(SkinSpawnList.Key key, object __result)
        {
            SkinGuidRuntimeFix.EnsureGuid(__result, key);
        }
    }

    public static class SkinKeyAliasPatch
    {
        [HarmonyPatch(typeof(SkinSpawnList), "Get", new Type[] { typeof(SkinSpawnList.Key) })]
        [HarmonyPrefix]
        public static void Prefix(ref SkinSpawnList.Key key)
        {
            if (key == SkinSpawnList.Key.Cryonaut)
            {
                var replacement = CryonautModelFix.GetReplacementSkinKey();
                MelonLogger.Msg($"[CRYONAUT-DEBUG] SkinSpawnList.Get key remap Cryonaut -> {replacement}");
                key = replacement;
            }
        }
    }
}
