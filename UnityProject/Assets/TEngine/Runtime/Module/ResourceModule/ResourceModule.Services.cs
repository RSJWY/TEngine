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

    public sealed class FileStreamEncryption : IBundleEncryptor
    {
        public BundleEncryptResult Encrypt(BundleEncryptArgs args)
        {
            var data = File.ReadAllBytes(args.FilePath);
            for (int i = 0; i < data.Length; i++)
                data[i] ^= BundleStream.KEY;
            return new BundleEncryptResult(true, data);
        }
    }

    public sealed class FileStreamDecryption : IBundleStreamDecryptor, IBundleMemoryDecryptor
    {
        public Stream CreateDecryptionStream(BundleDecryptArgs args)
            => new BundleStream(args.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        public int GetBufferSize(BundleDecryptArgs args) => 1024;

        public byte[] GetDecryptedData(BundleDecryptArgs args)
        {
            var data = args.FileData ?? File.ReadAllBytes(args.FilePath);
            for (int i = 0; i < data.Length; i++)
                data[i] ^= BundleStream.KEY;
            return data;
        }
    }

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

    public sealed class FileOffsetDecryption : IBundleOffsetDecryptor, IBundleMemoryDecryptor
    {
        public long GetFileOffset(BundleDecryptArgs args) => 32;

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

    public sealed class XXTEAEncryption : IBundleEncryptor
    {
        public BundleEncryptResult Encrypt(BundleEncryptArgs args)
            => new BundleEncryptResult(true, XXTEACrypto.Encrypt(File.ReadAllBytes(args.FilePath)));
    }

    public sealed class XXTEADecryption : IBundleMemoryDecryptor
    {
        public byte[] GetDecryptedData(BundleDecryptArgs args)
            => XXTEACrypto.Decrypt(args.FileData ?? File.ReadAllBytes(args.FilePath));
    }
}

public sealed class BundleStream : FileStream
{
    public const byte KEY = 64;

    public BundleStream(string path, FileMode mode, FileAccess access, FileShare share)
        : base(path, mode, access, share) { }

    public override int Read(byte[] array, int offset, int count)
    {
        int read = base.Read(array, offset, count);
        for (int i = offset; i < offset + read; i++)
            array[i] ^= KEY;
        return read;
    }
}

internal static class XXTEACrypto
{
    private const uint Delta = 0x9E3779B9;
    private static readonly uint[] Key = { 0x54454E47, 0x696E6548, 0x6F744469, 0x78585445 };

    public static byte[] Encrypt(byte[] data)
    {
        if (data == null || data.Length == 0)
            return Array.Empty<byte>();

        uint[] value = ToUInt32Array(data, true);
        int n = value.Length - 1;
        uint z = value[n];
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
                    uint y = value[p + 1];
                    z = value[p] += MX(sum, y, z, p, e);
                }
                uint first = value[0];
                z = value[n] += MX(sum, first, z, n, e);
            }
        }
        return ToByteArray(value, false);
    }

    public static byte[] Decrypt(byte[] data)
    {
        if (data == null || data.Length == 0)
            return Array.Empty<byte>();

        uint[] value = ToUInt32Array(data, false);
        int n = value.Length - 1;
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
                    uint z = value[p - 1];
                    y = value[p] -= MX(sum, y, z, p, e);
                }
                uint last = value[n];
                y = value[0] -= MX(sum, y, last, 0, e);
                sum -= Delta;
            }
        }
        return ToByteArray(value, true);
    }

    private static uint MX(uint sum, uint y, uint z, int p, uint e)
        => (((z >> 5) ^ (y << 2)) + ((y >> 3) ^ (z << 4))) ^
           ((sum ^ y) + (Key[(p & 3) ^ (int)e] ^ z));

    private static uint[] ToUInt32Array(byte[] data, bool includeLength)
    {
        int length = data.Length;
        int n = (length & 3) == 0 ? length >> 2 : (length >> 2) + 1;
        uint[] result = includeLength ? new uint[n + 1] : new uint[n];
        for (int i = 0; i < length; i++)
            result[i >> 2] |= (uint)data[i] << ((i & 3) << 3);
        if (includeLength)
            result[n] = (uint)length;
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
                throw new InvalidDataException("Invalid XXTEA data length.");
            n = length;
        }
        byte[] result = new byte[n];
        for (int i = 0; i < n; i++)
            result[i] = (byte)(data[i >> 2] >> ((i & 3) << 3));
        return result;
    }
}
