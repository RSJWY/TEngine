using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    [CreateAssetMenu(menuName = "TEngine/AOT Metadata Manifest", fileName = "AOTMetadataManifest")]
    public class AOTMetadataManifest : ScriptableObject
    {
        public const string ManifestAssetName = "AOTMetadataManifest";

        public const string JsonAssetExtension = ".json.bytes";

        public const string ManifestJsonAssetName = ManifestAssetName + JsonAssetExtension;

        public List<string> AOTMetaAssemblies = new List<string>();

        /// <summary>
        /// 序列化为 JSON 字符串（用于写入 .json.bytes 打包资产）。
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(new SerializedData { AOTMetaAssemblies = AOTMetaAssemblies ?? new List<string>() }, true);
        }

        /// <summary>
        /// 从 JSON 字节流反序列化出 AOT 元数据程序集列表。
        /// 兼容归档管线（RawFileObject.GetBytes）与非归档管线（TextAsset.bytes）。
        /// </summary>
        public static List<string> FromJsonBytes(byte[] jsonBytes)
        {
            if (jsonBytes == null || jsonBytes.Length == 0)
            {
                return new List<string>();
            }

            string json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            return FromJson(json);
        }

        /// <summary>
        /// 从 JSON 字符串反序列化出 AOT 元数据程序集列表。
        /// </summary>
        public static List<string> FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new List<string>();
            }

            try
            {
                var data = JsonUtility.FromJson<SerializedData>(json);
                return data?.AOTMetaAssemblies ?? new List<string>();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AOTMetadata] 解析 JSON 失败：{e.Message}");
                return new List<string>();
            }
        }

        [Serializable]
        private class SerializedData
        {
            public List<string> AOTMetaAssemblies = new List<string>();
        }
    }
}
