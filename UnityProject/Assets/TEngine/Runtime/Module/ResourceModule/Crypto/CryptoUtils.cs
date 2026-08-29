using System;
using System.Security.Cryptography;

namespace TEngine
{
    /// <summary>
    /// 加密算法公共工具：随机密钥生成与校验。
    /// </summary>
    internal static class CryptoUtils
    {
        /// <summary>
        /// 使用密码学安全随机数生成指定长度的字节数组。
        /// </summary>
        public static byte[] GenerateRandomBytes(int length)
        {
            var bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// 判断字节数组为 null 或全零。
        /// </summary>
        public static bool IsEmpty(byte[] array)
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

        /// <summary>
        /// 校验密钥长度，不合法时抛出异常（密钥配置丢失或损坏属于必须中断的严重错误）。
        /// </summary>
        public static void ValidateKey(byte[] key, int expectedLength, string name)
        {
            if (key == null)
                throw new InvalidOperationException($"[{name}] key is null. Missing CryptoKeyConfig asset in Resources/{ResourceConfigFolder}?");
            if (key.Length != expectedLength)
                throw new InvalidOperationException($"[{name}] key length must be {expectedLength} bytes, got {key.Length}.");
            if (IsEmpty(key))
                throw new InvalidOperationException($"[{name}] key is all zeros.");
        }

        /// <summary>
        /// 密钥配置资产所在的 Resources 子目录。
        /// </summary>
        public const string ResourceConfigFolder = "EncryptConfigs";
    }
}
