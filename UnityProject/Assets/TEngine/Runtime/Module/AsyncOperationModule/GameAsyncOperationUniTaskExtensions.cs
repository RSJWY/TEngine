using Cysharp.Threading.Tasks;
using System;
using System.Threading;

namespace TEngine
{
    /// <summary>
    /// <see cref="GameAsyncOperation"/> 的 UniTask 扩展。
    /// <remarks>提供 WithCancellation / ToUniTask / 进度上报等完整 UniTask 支持。</remarks>
    /// </summary>
    public static class GameAsyncOperationUniTaskExtensions
    {
        /// <summary>
        /// 直接 await 操作。等价于 ToUniTask()。
        /// </summary>
        public static UniTask.Awaiter GetAwaiter(this GameAsyncOperation operation)
        {
            return ToUniTask(operation).GetAwaiter();
        }

        /// <summary>
        /// 带取消令牌等待操作。
        /// <remarks>注意：此扩展只取消“等待”，操作本身会继续执行直到自然结束。
        /// 若需要“取消等待 = 中止操作”，请使用
        /// <see cref="IAsyncOperationModule.StartOperation(GameAsyncOperation, CancellationToken)"/> 提交操作。</remarks>
        /// </summary>
        public static UniTask WithCancellation(this GameAsyncOperation operation, CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return ToUniTask(operation, cancellationToken: cancellationToken, cancelImmediately: cancelImmediately);
        }

        /// <summary>
        /// 转换为 UniTask，支持进度上报与指定等待时机。
        /// </summary>
        public static UniTask ToUniTask(this GameAsyncOperation operation, IProgress<float> progress = null, PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            if (cancellationToken.IsCancellationRequested) return UniTask.FromCanceled(cancellationToken);
            if (operation.IsDone) return UniTask.CompletedTask;

            return new UniTask(ConfiguredSource.Create(operation, timing, progress, cancellationToken, cancelImmediately, out var token), token);
        }

        private sealed class ConfiguredSource : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<ConfiguredSource>
        {
            private static TaskPool<ConfiguredSource> _pool;
            private ConfiguredSource _nextNode;
            public ref ConfiguredSource NextNode => ref _nextNode;

            static ConfiguredSource()
            {
                TaskPool.RegisterSizeGetter(typeof(ConfiguredSource), () => _pool.Size);
            }

            private readonly Action<GameAsyncOperation> _completedCallback;
            private GameAsyncOperation _operation;
            private CancellationToken _cancellationToken;
            private CancellationTokenRegistration _cancellationTokenRegistration;
            private IProgress<float> _progress;
            private bool _cancelImmediately;
            private bool _completed;

            private UniTaskCompletionSourceCore<AsyncUnit> _core;

            private ConfiguredSource()
            {
                _completedCallback = HandleCompleted;
            }

            public static IUniTaskSource Create(GameAsyncOperation operation, PlayerLoopTiming timing, IProgress<float> progress, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!_pool.TryPop(out var result))
                {
                    result = new ConfiguredSource();
                }

                result._operation = operation;
                result._progress = progress;
                result._cancellationToken = cancellationToken;
                result._cancelImmediately = cancelImmediately;
                result._completed = false;

                if (cancelImmediately && cancellationToken.CanBeCanceled)
                {
                    result._cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(static state =>
                    {
                        var source = (ConfiguredSource)state;
                        source._core.TrySetCanceled(source._cancellationToken);
                    }, result);
                }

                TaskTracker.TrackActiveTask(result, 3);
                PlayerLoopHelper.AddAction(timing, result);

                operation.Completed += result._completedCallback;

                token = result._core.Version;
                return result;
            }

            private void HandleCompleted(GameAsyncOperation _)
            {
                if (_operation != null)
                {
                    _operation.Completed -= _completedCallback;
                }

                if (_completed) return;

                _completed = true;
                if (_cancellationToken.IsCancellationRequested)
                {
                    _core.TrySetCanceled(_cancellationToken);
                }
                else
                {
                    _core.TrySetResult(AsyncUnit.Default);
                }
            }

            public void GetResult(short token)
            {
                try
                {
                    _core.GetResult(token);
                }
                finally
                {
                    TryReturn();
                }
            }

            public UniTaskStatus GetStatus(short token)
            {
                return _core.GetStatus(token);
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                return _core.UnsafeGetStatus();
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                _core.OnCompleted(continuation, state, token);
            }

            public bool MoveNext()
            {
                if (_completed)
                {
                    return false;
                }

                if (_cancellationToken.IsCancellationRequested)
                {
                    _completed = true;
                    _core.TrySetCanceled(_cancellationToken);
                    return false;
                }

                if (_operation == null || _operation.IsDone)
                {
                    _completed = true;
                    _core.TrySetResult(AsyncUnit.Default);
                    return false;
                }

                if (_progress != null)
                {
                    _progress.Report(_operation.Progress);
                }

                return true;
            }

            private bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                _core.Reset();
                _operation = default;
                _progress = default;
                _cancellationToken = default;
                _cancellationTokenRegistration.Dispose();
                return _pool.TryPush(this);
            }
        }
    }
}
