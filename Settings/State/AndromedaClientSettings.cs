using System;
using System.Collections.Generic;

namespace Andromeda.Mod.Settings
{
    public static class AndromedaClientSettings
    {
        private static bool _initialized;
        private static readonly Dictionary<string, ISettingDefinition> _definitions = new Dictionary<string, ISettingDefinition>();
        private static readonly Dictionary<string, SettingUiDescriptor> _uiDescriptors = new Dictionary<string, SettingUiDescriptor>();

        public static readonly SettingDefinition<float> FirstPersonYawSensitivity = new SettingDefinition<float>(
            key: "Andromeda_FirstPersonYawSensitivity",
            defaultValue: 0.35f,
            parse: s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            format: v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
            validator: v => v >= 0.05f && v <= 3f
        );

        public static readonly SettingDefinition<bool> UpnpEnabled = new SettingDefinition<bool>(
            key: "Andromeda_UpnpEnabled",
            defaultValue: false,
            parse: s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            format: v => v ? "1" : "0"
        );

        public static readonly SettingDefinition<bool> NetworkDebuggerRequestCaptureEnabled = new SettingDefinition<bool>(
            key: "Andromeda_NetworkDebuggerRequestCaptureEnabled",
            defaultValue: false,
            parse: s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            format: v => v ? "1" : "0"
        );

        public static readonly SettingDefinition<bool> VerboseDebugLoggingEnabled = new SettingDefinition<bool>(
            key: "Andromeda_VerboseDebugLoggingEnabled",
            defaultValue: false,
            parse: s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            format: v => v ? "1" : "0"
        );

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            Register(FirstPersonYawSensitivity);
            Register(UpnpEnabled);
            Register(NetworkDebuggerRequestCaptureEnabled);
            Register(VerboseDebugLoggingEnabled);

            RegisterUi(FirstPersonYawSensitivity, new SettingUiDescriptor
            {
                Label = "First Person Yaw Sensitivity",
                Section = "Andromeda",
                Order = 20,
                ShowInLobby = false,
                ReadOnlyInLobby = false,
                ControlKind = SettingControlKind.Slider,
                Min = 0.05f,
                Max = 3f,
                Step = 0.01f
            });

            RegisterUi(UpnpEnabled, new SettingUiDescriptor
            {
                Label = "UPnP Port Forwarding",
                Section = "Andromeda",
                Order = 10,
                ShowInLobby = false,
                ReadOnlyInLobby = false,
                ControlKind = SettingControlKind.Toggle
            });

            RegisterUi(NetworkDebuggerRequestCaptureEnabled, new SettingUiDescriptor
            {
                Label = "Network Debugger Request Capture",
                Section = "Andromeda",
                Order = 30,
                ShowInLobby = false,
                ReadOnlyInLobby = false,
                ControlKind = SettingControlKind.Toggle
            });

            RegisterUi(VerboseDebugLoggingEnabled, new SettingUiDescriptor
            {
                Label = "Verbose Debug Logging",
                Section = "Andromeda",
                Order = 40,
                ShowInLobby = false,
                ReadOnlyInLobby = false,
                ControlKind = SettingControlKind.Toggle
            });

            foreach (var definition in _definitions.Values)
                definition.Load();

            FirstPersonYawSensitivity.Value = ClampSensitivity(FirstPersonYawSensitivity.Value);
        }

        public static void SaveAll()
        {
            EnsureInitialized();
            foreach (var definition in _definitions.Values)
                definition.Save();
            UnityEngine.PlayerPrefs.Save();
        }

        public static void SetFirstPersonYawSensitivity(float value, bool persist = true)
        {
            EnsureInitialized();
            FirstPersonYawSensitivity.Value = ClampSensitivity(value);
            if (persist)
                Save(FirstPersonYawSensitivity);
        }

        public static void SetUpnpEnabled(bool enabled, bool persist = true)
        {
            EnsureInitialized();
            UpnpEnabled.Value = enabled;
            if (persist)
                Save(UpnpEnabled);
        }

        public static void SetNetworkDebuggerRequestCaptureEnabled(bool enabled, bool persist = true)
        {
            EnsureInitialized();
            NetworkDebuggerRequestCaptureEnabled.Value = enabled;
            if (persist)
                Save(NetworkDebuggerRequestCaptureEnabled);
        }

        public static void SetVerboseDebugLoggingEnabled(bool enabled, bool persist = true)
        {
            EnsureInitialized();
            VerboseDebugLoggingEnabled.Value = enabled;
            if (persist)
                Save(VerboseDebugLoggingEnabled);
        }

        public static IReadOnlyDictionary<string, ISettingDefinition> Definitions
        {
            get
            {
                EnsureInitialized();
                return _definitions;
            }
        }

        public static bool TryGetUiDescriptor(ISettingDefinition definition, out SettingUiDescriptor descriptor)
        {
            EnsureInitialized();
            return _uiDescriptors.TryGetValue(definition.Key, out descriptor);
        }

        public static bool TrySetValue(string key, object value, bool persist = true)
        {
            EnsureInitialized();

            if (!_definitions.TryGetValue(key, out ISettingDefinition definition))
                return false;

            if (definition == FirstPersonYawSensitivity)
            {
                float parsed;
                try { parsed = Convert.ToSingle(value); }
                catch { return false; }

                SetFirstPersonYawSensitivity(parsed, persist);
                return true;
            }

            if (definition == UpnpEnabled)
            {
                bool parsed;
                try { parsed = Convert.ToBoolean(value); }
                catch { return false; }

                SetUpnpEnabled(parsed, persist);
                return true;
            }

            if (definition == NetworkDebuggerRequestCaptureEnabled)
            {
                bool parsed;
                try { parsed = Convert.ToBoolean(value); }
                catch { return false; }

                SetNetworkDebuggerRequestCaptureEnabled(parsed, persist);
                return true;
            }

            if (definition == VerboseDebugLoggingEnabled)
            {
                bool parsed;
                try { parsed = Convert.ToBoolean(value); }
                catch { return false; }

                SetVerboseDebugLoggingEnabled(parsed, persist);
                return true;
            }

            try
            {
                definition.BoxedValue = value;
                if (persist)
                {
                    definition.Save();
                    UnityEngine.PlayerPrefs.Save();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetValue(string key, out object value)
        {
            EnsureInitialized();

            if (_definitions.TryGetValue(key, out ISettingDefinition definition))
            {
                value = definition.BoxedValue;
                return true;
            }

            value = null;
            return false;
        }

        public static SettingControlKind GetAutoControlKind(ISettingDefinition definition)
        {
            EnsureInitialized();

            if (_uiDescriptors.TryGetValue(definition.Key, out SettingUiDescriptor ui) && ui.ControlKind.HasValue)
                return ui.ControlKind.Value;

            Type type = definition.ValueType;
            if (type == typeof(bool))
                return SettingControlKind.Toggle;
            if (type == typeof(int) || type == typeof(float))
                return SettingControlKind.Slider;
            return SettingControlKind.Input;
        }

        private static void Save<T>(SettingDefinition<T> definition)
        {
            definition.Save();
            UnityEngine.PlayerPrefs.Save();
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

        private static float ClampSensitivity(float value)
        {
            if (value > 10f)
                value /= 400f;
            return UnityEngine.Mathf.Clamp(value, 0.05f, 3f);
        }
    }
}
