using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>
    /// 数据绑定属性的非泛型接口，用于统一 Flush 脏数据。
    /// </summary>
    public interface IDataBindingProperty
    {
        /// <summary>
        /// 当前值是否已变化但尚未通知订阅方。
        /// </summary>
        bool IsDirty { get; }

        /// <summary>
        /// 如果存在脏数据，则通知订阅方。
        /// </summary>
        bool Flush();
    }

    /// <summary>
    /// 自定义数据绑定值。
    /// </summary>
    /// <typeparam name="T">绑定值类型。</typeparam>
    /// <remarks>
    /// 高频数据推荐使用 <see cref="SetDirty(T)"/> 标脏，再在合适时机调用 <see cref="Flush"/> 合批通知。
    /// </remarks>
    public sealed class DataBindingProperty<T> : IDataBindingProperty
    {
        private readonly List<Action<T>> _listeners = new List<Action<T>>();
        private T _value;
        private bool _hasValue;
        private bool _isDirty;
        private int _notifying;

        /// <summary>
        /// 当前绑定值。
        /// </summary>
        public T Value => _value;

        /// <summary>
        /// 是否已经写入过值。
        /// </summary>
        public bool HasValue => _hasValue;

        /// <inheritdoc />
        public bool IsDirty => _isDirty;

        /// <summary>
        /// 订阅值变化。
        /// </summary>
        /// <param name="listener">值变化回调。</param>
        /// <param name="notifyNow">已有值时是否立即回调当前值。</param>
        /// <returns>取消订阅句柄。</returns>
        public IDisposable Subscribe(Action<T> listener, bool notifyNow = true)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            _listeners.Add(listener);

            if (notifyNow && _hasValue)
            {
                listener(_value);
            }

            return new Subscription(this, listener);
        }

        /// <summary>
        /// 设置值并立即通知订阅方。
        /// </summary>
        public bool Set(T value)
        {
            if (!SetDirty(value))
            {
                return false;
            }

            Flush();
            return true;
        }

        /// <summary>
        /// 使用自定义相等判断设置值并立即通知订阅方。
        /// </summary>
        public bool Set(T value, Func<T, T, bool> equals)
        {
            if (!SetDirty(value, equals))
            {
                return false;
            }

            Flush();
            return true;
        }

        /// <summary>
        /// 设置值并标记为脏，不立即通知订阅方。
        /// </summary>
        public bool SetDirty(T value)
        {
            return SetDirty(value, EqualityComparer<T>.Default.Equals);
        }

        /// <summary>
        /// 使用自定义相等判断设置值并标记为脏，不立即通知订阅方。
        /// </summary>
        public bool SetDirty(T value, Func<T, T, bool> equals)
        {
            if (equals == null)
            {
                throw new ArgumentNullException(nameof(equals));
            }

            if (_hasValue && equals(_value, value))
            {
                return false;
            }

            _value = value;
            _hasValue = true;
            _isDirty = true;
            return true;
        }

        /// <summary>
        /// 静默写入值，不标脏、不通知。
        /// </summary>
        public void SetSilently(T value)
        {
            _value = value;
            _hasValue = true;
            _isDirty = false;
        }

        /// <inheritdoc />
        public bool Flush()
        {
            if (!_isDirty)
            {
                return false;
            }

            _isDirty = false;
            Notify(_value);
            return true;
        }

        private void Notify(T value)
        {
            _notifying++;
            int listenerCount = _listeners.Count;
            for (int i = 0; i < listenerCount; i++)
            {
                _listeners[i]?.Invoke(value);
            }
            _notifying--;

            if (_notifying == 0)
            {
                _listeners.RemoveAll(listener => listener == null);
            }
        }

        private void Unsubscribe(Action<T> listener)
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
            private DataBindingProperty<T> _owner;
            private Action<T> _listener;

            public Subscription(DataBindingProperty<T> owner, Action<T> listener)
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
