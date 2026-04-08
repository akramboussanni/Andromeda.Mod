using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using Windwalk.Net;
using MelonLoader;

namespace Andromeda.Mod.Patches
{
    internal static class NameColorTempDebug
    {
        // TEMP: set to false after diagnosing color flow.
        public static bool Enabled = true;

        public static void Log(string message)
        {
            if (!Enabled)
                return;

            MelonLogger.Msg($"[COLOR-PATCH][TEMP] {message}");
        }
    }

    [HarmonyPatch(typeof(ApiShared), "ParseResponse")]
    public static class ApiSharedParseResponsePatch
    {
        public static readonly Dictionary<string, Color> SteamIdToColor = new Dictionary<string, Color>();

        public static void Postfix(UnityWebRequest request, object __result)
        {
            if (request == null || request.downloadHandler == null || string.IsNullOrEmpty(request.downloadHandler.text))
                return;

            try
            {
                // Only intercept player profile data
                if (request.url.Contains("/players/get") || request.url.Contains("/players/auth/get"))
                {
                    NameColorTempDebug.Log($"ParseResponse hit: {request.url}");
                    var json = JObject.Parse(request.downloadHandler.text);
                    var data = json["data"];
                    
                    if (data == null)
                    {
                        NameColorTempDebug.Log("ParseResponse data field missing.");
                        return;
                    }

                    if (data.Type == JTokenType.Array)
                    {
                        int count = 0;
                        foreach (var p in data)
                        {
                            ExtractColor(p);
                            count++;
                        }
                        NameColorTempDebug.Log($"Processed players/get array entries: {count}. Cache size={SteamIdToColor.Count}");
                    }
                    else if (data.Type == JTokenType.Object)
                    {
                        ExtractColor(data);
                        NameColorTempDebug.Log($"Processed single player payload. Cache size={SteamIdToColor.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[COLOR-PATCH] Failed to parse nameColor from JSON: {ex.Message}");
            }
        }

        private static void ExtractColor(JToken p)
        {
            string sid = p["steamId"]?.ToString();
            string colorRaw = p["nameColor"]?.ToString();

            if (string.IsNullOrEmpty(sid))
            {
                NameColorTempDebug.Log("Skipping player with missing steamId.");
                return;
            }

            // Only accept strict HTML hex colors to prevent arbitrary color injection.
            if (NameColorEnforcer.TryParseApprovedColor(colorRaw, out var color))
            {
                SteamIdToColor[sid] = color;
                NameColorTempDebug.Log($"Cached color for steamId={sid}, raw='{colorRaw}'");
            }
            else
            {
                SteamIdToColor.Remove(sid);
                NameColorTempDebug.Log($"No valid color for steamId={sid}, raw='{colorRaw ?? "<null>"}'");
            }
        }
    }

    internal static class NameColorEnforcer
    {
        private static readonly Regex RichTextTagRegex = new Regex("<.*?>", RegexOptions.Compiled);
        private static readonly Dictionary<int, PlayerId> OverheadUiOwnerByInstanceId = new Dictionary<int, PlayerId>();

        public static string SanitizeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // Strip all rich text tags so players cannot inject their own formatting.
            return RichTextTagRegex.Replace(value, string.Empty);
        }

        public static string SanitizeUsername(string username)
        {
            return SanitizeRichText(username);
        }

        public static bool TryParseApprovedColor(string colorRaw, out Color color)
        {
            color = Color.white;

            if (string.IsNullOrWhiteSpace(colorRaw))
                return false;

            string normalized = colorRaw.Trim();
            if (!normalized.StartsWith("#"))
                normalized = "#" + normalized;

            // Strictly allow only #RRGGBB or #RRGGBBAA.
            if (!(normalized.Length == 7 || normalized.Length == 9))
                return false;

            for (int i = 1; i < normalized.Length; i++)
            {
                char c = normalized[i];
                bool isHex =
                    (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f') ||
                    (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }

            return ColorUtility.TryParseHtmlString(normalized, out color);
        }

        public static bool TryGetApprovedColor(PlayerId playerId, out Color color)
        {
            color = default;

            try
            {
                if (UserStore.Instance == null)
                {
                    NameColorTempDebug.Log($"TryGetApprovedColor miss: UserStore.Instance null for playerId={playerId}");
                    return false;
                }

                if (!UserStore.Instance.All.TryGetValue(playerId, out var user) || string.IsNullOrEmpty(user.steamId))
                {
                    NameColorTempDebug.Log($"TryGetApprovedColor miss: user/steamId missing for playerId={playerId}");
                    return false;
                }

                bool found = ApiSharedParseResponsePatch.SteamIdToColor.TryGetValue(user.steamId, out color);
                NameColorTempDebug.Log(
                    found
                        ? $"TryGetApprovedColor hit: playerId={playerId}, steamId={user.steamId}"
                        : $"TryGetApprovedColor miss: playerId={playerId}, steamId={user.steamId}, cacheSize={ApiSharedParseResponsePatch.SteamIdToColor.Count}"
                );
                return found;
            }
            catch
            {
                NameColorTempDebug.Log($"TryGetApprovedColor exception for playerId={playerId}");
                return false;
            }
        }

        public static void SetOverheadOwner(OverheadUI instance, PlayerId playerId)
        {
            if (instance == null)
                return;

            OverheadUiOwnerByInstanceId[instance.GetInstanceID()] = playerId;
        }

        public static bool TryGetOverheadOwner(OverheadUI instance, out PlayerId playerId)
        {
            playerId = default;
            return instance != null && OverheadUiOwnerByInstanceId.TryGetValue(instance.GetInstanceID(), out playerId);
        }

        public static void ApplyDisplayPolicy(TMP_Text textComponent, PlayerId playerId)
        {
            if (textComponent == null)
                return;

            textComponent.text = SanitizeUsername(textComponent.text);

            // Manual color application is safer than rich text: no user-controlled tags are interpreted.
            textComponent.richText = false;

            if (TryGetApprovedColor(playerId, out var approved))
                textComponent.color = approved;
        }
    }

    [HarmonyPatch(typeof(UserStore), "AddAll")]
    public static class UserStoreAddAllPatch
    {
        public static void Postfix()
        {
            try
            {
                var users = UserStore.Instance.All;
                if (users.Count > 0)
                {
                    var usersField = typeof(UserStore).GetField("users", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (usersField != null)
                    {
                        var usersDict = (Dictionary<PlayerId, UserStore.Data>)usersField.GetValue(UserStore.Instance);
                        foreach (var id in new List<PlayerId>(usersDict.Keys))
                        {
                            var data = usersDict[id];
                            data.username = NameColorEnforcer.SanitizeUsername(data.username);
                            usersDict[id] = data;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[COLOR-PATCH] Failed to apply colored names: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(VoicePlayerListItem), "Initialize")]
    public static class VoicePlayerListItemInitializePatch
    {
        private static readonly FieldInfo UsernameField = AccessTools.Field(typeof(VoicePlayerListItem), "username");

        public static void Prefix(ref string username)
        {
            username = NameColorEnforcer.SanitizeUsername(username);
        }

        public static void Postfix(VoicePlayerListItem __instance, PlayerId playerId)
        {
            var usernameText = UsernameField?.GetValue(__instance) as TMP_Text;
            NameColorEnforcer.ApplyDisplayPolicy(usernameText, playerId);
        }
    }

    [HarmonyPatch(typeof(LoadoutPlayerStateUI), "UpdateState")]
    public static class LoadoutPlayerStateUiUpdateStatePatch
    {
        private static readonly FieldInfo UsernameField = AccessTools.Field(typeof(LoadoutPlayerStateUI), "username");

        public static void Prefix(ref string username)
        {
            username = NameColorEnforcer.SanitizeUsername(username);
        }

        public static void Postfix(LoadoutPlayerStateUI __instance, PlayerId playerId)
        {
            var usernameText = UsernameField?.GetValue(__instance) as TMP_Text;
            NameColorEnforcer.ApplyDisplayPolicy(usernameText, playerId);
        }
    }

    [HarmonyPatch(typeof(PlayerMuteListItem), "Initialize")]
    public static class PlayerMuteListItemInitializePatch
    {
        private static readonly FieldInfo UsernameField = AccessTools.Field(typeof(PlayerMuteListItem), "username");

        public static void Prefix(ref string name)
        {
            name = NameColorEnforcer.SanitizeUsername(name);
        }

        public static void Postfix(PlayerMuteListItem __instance, PlayerId playerId)
        {
            var usernameText = UsernameField?.GetValue(__instance) as TMP_Text;
            NameColorEnforcer.ApplyDisplayPolicy(usernameText, playerId);
        }
    }

    [HarmonyPatch(typeof(EndGameStatsListItem), "Initialize")]
    public static class EndGameStatsListItemInitializePatch
    {
        private static readonly FieldInfo UsernameField = AccessTools.Field(typeof(EndGameStatsListItem), "username");

        public static void Postfix(EndGameStatsListItem __instance, AndromedaShared.PlayerResult playerResult)
        {
            var usernameText = UsernameField?.GetValue(__instance) as TMP_Text;
            NameColorEnforcer.ApplyDisplayPolicy(usernameText, playerResult.playerId);
        }
    }

    [HarmonyPatch(typeof(OverheadUI), "Initialize")]
    public static class OverheadUiInitializePatch
    {
        public static void Postfix(OverheadUI __instance, PlayerId playerId)
        {
            NameColorEnforcer.SetOverheadOwner(__instance, playerId);
        }
    }

    [HarmonyPatch(typeof(OverheadUI), "SetUsername")]
    public static class OverheadUiSetUsernamePatch
    {
        private static readonly FieldInfo UsernameField = AccessTools.Field(typeof(OverheadUI), "uiUsername");

        public static void Prefix(ref string username)
        {
            username = NameColorEnforcer.SanitizeUsername(username);
        }

        public static void Postfix(OverheadUI __instance)
        {
            if (!NameColorEnforcer.TryGetOverheadOwner(__instance, out var playerId))
                return;

            var usernameText = UsernameField?.GetValue(__instance) as TMP_Text;
            NameColorEnforcer.ApplyDisplayPolicy(usernameText, playerId);
        }
    }

    [HarmonyPatch(typeof(TextChat), "OnTextChat")]
    public static class TextChatOnTextChatPatch
    {
        private static readonly FieldInfo AlienChatColorField = AccessTools.Field(typeof(TextChat), "alienChatColor");
        private static readonly FieldInfo DeadChatColorField = AccessTools.Field(typeof(TextChat), "deadChatColor");
        private static readonly FieldInfo ChatMessagePrefabField = AccessTools.Field(typeof(TextChat), "chatMessagePrefab");
        private static readonly FieldInfo ChatMessageContentField = AccessTools.Field(typeof(TextChat), "chatMessageContent");

        public static bool Prefix(TextChat __instance, VoiceClient.Room room, PlayerId playerId, string message)
        {
            try
            {
                Log.Info("text chat message", ("playerId", playerId), ("room", room), ("message", message));

                var userTuple = UserStore.Instance.Fetch(playerId);
                if (!userTuple.Item2)
                {
                    Log.Info().Str("player_id", playerId.ToString()).Msg("received message from unknown player");
                    return false;
                }

                var content = ChatMessageContentField?.GetValue(__instance) as RectTransform;
                var prefab = ChatMessagePrefabField?.GetValue(__instance) as TMP_Text;
                if (content == null || prefab == null)
                    return false;

                var tmpText = UnityEngine.Object.Instantiate(prefab, content.transform);
                if (tmpText == null)
                    return false;

                string safeUsername = NameColorEnforcer.SanitizeUsername(userTuple.Item1.username);
                string safeMessage = NameColorEnforcer.SanitizeRichText(message);

                string usernameSegment = safeUsername;
                if (NameColorEnforcer.TryGetApprovedColor(playerId, out var approvedColor))
                {
                    string html = ColorUtility.ToHtmlStringRGBA(approvedColor);
                    usernameSegment = $"<color=#{html}>{safeUsername}</color>";
                    NameColorTempDebug.Log($"Chat apply color: playerId={playerId}, room={room}, color=#{html}, username='{safeUsername}'");
                }
                else
                {
                    NameColorTempDebug.Log($"Chat no color: playerId={playerId}, room={room}, username='{safeUsername}'");
                }

                string messageSegment = safeMessage;
                if (room == VoiceClient.Room.Dead)
                {
                    if (DeadChatColorField?.GetValue(__instance) is Color deadChatColor)
                    {
                        string deadHtml = ColorUtility.ToHtmlStringRGBA(deadChatColor);
                        messageSegment = $"<color=#{deadHtml}>{safeMessage}</color>";
                    }
                }
                else if (room == VoiceClient.Room.Alien)
                {
                    if (AlienChatColorField?.GetValue(__instance) is Color alienChatColor)
                    {
                        string alienHtml = ColorUtility.ToHtmlStringRGBA(alienChatColor);
                        messageSegment = $"<color=#{alienHtml}>{safeMessage}</color>";
                    }
                }

                // Keep rich text enabled here so username and room segments can be colored independently.
                tmpText.richText = true;
                tmpText.text = $"{usernameSegment}: {messageSegment}";

                // We fully handled rendering; skip original method.
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[COLOR-PATCH] Failed to apply chat name color: {ex.Message}");
                // If patch fails for any reason, let vanilla logic run.
                return true;
            }
        }
    }
}
