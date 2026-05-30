using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 远端资源地址查询服务类
    /// </summary>
    internal class RemoteServices : IRemoteServices
    {
        private readonly string _defaultHostServer;
        private readonly string _fallbackHostServer;

        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostServer = defaultHostServer;
            _fallbackHostServer = fallbackHostServer;
        }

        string IRemoteServices.GetRemoteMainURL(string fileName)
        {
            return $"{_defaultHostServer}/{fileName}";
        }

        string IRemoteServices.GetRemoteFallbackURL(string fileName)
        {
            return $"{_fallbackHostServer}/{fileName}";
        }
    }

    /// <summary>
    /// 文件流加密方式
    /// </summary>
    public class FileStreamEncryption : IEncryptionServices
    {
        public EncryptResult Encrypt(EncryptFileInfo fileInfo)
        {
            var fileData = File.ReadAllBytes(fileInfo.FileLoadPath);
            for (int i = 0; i < fileData.Length; i++)
            {
                fileData[i] ^= BundleStream.KEY;
            }

            EncryptResult result = new EncryptResult();
            result.Encrypted = true;
            result.EncryptedData = fileData;
            return result;
        }
    }

    /// <summary>
    /// 资源文件流加载解密类
    /// </summary>
    public class FileStreamDecryption : IDecryptionServices
    {
        /// <summary>
        /// 同步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo)
        {
            BundleStream bundleStream =
                new BundleStream(fileInfo.FileLoadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = bundleStream;
            decryptResult.Result =
                AssetBundle.LoadFromStream(bundleStream, 0, GetManagedReadBufferSize());
            return decryptResult;
        }

        /// <summary>
        /// 异步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo)
        {
            BundleStream bundleStream =
                new BundleStream(fileInfo.FileLoadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = bundleStream;
            decryptResult.CreateRequest =
                AssetBundle.LoadFromStreamAsync(bundleStream, 0, GetManagedReadBufferSize());
            return decryptResult;
        }

        /// <summary>
        /// 后备方式获取解密的资源包对象
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleFallback(DecryptFileInfo fileInfo)
        {
            return new DecryptResult();
        }

        /// <summary>
        /// 获取解密的字节数据
        /// </summary>
        byte[] IDecryptionServices.ReadFileData(DecryptFileInfo fileInfo)
        {
            throw new System.NotImplementedException();
        }

        /// <summary>
        /// 获取解密的文本数据
        /// </summary>
        string IDecryptionServices.ReadFileText(DecryptFileInfo fileInfo)
        {
            throw new System.NotImplementedException();
        }

        private static uint GetManagedReadBufferSize()
        {
            return 1024;
        }
    }

    /// <summary>
    /// 文件偏移加密方式
    /// </summary>
    public class FileOffsetEncryption : IEncryptionServices
    {
        public EncryptResult Encrypt(EncryptFileInfo fileInfo)
        {
            int offset = 32;
            byte[] fileData = File.ReadAllBytes(fileInfo.FileLoadPath);
            var encryptedData = new byte[fileData.Length + offset];
            Buffer.BlockCopy(fileData, 0, encryptedData, offset, fileData.Length);

            EncryptResult result = new EncryptResult();
            result.Encrypted = true;
            result.EncryptedData = encryptedData;
            return result;
        }
    }

    /// <summary>
    /// 资源文件偏移加载解密类
    /// </summary>
    public class FileOffsetDecryption : IDecryptionServices
    {
        /// <summary>
        /// 同步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo)
        {
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = null;
            decryptResult.Result =
                AssetBundle.LoadFromFile(fileInfo.FileLoadPath, 0, GetFileOffset());
            return decryptResult;
        }

        /// <summary>
        /// 异步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo)
        {
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = null;
            decryptResult.CreateRequest =
                AssetBundle.LoadFromFileAsync(fileInfo.FileLoadPath, 0, GetFileOffset());
            return decryptResult;
        }

        /// <summary>
        /// 后备方式获取解密的资源包对象
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleFallback(DecryptFileInfo fileInfo)
        {
            return new DecryptResult();
        }

        /// <summary>
        /// 获取解密的字节数据
        /// </summary>
        byte[] IDecryptionServices.ReadFileData(DecryptFileInfo fileInfo)
        {
            throw new System.NotImplementedException();
        }

        /// <summary>
        /// 获取解密的文本数据
        /// </summary>
        string IDecryptionServices.ReadFileText(DecryptFileInfo fileInfo)
        {
            throw new System.NotImplementedException();
        }

        private static ulong GetFileOffset()
        {
            return 32;
        }
    }




    public class XXTEAEncryption : IEncryptionServices
    {
        public EncryptResult Encrypt(EncryptFileInfo fileInfo)
        {
            byte[] fileData = File.ReadAllBytes(fileInfo.FileLoadPath);
            EncryptResult result = new EncryptResult();
            result.Encrypted = true;
            result.EncryptedData = XXTEACrypto.Encrypt(fileData);
            return result;
        }
    }

    public class XXTEADecryption : IDecryptionServices
    {
        DecryptResult IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo)
        {
            byte[] decryptedData = XXTEACrypto.Decrypt(File.ReadAllBytes(fileInfo.FileLoadPath));
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = null;
            decryptResult.Result = AssetBundle.LoadFromMemory(decryptedData);
            return decryptResult;
        }

        DecryptResult IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo)
        {
            byte[] decryptedData = XXTEACrypto.Decrypt(File.ReadAllBytes(fileInfo.FileLoadPath));
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = null;
            decryptResult.CreateRequest = AssetBundle.LoadFromMemoryAsync(decryptedData);
            return decryptResult;
        }

        DecryptResult IDecryptionServices.LoadAssetBundleFallback(DecryptFileInfo fileInfo)
        {
            byte[] decryptedData = XXTEACrypto.Decrypt(File.ReadAllBytes(fileInfo.FileLoadPath));
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = null;
            decryptResult.Result = AssetBundle.LoadFromMemory(decryptedData);
            return decryptResult;
        }

        byte[] IDecryptionServices.ReadFileData(DecryptFileInfo fileInfo)
        {
            return XXTEACrypto.Decrypt(File.ReadAllBytes(fileInfo.FileLoadPath));
        }

        string IDecryptionServices.ReadFileText(DecryptFileInfo fileInfo)
        {
            return System.Text.Encoding.UTF8.GetString(XXTEACrypto.Decrypt(File.ReadAllBytes(fileInfo.FileLoadPath)));
        }
    }

    #region WebDecryptionServices
    /// <summary>
    /// 资源文件偏移加载解密类
    /// </summary>
    public class FileOffsetWebDecryption : IWebDecryptionServices
    {
        public WebDecryptResult LoadAssetBundle(WebDecryptFileInfo fileInfo)
        {
            int offset = GetFileOffset();
            byte[] decryptedData = new byte[fileInfo.FileData.Length - offset];
            Buffer.BlockCopy(fileInfo.FileData, offset, decryptedData, 0, decryptedData.Length);
            // 从内存中加载AssetBundle
            WebDecryptResult decryptResult = new WebDecryptResult();
            decryptResult.Result = AssetBundle.LoadFromMemory(decryptedData);
            return decryptResult;
        }

        private static int GetFileOffset()
        {
            return 32;
        }
    }

    public class FileStreamWebDecryption : IWebDecryptionServices
    {
        public WebDecryptResult LoadAssetBundle(WebDecryptFileInfo fileInfo)
        {
            // 优化：使用Buffer批量操作替代逐字节异或
            byte[] decryptedData = new byte[fileInfo.FileData.Length];
            Buffer.BlockCopy(fileInfo.FileData, 0, decryptedData, 0, fileInfo.FileData.Length);

            for (int i = 0; i < decryptedData.Length; i++)
            {
                decryptedData[i] ^= BundleStream.KEY;
            }

            WebDecryptResult decryptResult = new WebDecryptResult();
            decryptResult.Result = AssetBundle.LoadFromMemory(decryptedData);
            return decryptResult;
        }
    }


    public class XXTEAWebDecryption : IWebDecryptionServices
    {
        public WebDecryptResult LoadAssetBundle(WebDecryptFileInfo fileInfo)
        {
            byte[] decryptedData = XXTEACrypto.Decrypt(fileInfo.FileData);
            WebDecryptResult decryptResult = new WebDecryptResult();
            decryptResult.Result = AssetBundle.LoadFromMemory(decryptedData);
            return decryptResult;
        }
    }
    #endregion
}

