using System;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Windwalk.Net;

namespace Andromeda.Mod.Patches
{
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
