using System;
using Tomlyn;

namespace TEngine
{
    public sealed class TomlynTomlHelper : Utility.Toml.ITomlHelper
    {
        private static readonly TomlSerializerOptions DefaultOptions = new TomlSerializerOptions();

        public string ToToml(object obj, object settings = null)
        {
            return TomlSerializer.Serialize(obj, obj.GetType(), GetOptions(settings));
        }

        public T ToObject<T>(string toml, object settings = null)
        {
            return TomlSerializer.Deserialize<T>(toml, GetOptions(settings));
        }

        public object ToObject(Type objectType, string toml, object settings = null)
        {
            return TomlSerializer.Deserialize(toml, objectType, GetOptions(settings));
        }

        private static TomlSerializerOptions GetOptions(object settings)
        {
            if (settings == null)
            {
                return DefaultOptions;
            }

            if (settings is TomlSerializerOptions options)
            {
                return options;
            }

            throw new GameFrameworkException($"TOML settings type is invalid: {settings.GetType().FullName}");
        }
    }
}