/// <summary>
/// 资源文件解密流
/// </summary>
public class BundleStream : FileStream
{
    public const byte KEY = 64;

    public BundleStream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access,
        share)
    {
    }

    public BundleStream(string path, FileMode mode) : base(path, mode)
    {
    }

    public override int Read(byte[] array, int offset, int count)
    {
        var index = base.Read(array, offset, count);
        for (int i = 0; i < array.Length; i++)
        {
            array[i] ^= KEY;
        }
        return index;
    }
}


internal static class XXTEACrypto
{
    private const uint Delta = 0x9E3779B9;
    private static readonly uint[] Key = { 0x54454E47, 0x696E6548, 0x6F744469, 0x78585445 };

    public static byte[] Encrypt(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        uint[] value = ToUInt32Array(data, true);
        uint[] key = Key;
        int n = value.Length - 1;
        uint z = value[n];
        uint y;
        uint sum = 0;
        uint q = (uint)(6 + 52 / (n + 1));

        unchecked
        {
            while (q-- > 0)
            {
                sum += Delta;
                uint e = (sum >> 2) & 3;
                for (int p = 0; p < n; p++)
                {
                    y = value[p + 1];
                    z = value[p] += MX(sum, y, z, p, e, key);
                }

                y = value[0];
                z = value[n] += MX(sum, y, z, n, e, key);
            }
        }

        return ToByteArray(value, false);
    }

