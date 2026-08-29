using System;
using System.IO;

namespace TEngine
{
    /// <summary>
    /// ChaCha20 解密流：keystream 以 64 字节块为单位按文件位置生成，支持随机寻址，
    /// 配合 AssetBundle.LoadFromStream 流式加载，无需整包进内存。
    /// 并发安全：对 Read/Seek/Position 加锁；位置变更时失效 keystream 缓存。
    /// </summary>
    public sealed class ChaCha20Stream : FileStream
    {
        private const int BlockSize = 64;
        private const int BlockBits = 6;

        private readonly byte[] _key;
        private readonly byte[] _nonce;
        private readonly byte[] _keystreamBlock = new byte[BlockSize];
        private long _cachedBlockIndex = -1;
        private readonly object _lock = new object();

        public ChaCha20Stream(byte[] key, byte[] nonce, string path, FileMode mode, FileAccess access, FileShare share)
            : base(path, mode, access, share)
        {
            _key = key;
            _nonce = nonce;
        }

        public override int Read(byte[] array, int offset, int count)
        {
            lock (_lock)
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

        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (_lock)
            {
                long newPos = base.Seek(offset, origin);
                // 位置变更后，缓存的 keystream 块不再保证有效。
                _cachedBlockIndex = -1;
                return newPos;
            }
        }

        public override long Position
        {
            get
            {
                lock (_lock)
                {
                    return base.Position;
                }
            }
            set
            {
                lock (_lock)
                {
                    base.Position = value;
                    // 位置变更后，缓存的 keystream 块不再保证有效。
                    _cachedBlockIndex = -1;
                }
            }
        }
    }
}
