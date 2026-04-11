using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Andromeda.Mod.Patches
{
    /// <summary>
    /// Patches LoadoutSelector.ParsePerkData to guard against a NullReferenceException
    /// that occurs on line 232 of the decompiled source:
    ///
    ///   ApiData.Perks.Where(p => p.Value.guid == guid).FirstOrDefault().Value.tier
    ///
    /// When no perk in ApiData.Perks matches the given guid, FirstOrDefault() returns
    /// default(KeyValuePair<string, PerkData>) whose .Value is null — causing an NRE.
    /// This typically happens when a client joins and receives a LoadoutSelectClient spawn
    /// message (EntityManagerClient.OnEntitySpawn) before ApiData is fully populated,
    /// or when the server sends a perk guid that the client's ApiData doesn't recognise.
    /// </summary>
    [HarmonyPatch]
    public static class LoadoutSelectorParsePerkDataPatch
    {
        public static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(LoadoutSelector), "ParsePerkData",
                new[] { typeof(string[]) });

        [HarmonyPrefix]
        public static bool Prefix(LoadoutSelector __instance, string[] perkGuids,
            ref LoadoutSelectionPerkGrid.DisplayData[] __result)
        {
            if (ApiData.Perks == null)
            {
                MelonLogger.Warning("[LOADOUT] ApiData.Perks is null during ParsePerkData — returning empty perk list.");
                __result = Array.Empty<LoadoutSelectionPerkGrid.DisplayData>();
                return false;
            }

            var displayDataList = new List<LoadoutSelectionPerkGrid.DisplayData>();

            foreach (string perkGuid in perkGuids)
            {
                string guid = perkGuid;
                var (_, perkSettings, found) = PerkSpawnList.Instance.GetByGuid(guid);
                if (!found || perkSettings.icon == null)
                    continue;

                // Safe tier lookup: guard against missing entry in ApiData.Perks.
                var matchingPerk = ApiData.Perks
                    .Where(p => p.Value != null && p.Value.guid == guid)
                    .Select(p => p.Value)
                    .FirstOrDefault();

                int tier = matchingPerk?.tier ?? 0;

                bool isCharacterPerk = ApiData.Characters != null &&
                    ApiData.Characters.Any(c =>
                        c.Value?.perks != null &&
                        ((IEnumerable<string>)c.Value.perks).Contains(guid));

                displayDataList.Add(new LoadoutSelectionPerkGrid.DisplayData
                {
                    guid = guid,
                    icon = perkSettings.icon,
                    tier = tier,
                    type = isCharacterPerk ? "character_perk" : "general_perk"
                });
            }

            __result = displayDataList.ToArray();
            return false; // Skip original
        }
    }
}
