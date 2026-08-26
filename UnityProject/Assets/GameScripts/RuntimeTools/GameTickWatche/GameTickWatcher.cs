using System.Diagnostics;
using TEngine;

namespace RuntimeTools
{
    /// <summary>
    /// 游戏逻辑计时器工具类。
    /// 用于测量某段代码的执行时间或两次调用之间的时间间隔，常用于性能分析、调试以及游戏循环中的耗时统计。
    /// 基于 System.Diagnostics.Stopwatch 实现，提供高精度（通常为微秒级或更高）的时间测量。
    /// </summary>
    public class GameTickWatcher
    {
        // 使用 Stopwatch 实例进行时间测量。
        // 声明为 readonly 表示该字段一旦在构造函数中初始化后就不会再被重新赋值，
        // 但 Stopwatch 对象本身的状态（如是否运行、已记录的时间）可以改变。
        private readonly Stopwatch m_stopwatch = new Stopwatch();

        /// <summary>
        /// 构造函数。
        /// 创建 GameTickWatcher 实例时立即启动计时器，因此实例创建后即可通过 ElapseTime() 获取自创建以来的时间。
        /// </summary>
        public GameTickWatcher() => m_stopwatch.Start();

        /// <summary>
        /// 重新启动计时器。
        /// 将计时器重置为零并立即开始计时，用于在每次需要重新测量时清零之前的累计时间。
        /// 典型场景：在游戏循环的每一帧开始时调用，以测量本帧逻辑的耗时。
        /// </summary>
        public void Restart() => m_stopwatch.Restart();

        /// <summary>
        /// 获取从计时器启动（或上次 Restart）以来经过的时间，单位为秒。
        /// 返回类型为 float 而非 double，是为了在游戏开发中平衡精度与内存/性能开销（float 占用 4 字节，通常足够用于表示秒级时间）。
        /// Stopwatch.Elapsed 属性返回 TimeSpan，通过 TotalSeconds 获取总秒数（包含小数部分）。
        /// </summary>
        /// <returns>经过的时间（秒，浮点数）</returns>
        public float ElapseTime() => (float)m_stopwatch.Elapsed.TotalSeconds;

        /// <summary>
        /// 通过日志系统输出当前经过的时间。
        /// 使用 DLogger.Info 记录一条信息级别的日志，内容格式为 "Used Time: X"（X 为秒数）。
        /// 便于在开发或调试过程中快速查看某段逻辑的耗时，无需手动拼接字符串。
        /// </summary>
        public void LogUsedTime() => Log.Info($"Used Time: {this.ElapseTime()}");

        /// <summary>
        /// 重写 ToString 方法，返回当前计时信息的字符串表示。
        /// 方便在调试器、日志或 UI 中直接输出该对象时获得可读的耗时信息。
        /// 格式与 LogUsedTime 输出的内容一致，但不会主动记录日志，仅返回字符串。
        /// </summary>
        /// <returns>形如 "Used Time: X" 的字符串</returns>
        public override string ToString() => $"Used Time: {this.ElapseTime()}";
    }
}