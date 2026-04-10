using System;
using System.Collections.Generic;
using UnityEngine;
using Windwalk.Net;
using Andromeda.Mod.Networking.Core;
using Andromeda.Mod.Networking.Messaging;

namespace Andromeda.Mod.Networking.Sync
{
    public sealed class NetworkSettingsSyncController
    {
        private readonly NetworkReplicationRegistry _registry;
        private readonly Func<NetClient> _netClientProvider;
        private readonly Func<NetServer> _netServerProvider;
        private readonly Func<bool> _isSessionActive;
        private readonly Func<bool> _canSendLocalChanges;
        private readonly Func<PlayerId, bool> _isSenderAuthorized;
        private readonly Action _onRemoteApplied;

        private readonly Dictionary<string, string> _clientState = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _serverState = new Dictionary<string, string>();

        private int _serverRevision;

        public NetworkSettingsSyncController(
            NetworkReplicationRegistry registry,
            Func<NetClient> netClientProvider,
            Func<NetServer> netServerProvider,
            Func<bool> isSessionActive,
            Func<bool> canSendLocalChanges,
            Func<PlayerId, bool> isSenderAuthorized,
            Action onRemoteApplied)
        {
            _registry = registry;
            _netClientProvider = netClientProvider;
            _netServerProvider = netServerProvider;
            _isSessionActive = isSessionActive;
            _canSendLocalChanges = canSendLocalChanges;
            _isSenderAuthorized = isSenderAuthorized;
            _onRemoteApplied = onRemoteApplied;

            NetworkMessageService.RegisterClientHandler<NetworkSettingsSyncMessage>(OnClientMessage);
            NetworkMessageService.RegisterServerHandler<NetworkSettingsSyncMessage>(OnServerMessage);
        }

        public void Update()
        {
            if (_isSessionActive != null && !_isSessionActive())
                _clientState.Clear();
        }

        public void PublishLocal(string source)
        {
            if (_canSendLocalChanges != null && !_canSendLocalChanges())
                return;

            var netClient = _netClientProvider?.Invoke();
            if ((UnityEngine.Object)netClient == (UnityEngine.Object)null)
                return;

            var snapshot = _registry.ExportSnapshot();
            ReplaceState(_clientState, snapshot);
            NetworkMessageService.SendToServer(NetworkSettingsSyncMessage.FromSnapshot(snapshot, source, 0));
        }

        public bool TryGetValue(string key, out string value)
        {
            return _clientState.TryGetValue(key, out value);
        }

        private void OnClientMessage(NetworkSettingsSyncMessage msg)
        {
            if (msg == null)
                return;

            var snapshot = msg.ToSnapshot();
            ReplaceState(_clientState, snapshot);
            _registry.ImportSnapshot(snapshot);
            _onRemoteApplied?.Invoke();
        }

        private void OnServerMessage(NetworkSettingsSyncMessage msg, PlayerId sender)
        {
            if (msg == null)
                return;

            if (_isSenderAuthorized != null && !_isSenderAuthorized(sender))
                return;

            var snapshot = msg.ToSnapshot();
            ReplaceState(_serverState, snapshot);
            _registry.ImportSnapshot(snapshot);
            _serverRevision++;

            var netServer = _netServerProvider?.Invoke();
            if ((UnityEngine.Object)netServer == (UnityEngine.Object)null)
                return;

            NetworkMessageService.Broadcast(NetworkSettingsSyncMessage.FromSnapshot(_serverState, msg.source, _serverRevision));
        }

        private static void ReplaceState(Dictionary<string, string> target, IReadOnlyDictionary<string, string> source)
        {
            target.Clear();
            if (source == null)
                return;

            foreach (var kv in source)
                target[kv.Key] = kv.Value;
        }
    }
}
