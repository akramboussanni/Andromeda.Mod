using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches
{
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
