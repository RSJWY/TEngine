using System.IO;

namespace TEngine
{
    /// <summary>
    /// XOR 解密流：按文件位置取模异或密钥，配合 AssetBundle.LoadFromStream 流式加载。
    /// 修正点：只异或实际读到的字节，并按文件绝对位置取密钥字节。
    /// </summary>
    public sealed class XorStream : FileStream
    {
        private readonly byte[] _key;

        public XorStream(byte[] key, string path, FileMode mode, FileAccess access, FileShare share)
            : base(path, mode, access, share)
        {
            _key = key;
        }

        public override int Read(byte[] array, int offset, int count)
        {
            long filePos = Position;
            int read = base.Read(array, offset, count);
            for (int i = 0; i < read; i++)
                array[offset + i] ^= _key[(filePos + i) % _key.Length];
            return read;
        }
    }
}
