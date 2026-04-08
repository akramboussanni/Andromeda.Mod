using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Andromeda.Mod
{
    public static partial class NetworkDebugger
    {
        private static Color GetStatusColor(string status)
        {
            if (status == "Pending") return Color.yellow;
            if (status == "Error") return Color.red;
            return Color.green;
        }

        private static string GetShortUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.AbsolutePath;
            }
            catch { return url; }
        }

        // --- Reflection Logic ---
        private static void BuildEndpointMap()
        {
            try
            {
                MelonLogger.Msg("Mapping ApiShared endpoints...");
                var apiType = typeof(ApiShared);
                var nestedTypes = apiType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

                foreach (var type in nestedTypes)
                {
                    if (type.IsSubclassOf(typeof(ApiShared.Request)))
                    {
                        try
                        {
                            var instance = Activator.CreateInstance(type) as ApiShared.Request;
                            if (instance != null)
                            {
                                string endpoint = instance.Endpoint;
                                if (!string.IsNullOrEmpty(endpoint))
                                {
                                    if (!endpoint.StartsWith("/")) endpoint = "/" + endpoint;
                                    EndpointTypeMap[endpoint] = type;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Failed to build endpoint map: {e}");
            }
        }

        private static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            try
            {
                object parsedJson = JsonConvert.DeserializeObject(json);
                return JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }

        [HarmonyPatch(typeof(UnityWebRequest), "SendWebRequest")]
        public static class WebRequestPatch
        {
            public static void Prefix(UnityWebRequest __instance)
            {
                try
                {
                    string url = __instance.url;
                    if (string.IsNullOrEmpty(url) || !url.StartsWith("http")) return;

                    // PERFORMANCE GATE: Only log/format if the debugger is open or it's a critical create request
                    bool isCreate = url.Contains("/party/create") || url.Contains("/games/new") || url.Contains("/games/custom/new");
                    if (!_showGui && !isCreate) return;

                    string body = "";
                    if (__instance.uploadHandler != null && __instance.uploadHandler.data != null)
                    {
                        body = Encoding.UTF8.GetString(__instance.uploadHandler.data);

                        // Inject Lobby Size into Create requests
                        if (url.Contains("/party/create") || url.Contains("/games/new") || url.Contains("/games/custom/new"))
                        {
                            try
                            {
                                var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
                                if (dict != null && int.TryParse(_lobbySizeInput, out int lobbySize))
                                {
                                    dict["maxPlayers"] = lobbySize;
                                    string newBody = JsonConvert.SerializeObject(dict);
                                    byte[] newBodyData = Encoding.UTF8.GetBytes(newBody);

                                    // Replace the upload handler with the modified body
                                    __instance.uploadHandler = new UploadHandlerRaw(newBodyData);
                                    __instance.SetRequestHeader("Content-Type", "application/json");
                                    body = newBody; // Update local body for the inspector
                                }
                            }
                            catch { }
                        }
                    }

                    // Storage is intentionally disabled; still allow request-body mutation above.
                    if (!_storeApiRequests) return;

                    string path = "Unknown";
                    string typeName = "Unknown/Custom";
                    string typeResponseName = "";
                    try
                    {
                        var uri = new Uri(url);
                        path = uri.AbsolutePath;
                        if (EndpointTypeMap.TryGetValue(path, out Type t))
                        {
                            typeName = t.Name;
                            if (typeName.EndsWith("Request"))
                            {
                                string baseName = typeName.Substring(0, typeName.Length - 7);
                                typeResponseName = baseName + "Response";
                            }
                        }
                    }
                    catch { }

                    var req = new NetworkRequest
                    {
                        Url = url,
                        Method = __instance.method,
                        RequestBody = FormatJson(body),
                        Status = "Pending",
                        ResolvedType = typeName,
                        ResolvedResponseType = typeResponseName,
                        Timestamp = Time.time
                    };

                    Requests.Add(req);
                    if (Requests.Count > MaxRequests) Requests.RemoveAt(0);
                }
                catch (Exception e) { MelonLogger.Error($"Prefix Error: {e}"); }
            }

            public static void Postfix(UnityWebRequest __instance, UnityWebRequestAsyncOperation __result)
            {
                if (__result == null) return;
                if (!_storeApiRequests) return;

                var req = Requests.LastOrDefault(r => r.Url == __instance.url && r.Status == "Pending");

                __result.completed += (operation) =>
                {
                    try
                    {
                        if (req == null) return;

                        req.ResponseCode = (int)__instance.responseCode;
                        req.Status = (__instance.isNetworkError || __instance.isHttpError) ? "Error" : "Success";
                        req.Error = __instance.error;

                        if (__instance.downloadHandler != null)
                        {
                            req.ResponseBody = FormatJson(__instance.downloadHandler.text);
                        }
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Error($"Async Callback Error: {e}");
                    }
                };
            }
        }
    }
}
