using System;
using System.Collections;
using System.Text;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace Andromeda.Mod
{
    public static class RestApi
    {
        // Local testing: http://127.0.0.1:8000
        // Production: https://andromeda.kimotherapy.dev
        public static string API_URL = "http://127.0.0.1:8000";
        public static string EVENTS_URL = API_URL;


        public static IEnumerator RegisterServerCoro(string sessionId, int port, string region)
        {
            MelonLogger.Msg("[REST] Registering server with PythonBackend...");
            var readyData = new 
            { 
                sessionId = sessionId,
                port = port,
                region = region
            };
            yield return PostRest("/server/ready", readyData);
        }

        public static IEnumerator RegisterHeartbeat(string sessionId)
        {
            yield return PostRest("/server/heartbeat", new { sessionId = sessionId });
        }

        public static void SendHeartbeat(string sessionId)
        {
            MelonCoroutines.Start(RegisterHeartbeat(sessionId));
        }

        public static IEnumerator SendShutdownCoro(string sessionId, string reason = null)
        {
            yield return PostRest("/server/shutdown", new { sessionId = sessionId, reason = reason });
        }

        public static void SendShutdown(string sessionId, string reason = null)
        {
            MelonCoroutines.Start(SendShutdownCoro(sessionId, reason));
        }

        public static IEnumerator PostRest(string endpoint, object body)
        {
            string json = JsonConvert.SerializeObject(body);
            var req = new UnityWebRequest(API_URL + endpoint, "POST");
            byte[] bodyData = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyData);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            string authToken = DedicatedServerStartup.IsServer ? "DEDICATED_SERVER_TOKEN" : Steam.AuthToken;
            if (!string.IsNullOrEmpty(authToken))
                req.SetRequestHeader("Authorization", authToken);

            yield return req.SendWebRequest();
            
            if (req.isNetworkError || req.responseCode != 200)
                MelonLogger.Error($"[REST-ERROR] {endpoint}: {req.error} ({req.responseCode})");
            else
                MelonLogger.Msg($"[REST-SUCCESS] {endpoint} OK");
        }
    }
}
