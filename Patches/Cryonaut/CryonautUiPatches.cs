using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Patches.Cryonaut
{
    [HarmonyPatch]
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

    [HarmonyPatch]
    public static class CryonautClassTabPatch
    {
        [HarmonyPatch(typeof(CharacterClassTab), "DisplayDetails")]
        [HarmonyPrefix]
        public static void PrefixDisplayDetails(CharacterSpawnList.Key characterKey, ref SkinSpawnList.Key? skinKey)
        {
            if (characterKey != CharacterSpawnList.Key.Cryonaut)
                return;

            CryonautModelFix.ApplyToApiData();
            var replacement = CryonautModelFix.GetReplacementSkinKey();
            skinKey = replacement;
            MelonLogger.Msg($"[CRYONAUT-DEBUG] CharacterClassTab.DisplayDetails forced skinKey={replacement}");
        }
    }

    [HarmonyPatch]
    public static class CryonautShowcasePatch
    {
        [HarmonyPatch(typeof(Showcase), "SetSkin")]
        [HarmonyPrefix]
        public static void PrefixSetSkin(ref SkinSpawnList.Key? key, bool force)
        {
            MelonLogger.Msg($"[CRYONAUT-DEBUG] Showcase.SetSkin incoming key={(key.HasValue ? key.Value.ToString() : "<null>")}");
            if (key.HasValue && key.Value == SkinSpawnList.Key.Cryonaut)
            {
                var replacement = CryonautModelFix.GetReplacementSkinKey();
                MelonLogger.Msg($"[CRYONAUT-DEBUG] Showcase.SetSkin remap Cryonaut -> {replacement}");
                key = replacement;
            }

            if (key.HasValue)
            {
                try
                {
                    var settings = SkinSpawnList.Instance.Get(key.Value);
                    string guid = settings != null ? settings.guid : "<null>";
                    bool hasPrefab = settings != null && settings.prefab != null;
                    MelonLogger.Msg($"[CRYONAUT-DEBUG] Showcase.SetSkin final key={key.Value} guid={guid} prefab={(hasPrefab ? "yes" : "no")}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[CRYONAUT] Showcase.SetSkin debug probe failed: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Showcase), "SetSkin")]
        [HarmonyPostfix]
        public static void PostfixSetSkin(Showcase __instance, SkinSpawnList.Key? key, bool force)
        {
            try
            {
                if (!key.HasValue)
                    return;

                var replacementKey = CryonautModelFix.GetReplacementSkinKey();
                if (key.Value != SkinSpawnList.Key.Cryonaut && key.Value != replacementKey)
                    return;

                var characterRoot = Traverse.Create(__instance).Field("characterRoot").GetValue<Transform>();
                var rotationRoot = Traverse.Create(__instance).Field("rotationRoot").GetValue<Transform>();
                if (characterRoot == null || rotationRoot == null)
                    return;

                if (characterRoot.childCount > 0)
                    return;

                var replacement = SkinSpawnList.Instance.Get(replacementKey);
                if (replacement == null || replacement.prefab == null)
                {
                    MelonLogger.Warning($"[CRYONAUT] Showcase fallback spawn failed: {replacementKey} prefab missing");
                    return;
                }

                var refs = UnityEngine.Object.Instantiate<CharacterReferences>(
                    replacement.prefab,
                    characterRoot.position,
                    rotationRoot.rotation,
                    characterRoot
                );
                WindwalkUtilities.SetAllToLayer(refs.gameObject, Layers.Showcase);

                Traverse.Create(__instance).Field("references").SetValue(refs);

                var itemKey = Traverse.Create(__instance).Field("itemKey").GetValue<ItemSpawnList.Key?>();
                __instance.SetItem(itemKey, true);

                MelonLogger.Msg($"[CRYONAUT-DEBUG] Showcase fallback spawned {replacement.guid} prefab manually");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CRYONAUT] Showcase fallback spawn exception: {ex.Message}");
            }
        }
    }
}
