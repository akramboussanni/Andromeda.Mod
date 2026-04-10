using System;
using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches
{
    public static class CryonautModelFix
    {
        private const string CryonautGuid = "cryonaut";
        private const string FinnGuid = "finn";
        private static readonly bool DebugLogsEnabled = false;

        public static SkinSpawnList.Key GetReplacementSkinKey()
        {
            SkinSpawnList.Key[] candidates =
            {
                SkinSpawnList.Key.WraithSpy,
                SkinSpawnList.Key.Spy,
                SkinSpawnList.Key.Medic,
            };

            foreach (var key in candidates)
            {
                try
                {
                    var settings = SkinSpawnList.Instance.Get(key);
                    if (settings != null && !string.IsNullOrEmpty(settings.guid) && settings.prefab != null)
                        return key;
                }
                catch
                {
                }
            }

            return SkinSpawnList.Key.WraithSpy;
        }

        private static void DebugLog(string message)
        {
            if (!DebugLogsEnabled)
                return;
            MelonLogger.Msg($"[CRYONAUT-DEBUG] {message}");
        }

        public static string GetReplacementSkinGuid()
        {
            try
            {
                var settings = SkinSpawnList.Instance.Get(GetReplacementSkinKey());
                if (settings != null && !string.IsNullOrEmpty(settings.guid))
                    return settings.guid;
            }
            catch
            {
            }

            return "wraith_spy";
        }

        public static void ApplyToApiData()
        {
            try
            {
                DebugLog("ApplyToApiData start");
                SkinSpawnList.Key replacementSkinKey = GetReplacementSkinKey();
                string replacementSkinGuid = GetReplacementSkinGuid();
                DebugLog($"ReplacementSkin resolved to: key={replacementSkinKey} guid={replacementSkinGuid}");

                if (ApiData.Characters != null && ApiData.Characters.TryGetValue(CryonautGuid, out var cryonautChar) && cryonautChar != null)
                {
                    string before = cryonautChar.skins == null ? "<null>" : string.Join(",", cryonautChar.skins);
                    cryonautChar.skins = new[] { replacementSkinGuid };
                    DebugLog($"ApiData.Characters[cryonaut].skins: {before} -> {string.Join(",", cryonautChar.skins)}");
                }
                else
                {
                    DebugLog("ApiData.Characters missing cryonaut entry");
                }

                if (ApiData.Characters != null && ApiData.Characters.TryGetValue(FinnGuid, out var finnChar) && finnChar != null)
                {
                    string before = finnChar.skins == null ? "<null>" : string.Join(",", finnChar.skins);
                    finnChar.skins = new[] { replacementSkinGuid };
                    DebugLog($"ApiData.Characters[finn].skins: {before} -> {string.Join(",", finnChar.skins)}");
                }

                if (ApiData.Profile?.characters != null)
                {
                    foreach (var ch in ApiData.Profile.characters)
                    {
                        if (!string.Equals(ch.guid, CryonautGuid, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(ch.guid, FinnGuid, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string before = ch.skins == null ? "<null>" : string.Join(",", ch.skins);
                        ch.skins = new[] { replacementSkinGuid, CryonautGuid };
                        DebugLog($"ApiData.Profile cryonaut skins: {before} -> {string.Join(",", ch.skins)}");
                    }
                }
                else
                {
                    DebugLog("ApiData.Profile.characters unavailable");
                }

                // Persist a valid skin key so CharacterProgressionTab and Showcase never fall back to broken cryonaut model.
                if (LocalUserData.Loadouts != null && LocalUserData.Loadouts.ContainsKey(CharacterSpawnList.Key.Cryonaut))
                {
                    var loadout = LocalUserData.Loadouts[CharacterSpawnList.Key.Cryonaut];
                    var before = loadout.skin;
                    loadout.skin = replacementSkinKey;
                    LocalUserData.Loadouts[CharacterSpawnList.Key.Cryonaut] = loadout;
                    LocalUserData.Save();
                    DebugLog($"LocalUserData cryonaut skin key: {before} -> {loadout.skin}");
                }
                else
                {
                    DebugLog("LocalUserData.Loadouts missing cryonaut key");
                }

                DebugLog("ApplyToApiData end");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CRYONAUT] Model remap fix failed: {ex.Message}");
            }
        }
    }
}
