using System.Collections.Concurrent;
using TouchSocket.Core;

namespace TEngine
{
    /// <summary>
    /// 早期日志缓冲区。
    /// </summary>
    /// <remarks>
    /// <para><see cref="UnityLoggerBridge"/> 在 <c>BeforeSplashScreen</c> 才订阅 <c>Application.logMessageReceivedThreaded</c>，
    /// 此前（如 <c>AfterAssembliesLoaded</c> 阶段）输出的日志只会进 Unity Console / Player.log，
    /// 不会被 TouchSocket 文件日志捕获。</para>
    /// <para>需要在这一极早阶段留痕的代码（如 Obfuz 密钥初始化）请使用 <see cref="Log.EarlyInfo"/> 等专用 API：
    /// 日志先经 <see cref="GameFrameworkLog"/> 正常输出到 Console，同时在本缓冲区留一份副本，
    /// 待 <see cref="UnityLoggerBridge.Init"/> 完成后由 <see cref="Flush"/> 补写到文件日志。</para>
    /// <para>仅建议启动早期、低频使用；正常运行期请直接使用 <see cref="Log.Info"/> 等常规 API。</para>
    /// </remarks>
    public static class EarlyLogBuffer
    {
        /// <summary>
        /// 缓冲区最大容量，超出后丢弃最早的条目，防止极端情况下内存膨胀。
        /// </summary>
        private const int MAX_CAPACITY = 256;

        private readonly struct Entry
        {
            public readonly LogLevel Level;
            public readonly string Message;

            public Entry(LogLevel level, string message)
            {
                Level = level;
                Message = message;
            }
        }

        private static readonly ConcurrentQueue<Entry> s_Queue = new ConcurrentQueue<Entry>();

        /// <summary>
        /// 缓冲区是否已关闭。<c>true</c> 表示 <see cref="Flush"/> 已执行过（或 <see cref="Reset"/> 重置前已关闭），
        /// 此后 <see cref="Enqueue"/> 直接丢弃，避免日志在文件里重复落盘。
        /// </summary>
        private static volatile bool s_Closed;

        /// <summary>
        /// 缓冲中是否还有未落盘的早期日志。
        /// </summary>
        public static bool HasPending => !s_Queue.IsEmpty;

        /// <summary>
        /// 往缓冲区追加一条早期日志。线程安全，可在任意初始化阶段调用。
        /// 缓冲区关闭后（日志桥接已就绪）调用为无操作，此时日志已通过正常路径落盘。
        /// </summary>
        internal static void Enqueue(LogLevel level, string message)
        {
            if (s_Closed)
            {
                return;
            }

            s_Queue.Enqueue(new Entry(level, message));
            while (s_Queue.Count > MAX_CAPACITY && s_Queue.TryDequeue(out _))
            {
                // 丢弃最旧条目，保证容量上限。
            }
        }

        /// <summary>
        /// 将缓冲的早期日志补写到文件日志，并关闭缓冲区。
        /// 由 <see cref="UnityLoggerBridge.Init"/> 在 <see cref="FileLogger"/> 就绪后调用。
        /// </summary>
        /// <param name="fileLogger">已初始化的文件日志器；为 <c>null</c> 时仅清空缓冲区。</param>
        internal static void Flush(FileLogger fileLogger)
        {
            s_Closed = true;
            while (s_Queue.TryDequeue(out Entry entry))
            {
                try
                {
                    // 加 [Early] 前缀标识这是补写的早期日志（落盘时间晚于实际发生时间）。
                    fileLogger?.Log(entry.Level, "Unity", string.Concat("[Early] ", entry.Message), null);
                }
                catch
                {
                    // 补写失败时静默吞掉，避免日志系统自身递归。
                }
            }
        }

        /// <summary>
        /// 清空缓冲区并重新开放，不补写。由 <see cref="UnityLoggerBridge.Shutdown"/> 调用，
        /// 兼容关闭 Domain Reload 的编辑器播放模式，防止上次会话残留。
        /// </summary>
        internal static void Reset()
        {
            s_Closed = false;
            while (s_Queue.TryDequeue(out _))
            {
            }
        }
    }
}
