using HarmonyLib;
using Andromeda.Mod.Features;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch(typeof(GeneralSettingsTab), "Start")]
    public static class GeneralSettingsTabPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GeneralSettingsTab __instance)
        {
            SettingsTabAutoUiFeature.EnsureInjected(__instance);
        }
    }
}
