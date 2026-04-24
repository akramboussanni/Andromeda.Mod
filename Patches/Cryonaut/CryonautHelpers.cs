using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Patches.Cryonaut
{
    [HarmonyPatch]
    public static class CryonautModelFix
    {
        private const string CryonautGuid = "cryonaut";
        private const string FinnGuid = "finn";
        private static readonly bool DebugLogsEnabled = true;

        public static SkinSpawnList.Key GetReplacementSkinKey()
        {
            SkinSpawnList.Key[] candidates =
            {
                SkinSpawnList.Key.WraithSpy,
                SkinSpawnList.Key.Spy,
                SkinSpawnList.Key.Medic,
            };

            foreach (var key in candidates)
            {
                try
                {
                    var settings = SkinSpawnList.Instance.Get(key);
                    if (settings != null && !string.IsNullOrEmpty(settings.guid) && settings.prefab != null)
                        return key;
                }
                catch
                {
                }
            }

            return SkinSpawnList.Key.WraithSpy;
        }

        private static void DebugLog(string message)
        {
            if (!DebugLogsEnabled)
                return;
            MelonLogger.Msg($"[CRYONAUT-DEBUG] {message}");
        }

        public static string GetReplacementSkinGuid()
        {
            try
            {
                var settings = SkinSpawnList.Instance.Get(GetReplacementSkinKey());
                if (settings != null && !string.IsNullOrEmpty(settings.guid))
                    return settings.guid;
            }
            catch
            {
            }

            return "wraith_spy";
        }

        public static void ApplyToApiData()
        {
            try
            {
                DebugLog("ApplyToApiData start");
                SkinSpawnList.Key replacementSkinKey = GetReplacementSkinKey();
                string replacementSkinGuid = GetReplacementSkinGuid();
                DebugLog($"ReplacementSkin resolved to: key={replacementSkinKey} guid={replacementSkinGuid}");

                if (ApiData.Characters != null && ApiData.Characters.TryGetValue(CryonautGuid, out var cryonautChar) && cryonautChar != null)
                {
                    string before = cryonautChar.skins == null ? "<null>" : string.Join(",", cryonautChar.skins);
                    cryonautChar.skins = new[] { replacementSkinGuid };
                    DebugLog($"ApiData.Characters[cryonaut].skins: {before} -> {string.Join(",", cryonautChar.skins)}");
                }
                else
                {
                    DebugLog("ApiData.Characters missing cryonaut entry");
                }

                if (ApiData.Characters != null && ApiData.Characters.TryGetValue(FinnGuid, out var finnChar) && finnChar != null)
                {
                    string before = finnChar.skins == null ? "<null>" : string.Join(",", finnChar.skins);
                    finnChar.skins = new[] { replacementSkinGuid };
                    DebugLog($"ApiData.Characters[finn].skins: {before} -> {string.Join(",", finnChar.skins)}");
                }

                if (ApiData.Profile?.characters != null)
                {
                    foreach (var ch in ApiData.Profile.characters)
                    {
                        if (!string.Equals(ch.guid, CryonautGuid, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(ch.guid, FinnGuid, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string before = ch.skins == null ? "<null>" : string.Join(",", ch.skins);
                        ch.skins = new[] { replacementSkinGuid, CryonautGuid };
                        DebugLog($"ApiData.Profile cryonaut skins: {before} -> {string.Join(",", ch.skins)}");
                    }
                }
                else
                {
                    DebugLog("ApiData.Profile.characters unavailable");
                }

                // Persist a valid skin key so CharacterProgressionTab and Showcase never fall back to broken cryonaut model.
                if (LocalUserData.Loadouts != null && LocalUserData.Loadouts.ContainsKey(CharacterSpawnList.Key.Cryonaut))
                {
                    var loadout = LocalUserData.Loadouts[CharacterSpawnList.Key.Cryonaut];
                    var before = loadout.skin;
                    loadout.skin = replacementSkinKey;
                    LocalUserData.Loadouts[CharacterSpawnList.Key.Cryonaut] = loadout;
                    LocalUserData.Save();
                    DebugLog($"LocalUserData cryonaut skin key: {before} -> {loadout.skin}");
                }
                else
                {
                    DebugLog("LocalUserData.Loadouts missing cryonaut key");
                }

                DebugLog("ApplyToApiData end");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CRYONAUT] Model remap fix failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    public static class SkinGuidRuntimeFix
    {
        private static bool _normalized;
        private static bool _normalizing;
        private static readonly Dictionary<string, string> AliasToGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string ToSnakeCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var chars = value.Trim().ToCharArray();
            var output = new System.Text.StringBuilder(chars.Length + 8);
            bool wroteUnderscore = false;

            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsLetterOrDigit(c))
                {
                    if (char.IsUpper(c) && output.Length > 0 && !wroteUnderscore)
                        output.Append('_');

                    output.Append(char.ToLowerInvariant(c));
                    wroteUnderscore = false;
                }
                else if (!wroteUnderscore && output.Length > 0)
                {
                    output.Append('_');
                    wroteUnderscore = true;
                }
            }

            string snake = output.ToString().Trim('_');
            if (snake.EndsWith("_skin_settings", StringComparison.OrdinalIgnoreCase))
                snake = snake.Substring(0, snake.Length - "_skin_settings".Length);
            else if (snake.EndsWith("_settings", StringComparison.OrdinalIgnoreCase))
                snake = snake.Substring(0, snake.Length - "_settings".Length);

            return snake.Trim('_');
        }

        private static string GetStringMember(object instance, string memberName)
        {
            if (instance == null)
                return null;

            var type = instance.GetType();
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(string))
                return field.GetValue(instance) as string;

            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(string) && prop.CanRead)
                return prop.GetValue(instance, null) as string;

            return null;
        }

        private static bool SetStringMember(object instance, string memberName, string value)
        {
            if (instance == null)
                return false;

            var type = instance.GetType();
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(string))
            {
                field.SetValue(instance, value);
                return true;
            }

            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(string) && prop.CanWrite)
            {
                prop.SetValue(instance, value, null);
                return true;
            }

            return false;
        }

        private static string BuildCandidateGuid(object settings, SkinSpawnList.Key key)
        {
            string fromName = ToSnakeCase(GetStringMember(settings, "name"));
            if (!string.IsNullOrEmpty(fromName))
                return fromName;

            return ToSnakeCase(key.ToString());
        }

        private static void TrackAliases(SkinSpawnList.Key key, string guid, object settings)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            AliasToGuid[key.ToString()] = guid;

            string snakeKey = ToSnakeCase(key.ToString());
            if (!string.IsNullOrEmpty(snakeKey))
                AliasToGuid[snakeKey] = guid;

            string name = GetStringMember(settings, "name");
            if (!string.IsNullOrEmpty(name))
            {
                AliasToGuid[name] = guid;
                string snakeName = ToSnakeCase(name);
                if (!string.IsNullOrEmpty(snakeName))
                    AliasToGuid[snakeName] = guid;
            }
        }

        public static void EnsureGuid(object settings, SkinSpawnList.Key key)
        {
            if (settings == null)
                return;

            string guid = GetStringMember(settings, "guid");
            if (string.IsNullOrWhiteSpace(guid))
            {
                string generated = BuildCandidateGuid(settings, key);
                if (!string.IsNullOrEmpty(generated) && SetStringMember(settings, "guid", generated))
                {
                    guid = generated;
                    MelonLogger.Msg($"[SKIN-GUID-FIX] Assigned missing guid: key={key} guid={generated}");
                }
            }

            TrackAliases(key, guid, settings);
        }

        public static void NormalizeAllSkins()
        {
            if (_normalized || _normalizing)
                return;

            if (SkinSpawnList.Instance == null)
                return;

            _normalizing = true;
            try
            {
                foreach (SkinSpawnList.Key key in Enum.GetValues(typeof(SkinSpawnList.Key)))
                {
                    try
                    {
                        var settings = SkinSpawnList.Instance.Get(key);
                        EnsureGuid(settings, key);
                    }
                    catch
                    {
                        // Skip problematic entries and continue normalizing remaining skins.
                    }
                }

                _normalized = true;
            }
            finally
            {
                _normalizing = false;
            }
        }

        public static string ResolveAlias(string requestedGuid)
        {
            if (string.IsNullOrWhiteSpace(requestedGuid))
                return requestedGuid;

            NormalizeAllSkins();
            if (AliasToGuid.TryGetValue(requestedGuid, out var mapped) && !string.IsNullOrEmpty(mapped))
                return mapped;

            return requestedGuid;
        }
    }
}
