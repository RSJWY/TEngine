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
        /// 序列化前会对列表去重（去空白、Trim、Ordinal 去重），保证产物干净。
        /// </summary>
        public string ToJson()
        {
            var normalized = Normalize(AOTMetaAssemblies);
            return JsonUtility.ToJson(new SerializedData { AOTMetaAssemblies = normalized }, true);
        }

        /// <summary>
        /// 从 JSON 字节流反序列化出 AOT 元数据程序集列表。
        /// 兼容归档管线（RawFileObject.GetBytes）与非归档管线（TextAsset.bytes）。
        /// 返回结果已去重。
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
        /// 返回结果已去重。
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
                return Normalize(data?.AOTMetaAssemblies);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AOTMetadata] 解析 JSON 失败：{e.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 就地归一化 AOTMetaAssemblies：去空白、Trim、Ordinal 去重，保持首次出现顺序。
        /// 供编辑器手动去重按钮调用。
        /// </summary>
        public void Dedupe()
        {
            AOTMetaAssemblies = Normalize(AOTMetaAssemblies);
        }

        /// <summary>
        /// 归一化程序集列表：去空白、Trim、Ordinal 去重，保持首次出现顺序。
        /// </summary>
        private static List<string> Normalize(List<string> assemblies)
        {
            if (assemblies == null || assemblies.Count == 0)
            {
                return new List<string>();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>(assemblies.Count);
            for (int i = 0; i < assemblies.Count; i++)
            {
                string assembly = assemblies[i];
                if (string.IsNullOrWhiteSpace(assembly))
                {
                    continue;
                }

                assembly = assembly.Trim();
                if (seen.Add(assembly))
                {
                    result.Add(assembly);
                }
            }

            return result;
        }

        [Serializable]
        private class SerializedData
        {
            public List<string> AOTMetaAssemblies = new List<string>();
        }
    }
}
