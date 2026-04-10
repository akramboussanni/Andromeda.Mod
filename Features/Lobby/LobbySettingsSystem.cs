using System;
using System.Collections.Generic;
using MelonLoader;
using Andromeda.Mod.Settings;
using LobbySettings = Andromeda.Mod.Settings.AndromedaSettings;

namespace Andromeda.Mod.Features
{
    public static class LobbySettingsSystem
    {
        public sealed class LobbySettingBlueprint
        {
            public ISettingDefinition Definition;
            public SettingUiDescriptor Ui;
            public SettingControlKind ControlKind;
        }

        public static int[] AllowedLobbySizes => LobbySettings.AllowedLobbySizes;
        public static int LobbySize => LobbySettings.LobbySize.Value;
        public static bool IsFirstPersonEnabled => LobbySettings.FirstPersonEnabled.Value;

        public static void Initialize()
        {
            LobbySettings.Initialize();
        }

        public static bool SetLobbySize(int maxPlayers, bool persist = true)
        {
            return LobbySettings.SetLobbySize(maxPlayers, persist);
        }

        public static void SetFirstPersonEnabled(bool enabled, bool persist = true)
        {
            LobbySettings.SetFirstPersonEnabled(enabled, persist);
        }

        public static string[] GetLobbySizeOptions()
        {
            return LobbySettings.GetLobbySizeOptions();
        }

        public static IReadOnlyList<LobbySettingBlueprint> BuildLobbyUiBlueprints()
        {
            Initialize();

            var list = new List<LobbySettingBlueprint>();
            var settings = LobbySettings.GetLobbySettings();
            for (int i = 0; i < settings.Count; i++)
            {
                var setting = settings[i];
                if (!LobbySettings.TryGetUiDescriptor(setting, out SettingUiDescriptor ui))
                    continue;

                list.Add(new LobbySettingBlueprint
                {
                    Definition = setting,
                    Ui = ui,
                    ControlKind = LobbySettings.GetAutoControlKind(setting)
                });
            }

            return list;
        }

        public static void TryPublishCurrentSpawnConfig(string source)
        {
            Initialize();

            if (!CoreSessionMessageClient.IsConfigured())
                return;

            try
            {
                MelonCoroutines.Start(CoreSessionMessageClient.PublishSpawnConfigCoro(
                    onePlayerMode: false,
                    maxPlayers: LobbySettings.LobbySize.Value,
                    ttlSeconds: 900,
                    source: source ?? "lobby-settings"
                ));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[LOBBY-SETTINGS] Failed to publish spawn config: " + ex.Message);
            }
        }
    }
}
