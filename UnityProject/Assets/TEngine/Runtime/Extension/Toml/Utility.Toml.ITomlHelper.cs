using System;

namespace TEngine
{
    public static partial class Utility
    {
        public static partial class Toml
        {
            public interface ITomlHelper
            {
                string ToToml(object obj, object settings = null);

                T ToObject<T>(string toml, object settings = null);

                object ToObject(Type objectType, string toml, object settings = null);
            }
        }
    }
}
