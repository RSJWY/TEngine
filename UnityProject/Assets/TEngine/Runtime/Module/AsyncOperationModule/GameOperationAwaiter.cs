using System;
using System.Runtime.CompilerServices;

namespace TEngine
{
    /// <summary>
    /// 支持异步编程的自定义 Awaiter。
    /// <remarks>失败不视为异常，<see cref="GetResult"/> 不抛出异常，调用方应自行检查 <see cref="GameAsyncOperation.Status"/>。</remarks>
    /// </summary>
    public readonly struct GameOperationAwaiter : ICriticalNotifyCompletion
    {
        private readonly GameAsyncOperation _operation;

        /// <summary>
        /// 创建操作等待器实例。
        /// </summary>
        /// <param name="operation">要等待的操作。</param>
        public GameOperationAwaiter(GameAsyncOperation operation)
        {
            _operation = operation;
        }

        /// <inheritdoc />
        public bool IsCompleted => _operation.IsDone;

        /// <summary>
        /// 获取操作结果。
        /// </summary>
        public void GetResult()
        {
        }

        /// <inheritdoc />
        public void OnCompleted(Action continuation)
        {
            UnsafeOnCompleted(continuation);
        }

        /// <inheritdoc />
        public void UnsafeOnCompleted(Action continuation)
        {
            _operation.Completed += (op) => continuation();
        }
    }
}
