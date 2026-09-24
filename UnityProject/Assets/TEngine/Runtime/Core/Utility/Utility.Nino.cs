using System;
using System.IO;
using Nino.Core;
using UnityEngine;

namespace TEngine
{
    public static partial class Utility
    {
        /// <summary>
        /// Nino 二进制序列化工具类。
        /// <para>封装 NinoSerializer/NinoDeserializer 的常用操作，统一错误处理、日志和文件 IO。</para>
        /// <para>所有需要序列化的 managed 类型（class/record/含引用的 struct）必须标记 <see cref="NinoTypeAttribute"/>。</para>
        /// </summary>
        public static class Nino
        {
            /// <summary>
            /// Nino 存档文件扩展名。
            /// </summary>
            public const string SaveFileExtension = ".bin";

            /// <summary>
            /// 将对象序列化为二进制数据。
            /// </summary>
            /// <param name="value">要序列化的对象，类型必须标记 <see cref="NinoTypeAttribute"/>。</param>
            /// <returns>序列化后的二进制数据；失败返回 null。</returns>
            public static byte[] Serialize<T>(T value)
            {
                if (value == null)
                {
                    Log.Error("[Utility.Nino] Serialize failed: value is null");
                    return null;
                }

                try
                {
                    return NinoSerializer.Serialize(value);
                }
                catch (Exception e)
                {
                    Log.Error($"[Utility.Nino] Serialize failed, type={typeof(T).Name}, error={e}");
                    return null;
                }
            }

            /// <summary>
            /// 将对象序列化并写入文件。
            /// </summary>
            /// <param name="value">要序列化的对象。</param>
            /// <param name="filePath">目标文件路径。</param>
            /// <returns>是否写入成功。</returns>
            public static bool SerializeToFile<T>(T value, string filePath)
            {
                byte[] data = Serialize(value);
                if (data == null)
                {
                    return false;
                }

                return WriteFileBytes(filePath, data);
            }

            /// <summary>
            /// 将对象序列化为 Base64 字符串（用于 PlayerPrefs 等字符串存储）。
            /// </summary>
            /// <param name="value">要序列化的对象。</param>
            /// <returns>Base64 字符串；失败返回 null。</returns>
            public static string SerializeToBase64<T>(T value)
            {
                byte[] data = Serialize(value);
                if (data == null)
                {
                    return null;
                }

                return Convert.ToBase64String(data);
            }

            /// <summary>
            /// 从二进制数据反序列化为新对象。
            /// </summary>
            /// <param name="data">二进制数据。</param>
            /// <returns>反序列化的对象；失败返回 default。</returns>
            public static T Deserialize<T>(byte[] data)
            {
                if (data == null || data.Length == 0)
                {
                    return default;
                }

                try
                {
                    return NinoDeserializer.Deserialize<T>(data);
                }
                catch (Exception e)
                {
                    Log.Error($"[Utility.Nino] Deserialize failed, type={typeof(T).Name}, length={data.Length}, error={e}");
                    return default;
                }
            }

            /// <summary>
            /// 从二进制数据反序列化并填充到已有对象（引用反序列化，零分配）。
            /// <para>适用于 struct 类型；class 类型请用 <see cref="DeserializeIntoClass{T}"/>。</para>
            /// </summary>
            /// <param name="data">二进制数据。</param>
            /// <param name="target">目标对象，序列化数据将覆盖其字段。</param>
            /// <returns>是否成功。</returns>
            public static bool DeserializeInto<T>(byte[] data, ref T target) where T : struct
            {
                if (data == null || data.Length == 0)
                {
                    return false;
                }

                try
                {
                    NinoDeserializer.Deserialize(data, ref target);
                    return true;
                }
                catch (Exception e)
                {
                    Log.Error($"[Utility.Nino] DeserializeInto failed, type={typeof(T).Name}, length={data.Length}, error={e}");
                    return false;
                }
            }

