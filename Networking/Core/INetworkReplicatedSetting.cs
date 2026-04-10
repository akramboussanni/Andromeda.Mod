namespace Andromeda.Mod.Networking.Core
{
    public interface INetworkReplicatedSetting
    {
        string Key { get; }
        string ExportValue();
        void ImportValue(string rawValue);
    }
}
