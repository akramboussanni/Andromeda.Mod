using System;

namespace Andromeda.Mod.Networking.Core
{
    public sealed class DelegateNetworkReplicatedSetting : INetworkReplicatedSetting
    {
        private readonly Func<string> _export;
        private readonly Action<string> _import;

        public DelegateNetworkReplicatedSetting(string key, Func<string> export, Action<string> import)
        {
            Key = key;
            _export = export;
            _import = import;
        }

        public string Key { get; }

        public string ExportValue()
        {
            return _export?.Invoke() ?? string.Empty;
        }

        public void ImportValue(string rawValue)
        {
            _import?.Invoke(rawValue ?? string.Empty);
        }
    }
}
