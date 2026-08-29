using System;
using System.IO;

namespace TEngine
{
    /// <summary>
    /// XOR 解密流：按文件位置取模异或密钥，配合 AssetBundle.LoadFromStream 流式加载。
    /// 只异或实际读到的字节，并按文件绝对位置取密钥字节。
    /// 并发安全：AssetBundle.LoadFromStream 的回调可能跨线程并发读同一流，
    /// 故对 Read/Seek/Position 加锁，保证“取位置→读底层→异或”整体原子。
    /// </summary>
    public sealed class XorStream : FileStream
    {
        private readonly byte[] _key;
        private readonly object _lock = new object();

        public XorStream(byte[] key, string path, FileMode mode, FileAccess access, FileShare share)
            : base(path, mode, access, share)
        {
            _key = key;
        }

        public override int Read(byte[] array, int offset, int count)
        {
            lock (_lock)
            {
                long filePos = Position;
                int read = base.Read(array, offset, count);
                for (int i = 0; i < read; i++)
                    array[offset + i] ^= _key[(filePos + i) % _key.Length];
                return read;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (_lock)
            {
                return base.Seek(offset, origin);
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
                }
            }
        }
    }
}
