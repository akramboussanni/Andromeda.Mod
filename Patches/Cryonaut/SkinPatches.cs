using System;
using System.Linq;
using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches.Cryonaut
{
    [HarmonyPatch]
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

    // [HarmonyPatch] - Muted due to undefined target method
    public static class SkinGuidBackfillPatch
    {
        [HarmonyPatch(typeof(SkinSpawnList), "Get", new Type[] { typeof(SkinSpawnList.Key) })]
        [HarmonyPostfix]
        public static void Postfix(SkinSpawnList.Key key, object __result)
        {
            SkinGuidRuntimeFix.EnsureGuid(__result, key);
        }
    }

    // [HarmonyPatch] - Muted due to undefined target method
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

    [HarmonyPatch]
    public static class CryonautSkinOwnershipAliasPatch
    {
        [HarmonyPatch(typeof(LoadoutSelectShared), "CheckSkinOwnership")]
        [HarmonyPostfix]
        public static void Postfix(
            PlayerId playerId,
            SkinSpawnList.Key key,
            CharacterSpawnList.Key characterKey,
            ref bool __result)
        {
            if (__result)
                return;

            try
            {
                string characterGuid = CharacterSpawnList.Instance.Get(characterKey).guid;
                if (!string.Equals(characterGuid, "cryonaut", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(characterGuid, "finn", StringComparison.OrdinalIgnoreCase))
                    return;

                string skinGuid = SkinSpawnList.Instance.Get(key).guid;
                string replacementGuid = CryonautModelFix.GetReplacementSkinGuid();
                SkinSpawnList.Key replacementKey = CryonautModelFix.GetReplacementSkinKey();
                if (!string.Equals(skinGuid, replacementGuid, StringComparison.OrdinalIgnoreCase)
                    && key != replacementKey)
                    return;

                var (data, loaded) = UserStore.Instance.Fetch(playerId);
                if (!loaded || data.characters == null)
                    return;

                var cryonaut = data.characters.FirstOrDefault(c =>
                    string.Equals(c.guid, "cryonaut", StringComparison.OrdinalIgnoreCase));
                if (cryonaut?.skins == null)
                    return;

                if (cryonaut.skins.Any(s => string.Equals(s, "cryonaut", StringComparison.OrdinalIgnoreCase)))
                {
                    __result = true;
                    MelonLogger.Msg($"[CRYONAUT-DEBUG] Ownership alias granted for player {playerId}: skin={skinGuid}, key={key}");
                }
                else
                {
                    MelonLogger.Msg($"[CRYONAUT-DEBUG] Ownership alias NOT granted for player {playerId}: profile skins={string.Join(",", cryonaut.skins)}");
                }
            }
            catch
            {
                // Keep gameplay resilient if profile data is unavailable.
            }
        }
    }
}
