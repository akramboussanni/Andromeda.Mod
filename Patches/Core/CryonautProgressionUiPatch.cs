using System;
using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches
{
    public static class CryonautProgressionUiPatch
    {
        [HarmonyPatch(typeof(CharacterProgressionTab), "DisplayPerkProgression")]
        [HarmonyPrefix]
        public static void PrefixDisplayPerkProgression(CharacterProgressionTab __instance)
        {
            try
            {
                string currentCharacterGuid = Traverse.Create(__instance).Field("currentCharacterGuid").GetValue<string>();
                MelonLogger.Msg($"[CRYONAUT-DEBUG] DisplayPerkProgression currentCharacterGuid={currentCharacterGuid}");
                if (!string.Equals(currentCharacterGuid, "cryonaut", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(currentCharacterGuid, "finn", StringComparison.OrdinalIgnoreCase))
                    return;

                CryonautModelFix.ApplyToApiData();

                var currentLoadout = Traverse.Create(__instance).Field("currentLoadout").GetValue<CharacterProgressionTab.CharacterLoadout>();
                currentLoadout.skin = CryonautModelFix.GetReplacementSkinKey();
                Traverse.Create(__instance).Field("currentLoadout").SetValue(currentLoadout);
                MelonLogger.Msg($"[CRYONAUT-DEBUG] DisplayPerkProgression forced currentLoadout.skin={currentLoadout.skin}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CRYONAUT] DisplayPerkProgression remap failed: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(CharacterProgressionTab), "DisplayCharacterLoadout")]
        [HarmonyPrefix]
        public static void PrefixDisplay(CharacterSpawnList.Key characterKey)
        {
            if (characterKey != CharacterSpawnList.Key.Cryonaut)
                return;

            try
            {
                MelonLogger.Msg("[CRYONAUT-DEBUG] DisplayCharacterLoadout prefix hit for Cryonaut");
                CryonautModelFix.ApplyToApiData();

                if (LocalUserData.Loadouts != null && LocalUserData.Loadouts.ContainsKey(CharacterSpawnList.Key.Cryonaut))
                {
                    var replacement = CryonautModelFix.GetReplacementSkinKey();
                    var loadout = LocalUserData.Loadouts[CharacterSpawnList.Key.Cryonaut];
                    var before = loadout.skin;
                    loadout.skin = replacement;
                    LocalUserData.Loadouts[CharacterSpawnList.Key.Cryonaut] = loadout;
                    LocalUserData.Save();
                    MelonLogger.Msg($"[CRYONAUT-DEBUG] DisplayCharacterLoadout forced local loadout skin {before} -> {loadout.skin}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CRYONAUT] DisplayCharacterLoadout remap failed: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(CharacterProgressionTab), "DisplayCharacterLoadout")]
        [HarmonyPostfix]
        public static void PostfixDisplay(CharacterSpawnList.Key characterKey)
        {
            if (characterKey != CharacterSpawnList.Key.Cryonaut)
                return;

            try
            {
                var replacement = CryonautModelFix.GetReplacementSkinKey();
                UserInterface.Instance.Showcase.SetSkin(new SkinSpawnList.Key?(replacement), true);
                MelonLogger.Msg($"[CRYONAUT-DEBUG] DisplayCharacterLoadout postfix forced Showcase.SetSkin({replacement}, force=true)");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CRYONAUT] DisplayCharacterLoadout postfix force failed: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(CharacterProgressionTab), "SetupSkinSelectionGrid")]
        [HarmonyPrefix]
        public static void PrefixSetupSkinGrid(CharacterProgressionTab __instance)
        {
            try
            {
                string currentCharacterGuid = Traverse.Create(__instance).Field("currentCharacterGuid").GetValue<string>();
                MelonLogger.Msg($"[CRYONAUT-DEBUG] SetupSkinSelectionGrid currentCharacterGuid={currentCharacterGuid}");
                if (!string.Equals(currentCharacterGuid, "cryonaut", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(currentCharacterGuid, "finn", StringComparison.OrdinalIgnoreCase))
                    return;

                MelonLogger.Msg("[CRYONAUT-DEBUG] SetupSkinSelectionGrid prefix hit for cryonaut");
                CryonautModelFix.ApplyToApiData();

                if (ApiData.Characters != null && ApiData.Characters.TryGetValue("cryonaut", out var cryonautChar) && cryonautChar?.skins != null)
                {
                    MelonLogger.Msg($"[CRYONAUT-DEBUG] Grid source skins: {string.Join(",", cryonautChar.skins)}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CRYONAUT] SetupSkinSelectionGrid remap failed: {ex.Message}");
            }
        }
    }
}
