using System;
using Andromeda.Mod.Networking.Core;
using Andromeda.Mod.Networking.Sync;
using UnityEngine;
using Windwalk.Net;
using LobbySettings = Andromeda.Mod.Settings.AndromedaSettings;

namespace Andromeda.Mod.Features
{
    public static class LobbySettingsReplicationFeature
    {
        public const string FirstPersonKey = "first_person_enabled";
        public const string CheatsKey = "cheats_enabled";

        private static NetworkReplicationRegistry _registry;
        private static NetworkSettingsSyncController _controller;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            _registry = new NetworkReplicationRegistry();

            // Example setting registration. Add more calls here to expand replication.
            RegisterSetting(NetworkReplicatedSetting.Bool(
                FirstPersonKey,
                () => LobbySettings.FirstPersonEnabled.Value,
                value => LobbySettings.SetFirstPersonEnabled(value, persist: false)
            ));

            RegisterSetting(NetworkReplicatedSetting.Bool(
                CheatsKey,
                () => LobbySettings.CheatsEnabled.Value,
                value => LobbySettings.SetCheatsEnabled(value, persist: false)
            ));

            _controller = new NetworkSettingsSyncController(
                registry: _registry,
                netClientProvider: () => Singleton.Existing<NetClient>(),
                netServerProvider: () => Singleton.Existing<NetServer>(),
                isSessionActive: () => Singleton.Tracked<CustomPartyClient>() != null,
                canSendLocalChanges: IsLocalPartyLeader,
                isSenderAuthorized: sender =>
                {
                    var customPartyServer = UnityEngine.Object.FindObjectOfType<CustomPartyServer>();
                    if ((UnityEngine.Object)customPartyServer == (UnityEngine.Object)null)
                        return false;
                    return customPartyServer.leader.HasValue && customPartyServer.leader.Value == sender;
                },
                onRemoteApplied: LobbyOptionsAutoUiFeature.RefreshInjectedRows
            );
        }

        public static void Update()
        {
            Initialize();
            _controller.Update();
        }

        public static void RegisterSetting(INetworkReplicatedSetting setting)
        {
            Initialize();
            _registry.Register(setting);
        }

        public static void OnCustomPartyOptionsChanged()
        {
            PublishFromLocalHost("party-options-change");
        }

        public static void PublishFromLocalHost(string source)
        {
            Initialize();
            _controller.PublishLocal(source ?? "host-change");
        }

        public static bool IsLocalPartyLeader()
        {
            var customParty = Singleton.Existing<CustomPartyClient>();
            if ((UnityEngine.Object)customParty == (UnityEngine.Object)null)
                return false;

            var netClient = Singleton.Existing<NetClient>();
            if ((UnityEngine.Object)netClient == (UnityEngine.Object)null)
                return false;

            var data = customParty.GetData();
            if (!data.Item1.HasValue)
                return false;

            return data.Item1.Value == netClient.LocalPlayer;
        }

        public static bool TryGetBoolean(string key, out bool value)
        {
            value = false;

            Initialize();
            if (!_controller.TryGetValue(key, out string raw))
                return false;

            if (raw == "1")
            {
                value = true;
                return true;
            }

            if (raw == "0")
            {
                value = false;
                return true;
            }

            return bool.TryParse(raw, out value);
        }

        private static PlayerId? _cachedLeaderId;
        private static string _cachedLeaderSteamId;

        public static PlayerId? CachedLeaderId => _cachedLeaderId;
        public static string CachedLeaderSteamId => _cachedLeaderSteamId;

        public static void SetCachedLeader(PlayerId? leader)
        {
            _cachedLeaderId = leader;
            _cachedLeaderSteamId = null;

            if (!leader.HasValue)
                return;

            var programServer = Singleton.Existing<ProgramServer>();
            if ((UnityEngine.Object)programServer != (UnityEngine.Object)null)
            {
                var player = programServer.ByPlayerId(leader.Value);
                _cachedLeaderSteamId = player.HasValue ? player.Value.profile?.steamId : null;
            }
        }

    }
}
