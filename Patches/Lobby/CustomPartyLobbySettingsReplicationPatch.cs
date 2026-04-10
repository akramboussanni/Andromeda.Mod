using HarmonyLib;
using Andromeda.Mod.Features;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch(typeof(CustomPartyClient), "OnChangeOptions")]
    public static class CustomPartyLobbySettingsReplicationPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            LobbySettingsReplicationFeature.OnCustomPartyOptionsChanged();
        }
    }
}
