using System;
using System.Net;
using System.Net.Sockets;
using MelonLoader;

namespace Andromeda.Mod.Features
{
    public static class UPnPFeature
    {
        public static void OpenPort(int port, string description)
        {
            try
            {
                MelonLogger.Msg($"[UPnP] Attempting to open port {port} ({description})...");
                
                // We use dynamic (late-bound COM) to avoid needing to add a reference to NATUPNPLib.dll
                Type upnpNatType = Type.GetTypeFromProgID("NATUPnP.UPnPNAT");
                if (upnpNatType == null)
                {
                    MelonLogger.Warning("[UPnP] NATUPnP.UPnPNAT not found on this system.");
                    return;
                }

                object upnpNat = Activator.CreateInstance(upnpNatType);
                var mappings = upnpNat.GetType().GetProperty("StaticPortMappingCollection").GetValue(upnpNat, null);

                if (mappings == null)
                {
                    MelonLogger.Warning("[UPnP] Could not retrieve the Static Port Mapping Collection. Is UPnP enabled on your router?");
                    return;
                }

                // Add mapping (Port, Protocol, InternalPort, InternalClient, Enabled, Description)
                // Protocol: "TCP" or "UDP"
                string localIp = GetLocalIPAddress();
                
                // The Add method signature is: Add(int externalPort, string protocol, int internalPort, string internalClient, bool enabled, string description)
                var addMethod = mappings.GetType().GetMethod("Add");
                if (addMethod != null)
                {
                    addMethod.Invoke(mappings, new object[] { port, "TCP", port, localIp, true, description });
                    MelonLogger.Msg($"[UPnP] SUCCESS: Port {port} (TCP) is now forwarded to {localIp}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[UPnP] Failed to open port {port}: {ex.Message}");
            }
        }

        private static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }
    }
}