            /// <summary>
            /// 从二进制数据反序列化并填充到已有 class 对象（引用反序列化，零分配）。
            /// </summary>
            /// <param name="data">二进制数据。</param>
            /// <param name="target">目标对象，类型必须标记 <see cref="NinoTypeAttribute"/>。</param>
            /// <returns>是否成功。</returns>
            public static bool DeserializeIntoClass<T>(byte[] data, T target) where T : class
            {
                if (data == null || data.Length == 0 || target == null)
                {
                    return false;
                }

                try
                {
                    object obj = target;
                    NinoDeserializer.Deserialize(data, target.GetType(), ref obj);
                    return true;
                }
                catch (Exception e)
                {
                    Log.Error($"[Utility.Nino] DeserializeIntoClass failed, type={typeof(T).Name}, length={data.Length}, error={e}");
                    return false;
                }
            }

            /// <summary>
            /// 从文件读取并反序列化为新对象。
            /// </summary>
            /// <param name="filePath">源文件路径。</param>
            /// <returns>反序列化的对象；文件不存在或失败返回 default。</returns>
            public static T DeserializeFromFile<T>(string filePath)
            {
                byte[] data = ReadFileBytes(filePath);
                if (data == null)
                {
                    return default;
                }

                return Deserialize<T>(data);
            }

            /// <summary>
            /// 从 Base64 字符串反序列化为新对象。
            /// </summary>
            /// <param name="base64Str">Base64 字符串。</param>
            /// <returns>反序列化的对象；失败返回 default。</returns>
            public static T DeserializeFromBase64<T>(string base64Str)
            {
                if (string.IsNullOrEmpty(base64Str))
                {
                    return default;
                }

                try
                {
                    byte[] data = Convert.FromBase64String(base64Str);
                    return Deserialize<T>(data);
                }
                catch (Exception e)
                {
                    Log.Error($"[Utility.Nino] DeserializeFromBase64 failed, type={typeof(T).Name}, error={e}");
                    return default;
                }
            }

            /// <summary>
            /// 检查指定路径是否存在旧版 JSON 存档文件。
            /// </summary>
            /// <param name="binFilePath">Nino 二进制文件路径。</param>
            /// <returns>对应的旧 JSON 文件路径（若存在），否则 null。</returns>
            public static string GetLegacyJsonPath(string binFilePath)
            {
                string dir = System.IO.Path.GetDirectoryName(binFilePath);
                string fileName = System.IO.Path.GetFileNameWithoutExtension(binFilePath);
                string jsonPath = System.IO.Path.Combine(dir, $"{fileName}.json");

                return System.IO.File.Exists(jsonPath) ? jsonPath : null;
            }

            /// <summary>
            /// 删除旧版 JSON 存档文件（迁移成功后调用）。
            /// </summary>
            /// <param name="jsonFilePath">JSON 文件路径。</param>
            /// <returns>是否删除成功。</returns>
            public static bool DeleteLegacyJson(string jsonFilePath)
            {
                if (string.IsNullOrEmpty(jsonFilePath) || !System.IO.File.Exists(jsonFilePath))
                {
                    return false;
                }

                try
                {
                    System.IO.File.Delete(jsonFilePath);
                    return true;
                }
                catch (Exception e)
                {
                    Log.Error($"[Utility.Nino] DeleteLegacyJson failed, path={jsonFilePath}, error={e}");
                    return false;
                }
            }

            /// <summary>
            /// 获取 Nino 存档文件路径（将任意扩展名替换为 .bin）。
            /// </summary>
            /// <param name="directory">存档目录。</param>
            /// <param name="fileName">存档文件名（不含扩展名）。</param>
            /// <returns>完整的 .bin 文件路径。</returns>
            public static string GetSaveFilePath(string directory, string fileName)
                => System.IO.Path.Combine(directory, $"{fileName}{SaveFileExtension}");

            private static bool WriteFileBytes(string filePath, byte[] data)
            {
                try
                {
                    string dir = System.IO.Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                    }

                    System.IO.File.WriteAllBytes(filePath, data);
                    return true;
                }
                catch (Exception e)
                {
                    Log.Error($"[Utility.Nino] WriteFile failed, path={filePath}, length={data?.Length ?? 0}, error={e}");
                    return false;
                }
            }

            private static byte[] ReadFileBytes(string filePath)
            {
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                {
                    return null;
                }

                try
                {
                    return System.IO.File.ReadAllBytes(filePath);
                }
                catch (Exception e)
                {
                    Log.Error($"[Utility.Nino] ReadFile failed, path={filePath}, error={e}");
                    return null;
                }
            }
        }
    }
}
