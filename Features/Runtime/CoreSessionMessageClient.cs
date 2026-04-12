using System;
using System.Collections;
using System.Text;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Andromeda.Mod.Features
{
    public static class CoreSessionMessageClient
    {
        public static bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(DedicatedServerStartup.SessionId)
                && !string.IsNullOrWhiteSpace(DedicatedServerStartup.ChannelCode)
                && !string.IsNullOrWhiteSpace(DedicatedServerStartup.ChannelKey)
                && !string.IsNullOrWhiteSpace(RestApi.API_URL);
        }

        public static IEnumerator PublishSpawnConfigCoro(bool onePlayerMode, int? maxPlayers, int ttlSeconds, string source, bool? cheatsEnabled = null)
        {
            if (!IsConfigured())
                yield break;

            string url = RestApi.API_URL + "/server/session/"
                + UnityWebRequest.EscapeURL(DedicatedServerStartup.SessionId)
                + "/spawn-config";

            var body = new
            {
                onePlayerMode = onePlayerMode,
                maxPlayers = maxPlayers,
                ttlSeconds = ttlSeconds,
                source = source ?? "unknown",
                cheatsEnabled = cheatsEnabled ?? Andromeda.Mod.Settings.AndromedaSettings.CheatsEnabled.Value,
            };

            string json = JsonConvert.SerializeObject(body);
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-Channel-Code", DedicatedServerStartup.ChannelCode);
                req.SetRequestHeader("X-Channel-Key", DedicatedServerStartup.ChannelKey);
                req.SetRequestHeader("X-Process", "lobby");
                req.timeout = 5;

                yield return req.SendWebRequest();

                if (req.isNetworkError || req.isHttpError)
                    MelonLogger.Warning($"[CORE-MSG] Failed to publish spawn config: {req.error} ({req.responseCode})");
            }
        }
    }
}
