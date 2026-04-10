using HarmonyLib;
using Andromeda.Mod.Features;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch(typeof(PlayPanel), "OnEnable")]
    public static class PlayPanelLobbySettingsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayPanel __instance)
        {
            LobbyOptionsAutoUiFeature.EnsureInjected(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayPanel), "Render")]
    public static class PlayPanelLobbySettingsRenderPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayPanel __instance)
        {
            LobbyOptionsAutoUiFeature.EnsureInjected(__instance);
        }
    }
}
