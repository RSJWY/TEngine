using System;
using System.Globalization;
using Newtonsoft.Json;

namespace TEngine
{
    public sealed class NewtonsoftJsonHelper : Utility.Json.IJsonHelper
    {
        private static readonly JsonSerializerSettings DefaultSettings = new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        public string ToJson(object obj, object settings = null)
        {
            return JsonConvert.SerializeObject(obj, settings as JsonSerializerSettings ?? DefaultSettings);
        }

        public T ToObject<T>(string json, object settings = null)
        {
            return JsonConvert.DeserializeObject<T>(json, settings as JsonSerializerSettings ?? DefaultSettings);
        }

        public object ToObject(Type objectType, string json, object settings = null)
        {
            return JsonConvert.DeserializeObject(json, objectType, settings as JsonSerializerSettings ?? DefaultSettings);
        }
    }
}
