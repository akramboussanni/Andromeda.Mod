using System;
using System.Reflection;
using System.Net.Sockets;
using HarmonyLib;
using MelonLoader;

namespace Andromeda.Mod.Patches
{
    public static class LogWriterRedirectPatch
    {
        [HarmonyPatch(typeof(LogWriter), "TcpReconnect")]
        [HarmonyPrefix]
        public static bool Prefix(LogWriter __instance)
        {
            try
            {
                var tcpClientField = typeof(LogWriter).GetField("tcpClient", BindingFlags.NonPublic | BindingFlags.Instance);
                var tcpStreamField = typeof(LogWriter).GetField("tcpStream", BindingFlags.NonPublic | BindingFlags.Instance);

                var oldClient = (TcpClient)tcpClientField.GetValue(__instance);
                var oldStream = (NetworkStream)tcpStreamField.GetValue(__instance);
                oldStream?.Close();
                oldClient?.Close();

                var newClient = new TcpClient("127.0.0.1", 9090);
                tcpClientField.SetValue(__instance, newClient);
                tcpStreamField.SetValue(__instance, newClient.GetStream());
                
                MelonLogger.Msg("[LOG-PATCH] Redirecting internal engine logs to 127.0.0.1:9090");
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[LOG-PATCH] Failed to redirect logs: {ex.Message}");
                return true; 
            }
        }
    }
}
