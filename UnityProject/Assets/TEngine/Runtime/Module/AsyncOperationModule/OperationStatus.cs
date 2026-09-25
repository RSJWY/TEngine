using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 异步操作状态。
    /// </summary>
    public enum OperationStatus
    {
        /// <summary>
        /// 尚未启动。
        /// </summary>
        None = 0,

        /// <summary>
        /// 正在执行。
        /// </summary>
        Processing = 1,

        /// <summary>
        /// 已成功。
        /// </summary>
        Succeeded = 2,

        /// <summary>
        /// 已失败。
        /// </summary>
        Failed = 3,
    }

    /// <summary>
    /// 异步操作调试快照，供可视化监控组件读取。
    /// </summary>
    public sealed class AsyncOperationDebugInfo
    {
        /// <summary>
        /// 操作类型名。
        /// </summary>
        public string OperationName;

        /// <summary>
        /// 操作描述。
        /// </summary>
        public string Description;

        /// <summary>
        /// 所属调度器名称。
        /// </summary>
        public string SchedulerName;

        /// <summary>
        /// 优先级（值越大越优先）。
        /// </summary>
        public uint Priority;

        /// <summary>
        /// 当前进度（0-1）。
        /// </summary>
        public float Progress;

        /// <summary>
        /// 当前状态。
        /// </summary>
        public OperationStatus Status;

        /// <summary>
        /// 失败时的错误信息。
        /// </summary>
        public string Error;

        /// <summary>
        /// 已执行的帧数。
        /// </summary>
        public int ElapsedFrames;

        /// <summary>
        /// 已耗时（毫秒）。
        /// </summary>
        public long ElapsedMilliseconds;

        /// <summary>
        /// 是否被同步等待中（WaitForCompletion）。
        /// </summary>
        public bool IsWaitForCompletion;
    }

    /// <summary>
    /// 调度器调试快照。
    /// </summary>
    public sealed class OperationSchedulerDebugInfo
    {
        /// <summary>
        /// 调度器名称。
        /// </summary>
        public string Name;

        /// <summary>
        /// 调度器优先级。
        /// </summary>
        public uint Priority;

        /// <summary>
        /// 执行中的操作数。
        /// </summary>
        public int RunningCount;

        /// <summary>
        /// 待合并的操作数。
        /// </summary>
        public int PendingCount;

        /// <summary>
        /// 操作明细。
        /// </summary>
        public readonly List<AsyncOperationDebugInfo> Operations = new List<AsyncOperationDebugInfo>(16);
    }
}
