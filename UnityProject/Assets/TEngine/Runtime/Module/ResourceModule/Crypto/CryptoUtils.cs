using System;

namespace TEngine
{
    /// <summary>
    /// 加密算法公共工具：密钥校验。
    /// <remarks>
    /// 随机密钥生成已移至 Editor 程序集（<see cref="CryptoKeyConfig{T}"/>），
    /// 运行时只需要校验密钥有效性。
    /// </remarks>
    /// </summary>
    internal static class CryptoUtils
    {
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
                throw new InvalidOperationException($"[{name}] key is null. KeyStore not baked?");
            if (key.Length != expectedLength)
                throw new InvalidOperationException($"[{name}] key length must be {expectedLength} bytes, got {key.Length}.");
            if (IsEmpty(key))
                throw new InvalidOperationException($"[{name}] key is all zeros.");
        }
    }
}
