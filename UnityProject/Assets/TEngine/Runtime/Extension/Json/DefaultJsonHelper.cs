using System;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 默认 JSON 函数集辅助器。
    /// </summary>
    public class DefaultJsonHelper : Utility.Json.IJsonHelper
    {
        /// <summary>
        /// 将对象序列化为 JSON 字符串。
        /// </summary>
        /// <param name="obj">要序列化的对象。</param>
        /// <param name="settings">序列化设置。</param>
        /// <returns>序列化后的 JSON 字符串。</returns>
        public string ToJson(object obj, object settings = null)
        {
            return JsonUtility.ToJson(obj);
        }

        /// <summary>
        /// 将 JSON 字符串反序列化为对象。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <param name="settings">序列化设置。</param>
        /// <returns>反序列化后的对象。</returns>
        public T ToObject<T>(string json, object settings = null)
        {
            return JsonUtility.FromJson<T>(json);
        }

        /// <summary>
        /// 将 JSON 字符串反序列化为对象。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <param name="settings">序列化设置。</param>
        /// <returns>反序列化后的对象。</returns>
        public object ToObject(Type objectType, string json, object settings = null)
        {
            return JsonUtility.FromJson(json, objectType);
        }

        /// <summary>
        /// 将 JSON 字符串反序列化并填充到已有对象（覆盖写入）。
        /// 注意：Unity JsonUtility 不支持 PopulateObject，此实现为创建新对象后手动复制字段。
        /// 如需精确覆盖语义请使用 NewtonsoftJsonHelper。
        /// </summary>
        public void FromJsonOverwrite(string json, object obj, object settings = null)
        {
            if (obj == null || string.IsNullOrEmpty(json))
            {
                return;
            }

            var newObj = JsonUtility.FromJson(json, obj.GetType());
            // 回退：序列化新对象再反序列化到目标（逐字段覆盖）
            var wrapper = JsonUtility.ToJson(newObj);
            JsonUtility.FromJsonOverwrite(wrapper, obj);
        }
    }
}