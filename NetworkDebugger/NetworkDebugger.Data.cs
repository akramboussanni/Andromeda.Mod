using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace Andromeda.Mod
{
    public static partial class NetworkDebugger
    {
        private static IEnumerable<object> GetDataList(string category)
        {
            object instance = null;
            switch (category)
            {
                case "Characters": instance = CharacterSpawnList.Instance; break;
                case "Items": instance = ItemSpawnList.Instance; break;
                case "Abilities": instance = AbilitySpawnList.Instance; break;
                case "Perks": instance = PerkSpawnList.Instance; break;
                case "Skins":
                    try
                    {
                        Patches.SkinGuidRuntimeFix.NormalizeAllSkins();
                    }
                    catch { }
                    instance = SkinSpawnList.Instance;
                    break;
            }

            if (instance == null) return new List<object>();

            var getEntries = instance.GetType().GetMethod("GetEntries");
            if (getEntries == null) return new List<object>();

            var entries = getEntries.Invoke(instance, null) as IEnumerable;
            if (entries == null) return new List<object>();

            var result = new List<object>();
            foreach (var e in entries) result.Add(e);
            return result;
        }

        private static string GetItemName(object entry)
        {
            if (entry == null) return "null";
            var type = entry.GetType();
            var keyField = type.GetField("key");
            if (keyField != null)
            {
                var val = keyField.GetValue(entry);
                return val?.ToString() ?? "null";
            }
            return entry.ToString();
        }

        private static void DrawObjectInspector(object obj)
        {
            if (obj == null) return;
            var type = obj.GetType();
            GUILayout.Label($"<b>Type:</b> {type.Name}");

            var keyField = type.GetField("key");
            var valueField = type.GetField("value");

            if (keyField != null)
            {
                GUILayout.Label($"<b>Key (Enum):</b> {keyField.GetValue(obj)}");
            }

            if (valueField != null)
            {
                var settingsObj = valueField.GetValue(obj);
                GUILayout.Space(5);
                GUILayout.Label("<b>Settings Object:</b>");
                if (settingsObj != null)
                {
                    var keyObj = keyField != null ? keyField.GetValue(obj) : null;
                    var resolvedGuid = ResolveGuidWithFallback(settingsObj, keyObj);
                    if (!string.IsNullOrEmpty(resolvedGuid))
                    {
                        var currentGuid = GetStringMember(settingsObj, "guid");
                        if (string.IsNullOrWhiteSpace(currentGuid))
                            SetStringMember(settingsObj, "guid", resolvedGuid);
                    }

                    DrawReflectedFields(settingsObj);

                    if (!string.IsNullOrEmpty(resolvedGuid))
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("<b>resolvedGuid:</b> ", GUILayout.Width(150));
                        GUILayout.Label(resolvedGuid);
                        GUILayout.EndHorizontal();
                    }
                }
                else
                {
                    GUILayout.Label("null");
                }
            }
            else
            {
                DrawReflectedFields(obj);
            }
        }

        private static void DrawReflectedFields(object obj)
        {
            if (obj == null) return;
            var type = obj.GetType();

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var val = field.GetValue(obj);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{field.Name}:</b> ", GUILayout.Width(150));
                GUILayout.Label(val?.ToString() ?? "null");
                GUILayout.EndHorizontal();
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                object val = null;
                try { val = prop.GetValue(obj, null); } catch { val = "Error"; }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{prop.Name}:</b> ", GUILayout.Width(150));
                GUILayout.Label(val?.ToString() ?? "null");
                GUILayout.EndHorizontal();
            }
        }

        private static string GetStringMember(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return null;

            var type = obj.GetType();
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(string))
                return field.GetValue(obj) as string;

            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanRead && prop.PropertyType == typeof(string))
                return prop.GetValue(obj, null) as string;

            return null;
        }

        private static bool SetStringMember(object obj, string memberName, string value)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return false;

            var type = obj.GetType();
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(string))
            {
                field.SetValue(obj, value);
                return true;
            }

            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string))
            {
                prop.SetValue(obj, value, null);
                return true;
            }

            return false;
        }

        private static string ToSnakeCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var sb = new System.Text.StringBuilder(value.Length + 8);
            bool wroteUnderscore = false;

            foreach (char c in value.Trim())
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (char.IsUpper(c) && sb.Length > 0 && !wroteUnderscore)
                        sb.Append('_');

                    sb.Append(char.ToLowerInvariant(c));
                    wroteUnderscore = false;
                }
                else if (sb.Length > 0 && !wroteUnderscore)
                {
                    sb.Append('_');
                    wroteUnderscore = true;
                }
            }

            string snake = sb.ToString().Trim('_');
            if (snake.EndsWith("_skin_settings", StringComparison.OrdinalIgnoreCase))
                snake = snake.Substring(0, snake.Length - "_skin_settings".Length);
            else if (snake.EndsWith("_settings", StringComparison.OrdinalIgnoreCase))
                snake = snake.Substring(0, snake.Length - "_settings".Length);

            return snake.Trim('_');
        }

        private static string ResolveGuidWithFallback(object settingsObj, object keyObj)
        {
            string guid = GetStringMember(settingsObj, "guid");
            if (!string.IsNullOrWhiteSpace(guid))
                return guid;

            string skinName = GetStringMember(settingsObj, "skinName");
            string name = GetStringMember(settingsObj, "name");
            string keyName = keyObj?.ToString();

            if (!string.IsNullOrWhiteSpace(name)) return ToSnakeCase(name);
            if (!string.IsNullOrWhiteSpace(keyName)) return ToSnakeCase(keyName);
            if (!string.IsNullOrWhiteSpace(skinName)) return ToSnakeCase(skinName);

            return null;
        }

        public static void DumpData()
        {
            MelonLoader.MelonLogger.Msg("Starting Data Dump...");

            string dumpDir = Path.Combine(Directory.GetCurrentDirectory(), "constant_data");
            if (!Directory.Exists(dumpDir))
            {
                Directory.CreateDirectory(dumpDir);
            }

            DumpFile(dumpDir, "characters.json", ExtractCharacters());
            DumpFile(dumpDir, "items.json", ExtractItems());
            DumpFile(dumpDir, "abilities.json", ExtractAbilities());
            DumpFile(dumpDir, "perks.json", ExtractPerks());
            DumpFile(dumpDir, "skins.json", ExtractSkins());
            DumpFile(dumpDir, "progression.json", ExtractProgression());

            MelonLoader.MelonLogger.Msg($"Data dumped to {dumpDir}");
        }

        private static void DumpFile(string dir, string filename, object data)
        {
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(Path.Combine(dir, filename), json);
            MelonLoader.MelonLogger.Msg($"Exported {filename}");
        }

        private static object ExtractCharacters() => ExtractEnumMapping(CharacterSpawnList.Instance);
        private static object ExtractItems() => ExtractEnumMapping(ItemSpawnList.Instance);
        private static object ExtractAbilities() => ExtractEnumMapping(AbilitySpawnList.Instance);
        private static object ExtractPerks() => ExtractEnumMapping(PerkSpawnList.Instance);
        private static object ExtractSkins() => ExtractEnumMapping(SkinSpawnList.Instance);

        private static object ExtractProgression()
        {
            var progressionData = new Dictionary<string, object>();
            var charProgress = new Dictionary<CharacterSpawnList.Key, (AbilitySpawnList.Key baseAbility, PerkSpawnList.Key[] uniquePerks)>
            {
                { CharacterSpawnList.Key.Medic, (AbilitySpawnList.Key.Heal_1, new[] { PerkSpawnList.Key.Bedside_Manner_1, PerkSpawnList.Key.Remote_Diagnostics_1 }) },
                { CharacterSpawnList.Key.Spy, (AbilitySpawnList.Key.Scan_1, new[] { PerkSpawnList.Key.Ninja_1, PerkSpawnList.Key.Advanced_Optics_1 }) },
                { CharacterSpawnList.Key.Scientist, (AbilitySpawnList.Key.TeleportDeploy_1, new[] { PerkSpawnList.Key.Critical_Failure_1, PerkSpawnList.Key.Fusion_Cell_1 }) },
                { CharacterSpawnList.Key.Commando, (AbilitySpawnList.Key.BattleRage_1, new[] { PerkSpawnList.Key.Knuckle_Dusters_1, PerkSpawnList.Key.Second_Wind_1 }) },
                { CharacterSpawnList.Key.Captain, (AbilitySpawnList.Key.Shield_1, new[] { PerkSpawnList.Key.Dead_Shot_1, PerkSpawnList.Key.Mercenary_1 }) },
                { CharacterSpawnList.Key.SpaceMonkey, (AbilitySpawnList.Key.CombatRoll_1, new[] { PerkSpawnList.Key.Lean_Build_1, PerkSpawnList.Key.Scrappy_1 }) },
                { CharacterSpawnList.Key.Grifter, (AbilitySpawnList.Key.SpellSteal_1, new[] { PerkSpawnList.Key.Channeler_1, PerkSpawnList.Key.Alien_Affinity_1 }) },
                { CharacterSpawnList.Key.Officer, (AbilitySpawnList.Key.LockDoor_2, new[] { PerkSpawnList.Key.Utility_Belt_1, PerkSpawnList.Key.Bullet_Proof_Vest_1 }) },
                { CharacterSpawnList.Key.Assassin, (AbilitySpawnList.Key.ShadowSneak_1, new[] { PerkSpawnList.Key.Concealed_Blade_1, PerkSpawnList.Key.Backstab_1 }) }
            };

            var charactersDict = new Dictionary<string, object>();

            foreach (var kvp in charProgress)
            {
                var charKey = kvp.Key;
                var charSettings = CharacterSpawnList.Instance.Get(charKey);
                if (charSettings == null) continue;

                var abilityKey = kvp.Value.baseAbility;
                var perkKeys = kvp.Value.uniquePerks;

                var abilityData = new Dictionary<string, object>();
                var abilityBaseInfo = AbilitySpawnList.Instance.Get(abilityKey);
                if (abilityBaseInfo != null)
                {
                    var abilityTiers = new List<string>();
                    string baseName = abilityKey.ToString();
                    string rootName = baseName.EndsWith("_1") ? baseName.Substring(0, baseName.Length - 2) : baseName;

                    foreach (var entry in AbilitySpawnList.Instance.GetEntries())
                    {
                        string entryName = entry.key.ToString();
                        if (entryName == rootName || entryName.StartsWith(rootName + "_"))
                        {
                            if (entry.value != null) abilityTiers.Add(entry.value.guid);
                        }
                    }

                    abilityData["base_guid"] = abilityBaseInfo.guid;
                    abilityData["tiers"] = abilityTiers;
                }

                var perksList = new List<object>();
                foreach (var pKey in perkKeys)
                {
                    var pSettings = PerkSpawnList.Instance.Get(pKey);
                    if (pSettings != null)
                    {
                        var perkTiers = new List<string>();
                        string pBaseName = pKey.ToString();
                        string pRootName = pBaseName.EndsWith("_1") ? pBaseName.Substring(0, pBaseName.Length - 2) : pBaseName;

                        foreach (var entry in PerkSpawnList.Instance.GetEntries())
                        {
                            string entryName = entry.key.ToString();
                            if (entryName == pRootName || entryName.StartsWith(pRootName + "_"))
                            {
                                if (entry.value != null) perkTiers.Add(entry.value.guid);
                            }
                        }

                        perksList.Add(new
                        {
                            base_guid = pSettings.guid,
                            tiers = perkTiers
                        });
                    }
                }

                charactersDict[charSettings.guid] = new
                {
                    ability = abilityData,
                    perks = perksList
                };
            }

            progressionData["characters"] = charactersDict;
            return progressionData;
        }

        private static object ExtractEnumMapping(object instance)
        {
            if (instance == null) return null;
            var type = instance.GetType();
            var getEntries = type.GetMethod("GetEntries");
            if (getEntries == null) return null;

            var entries = getEntries.Invoke(instance, null) as IEnumerable;
            if (entries == null) return null;

            var list = new List<object>();
            foreach (var entry in entries)
            {
                var entryType = entry.GetType();
                var keyField = entryType.GetField("key");
                var valueField = entryType.GetField("value");
                if (valueField == null) continue;

                var settings = valueField.GetValue(entry);
                if (settings == null) continue;

                var settingsType = settings.GetType();
                var guidField = settingsType.GetField("guid") ?? settingsType.GetProperty("guid") as MemberInfo;

                string guidVal = null;
                if (guidField is FieldInfo fi) guidVal = fi.GetValue(settings) as string;
                else if (guidField is PropertyInfo pi) guidVal = pi.GetValue(settings, null) as string;

                var nameField = settingsType.GetField("name") ?? settingsType.GetProperty("name") as MemberInfo;
                object nameVal = null;
                if (nameField is FieldInfo ni) nameVal = ni.GetValue(settings);
                else if (nameField is PropertyInfo npi) nameVal = npi.GetValue(settings, null);

                if (string.IsNullOrEmpty(guidVal) && keyField != null)
                {
                    var keyVal = keyField.GetValue(entry);
                    if (keyVal != null) guidVal = keyVal.ToString();
                }

                if (!string.IsNullOrEmpty(guidVal))
                {
                    list.Add(new
                    {
                        guid = guidVal,
                        name = nameVal?.ToString() ?? guidVal,
                        purchasable = true,
                        cost = 0
                    });
                }
            }
            return list;
        }
    }
}
