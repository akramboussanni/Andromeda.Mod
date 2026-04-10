using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches
{
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
}
