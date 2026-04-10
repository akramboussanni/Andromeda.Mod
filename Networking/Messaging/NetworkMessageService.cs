using System;
using System.Collections.Generic;
using UnityEngine;
using Windwalk.Net;

namespace Andromeda.Mod.Networking.Messaging
{
    public static class NetworkMessageService
    {
        private static readonly List<Action<NetClient>> ClientBinders = new List<Action<NetClient>>();
        private static readonly List<Action<NetServer>> ServerBinders = new List<Action<NetServer>>();

        private static int _registeredClientInstanceId;
        private static int _registeredServerInstanceId;

        public static void Update()
        {
            var netClient = Singleton.Existing<NetClient>();
            if ((UnityEngine.Object)netClient != (UnityEngine.Object)null)
            {
                int id = netClient.GetInstanceID();
                if (id != _registeredClientInstanceId)
                {
                    _registeredClientInstanceId = id;
                    for (int i = 0; i < ClientBinders.Count; i++)
                        ClientBinders[i](netClient);
                }
            }
            else
            {
                _registeredClientInstanceId = 0;
            }

            var netServer = Singleton.Existing<NetServer>();
            if ((UnityEngine.Object)netServer != (UnityEngine.Object)null)
            {
                int id = netServer.GetInstanceID();
                if (id != _registeredServerInstanceId)
                {
                    _registeredServerInstanceId = id;
                    for (int i = 0; i < ServerBinders.Count; i++)
                        ServerBinders[i](netServer);
                }
            }
            else
            {
                _registeredServerInstanceId = 0;
            }
        }

        public static void RegisterClientHandler<T>(Action<T> handler) where T : NetMessage, new()
        {
            if (handler == null)
                return;

            Action<NetClient> binder = netClient => netClient.RegisterHandler<T>(handler);
            ClientBinders.Add(binder);

            var active = Singleton.Existing<NetClient>();
            if ((UnityEngine.Object)active != (UnityEngine.Object)null)
                binder(active);
        }

        public static void RegisterServerHandler<T>(Action<T, PlayerId> handler) where T : NetMessage, new()
        {
            if (handler == null)
                return;

            Action<NetServer> binder = netServer => netServer.RegisterHandler<T>(handler);
            ServerBinders.Add(binder);

            var active = Singleton.Existing<NetServer>();
            if ((UnityEngine.Object)active != (UnityEngine.Object)null)
                binder(active);
        }

        public static bool SendToServer(NetMessage message)
        {
            var netClient = Singleton.Existing<NetClient>();
            if ((UnityEngine.Object)netClient == (UnityEngine.Object)null)
                return false;

            return netClient.SendReliable(message);
        }

        public static bool Broadcast(NetMessage message)
        {
            var netServer = Singleton.Existing<NetServer>();
            if ((UnityEngine.Object)netServer == (UnityEngine.Object)null)
                return false;

            return netServer.SendAllReliable(message);
        }

        public static bool SendToPlayer(PlayerId playerId, NetMessage message)
        {
            var netServer = Singleton.Existing<NetServer>();
            if ((UnityEngine.Object)netServer == (UnityEngine.Object)null)
                return false;

            return netServer.SendReliable(playerId, message);
        }
    }
}
