using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 异步操作调度器。
    /// <remarks>双队列设计：新增操作先进 pending，下一次 Update 合并进 running。</remarks>
    /// </summary>
    internal class OperationScheduler : IComparable<OperationScheduler>
    {
        private readonly List<GameAsyncOperation> _runningOperations = new List<GameAsyncOperation>(100);
        private readonly List<GameAsyncOperation> _pendingOperations = new List<GameAsyncOperation>(100);
        private uint _priority;

        /// <summary>
        /// 调度器名称。
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// 调度器优先级（值越大越优先）。
        /// </summary>
        public uint Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    IsDirty = true;
                }
            }
        }

        /// <summary>
        /// 优先级是否已变更（需要重新排序）。
        /// </summary>
        public bool IsDirty { get; set; }

        /// <summary>
        /// 创建顺序（用于同优先级稳定排序）。
        /// </summary>
        public int CreationOrder { get; private set; }

        /// <summary>
        /// 创建异步操作调度器实例。
        /// </summary>
        public OperationScheduler(string name, int creationOrder)
        {
            Name = name;
            CreationOrder = creationOrder;
        }

        /// <summary>
        /// 开始处理异步操作。
        /// </summary>
        /// <remarks>操作立即启动，下一次 Update 时合并到执行队列。</remarks>
        public void StartOperation(GameAsyncOperation operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            _pendingOperations.Add(operation);
            operation.StartOperation();
        }

        /// <summary>
        /// 更新调度器。
        /// </summary>
        public void Update()
        {
            // 移除已经完成的异步操作
            for (int i = _runningOperations.Count - 1; i >= 0; i--)
            {
                var operation = _runningOperations[i];
                if (operation.IsCompleted)
                {
                    _runningOperations.RemoveAt(i);
                }
            }

            // 添加新增的异步操作
            if (_pendingOperations.Count > 0)
            {
                _runningOperations.AddRange(_pendingOperations);
                _pendingOperations.Clear();
            }

            // 检测是否需要执行排序
            bool isDirty = false;
            foreach (var operation in _runningOperations)
            {
                if (operation.IsDirty)
                {
                    operation.IsDirty = false;
                    isDirty = true;
                }
            }
            if (isDirty)
            {
                _runningOperations.Sort();
            }

            // 更新进行中的异步操作
            for (int i = 0; i < _runningOperations.Count; i++)
            {
                // 检查时间切片预算
                if (AsyncOperationModule.IsBusy)
                    break;

                var operation = _runningOperations[i];
                if (operation.IsCompleted)
                    continue;

                operation.UpdateOperation();
            }
        }

        /// <summary>
        /// 中止所有任务。
        /// </summary>
        public void AbortAll()
        {
            // 终止临时队列里的任务
            foreach (var operation in _pendingOperations)
            {
                operation.AbortOperation();
            }
            _pendingOperations.Clear();

            // 终止正在进行的任务
            foreach (var operation in _runningOperations)
            {
                operation.AbortOperation();
            }
            _runningOperations.Clear();
        }

        /// <summary>
        /// 获取调试快照。
        /// </summary>
        public OperationSchedulerDebugInfo GetDebugInfo()
        {
            var info = new OperationSchedulerDebugInfo();
            info.Name = Name;
            info.Priority = Priority;
            info.RunningCount = _runningOperations.Count;
            info.PendingCount = _pendingOperations.Count;

            foreach (var operation in _runningOperations)
            {
                info.Operations.Add(operation.GetDebugInfo(Name));
            }

            foreach (var operation in _pendingOperations)
            {
                info.Operations.Add(operation.GetDebugInfo(Name));
            }

            return info;
        }

        #region 排序接口实现
        /// <inheritdoc />
        public int CompareTo(OperationScheduler other)
        {
            // 优先级高的排前面
            int result = other.Priority.CompareTo(this.Priority);
            if (result == 0)
            {
                // 优先级相同，按创建顺序
                result = this.CreationOrder.CompareTo(other.CreationOrder);
            }
            return result;
        }
        #endregion
    }
}
