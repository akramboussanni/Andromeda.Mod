namespace Andromeda.Mod.Settings
{
    public sealed class SettingUiDescriptor
    {
        public string Label;
        public string Section;
        public int Order;
        public bool ShowInLobby;
        public bool ReadOnlyInLobby;
        public SettingControlKind? ControlKind;
        public float? Min;
        public float? Max;
        public float? Step;
        public string[] Choices;
    }
}
