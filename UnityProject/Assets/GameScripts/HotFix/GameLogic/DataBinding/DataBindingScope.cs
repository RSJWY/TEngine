using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>
    /// 数据绑定订阅作用域。
    /// </summary>
    /// <remarks>
    /// 用于集中保存多个 Subscribe 返回的句柄，并在对象销毁或模块释放时一次性取消订阅。
    /// </remarks>
    public sealed class DataBindingScope : IDisposable
    {
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private bool _disposed;

        /// <summary>
        /// 将订阅句柄加入当前作用域。
        /// </summary>
        /// <returns>原订阅句柄，便于链式保存或调试。</returns>
        public T Add<T>(T subscription) where T : IDisposable
        {
            if (ReferenceEquals(subscription, null))
            {
                return default;
            }

            if (_disposed)
            {
                subscription.Dispose();
                return subscription;
            }

            _subscriptions.Add(subscription);
            return subscription;
        }

        /// <summary>
        /// 取消当前作用域中保存的所有订阅。
        /// </summary>
        public void Clear()
        {
            for (int i = _subscriptions.Count - 1; i >= 0; i--)
            {
                _subscriptions[i]?.Dispose();
            }

            _subscriptions.Clear();
        }

        /// <summary>
        /// 释放作用域并取消所有订阅。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Clear();
        }
    }
}
