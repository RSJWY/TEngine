using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 资源清单 ChaCha20 加密器（构建期使用）。
    /// 与 <see cref="ManifestChaCha20Decryptor"/> 配对，密钥来自 <see cref="KeyStore"/>（与 Bundle 密钥相互独立）。
    /// </summary>
    public sealed class ManifestChaCha20Encryptor : IManifestEncryptor
    {
        byte[] IManifestEncryptor.Encrypt(byte[] fileData)
        {
            if (fileData == null || fileData.Length == 0)
                return fileData;
            return ChaCha20Util.Encrypt(fileData, KeyStore.ManifestChaCha20Key, KeyStore.ManifestChaCha20Nonce);
        }
    }

    /// <summary>
    /// 资源清单 ChaCha20 解密器（运行时使用）。
    /// 与 <see cref="ManifestChaCha20Encryptor"/> 配对，密钥来自 <see cref="KeyStore"/>（与 Bundle 密钥相互独立）。
    /// </summary>
    public sealed class ManifestChaCha20Decryptor : IManifestDecryptor
    {
        byte[] IManifestDecryptor.Decrypt(byte[] fileData)
        {
            if (fileData == null || fileData.Length == 0)
                return fileData;
            return ChaCha20Util.Decrypt(fileData, KeyStore.ManifestChaCha20Key, KeyStore.ManifestChaCha20Nonce);
        }
    }
}
