using HarmonyLib;
using TMPro;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Reflection;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch(typeof(GraphicsSettingsTab), "Start")]
    public static class GraphicsSettingsResolutionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GraphicsSettingsTab __instance)
        {
            if (__instance == null) return;
            
            var dropdown = Traverse.Create(__instance).Field("resolutionDropdown").GetValue<TMP_Dropdown>();
            if (dropdown == null) return;

            // 1. Deduplicate resolutions from Screen.resolutions
            // Unity often returns multiples of the same resolution for different refresh rates.
            var rawResolutions = Screen.resolutions;
            if (rawResolutions == null || rawResolutions.Length == 0) return;

            var uniqueMap = new Dictionary<string, int>(); // "Width x Height" -> bestRawIndex

            for (int i = 0; i < rawResolutions.Length; i++)
            {
                var r = rawResolutions[i];
                string key = $"{r.width} x {r.height}";
                
                // Keep the raw index that offers the highest refresh rate for this resolution
                if (!uniqueMap.ContainsKey(key) || rawResolutions[uniqueMap[key]].refreshRate < r.refreshRate)
                {
                    uniqueMap[key] = i;
                }
            }

            // 2. Sort the keys logically (by Width, then Height)
            var sortedKeys = uniqueMap.Keys.ToList();
            sortedKeys.Sort((a, b) =>
            {
                var partsA = a.Split(new[] { " x " }, System.StringSplitOptions.None);
                var partsB = b.Split(new[] { " x " }, System.StringSplitOptions.None);
                if (partsA.Length < 2 || partsB.Length < 2) return 0;
                
                int wA = int.Parse(partsA[0]);
                int wB = int.Parse(partsB[0]);
                if (wA != wB) return wA.CompareTo(wB);
                
                return int.Parse(partsA[1]).CompareTo(int.Parse(partsB[1]));
            });

            // 3. Update the dropdown options
            dropdown.ClearOptions();
            dropdown.AddOptions(sortedKeys);

            // 4. Set the current value based on the saved raw index
            int currentRawIndex = LocalUserData.ResolutionIndex;
            if (currentRawIndex >= 0 && currentRawIndex < rawResolutions.Length)
            {
                var currentRes = rawResolutions[currentRawIndex];
                string currentKey = $"{currentRes.width} x {currentRes.height}";
                int newIndex = sortedKeys.IndexOf(currentKey);
                if (newIndex >= 0)
                {
                    dropdown.SetValueWithoutNotify(newIndex);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GraphicsSettingsTab), "OnResolutionChanged")]
    public static class GraphicsSettingsResolutionChangePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(GraphicsSettingsTab __instance, int index)
        {
            if (__instance == null) return true;
            
            var dropdown = Traverse.Create(__instance).Field("resolutionDropdown").GetValue<TMP_Dropdown>();
            if (dropdown == null || index < 0 || index >= dropdown.options.Count) return true;

            string selectedKey = dropdown.options[index].text;
            
            // Map the deduplicated dropdown selection back to the best raw index (highest refresh rate)
            var rawResolutions = Screen.resolutions;
            int bestRawIndex = -1;
            int maxRefresh = -1;

            for (int i = 0; i < rawResolutions.Length; i++)
            {
                var r = rawResolutions[i];
                if ($"{r.width} x {r.height}" == selectedKey)
                {
                    if (r.refreshRate > maxRefresh)
                    {
                        maxRefresh = r.refreshRate;
                        bestRawIndex = i;
                    }
                }
            }

            if (bestRawIndex != -1)
            {
                LocalUserData.ResolutionIndex = bestRawIndex;
                LocalUserData.Save();
                
                // Trigger the game's internal resolution application
                typeof(GraphicsSettingsTab)
                    .GetMethod("SetResolution", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(__instance, null);
                    
                return false; // Skip the original logic which uses the incorrect index
            }

            return true;
        }
    }
}
