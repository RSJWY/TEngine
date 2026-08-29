using System.IO;

namespace TEngine
{
    /// <summary>
    /// ChaCha20 解密流：keystream 以 64 字节块为单位按文件位置生成，支持随机寻址，
    /// 配合 AssetBundle.LoadFromStream 流式加载，无需整包进内存。
    /// </summary>
    public sealed class ChaCha20Stream : FileStream
    {
        private const int BlockSize = 64;
        private const int BlockBits = 6;

        private readonly byte[] _key;
        private readonly byte[] _nonce;
        private readonly byte[] _keystreamBlock = new byte[BlockSize];
        private long _cachedBlockIndex = -1;

        public ChaCha20Stream(byte[] key, byte[] nonce, string path, FileMode mode, FileAccess access, FileShare share)
            : base(path, mode, access, share)
        {
            _key = key;
            _nonce = nonce;
        }

        public override int Read(byte[] array, int offset, int count)
        {
            long filePos = Position;
            int read = base.Read(array, offset, count);
            for (int i = 0; i < read; i++)
            {
                long pos = filePos + i;
                long blockIndex = pos >> BlockBits;
                int inBlock = (int)(pos & (BlockSize - 1));
                if (blockIndex != _cachedBlockIndex)
                {
                    ChaCha20Util.GenerateBlock((uint)blockIndex, _key, _nonce, _keystreamBlock);
                    _cachedBlockIndex = blockIndex;
                }
                array[offset + i] ^= _keystreamBlock[inBlock];
            }
            return read;
        }
    }
}
