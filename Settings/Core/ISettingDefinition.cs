using System;

namespace Andromeda.Mod.Settings
{
    public interface ISettingDefinition
    {
        string Key { get; }
        Type ValueType { get; }
        object BoxedDefaultValue { get; }
        object BoxedValue { get; set; }
        void Load();
        void Save();
    }
}
