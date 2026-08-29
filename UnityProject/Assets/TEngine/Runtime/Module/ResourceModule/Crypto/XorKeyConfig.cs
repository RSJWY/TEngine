using UnityEngine;
using Random = UnityEngine.Random;
using Sirenix.OdinInspector;

namespace TEngine
{
    /// <summary>
    /// XOR 加密密钥配置：随机 16~128 字节 key，按文件位置取模使用。
    /// </summary>
    public class XorKeyConfig : CryptoKeyConfig<XorKeyConfig>
    {
        [SerializeField, HideInInspector]
        private byte[] _key;

        /// <summary>实际用于加解密的密钥字节。</summary>
        public byte[] key => _key;

        [ShowInInspector, LabelText("密钥（Hex）")]
        [InfoBox("XOR 密钥为 16~128 字节随机数据，按文件位置取模使用。修改后需重新打包全部资源。", InfoMessageType.None)]
        public string KeyHex
        {
            get => ToHex(_key);
            set => _key = ParseHex(value, _key);
        }

        [Button("重新生成密钥")]
        public override void RegenerateKey()
        {
            _key = CryptoUtils.GenerateRandomBytes(Random.Range(16, 129));
#if UNITY_EDITOR
            MarkDirty();
#endif
        }

        protected override void EnsureKey()
        {
            if (CryptoUtils.IsEmpty(_key))
            {
                _key = CryptoUtils.GenerateRandomBytes(32);
            }
        }
    }
}
