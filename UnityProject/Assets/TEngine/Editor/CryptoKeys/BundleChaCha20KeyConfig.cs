using UnityEngine;

using Sirenix.OdinInspector;

namespace TEngine
{
    /// <summary>
    /// Bundle 加密用 ChaCha20 密钥配置：32 字节 key + 12 字节 nonce。
    /// 与 <see cref="ManifestChaCha20KeyConfig"/>（清单用）独立存放，避免从清单解密链路逆向到 Bundle 密钥。
    /// <remarks>Editor only：密钥值通过烘焙脚本写入 <see cref="KeyStore"/>。</remarks>
    /// </summary>
    [CreateAssetMenu(menuName = "TEngine/加密密钥/Bundle ChaCha20", fileName = "BundleChaCha20KeyConfig")]
    public class BundleChaCha20KeyConfig : CryptoKeyConfig<BundleChaCha20KeyConfig>
    {
        [SerializeField, HideInInspector]
        private byte[] _key;

        [SerializeField, HideInInspector]
        private byte[] _nonce;

        /// <summary>32 字节密钥。</summary>
        public byte[] key => _key;

        /// <summary>12 字节 nonce。</summary>
        public byte[] nonce => _nonce;

        [ShowInInspector, LabelText("密钥（Hex，32 字节）")]
        [InfoBox("ChaCha20 需要 32 字节 key + 12 字节 nonce。修改后需重新打包全部资源并烘焙密钥到代码。", InfoMessageType.None)]
        public string KeyHex
        {
            get => ToHex(_key);
            set => _key = ParseHex(value, _key, ChaCha20Util.KeyLength);
        }

        [ShowInInspector, LabelText("Nonce（Hex，12 字节）")]
        public string NonceHex
        {
            get => ToHex(_nonce);
            set => _nonce = ParseHex(value, _nonce, ChaCha20Util.NonceLength);
        }

        [Button("重新生成密钥")]
        public override void RegenerateKey()
        {
            _key = GenerateRandomBytes(ChaCha20Util.KeyLength);
            _nonce = GenerateRandomBytes(ChaCha20Util.NonceLength);
            MarkDirty();
        }

        protected override void EnsureKey()
        {
            if (_key == null || _key.Length != ChaCha20Util.KeyLength || IsEmpty(_key))
            {
                _key = GenerateRandomBytes(ChaCha20Util.KeyLength);
            }
            if (_nonce == null || _nonce.Length != ChaCha20Util.NonceLength || IsEmpty(_nonce))
            {
                _nonce = GenerateRandomBytes(ChaCha20Util.NonceLength);
            }
        }
    }
}
