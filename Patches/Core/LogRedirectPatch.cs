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
        [HarmonyPatch(typeof(LogWriter), "TcpReconnect")]
        [HarmonyPrefix]
        public static bool Prefix(LogWriter __instance)
        {
            try
            {
                string host = NetworkDebugger.LogHost;

                var tcpClientField = typeof(LogWriter).GetField("tcpClient", BindingFlags.NonPublic | BindingFlags.Instance);
                var tcpStreamField = typeof(LogWriter).GetField("tcpStream",  BindingFlags.NonPublic | BindingFlags.Instance);

                ((NetworkStream)tcpStreamField?.GetValue(__instance))?.Close();
                ((TcpClient)tcpClientField?.GetValue(__instance))?.Close();

                var newClient = new TcpClient(host, 9090);
                tcpClientField.SetValue(__instance, newClient);
                tcpStreamField.SetValue(__instance, newClient.GetStream());

                MelonLogger.Msg($"[LOG-PATCH] Engine logs → {host}:9090");
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LOG-PATCH] Redirect failed: {ex.Message}");
                return true;
            }
        }
    }
}
