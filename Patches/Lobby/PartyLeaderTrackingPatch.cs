using HarmonyLib;
using Andromeda.Mod.Features;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch(typeof(CustomPartyServer), "SendState")]
    public static class PartyLeaderTrackingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CustomPartyServer __instance)
        {
            LobbySettingsReplicationFeature.SetCachedLeader(__instance.leader);
        }
    }
}
