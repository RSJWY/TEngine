using System.Collections.Generic;
using System.Threading;

namespace TEngine
{
    /// <summary>
    /// 自定义异步操作模块接口。
    /// </summary>
    public interface IAsyncOperationModule
    {
        /// <summary>
        /// 全局调度器名称。
        /// </summary>
        string GlobalSchedulerName { get; }

        /// <summary>
        /// 每帧最大执行预算（毫秒）。小于最小值会被钳制。
        /// </summary>
        long MaxTimeSlice { get; set; }

        /// <summary>
        /// 将异步操作提交到全局调度器执行。
        /// </summary>
        /// <param name="operation">要执行的异步操作。</param>
        void StartOperation(GameAsyncOperation operation);

        /// <summary>
        /// 将异步操作提交到全局调度器执行，并将操作生命周期绑定到取消令牌。
        /// </summary>
        /// <param name="operation">要执行的异步操作。</param>
        /// <param name="cancellationToken">取消令牌；令牌触发时操作会被立即中止。</param>
        /// <remarks>
        /// <b>独占要求</b>：该重载会把 <c>token</c> 取消直接转换为 <see cref="GameAsyncOperation"/> 的 <c>Abort()</c>，
        /// 因此传入的操作实例必须由调用方独占：不要把它同时提交给其它调度器，不要让多处代码共享同一个等待。
        /// 否则任一处取消都会让所有等待方收到 <c>Failed("Operation was aborted.")</c>。
        /// </remarks>
        void StartOperation(GameAsyncOperation operation, CancellationToken cancellationToken);

        /// <summary>
        /// 将异步操作提交到指定调度器执行。
        /// </summary>
        /// <param name="schedulerName">调度器名称；不存在时抛出异常。</param>
        /// <param name="operation">要执行的异步操作。</param>
        void StartOperation(string schedulerName, GameAsyncOperation operation);

        /// <summary>
        /// 将异步操作提交到指定调度器执行，并将操作生命周期绑定到取消令牌。
        /// </summary>
        /// <param name="schedulerName">调度器名称；不存在时抛出异常。</param>
        /// <param name="operation">要执行的异步操作。</param>
        /// <param name="cancellationToken">取消令牌；令牌触发时操作会被立即中止。</param>
        /// <remarks>同 <see cref="StartOperation(GameAsyncOperation, CancellationToken)"/> 的独占要求。</remarks>
        void StartOperation(string schedulerName, GameAsyncOperation operation, CancellationToken cancellationToken);

        /// <summary>
        /// 创建指定名称的调度器。
        /// </summary>
        /// <param name="schedulerName">调度器名称。</param>
        /// <param name="priority">调度器优先级。</param>
        void CreateScheduler(string schedulerName, uint priority = 0);

        /// <summary>
        /// 销毁指定调度器并中止其上所有操作。
        /// </summary>
        /// <param name="schedulerName">调度器名称。</param>
        void DestroyScheduler(string schedulerName);

        /// <summary>
        /// 中止并清空指定调度器上所有正在执行的操作。
        /// </summary>
        /// <param name="schedulerName">调度器名称。</param>
        void AbortSchedulerOperations(string schedulerName);

        /// <summary>
        /// 获取所有调度器的调试快照。
        /// </summary>
        List<OperationSchedulerDebugInfo> GetDebugInfo();
    }
}
