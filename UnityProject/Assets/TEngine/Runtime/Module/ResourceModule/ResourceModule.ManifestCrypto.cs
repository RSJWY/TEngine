using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 资源清单 ChaCha20 加密器（构建期使用）。
    /// 与 <see cref="ManifestChaCha20Decryptor"/> 配对，密钥来自 <see cref="ManifestChaCha20KeyConfig"/>。
    /// </summary>
    public sealed class ManifestChaCha20Encryptor : IManifestEncryptor
    {
        byte[] IManifestEncryptor.Encrypt(byte[] fileData)
        {
            if (fileData == null || fileData.Length == 0)
                return fileData;
            var config = ManifestChaCha20KeyConfig.Instance;
            return ChaCha20Util.Encrypt(fileData, config.key, config.nonce);
        }
    }

    /// <summary>
    /// 资源清单 ChaCha20 解密器（运行时使用）。
    /// 与 <see cref="ManifestChaCha20Encryptor"/> 配对，密钥来自 <see cref="ManifestChaCha20KeyConfig"/>。
    /// </summary>
    public sealed class ManifestChaCha20Decryptor : IManifestDecryptor
    {
        byte[] IManifestDecryptor.Decrypt(byte[] fileData)
        {
            if (fileData == null || fileData.Length == 0)
                return fileData;
            var config = ManifestChaCha20KeyConfig.Instance;
            return ChaCha20Util.Decrypt(fileData, config.key, config.nonce);
        }
    }
}
