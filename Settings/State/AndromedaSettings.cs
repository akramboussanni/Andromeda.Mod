using System;
using System.Collections.Generic;
using UnityEngine;

namespace Andromeda.Mod.Settings
{
    public static class AndromedaSettings
    {
        private static bool _initialized;
        private static readonly Dictionary<string, ISettingDefinition> _definitions = new Dictionary<string, ISettingDefinition>();
        private static readonly Dictionary<string, SettingUiDescriptor> _uiDescriptors = new Dictionary<string, SettingUiDescriptor>();

        public static readonly int[] AllowedLobbySizes = { 8, 10, 12, 14, 16, 20 };

        public static readonly SettingDefinition<int> LobbySize = new SettingDefinition<int>(
            key: "Andromeda_LobbySize",
            defaultValue: 12,
            parse: s => int.Parse(s),
            format: v => v.ToString(),
            validator: v => Array.IndexOf(AllowedLobbySizes, v) >= 0
        );

        public static readonly SettingDefinition<bool> FirstPersonEnabled = new SettingDefinition<bool>(
            key: "Andromeda_FirstPersonEnabled",
            defaultValue: false,
            parse: s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            format: v => v ? "1" : "0"
        );

        public static readonly SettingDefinition<bool> CheatsEnabled = new SettingDefinition<bool>(
            key: "Andromeda_CheatsEnabled",
            defaultValue: false,
            parse: s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            format: v => v ? "1" : "0"
        );

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            Register(LobbySize);
            Register(FirstPersonEnabled);
            Register(CheatsEnabled);

            RegisterUi(LobbySize, new SettingUiDescriptor
            {
                Label = "Lobby Size",
                ShowInLobby = true,
                ReadOnlyInLobby = true,
                ControlKind = SettingControlKind.Choice,
                Choices = GetLobbySizeOptions()
            });

            RegisterUi(FirstPersonEnabled, new SettingUiDescriptor
            {
                Label = "First Person Mode",
                ShowInLobby = true,
                ReadOnlyInLobby = false,
                ControlKind = SettingControlKind.Toggle
            });

            RegisterUi(CheatsEnabled, new SettingUiDescriptor
            {
                Label = "Cheats",
                ShowInLobby = true,
                ReadOnlyInLobby = false,
                ControlKind = SettingControlKind.Toggle
            });

            foreach (var definition in _definitions.Values)
                definition.Load();

            DedicatedServerStartup.MaxPlayers = LobbySize.Value;
        }

        public static void SaveAll()
        {
            EnsureInitialized();
            foreach (var definition in _definitions.Values)
                definition.Save();
            PlayerPrefs.Save();
        }

        public static void Save<T>(SettingDefinition<T> definition)
        {
            EnsureInitialized();
            definition.Save();
            PlayerPrefs.Save();
        }

        public static bool SetLobbySize(int lobbySize, bool persist = true)
        {
            EnsureInitialized();
            if (!LobbySize.IsValid(lobbySize))
                return false;

            LobbySize.Value = lobbySize;
            DedicatedServerStartup.MaxPlayers = lobbySize;

            if (persist)
                Save(LobbySize);

            return true;
        }

        public static void SetFirstPersonEnabled(bool enabled, bool persist = true)
        {
            EnsureInitialized();
            FirstPersonEnabled.Value = enabled;
            if (persist)
                Save(FirstPersonEnabled);
        }

        public static void SetCheatsEnabled(bool enabled, bool persist = true)
        {
            EnsureInitialized();
            CheatsEnabled.Value = enabled;
            if (persist)
                Save(CheatsEnabled);
        }

        public static string[] GetLobbySizeOptions()
        {
            EnsureInitialized();
            string[] options = new string[AllowedLobbySizes.Length];
            for (int i = 0; i < AllowedLobbySizes.Length; i++)
                options[i] = AllowedLobbySizes[i].ToString();
            return options;
        }

        public static IReadOnlyDictionary<string, ISettingDefinition> Definitions
        {
            get
            {
                EnsureInitialized();
                return _definitions;
            }
        }

        public static IReadOnlyList<ISettingDefinition> GetLobbySettings()
        {
            EnsureInitialized();
            var list = new List<ISettingDefinition>();
            foreach (var kv in _definitions)
            {
                if (_uiDescriptors.TryGetValue(kv.Key, out SettingUiDescriptor ui) && ui.ShowInLobby)
                    list.Add(kv.Value);
            }
            return list;
        }

        public static bool TryGetUiDescriptor(ISettingDefinition definition, out SettingUiDescriptor descriptor)
        {
            EnsureInitialized();
            return _uiDescriptors.TryGetValue(definition.Key, out descriptor);
        }

        public static SettingControlKind GetAutoControlKind(ISettingDefinition definition)
        {
            EnsureInitialized();

            if (_uiDescriptors.TryGetValue(definition.Key, out SettingUiDescriptor ui) && ui.ControlKind.HasValue)
                return ui.ControlKind.Value;

            Type type = definition.ValueType;
            if (type == typeof(bool))
                return SettingControlKind.Toggle;

            if (_uiDescriptors.TryGetValue(definition.Key, out ui)
                && ui.Choices != null
                && ui.Choices.Length > 0)
                return SettingControlKind.Choice;

            if (type == typeof(int) || type == typeof(float))
                return SettingControlKind.Slider;

            return SettingControlKind.Input;
        }

        private static void Register(ISettingDefinition definition)
        {
            _definitions[definition.Key] = definition;
        }

        private static void RegisterUi(ISettingDefinition definition, SettingUiDescriptor descriptor)
        {
            _uiDescriptors[definition.Key] = descriptor;
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
                Initialize();
        }

    }
}
