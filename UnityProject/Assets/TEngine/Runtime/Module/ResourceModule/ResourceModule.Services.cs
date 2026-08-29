using System;
using System.Collections.Generic;
using System.IO;
using YooAsset;

namespace TEngine
{
    internal sealed class RemoteServices : IRemoteService
    {
        private readonly string _defaultHostServer;
        private readonly string _fallbackHostServer;
        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostServer = defaultHostServer;
            _fallbackHostServer = fallbackHostServer;
        }
        public IReadOnlyList<string> GetRemoteUrls(string fileName)
        {
            var urls = new List<string>(2);
            if (!string.IsNullOrEmpty(_defaultHostServer))
                urls.Add($"{_defaultHostServer}/{fileName}");
            if (!string.IsNullOrEmpty(_fallbackHostServer))
                urls.Add($"{_fallbackHostServer}/{fileName}");
            return urls;
        }
    }

    /// <summary>
    /// 一套加密方案的运行时集合。
    /// 本地文件系统使用流式（或偏移式）解密器以避免整包内存峰值；
    /// Web 文件系统的数据已在内存中，只能使用内存式解密器（YooAsset 的 Web 加载操作仅支持 IBundleMemoryDecryptor）。
    /// </summary>
    public sealed class BundleCrypto
    {
        /// <summary>构建管线使用的加密器。</summary>
        public IBundleEncryptor Encryptor { get; private set; }

        /// <summary>本地文件系统（Builtin/Sandbox）使用的解密器。</summary>
        public IBundleDecryptor Local { get; private set; }

        /// <summary>Web 文件系统（WebNetwork/WebServer/WeChat）使用的解密器。</summary>
        public IBundleDecryptor Web { get; private set; }

        /// <summary>
        /// 按加密类型创建对应方案。None 返回 null（不加密）。
        /// </summary>
        public static BundleCrypto Create(EncryptionType type)
        {
            switch (type)
            {
                case EncryptionType.FileOffSet:
                    return new BundleCrypto
                    {
                        Encryptor = new FileOffsetEncryption(),
                        Local = new FileOffsetDecryption(),
                        Web = new FileOffsetMemoryDecryption(),
                    };
                case EncryptionType.FileStream:
                    return new BundleCrypto
                    {
                        Encryptor = new XorBundleEncryption(),
                        Local = new XorStreamDecryption(),
                        Web = new XorMemoryDecryption(),
                    };
                case EncryptionType.ChaCha20:
                    return new BundleCrypto
                    {
                        Encryptor = new ChaCha20BundleEncryption(),
                        Local = new ChaCha20StreamDecryption(),
                        Web = new ChaCha20MemoryDecryption(),
                    };
                default:
                    return null;
            }
        }
    }

    #region FileOffset 文件偏移（伪加密）

    public sealed class FileOffsetEncryption : IBundleEncryptor
    {
        public BundleEncryptResult Encrypt(BundleEncryptArgs args)
        {
            const int offset = 32;
            var data = File.ReadAllBytes(args.FilePath);
            var encrypted = new byte[data.Length + offset];
            Buffer.BlockCopy(data, 0, encrypted, offset, data.Length);
            return new BundleEncryptResult(true, encrypted);
        }
    }

    /// <summary>
    /// 偏移式解密：本地文件直接跳过头部加载，零拷贝。
    /// </summary>
    public sealed class FileOffsetDecryption : IBundleOffsetDecryptor
    {
        public long GetFileOffset(BundleDecryptArgs args) => 32;
    }

    /// <summary>
    /// 偏移式解密的内存版本，供 Web 文件系统使用。
    /// </summary>
    public sealed class FileOffsetMemoryDecryption : IBundleMemoryDecryptor
    {
        public byte[] GetDecryptedData(BundleDecryptArgs args)
        {
            const int offset = 32;
            var data = args.FileData ?? File.ReadAllBytes(args.FilePath);
            if (data.Length < offset)
                throw new InvalidDataException("Encrypted bundle is smaller than the configured file offset.");

            var decrypted = new byte[data.Length - offset];
            Buffer.BlockCopy(data, offset, decrypted, 0, decrypted.Length);
            return decrypted;
        }
    }

    #endregion

    #region XOR 变长密钥流加密（本地流式）

    public sealed class XorBundleEncryption : IBundleEncryptor
    {
        public BundleEncryptResult Encrypt(BundleEncryptArgs args)
        {
            var key = XorKeyConfig.Instance.key;
            if (CryptoUtils.IsEmpty(key))
                throw new InvalidOperationException("[Xor] key is empty. Missing XorKeyConfig asset in Resources/EncryptConfigs?");
            var data = File.ReadAllBytes(args.FilePath);
            for (int i = 0; i < data.Length; i++)
                data[i] ^= key[i % key.Length];
            return new BundleEncryptResult(true, data);
        }
    }

    public sealed class XorStreamDecryption : IBundleStreamDecryptor
    {
        public Stream CreateDecryptionStream(BundleDecryptArgs args)
            => new XorStream(XorKeyConfig.Instance.key, args.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        public int GetBufferSize(BundleDecryptArgs args) => 2048;
    }

    public sealed class XorMemoryDecryption : IBundleMemoryDecryptor
    {
        public byte[] GetDecryptedData(BundleDecryptArgs args)
        {
            var key = XorKeyConfig.Instance.key;
            var data = args.FileData ?? File.ReadAllBytes(args.FilePath);
            for (int i = 0; i < data.Length; i++)
                data[i] ^= key[i % key.Length];
            return data;
        }
    }

    #endregion

    #region ChaCha20（本地流式 / Web 内存式）

    public sealed class ChaCha20BundleEncryption : IBundleEncryptor
    {
        public BundleEncryptResult Encrypt(BundleEncryptArgs args)
        {
            var config = ChaCha20KeyConfig.Instance;
            return new BundleEncryptResult(true,
                ChaCha20Util.Encrypt(File.ReadAllBytes(args.FilePath), config.key, config.nonce));
        }
    }

    public sealed class ChaCha20StreamDecryption : IBundleStreamDecryptor
    {
        public Stream CreateDecryptionStream(BundleDecryptArgs args)
        {
            var config = ChaCha20KeyConfig.Instance;
            return new ChaCha20Stream(config.key, config.nonce, args.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public int GetBufferSize(BundleDecryptArgs args) => 2048;
    }

    public sealed class ChaCha20MemoryDecryption : IBundleMemoryDecryptor
    {
        public byte[] GetDecryptedData(BundleDecryptArgs args)
        {
            var config = ChaCha20KeyConfig.Instance;
            var data = args.FileData ?? File.ReadAllBytes(args.FilePath);
            return ChaCha20Util.Decrypt(data, config.key, config.nonce);
        }
    }

    #endregion
}
