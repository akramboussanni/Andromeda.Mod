using System;
using System.Reflection;
using System.Net.Sockets;
using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches
{
    /// <summary>
    /// Redirects the game's internal TCP log writer to the Andromeda log server
    /// (port 9090 on the same host as the API, instead of localhost).
    /// </summary>
    [HarmonyPatch]
    public static class LogWriterRedirectPatch
    {
        // 1. Patch the main reconnection method
        [HarmonyPatch(typeof(LogWriter), "TcpReconnect")]
        [HarmonyPrefix]
        public static bool PrefixReconnect(LogWriter __instance)
        {
            DoRedirect(__instance);
            return false;
        }

        // 2. Patch the write overload that does its own manual reconnection
        [HarmonyPatch(typeof(LogWriter), "TcpWrite", typeof(byte[]), typeof(int), typeof(int))]
        [HarmonyPrefix]
        public static void PrefixWrite(LogWriter __instance)
        {
            var tcpClientField = typeof(LogWriter).GetField("tcpClient", BindingFlags.NonPublic | BindingFlags.Instance);
            var client = (TcpClient)tcpClientField?.GetValue(__instance);
            
            // If the client is null or disconnected, force a redirect/connect to OUR server before the original method 
            // tries to 'new' it with the hardcoded "log.windwalk.games" address.
            if (client == null || !client.Connected)
            {
                DoRedirect(__instance);
            }
        }

        private static void DoRedirect(LogWriter __instance)
        {
            try
            {
                string host = NetworkDebugger.LogHost;
                int port = 9090;

                var tcpClientField = typeof(LogWriter).GetField("tcpClient", BindingFlags.NonPublic | BindingFlags.Instance);
                var tcpStreamField = typeof(LogWriter).GetField("tcpStream",  BindingFlags.NonPublic | BindingFlags.Instance);

                ((NetworkStream)tcpStreamField?.GetValue(__instance))?.Close();
                ((TcpClient)tcpClientField?.GetValue(__instance))?.Close();

                var newClient = new TcpClient(host, port);
                tcpClientField.SetValue(__instance, newClient);
                tcpStreamField.SetValue(__instance, newClient.GetStream());

                MelonLogger.Msg($"[LOG-PATCH] Switched Engine logs → {host}:{port}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LOG-PATCH] Redirect failed: {ex.Message}");
            }
        }
    }
}
