using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>
    /// 自定义数据绑定信号。
    /// </summary>
    /// <remarks>
    /// 适合按钮按下、一次性触发、状态边沿这类“发生过一次”的数据，不适合表达持续状态。
    /// </remarks>
    public sealed class DataBindingSignal
    {
        private readonly List<Action> _listeners = new List<Action>();
        private int _notifying;

        /// <summary>
        /// 订阅信号。
        /// </summary>
        /// <param name="listener">信号触发回调。</param>
        /// <returns>取消订阅句柄。</returns>
        public IDisposable Subscribe(Action listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            _listeners.Add(listener);
            return new Subscription(this, listener);
        }

        /// <summary>
        /// 触发信号。
        /// </summary>
        public void Emit()
        {
            _notifying++;
            int listenerCount = _listeners.Count;
            for (int i = 0; i < listenerCount; i++)
            {
                _listeners[i]?.Invoke();
            }
            _notifying--;

            if (_notifying == 0)
            {
                _listeners.RemoveAll(listener => listener == null);
            }
        }

        private void Unsubscribe(Action listener)
        {
            if (_notifying > 0)
            {
                for (int i = 0; i < _listeners.Count; i++)
                {
                    if (_listeners[i] == listener)
                    {
                        _listeners[i] = null;
                        return;
                    }
                }

                return;
            }

            _listeners.Remove(listener);
        }

        private sealed class Subscription : IDisposable
        {
            private DataBindingSignal _owner;
            private Action _listener;

            public Subscription(DataBindingSignal owner, Action listener)
            {
                _owner = owner;
                _listener = listener;
            }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                _owner.Unsubscribe(_listener);
                _owner = null;
                _listener = null;
            }
        }
    }
}
