using System;
using System.Collections.Generic;
using UnityEngine;

namespace Andromeda.Mod.Settings
{
    public sealed class SettingDefinition<T> : ISettingDefinition
    {
        private readonly Func<string, T> _parse;
        private readonly Func<T, string> _format;
        private readonly Predicate<T> _validator;

        private T _value;

        public string Key { get; private set; }
        public T DefaultValue { get; private set; }

        public event Action<T> OnChanged;

        public SettingDefinition(
            string key,
            T defaultValue,
            Func<string, T> parse,
            Func<T, string> format,
            Predicate<T> validator = null)
        {
            Key = key;
            DefaultValue = defaultValue;
            _parse = parse;
            _format = format;
            _validator = validator;
            _value = defaultValue;
        }

        public T Value
        {
            get { return _value; }
            set
            {
                T safeValue = IsValid(value) ? value : DefaultValue;
                if (EqualityComparer<T>.Default.Equals(_value, safeValue))
                    return;

                _value = safeValue;
                Action<T> onChanged = OnChanged;
                if (onChanged != null)
                    onChanged(_value);
            }
        }

        public object BoxedDefaultValue
        {
            get { return DefaultValue; }
        }

        public Type ValueType
        {
            get { return typeof(T); }
        }

        public object BoxedValue
        {
            get { return Value; }
            set
            {
                if (value is T typed)
                    Value = typed;
                else
                    Value = DefaultValue;
            }
        }

        public bool IsValid(T value)
        {
            return _validator == null || _validator(value);
        }

        public void Load()
        {
            if (!PlayerPrefs.HasKey(Key))
            {
                Value = DefaultValue;
                return;
            }

            try
            {
                object parsedValue;

                if (typeof(T) == typeof(int))
                    parsedValue = PlayerPrefs.GetInt(Key, Convert.ToInt32(DefaultValue));
                else if (typeof(T) == typeof(bool))
                    parsedValue = PlayerPrefs.GetInt(Key, Convert.ToBoolean(DefaultValue) ? 1 : 0) == 1;
                else if (typeof(T) == typeof(float))
                    parsedValue = PlayerPrefs.GetFloat(Key, Convert.ToSingle(DefaultValue));
                else if (typeof(T) == typeof(string))
                    parsedValue = PlayerPrefs.GetString(Key, Convert.ToString(DefaultValue));
                else
                    parsedValue = _parse(PlayerPrefs.GetString(Key, _format(DefaultValue)));

                T typed = parsedValue is T value ? value : DefaultValue;
                Value = IsValid(typed) ? typed : DefaultValue;
            }
            catch
            {
                Value = DefaultValue;
            }
        }

        public void Save()
        {
            if (typeof(T) == typeof(int))
                PlayerPrefs.SetInt(Key, Convert.ToInt32(Value));
            else if (typeof(T) == typeof(bool))
                PlayerPrefs.SetInt(Key, Convert.ToBoolean(Value) ? 1 : 0);
            else if (typeof(T) == typeof(float))
                PlayerPrefs.SetFloat(Key, Convert.ToSingle(Value));
            else
                PlayerPrefs.SetString(Key, _format(Value));
        }
    }
}
