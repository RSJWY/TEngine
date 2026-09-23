using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace TEngine
{
    /// <summary>
    /// 自定义异步操作模块。
    /// <remarks>把业务自定义的异步流程交给本模块的调度器驱动；由 <see cref="ModuleSystem"/> 每帧轮询。</remarks>
    /// </summary>
    internal class AsyncOperationModule : Module, IUpdateModule, IAsyncOperationModule
    {
        private const long MinTimeSlice = 10; // 最小时间片（毫秒）
        private const string GlobalSchedulerKey = "TEngineAsyncOperationGlobalScheduler";

        private static AsyncOperationModule _instance;
        private static Stopwatch _stopwatch;
        private static long _frameStartTime;
        private static long _maxTimeSlice = long.MaxValue;

        private readonly Dictionary<string, OperationScheduler> _schedulerDict = new Dictionary<string, OperationScheduler>(8);
        private readonly List<OperationScheduler> _schedulerList = new List<OperationScheduler>(8);
        private readonly List<OperationSchedulerDebugInfo> _debugInfoCache = new List<OperationSchedulerDebugInfo>(8);
        private int _nextCreationOrder;

        /// <summary>
        /// 当前帧的时间切片预算是否已用完。
        /// </summary>
        internal static bool IsBusy
        {
            get
            {
                if (_stopwatch == null)
                    return false;

                if (_maxTimeSlice == long.MaxValue)
                    return false;

                return _stopwatch.ElapsedMilliseconds - _frameStartTime >= _maxTimeSlice;
            }
        }

        /// <inheritdoc />
        public string GlobalSchedulerName => GlobalSchedulerKey;

        /// <inheritdoc />
        public long MaxTimeSlice
        {
            get => _maxTimeSlice;
            set
            {
                if (value < MinTimeSlice)
                {
                    _maxTimeSlice = MinTimeSlice;
                    Log.Warning($"MaxTimeSlice must be at least {MinTimeSlice} ms, clamped to {MinTimeSlice}.");
                }
                else
                {
                    _maxTimeSlice = value;
                }
            }
        }

        public override int Priority => 70;

        public override void OnInit()
        {
            _instance = this;

            _stopwatch = Stopwatch.StartNew();
            _frameStartTime = 0;
            _maxTimeSlice = long.MaxValue;
            _nextCreationOrder = 0;

            // 创建全局调度器
            var scheduler = new OperationScheduler(GlobalSchedulerKey, _nextCreationOrder++);
            _schedulerDict.Add(GlobalSchedulerKey, scheduler);
            _schedulerList.Add(scheduler);
        }

        public override void Shutdown()
        {
            foreach (var scheduler in _schedulerList)
            {
                scheduler.AbortAll();
            }
            _schedulerDict.Clear();
            _schedulerList.Clear();
            _debugInfoCache.Clear();

            _stopwatch = null;
            _frameStartTime = 0;
            _maxTimeSlice = long.MaxValue;
            _instance = null;
        }

        /// <inheritdoc />
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            // 更新帧时间
            _frameStartTime = _stopwatch.ElapsedMilliseconds;

            // 检测是否需要执行排序
            bool isDirty = false;
            for (int i = 0; i < _schedulerList.Count; i++)
            {
                if (_schedulerList[i].IsDirty)
                {
                    _schedulerList[i].IsDirty = false;
                    isDirty = true;
                }
            }
            if (isDirty)
            {
                _schedulerList.Sort();
            }

            // 更新调度器
            for (int i = 0; i < _schedulerList.Count; i++)
            {
                if (IsBusy)
                    break;

                _schedulerList[i].Update();
            }
        }

        /// <inheritdoc />
        public void StartOperation(GameAsyncOperation operation)
        {
            StartOperation(GlobalSchedulerKey, operation);
        }

        /// <inheritdoc />
        public void StartOperation(GameAsyncOperation operation, CancellationToken cancellationToken)
        {
            StartOperation(GlobalSchedulerKey, operation, cancellationToken);
        }

        /// <inheritdoc />
        public void StartOperation(string schedulerName, GameAsyncOperation operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            var scheduler = GetScheduler(schedulerName);
            scheduler.StartOperation(operation);
        }

        /// <inheritdoc />
        public void StartOperation(string schedulerName, GameAsyncOperation operation, CancellationToken cancellationToken)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            // 注意：独占要求。token 取消即中止操作本体，共享等待会收到 aborted。
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(static op => ((GameAsyncOperation)op).AbortOperation(), operation);
            }

            StartOperation(schedulerName, operation);
        }

        /// <inheritdoc />
        public void CreateScheduler(string schedulerName, uint priority = 0)
        {
            if (string.IsNullOrWhiteSpace(schedulerName))
                throw new GameFrameworkException("Scheduler name is null or empty.");

            if (_schedulerDict.ContainsKey(schedulerName))
                throw new GameFrameworkException($"Operation scheduler already exists: '{schedulerName}'.");

            var scheduler = new OperationScheduler(schedulerName, _nextCreationOrder++);
            _schedulerDict.Add(schedulerName, scheduler);
            _schedulerList.Add(scheduler);
            scheduler.Priority = priority;
        }

        /// <inheritdoc />
        public void DestroyScheduler(string schedulerName)
        {
            if (schedulerName == GlobalSchedulerKey)
                throw new GameFrameworkException("Cannot destroy the global scheduler.");

            if (_schedulerDict.TryGetValue(schedulerName, out var scheduler))
            {
                scheduler.AbortAll();
                _schedulerDict.Remove(schedulerName);
                _schedulerList.Remove(scheduler);
            }
        }

        /// <inheritdoc />
        public void AbortSchedulerOperations(string schedulerName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.AbortAll();
        }

        /// <inheritdoc />
        public List<OperationSchedulerDebugInfo> GetDebugInfo()
        {
            _debugInfoCache.Clear();
            foreach (var scheduler in _schedulerList)
            {
                _debugInfoCache.Add(scheduler.GetDebugInfo());
            }
            return _debugInfoCache;
        }

        private OperationScheduler GetScheduler(string schedulerName)
        {
            if (_schedulerDict.TryGetValue(schedulerName, out var scheduler))
            {
                return scheduler;
            }

            throw new GameFrameworkException($"Operation scheduler not found: '{schedulerName}'.");
        }
    }
}