    public static byte[] Decrypt(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        uint[] value = ToUInt32Array(data, false);
        uint[] key = Key;
        int n = value.Length - 1;
        uint z;
        uint y = value[0];
        uint q = (uint)(6 + 52 / (n + 1));
        uint sum = q * Delta;

        unchecked
        {
            while (sum != 0)
            {
                uint e = (sum >> 2) & 3;
                for (int p = n; p > 0; p--)
                {
                    z = value[p - 1];
                    y = value[p] -= MX(sum, y, z, p, e, key);
                }

                z = value[n];
                y = value[0] -= MX(sum, y, z, 0, e, key);
                sum -= Delta;
            }
        }

        return ToByteArray(value, true);
    }

    private static uint MX(uint sum, uint y, uint z, int p, uint e, uint[] key)
    {
        return (((z >> 5) ^ (y << 2)) + ((y >> 3) ^ (z << 4))) ^ ((sum ^ y) + (key[(p & 3) ^ (int)e] ^ z));
    }

    private static uint[] ToUInt32Array(byte[] data, bool includeLength)
    {
        int length = data.Length;
        int n = ((length & 3) == 0) ? (length >> 2) : ((length >> 2) + 1);
        uint[] result = includeLength ? new uint[n + 1] : new uint[n];

        for (int i = 0; i < length; i++)
        {
            result[i >> 2] |= (uint)data[i] << ((i & 3) << 3);
        }

        if (includeLength)
        {
            result[n] = (uint)length;
        }

        return result;
    }

    private static byte[] ToByteArray(uint[] data, bool includeLength)
    {
        int n = data.Length << 2;
        if (includeLength)
        {
            int length = (int)data[data.Length - 1];
            n -= 4;
            if (length < n - 3 || length > n)
            {
                throw new InvalidDataException("Invalid XXTEA data length.");
            }

            n = length;
        }

        byte[] result = new byte[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = (byte)(data[i >> 2] >> ((i & 3) << 3));
        }

        return result;
    }
}
