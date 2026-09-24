using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 加密密钥配置基类（Editor only）。
    /// 从 Assets/TEngine/Editor/CryptoKeys/EncryptConfigs 加载单例资产，
    /// 编辑器下不存在时自动创建并生成随机密钥。
    /// <remarks>
    /// 密钥值不再通过 Resources 打入运行时包；构建前通过烘焙脚本写入
    /// <see cref="KeyStore"/>（代码常量），配合 Obfuz FieldEncrypt 保护。
    /// </remarks>
    /// </summary>
    public abstract class CryptoKeyConfig<T> : ScriptableObject where T : CryptoKeyConfig<T>
    {
        private static T _instance;

        internal const string ConfigFolderPath = "Assets/TEngine/Editor/CryptoKeys/EncryptConfigs";

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    string assetPath = Path.Combine(ConfigFolderPath, typeof(T).Name + ".asset");
                    _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
                    if (_instance == null)
                    {
                        var instance = CreateInstance<T>();
                        string folder = ConfigFolderPath;
                        if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
                        {
                            Directory.CreateDirectory(folder);
                            UnityEditor.AssetDatabase.ImportAsset(folder, UnityEditor.ImportAssetOptions.ForceUpdate);
                        }
                        UnityEditor.AssetDatabase.CreateAsset(instance, assetPath);
                        UnityEditor.AssetDatabase.SaveAssets();
                        UnityEditor.AssetDatabase.Refresh();
                        _instance = instance;
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 重新生成随机密钥（Inspector 按钮）。
        /// </summary>
        public abstract void RegenerateKey();

        protected virtual void OnEnable()
        {
            EnsureKey();
        }

        protected void MarkDirty()
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        /// <summary>子类实现：密钥为空或长度不合法时生成随机密钥。</summary>
        protected abstract void EnsureKey();

        protected static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

        /// <summary>
        /// 解析 hex 字符串为字节数组；非法输入保留原值并告警。
        /// </summary>
        protected static byte[] ParseHex(string hex, byte[] fallback, int expectedLength = 0)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                Debug.LogWarning("[CryptoKeyConfig] 输入为空，保留原密钥。");
                return fallback;
            }

            hex = hex.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex.Substring(2);
            }

            if (hex.Length == 0 || hex.Length % 2 != 0 || hex.Any(c => !Uri.IsHexDigit(c)))
            {
                Debug.LogWarning("[CryptoKeyConfig] 非法的 hex 字符串（需为偶数长度的十六进制字符），保留原密钥。");
                return fallback;
            }

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            if (expectedLength > 0 && bytes.Length != expectedLength)
            {
                Debug.LogWarning($"[CryptoKeyConfig] 密钥长度必须为 {expectedLength} 字节，当前 {bytes.Length} 字节，保留原密钥。");
                return fallback;
            }

            if (bytes.All(b => b == 0))
            {
                Debug.LogWarning("[CryptoKeyConfig] 密钥不能全为零，保留原密钥。");
                return fallback;
            }

            return bytes;
        }

        /// <summary>
        /// 使用密码学安全随机数生成指定长度的字节数组。
        /// </summary>
        protected static byte[] GenerateRandomBytes(int length)
        {
            var bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }

        protected static bool IsEmpty(byte[] array)
        {
            if (array == null)
                return true;
            foreach (byte b in array)
            {
                if (b != 0)
                    return false;
            }
            return true;
        }
    }
}
