using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Patches
{
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
