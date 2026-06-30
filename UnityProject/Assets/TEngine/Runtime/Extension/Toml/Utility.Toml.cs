using System;

namespace TEngine
{
    public static partial class Utility
    {
        public static partial class Toml
        {
            private static ITomlHelper _tomlHelper = new TomlynTomlHelper();

            public static void SetTomlHelper(ITomlHelper tomlHelper)
            {
                _tomlHelper = tomlHelper;
            }

            public static string ToToml(object obj, object settings = null)
            {
                if (_tomlHelper == null)
                {
                    throw new GameFrameworkException("TOML helper is invalid.");
                }

                if (obj == null)
                {
                    throw new GameFrameworkException("Object is invalid.");
                }

                try
                {
                    return _tomlHelper.ToToml(obj, settings);
                }
                catch (Exception exception)
                {
                    if (exception is GameFrameworkException)
                    {
                        throw;
                    }

                    throw new GameFrameworkException(Text.Format("Can not convert to TOML with exception '{0}'.", exception), exception);
                }
            }

            public static T ToObject<T>(string toml, object settings = null)
            {
                if (_tomlHelper == null)
                {
                    throw new GameFrameworkException("TOML helper is invalid.");
                }

                try
                {
                    return _tomlHelper.ToObject<T>(toml, settings);
                }
                catch (Exception exception)
                {
                    if (exception is GameFrameworkException)
                    {
                        throw;
                    }

                    throw new GameFrameworkException(Text.Format("Can not convert to object with exception '{0}'.", exception), exception);
                }
            }

            public static object ToObject(Type objectType, string toml, object settings = null)
            {
                if (_tomlHelper == null)
                {
                    throw new GameFrameworkException("TOML helper is invalid.");
                }

                if (objectType == null)
                {
                    throw new GameFrameworkException("Object type is invalid.");
                }

                try
                {
                    return _tomlHelper.ToObject(objectType, toml, settings);
                }
                catch (Exception exception)
                {
                    if (exception is GameFrameworkException)
                    {
                        throw;
                    }

                    throw new GameFrameworkException(Text.Format("Can not convert to object with exception '{0}'.", exception), exception);
                }
            }
        }
    }
}
