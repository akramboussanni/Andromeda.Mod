using System;
using System.Collections.Generic;
using MelonLoader;

namespace Andromeda.Mod.Features
{
    public static class PerkFeatures
    {
        /*public static void SwapPerkSprites()
        {
            try
            {
                MelonLogger.Msg("[FEAT] Swapping perk sprites...");
                
                var entries = PerkSpawnList.Instance.GetEntries();
                var groups = new Dictionary<string, SortedDictionary<int, PerkSettings>>();

                foreach (var entry in entries)
                {
                    string key = entry.key.ToString();
                    PerkSettings settings = entry.value;
                    
                    if (settings == null) continue;

                    if (char.IsDigit(key[key.Length - 1]) && key[key.Length - 2] == '_')
                    {
                        int tier = int.Parse(key[key.Length - 1].ToString());
                        string baseName = key.Substring(0, key.Length - 2);

                        if (!groups.ContainsKey(baseName))
                            groups[baseName] = new SortedDictionary<int, PerkSettings>();
                        
                        groups[baseName][tier] = settings;
                    }
                }

                int swappedCount = 0;
                foreach (var kvp in groups)
                {
                    var tiers = kvp.Value;
                    if (tiers.ContainsKey(1) && tiers.ContainsKey(2) && tiers.ContainsKey(3))
                    {
                        var t1 = tiers[1];
                        var t2 = tiers[2];
                        var t3 = tiers[3];

                        var s1 = t1.icon;
                        var s2 = t2.icon;
                        var s3 = t3.icon;
                        
                        t1.icon = s2;
                        t2.icon = s3;
                        t3.icon = s1;
                            
                        swappedCount++;
                    }
                }
                MelonLogger.Msg($"[FEAT] Swapped sprites for {swappedCount} perk groups.");
            }
            catch (Exception e)
            {
                MelonLogger.Error($"[FEAT-ERROR] Failed to swap perk sprites: {e}");
            }
        }*/
    }
}
