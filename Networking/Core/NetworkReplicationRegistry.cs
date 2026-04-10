using System;
using System.Collections.Generic;
using MelonLoader;

namespace Andromeda.Mod.Networking.Core
{
    public sealed class NetworkReplicationRegistry
    {
        private readonly Dictionary<string, INetworkReplicatedSetting> _settingsByKey = new Dictionary<string, INetworkReplicatedSetting>();

        public void Register(INetworkReplicatedSetting setting)
        {
            if (setting == null || string.IsNullOrWhiteSpace(setting.Key))
                return;

            _settingsByKey[setting.Key] = setting;
        }

        public Dictionary<string, string> ExportSnapshot()
        {
            var snapshot = new Dictionary<string, string>();
            foreach (var kv in _settingsByKey)
            {
                try
                {
                    snapshot[kv.Key] = kv.Value.ExportValue() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[NET-FRAMEWORK] Failed to export setting '" + kv.Key + "': " + ex.Message);
                }
            }

            return snapshot;
        }

        public void ImportSnapshot(IReadOnlyDictionary<string, string> snapshot)
        {
            if (snapshot == null)
                return;

            foreach (var kv in snapshot)
            {
                if (!_settingsByKey.TryGetValue(kv.Key, out INetworkReplicatedSetting setting))
                    continue;

                try
                {
                    setting.ImportValue(kv.Value);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[NET-FRAMEWORK] Failed to import setting '" + kv.Key + "': " + ex.Message);
                }
            }
        }
    }
}
