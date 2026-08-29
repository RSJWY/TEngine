using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 加密密钥配置基类：从 Resources/EncryptConfigs 加载单例资产，
    /// 编辑器下不存在时自动创建并生成随机密钥。
    /// </summary>
    public abstract class CryptoKeyConfig<T> : ScriptableObject where T : CryptoKeyConfig<T>
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<T>(Path.Combine(CryptoUtils.ResourceConfigFolder, typeof(T).Name));
#if UNITY_EDITOR
                    if (_instance == null)
                    {
                        var instance = CreateInstance<T>();
                        string folder = Path.Combine("Assets/Resources", CryptoUtils.ResourceConfigFolder);
                        if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
                        {
                            UnityEditor.AssetDatabase.CreateFolder("Assets/Resources", CryptoUtils.ResourceConfigFolder);
                        }
                        UnityEditor.AssetDatabase.CreateAsset(instance,
                            Path.Combine(folder, typeof(T).Name + ".asset"));
                        UnityEditor.AssetDatabase.SaveAssets();
                        UnityEditor.AssetDatabase.Refresh();
                        _instance = instance;
                    }
#else
                    if (_instance == null)
                    {
                        // 不能仅记录日志后继续返回 null：后续访问 .key 会 NRE，
                        // 此时包初始化已进行到一半，错误时机晚、定位困难。
                        // 直接抛异常让资源包初始化在最早点失败。
                        throw new InvalidOperationException(
                            $"CryptoKeyConfig<{typeof(T).Name}> not found in Resources/{CryptoUtils.ResourceConfigFolder}. " +
                            "请在编辑器内构建密钥资产后再打运行时包。");
                    }
#endif
                }
                return _instance;
            }
        }

        /// <summary>
        /// 重新生成随机密钥（Inspector 按钮）。
        /// </summary>
        public abstract void RegenerateKey();

#if UNITY_EDITOR
        protected virtual void OnEnable()
        {
            // 仅编辑器自动补齐密钥；运行时生成随机密钥会与打包端不一致。
            EnsureKey();
        }

        protected void MarkDirty()
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
        }
#endif

        /// <summary>子类实现：密钥为空或长度不合法时生成随机密钥（仅编辑器调用）。</summary>
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
                Log.Warning("[CryptoKeyConfig] 输入为空，保留原密钥。");
                return fallback;
            }

            hex = hex.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex.Substring(2);
            }

            if (hex.Length == 0 || hex.Length % 2 != 0 || hex.Any(c => !Uri.IsHexDigit(c)))
            {
                Log.Warning("[CryptoKeyConfig] 非法的 hex 字符串（需为偶数长度的十六进制字符），保留原密钥。");
                return fallback;
            }

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            if (expectedLength > 0 && bytes.Length != expectedLength)
            {
                Log.Warning($"[CryptoKeyConfig] 密钥长度必须为 {expectedLength} 字节，当前 {bytes.Length} 字节，保留原密钥。");
                return fallback;
            }

            if (bytes.All(b => b == 0))
            {
                Log.Warning("[CryptoKeyConfig] 密钥不能全为零，保留原密钥。");
                return fallback;
            }

            return bytes;
        }
    }
}
