using UnityEngine;
using Random = UnityEngine.Random;
using Sirenix.OdinInspector;

namespace TEngine
{
    /// <summary>
    /// Bundle 加密用 XOR 密钥配置：随机 16~128 字节 key，按文件位置取模使用。
    /// <remarks>Editor only：密钥值通过烘焙脚本写入 <see cref="KeyStore"/>。</remarks>
    /// </summary>
    [CreateAssetMenu(menuName = "TEngine/加密密钥/Bundle Xor", fileName = "BundleXorKeyConfig")]
    public class BundleXorKeyConfig : CryptoKeyConfig<BundleXorKeyConfig>
    {
        [SerializeField, HideInInspector]
        private byte[] _key;

        /// <summary>实际用于加解密的密钥字节。</summary>
        public byte[] key => _key;

        [ShowInInspector, LabelText("密钥（Hex）")]
        [InfoBox("XOR 密钥为 16~128 字节随机数据，按文件位置取模使用。修改后需重新打包全部资源并烘焙密钥到代码。", InfoMessageType.None)]
        public string KeyHex
        {
            get => ToHex(_key);
            set => _key = ParseHex(value, _key);
        }

        [Button("重新生成密钥")]
        public override void RegenerateKey()
        {
            _key = GenerateRandomBytes(Random.Range(16, 129));
            MarkDirty();
        }

        protected override void EnsureKey()
        {
            if (IsEmpty(_key))
            {
                _key = GenerateRandomBytes(32);
            }
        }
    }
}
