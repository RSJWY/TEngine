using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace TEngine
{
    /// <summary>
    /// 模块级自定义异步操作基类。
    /// <remarks>由 <see cref="AsyncOperationModule"/> 的调度器驱动每帧更新；支持协程等待、async/await 等待与同步等待。</remarks>
    /// </summary>
    public abstract class GameAsyncOperation : IEnumerator, IComparable<GameAsyncOperation>
    {
        private List<GameAsyncOperation> _children;
        private Action<GameAsyncOperation> _completedCallback;
        private List<Action<GameAsyncOperation>> _completedCallbackList;
        private OperationStatus _status = OperationStatus.None;
        private string _error;
        private uint _priority;
        private int _startFrame;
        private long _startTimestamp;

        /// <summary>
        /// 标记脏（用于调度器检测并重排）。
        /// </summary>
        internal bool IsDirty { get; set; }

        /// <summary>
        /// 任务是否已结束（已触发回调和收尾）。
        /// </summary>
        internal bool IsCompleted { get; private set; }

        /// <summary>
        /// 是否正处于同步等待状态。
        /// </summary>
        protected bool IsWaitForCompletion { get; private set; }

        /// <summary>
        /// 当前帧时间切片是否已用完。
        /// <remarks>同步等待时始终返回 false，以确保操作能持续执行直到完成。</remarks>
        /// </summary>
        protected bool IsBusy
        {
            get
            {
                if (IsWaitForCompletion)
                    return false;
                return AsyncOperationModule.IsBusy;
            }
        }

        /// <summary>
        /// 任务优先级（值越大越优先执行）。
        /// </summary>
        public uint Priority
        {
            get => _priority;
            set
            {
                if (_priority == value)
                    return;
                _priority = value;
                IsDirty = true;
            }
        }

        /// <summary>
        /// 异步操作的处理进度（0f - 1f）。
        /// </summary>
        public float Progress { get; protected set; }

        /// <summary>
        /// 异步操作是否已结束。
        /// </summary>
        public bool IsDone => Status == OperationStatus.Succeeded || Status == OperationStatus.Failed;

        /// <summary>
        /// 操作失败时的错误描述。
        /// </summary>
        public string Error => _error;

        /// <summary>
        /// 异步操作的当前状态。
        /// </summary>
        public OperationStatus Status => _status;

        /// <summary>
        /// 异步操作的完成事件。
        /// <remarks>若注册时操作已完成，回调将立即执行。</remarks>
        /// </summary>
        public event Action<GameAsyncOperation> Completed
        {
            add
            {
                if (IsDone)
                {
                    try
                    {
                        if (value != null)
                            value.Invoke(this);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Exception in completion callback: {ex}.");
                    }
                }
                else
                {
                    if (_completedCallback == null)
                    {
                        _completedCallback = value;
                    }
                    else
                    {
                        if (_completedCallbackList == null)
                            _completedCallbackList = new List<Action<GameAsyncOperation>>(4);
                        _completedCallbackList.Add(value);
                    }
                }
            }
            remove
            {
                if (value == null)
                    return;

                if (_completedCallback == value)
                {
                    _completedCallback = null;
                }
                else if (_completedCallbackList != null)
                {
                    _completedCallbackList.Remove(value);
                }
            }
        }

        /// <summary>
        /// 同步等待异步执行完毕。
        /// <remarks>会在当前调用内循环执行直到操作完成；操作必须已提交到调度器或支持自驱动。</remarks>
        public void WaitForCompletion()
        {
            // 注意：防止异步操作被挂起陷入无限死循环
            if (_status == OperationStatus.None)
            {
                StartOperation();
            }

            if (IsWaitForCompletion == false)
            {
                IsWaitForCompletion = true;

                try
                {
                    if (IsDone == false)
                        InternalWaitForCompletion();

                    if (IsDone == false)
                    {
                        _error = $"Operation '{GetType().Name}' did not complete during synchronous wait.";
                        _status = OperationStatus.Failed;
                        Log.Error(_error);
                    }
                }
                catch (Exception ex)
                {
                    // 注意：如果同步等待已经通知到了业务层，那么无需再设置失败状态。
                    if (IsCompleted)
                    {
                        UnityEngine.Debug.LogException(ex);
                    }
                    else
                    {
                        _error = ex.ToString();
                        _status = OperationStatus.Failed;
                        Log.Error($"Exception in {GetType().Name}.InternalWaitForCompletion: {ex}.");
                    }
                }
                finally
                {
                    // 注意：强制收尾，确保回调能触发
                    CompleteOperation();
                }
            }
        }

        /// <summary>
        /// 开始异步操作。
        /// </summary>
        internal void StartOperation()
        {
            if (_status == OperationStatus.None)
            {
                _status = OperationStatus.Processing;
                _startFrame = UnityEngine.Time.frameCount;
                _startTimestamp = Stopwatch.GetTimestamp();

                try
                {
                    InternalStart();
                }
                catch (Exception ex)
                {
                    // 注意：无论子类是否已调用 SetResult/SetError，
                    // 内部逻辑抛出异常一律视为该异步任务失败。
                    _error = ex.ToString();
                    _status = OperationStatus.Failed;
                    Log.Error($"Exception in {GetType().Name}.InternalStart: {ex}.");
                }

                // 注意：同步完成的操作立即收尾
                if (IsDone)
                {
                    CompleteOperation();
                }
            }
        }

        /// <summary>
        /// 更新异步操作。
        /// </summary>
        internal void UpdateOperation()
        {
            if (IsDone == false)
            {
                try
                {
                    InternalUpdate();
                }
                catch (Exception ex)
                {
                    _error = ex.ToString();
                    _status = OperationStatus.Failed;
                    Log.Error($"Exception in {GetType().Name}.InternalUpdate: {ex}.");
                }
            }

            if (IsDone && IsCompleted == false)
            {
                CompleteOperation();
            }
        }

        /// <summary>
        /// 终止异步任务（递归中止所有子任务）。
        /// </summary>
        internal void AbortOperation()
        {
            // 终止所有子任务
            if (_children != null)
            {
                for (int i = _children.Count - 1; i >= 0; i--)
                {
                    var child = _children[i];
                    if (child.IsCompleted == false)
                        child.AbortOperation();
                }
            }

            if (IsDone == false)
            {
                _error = "Operation was aborted.";
                _status = OperationStatus.Failed;
                Log.Warning($"Async operation '{GetType().Name}' has been aborted.");
            }

            // 注意：强制收尾，确保回调能触发
            CompleteOperation();
        }

        /// <summary>
        /// 内部启动方法（子类必须实现）。
        /// <remarks>只负责发起异步请求，不要写阻塞逻辑，真正的等待交给 <see cref="InternalUpdate"/> 分帧推进。</remarks>
        /// </summary>
        protected abstract void InternalStart();

        /// <summary>
        /// 内部更新方法（子类必须实现）。
        /// </summary>
        protected abstract void InternalUpdate();

        /// <summary>
        /// 内部释放方法（子类可选实现）。
        /// <remarks>统一的清理入口，无论操作是成功、失败还是被中止都会执行。</remarks>
        /// </summary>
        protected virtual void InternalDispose()
        {
        }

        /// <summary>
        /// 获取操作的描述信息（子类可选实现）。
        /// </summary>
        protected virtual string InternalGetDescription()
        {
            return string.Empty;
        }

        /// <summary>
        /// 内部同步等待方法（子类可选实现）。
        /// <remarks>默认循环驱动自身直到完成；子类可重写以支持更高效的同步等待。</remarks>
        /// </summary>
        protected virtual void InternalWaitForCompletion()
        {
            while (IsDone == false)
            {
                UpdateOperation();

                // 注意：短暂休眠避免完全占用CPU资源
                System.Threading.Thread.Sleep(1);
            }
        }

        /// <summary>
        /// 将操作标记为成功完成。
        /// </summary>
        protected void SetResult()
        {
            if (IsDone)
                throw new InvalidOperationException(
                    $"Operation '{GetType().Name}' has already completed and cannot transition to another final state.");
            _status = OperationStatus.Succeeded;
        }

        /// <summary>
        /// 将操作标记为失败。
        /// </summary>
        /// <param name="error">错误描述。</param>
        protected void SetError(string error)
        {
            if (IsDone)
                throw new InvalidOperationException(
                    $"Operation '{GetType().Name}' has already completed and cannot transition to another final state.");
            _error = error;
            _status = OperationStatus.Failed;
        }

        /// <summary>
        /// 计算多阶段操作的整体进度。
        /// </summary>
        /// <param name="stageIndex">当前阶段索引（从0开始）。</param>
        /// <param name="stageCount">阶段总数。</param>
        /// <param name="remaining">当前阶段剩余工作量。</param>
        /// <param name="total">当前阶段总工作量。</param>
        protected float CalculateMultiStageProgress(int stageIndex, int stageCount, int remaining, int total)
        {
            if (total <= 0)
                return (stageIndex + 1f) / stageCount;
            float stageProgress = 1f - remaining / (float)total;
            return (stageIndex + stageProgress) / stageCount;
        }

        /// <summary>
        /// 计算多阶段操作的整体进度。
        /// </summary>
        /// <param name="stageIndex">当前阶段索引（从0开始）。</param>
        /// <param name="stageCount">阶段总数。</param>
        /// <param name="stageProgress">当前阶段进度（0-1）。</param>
        protected float CalculateMultiStageProgress(int stageIndex, int stageCount, float stageProgress)
        {
            if (stageProgress < 0f)
                stageProgress = 0f;
            else if (stageProgress > 1f)
                stageProgress = 1f;
            return (stageIndex + stageProgress) / stageCount;
        }

        /// <summary>
        /// 添加子任务。子任务会随父任务一起被中止。
        /// </summary>
        /// <param name="child">要添加的子任务。</param>
        protected void AddChildOperation(GameAsyncOperation child)
        {
            if (_children == null)
                _children = new List<GameAsyncOperation>(10);

#if UNITY_EDITOR || DEBUG
            if (child == null)
                throw new GameFrameworkException("Child operation is null.");

            if (ReferenceEquals(child, this))
                throw new GameFrameworkException("Cannot add operation as its own child.");

            if (_children.Contains(child))
                throw new GameFrameworkException($"Child operation '{child.GetType().Name}' already exists.");

            // 禁止形成环依赖
            if (WouldCreateCycle(child))
                throw new GameFrameworkException($"Adding '{child.GetType().Name}' would create a circular dependency with '{GetType().Name}'.");
#endif

            _children.Add(child);
        }

        /// <summary>
        /// 移除子任务。
        /// </summary>
        /// <param name="child">要移除的子任务。</param>
        protected void RemoveChildOperation(GameAsyncOperation child)
        {
            if (_children == null)
                return;

#if UNITY_EDITOR || DEBUG
            if (child == null)
                throw new GameFrameworkException("Child operation is null.");

            if (_children.Contains(child) == false)
                throw new GameFrameworkException($"Child operation '{child.GetType().Name}' not found.");
#endif

            _children.Remove(child);
        }

        /// <summary>
        /// 执行一次更新逻辑。
        /// </summary>
        protected void ExecuteOnce()
        {
            if (IsDone)
                return;

            UpdateOperation();
        }

        /// <summary>
        /// 批量执行一定次数的更新逻辑。
        /// </summary>
        /// <param name="count">最大执行次数，默认1000次。</param>
        /// <remarks>用于需要快速完成但又不想完全阻塞主线程的场景。</remarks>
        protected void ExecuteBatch(int count = 1000)
        {
            if (IsDone)
                return;

            int runCount = count;
            while (true)
            {
                UpdateOperation();
                if (IsDone)
                    break;

                runCount--;
                if (runCount <= 0)
                    break;
            }
        }

        /// <summary>
        /// 循环执行更新逻辑直到操作完成。
        /// </summary>
        /// <param name="sleepMilliseconds">每次循环后的休眠时长（毫秒）。</param>
        /// <remarks>该方法会阻塞调用线程。</remarks>
        protected void ExecuteUntilComplete(int sleepMilliseconds = 1)
        {
            if (IsDone)
                return;

            while (true)
            {
                UpdateOperation();
                if (IsDone)
                    break;

                System.Threading.Thread.Sleep(sleepMilliseconds);
            }
        }

        /// <summary>
        /// 完成异步任务（触发回调并收尾）。
        /// </summary>
        private void CompleteOperation()
        {
            if (IsCompleted == false)
            {
                IsCompleted = true;
                Progress = 1f;

                try
                {
                    InternalDispose();
                }
                catch (Exception ex)
                {
                    Log.Error($"Exception in {GetType().Name}.InternalDispose: {ex}.");
                }

                InvokeCompletedCallbacks();
            }
        }

        /// <summary>
        /// 触发所有已注册的完成回调并清空。
        /// </summary>
        private void InvokeCompletedCallbacks()
        {
            if (_completedCallback != null)
            {
                try
                {
                    _completedCallback.Invoke(this);
                }
                catch (Exception ex)
                {
                    Log.Error($"Exception in completion callback: {ex}.");
                }
                _completedCallback = null;
            }

            if (_completedCallbackList != null)
            {
                for (int i = 0; i < _completedCallbackList.Count; i++)
                {
                    try
                    {
                        _completedCallbackList[i].Invoke(this);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Exception in completion callback: {ex}.");
                    }
                }
                _completedCallbackList = null;
            }
        }

        /// <summary>
        /// 检测添加子任务是否会形成循环依赖。
        /// </summary>
        private bool WouldCreateCycle(GameAsyncOperation child)
        {
            const int MaxCycleCheckDepth = 4096;
            var stack = new Stack<GameAsyncOperation>();
            var visited = new HashSet<GameAsyncOperation>();
            stack.Push(child);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node == null)
                    continue;

                if (visited.Add(node) == false)
                    continue;

                if (visited.Count > MaxCycleCheckDepth)
                    throw new GameFrameworkException("Child operation graph is too large, cycle check aborted.");

                if (ReferenceEquals(node, this))
                    return true;

                if (node._children == null)
                    continue;

                for (int i = 0; i < node._children.Count; i++)
                {
                    stack.Push(node._children[i]);
                }
            }

            return false;
        }

        /// <summary>
        /// 获取调试快照。
        /// </summary>
        internal AsyncOperationDebugInfo GetDebugInfo(string schedulerName)
        {
            var info = new AsyncOperationDebugInfo();
            info.OperationName = GetType().Name;
            info.Description = InternalGetDescription();
            info.SchedulerName = schedulerName;
            info.Priority = Priority;
            info.Progress = Progress;
            info.Status = Status;
            info.Error = Error;
            info.IsWaitForCompletion = IsWaitForCompletion;

            if (_status != OperationStatus.None)
            {
                info.ElapsedFrames = UnityEngine.Time.frameCount - _startFrame;
                info.ElapsedMilliseconds = (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency;
            }

            return info;
        }

        #region 排序接口实现
        /// <inheritdoc />
        public int CompareTo(GameAsyncOperation other)
        {
            return other.Priority.CompareTo(this.Priority);
        }
        #endregion

        #region 异步编程相关
        /// <summary>
        /// 获取用于 async/await 的等待器。
        /// </summary>
        public GameOperationAwaiter GetAwaiter()
        {
            return new GameOperationAwaiter(this);
        }
        #endregion

        #region IEnumerator 实现（支持 yield return operation）
        /// <inheritdoc />
        bool IEnumerator.MoveNext()
        {
            return !IsDone;
        }

        /// <inheritdoc />
        void IEnumerator.Reset()
        {
        }

        /// <inheritdoc />
        object IEnumerator.Current => null;
        #endregion
    }
}
