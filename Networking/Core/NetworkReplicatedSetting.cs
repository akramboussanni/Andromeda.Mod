using System;
using System.Globalization;

namespace Andromeda.Mod.Networking.Core
{
    public static class NetworkReplicatedSetting
    {
        public static INetworkReplicatedSetting Bool(string key, Func<bool> getter, Action<bool> setter)
        {
            return new DelegateNetworkReplicatedSetting(
                key,
                () => getter() ? "1" : "0",
                raw =>
                {
                    if (TryParseBool(raw, out bool parsed))
                        setter(parsed);
                }
            );
        }

        public static INetworkReplicatedSetting Int(string key, Func<int> getter, Action<int> setter)
        {
            return new DelegateNetworkReplicatedSetting(
                key,
                () => getter().ToString(CultureInfo.InvariantCulture),
                raw =>
                {
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        setter(parsed);
                }
            );
        }

        public static INetworkReplicatedSetting Float(string key, Func<float> getter, Action<float> setter)
        {
            return new DelegateNetworkReplicatedSetting(
                key,
                () => getter().ToString(CultureInfo.InvariantCulture),
                raw =>
                {
                    if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                        setter(parsed);
                }
            );
        }

        public static INetworkReplicatedSetting String(string key, Func<string> getter, Action<string> setter)
        {
            return new DelegateNetworkReplicatedSetting(
                key,
                () => getter() ?? string.Empty,
                raw => setter(raw ?? string.Empty)
            );
        }

        private static bool TryParseBool(string raw, out bool value)
        {
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
    }
}
