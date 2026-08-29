using System;
using System.Buffers.Binary;

namespace TEngine
{
    /// <summary>
    /// ChaCha20 流密码（RFC 7539），纯 C# 实现，无平台依赖。
    /// 流密码加解密为同一变换。
    /// </summary>
    public static class ChaCha20Util
    {
        public const int KeyLength = 32;
        public const int NonceLength = 12;

        /// <summary>
        /// 整包加密。
        /// </summary>
        public static byte[] Encrypt(byte[] data, byte[] key, byte[] nonce)
        {
            ValidateKeyAndNonce(key, nonce);
            return Transform(data, key, nonce);
        }

        /// <summary>
        /// 整包解密（与加密同一变换）。
        /// </summary>
        public static byte[] Decrypt(byte[] data, byte[] key, byte[] nonce)
        {
            ValidateKeyAndNonce(key, nonce);
            return Transform(data, key, nonce);
        }

        /// <summary>
        /// 生成第 blockCounter 个 64 字节 keystream 块，供流式解密按文件位置随机取用。
        /// </summary>
        public static void GenerateBlock(uint blockCounter, byte[] key, byte[] nonce, Span<byte> output)
        {
            ValidateKeyAndNonce(key, nonce);
            Span<uint> state = stackalloc uint[16];
            InitState(state, blockCounter, key, nonce);
            ChaCha20BlockCore(state, output);
        }

        private static byte[] Transform(byte[] data, byte[] key, byte[] nonce)
        {
            byte[] result = new byte[data.Length];
            Span<uint> state = stackalloc uint[16];
            Span<byte> keystream = stackalloc byte[64];

            int offset = 0;
            uint blockCounter = 0;
            while (offset < data.Length)
            {
                InitState(state, blockCounter, key, nonce);
                ChaCha20BlockCore(state, keystream);

                int blockSize = Math.Min(64, data.Length - offset);
                for (int i = 0; i < blockSize; i++)
                    result[offset + i] ^= keystream[i];

                offset += blockSize;
                blockCounter++;
            }

            return result;
        }

        private static void InitState(Span<uint> state, uint blockCounter, byte[] key, byte[] nonce)
        {
            // 常量 "expand 32-byte k"
            state[0] = 0x61707865;
            state[1] = 0x3320646e;
            state[2] = 0x79622d32;
            state[3] = 0x6b206574;
            for (int i = 0; i < 8; i++)
                state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(i * 4, 4));
            state[12] = blockCounter;
            for (int i = 0; i < 3; i++)
                state[13 + i] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.AsSpan(i * 4, 4));
        }

        private static void ChaCha20BlockCore(Span<uint> state, Span<byte> output)
        {
            Span<uint> working = stackalloc uint[16];
            state.CopyTo(working);

            for (int round = 0; round < 10; round++)
            {
                QuarterRound(working, 0, 4, 8, 12);
                QuarterRound(working, 1, 5, 9, 13);
                QuarterRound(working, 2, 6, 10, 14);
                QuarterRound(working, 3, 7, 11, 15);
                QuarterRound(working, 0, 5, 10, 15);
                QuarterRound(working, 1, 6, 11, 12);
                QuarterRound(working, 2, 7, 8, 13);
                QuarterRound(working, 3, 4, 9, 14);
            }

            for (int i = 0; i < 16; i++)
            {
                working[i] += state[i];
                BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(i * 4, 4), working[i]);
            }
        }

        private static void QuarterRound(Span<uint> s, int a, int b, int c, int d)
        {
            s[a] += s[b];
            s[d] ^= s[a];
            s[d] = RotateLeft(s[d], 16);
            s[c] += s[d];
            s[b] ^= s[c];
            s[b] = RotateLeft(s[b], 12);
            s[a] += s[b];
            s[d] ^= s[a];
            s[d] = RotateLeft(s[d], 8);
            s[c] += s[d];
            s[b] ^= s[c];
            s[b] = RotateLeft(s[b], 7);
        }

        private static uint RotateLeft(uint value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }

        private static void ValidateKeyAndNonce(byte[] key, byte[] nonce)
        {
            CryptoUtils.ValidateKey(key, KeyLength, "ChaCha20");
            CryptoUtils.ValidateKey(nonce, NonceLength, "ChaCha20");
        }
    }
}
