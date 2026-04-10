using HarmonyLib;
using Andromeda.Mod.Features;
using System.Linq;

namespace Andromeda.Mod.Patches.Lobby
{
    [HarmonyPatch(typeof(CustomPartyServer), "<Setup>b__5_2")]
    public static class CustomPartyServerStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CustomPartyServer __instance, ref bool __result)
        {
            if (!__result && ForceStartFeature.ForceStartTriggered)
            {
                if (__instance.leader == null || 
                    (__instance.lobby.Ready.Count() > 0 && __instance.lobby.Unready.Count() == 0))
                {
                    __result = true;
                    ForceStartFeature.ForceStartTriggered = false;
                }
            }
        }
    }
}
