using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    public delegate void TimerHandler(object[] args);

    internal class TimerModule : Module, IUpdateModule, ITimerModule
    {
        [Serializable]
        internal class Timer
        {
            public int timerId = 0;
            public float curTime = 0;
            public float time = 0;
            public TimerHandler Handler;
            public bool isLoop = false;
            public bool isNeedRemove = false;
            public bool isRunning = false;
            public bool isUnscaled = false;
            public bool hasLoopCount = false;
            public int loopCount = 0;
            [NonSerialized] public object[] Args = null;
        }

        private int _curTimerId = 0;
        private readonly GameFrameworkLinkedList<Timer> _timerList = new GameFrameworkLinkedList<Timer>();
        private readonly GameFrameworkLinkedList<Timer> _unscaledTimerList = new GameFrameworkLinkedList<Timer>();

        private bool _hasBadFrame = false;
        private bool _hasUnscaledBadFrame = false;
        private const int MaxBadFrameCheckCount = 10;

        /// <summary>
        /// 添加计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <param name="args">传参。(避免闭包)</param>
        /// <returns>计时器Id。</returns>
        public int AddTimer(TimerHandler callback, float time, bool isLoop = false, bool isUnscaled = false, params object[] args)
        {
            Timer timer = new Timer
            {
                timerId = ++_curTimerId,
                curTime = time,
                time = time,
                Handler = callback,
                isLoop = isLoop,
                isUnscaled = isUnscaled,
                Args = args,
                isNeedRemove = false,
                isRunning = true,
                hasLoopCount = false,
                loopCount = 0
            };

            InsertTimer(timer);
            return timer.timerId;
        }

        /// <summary>
        /// 添加指定循环次数的计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="loopCount">循环次数。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <param name="args">传参。(避免闭包)</param>
        /// <returns>计时器Id。</returns>
        public int AddLoopCountTimer(TimerHandler callback, float time, int loopCount, bool isUnscaled = false, params object[] args)
        {
            Timer timer = new Timer
            {
                timerId = ++_curTimerId,
                curTime = time,
                time = time,
                Handler = callback,
                isLoop = true,
                isUnscaled = isUnscaled,
                Args = args,
                isNeedRemove = false,
                isRunning = true,
                hasLoopCount = true,
                loopCount = loopCount
            };

            InsertTimer(timer);
            return timer.timerId;
        }

        private void InsertTimer(Timer timer)
        {
            if (timer.isUnscaled)
            {
                if (_unscaledTimerList.Count <= 0)
                {
                    _unscaledTimerList.AddLast(timer);
                    return;
                }

                LinkedListNode<Timer> curNode = _unscaledTimerList.First;
                while (curNode != null && curNode.Value.curTime <= timer.curTime)
                {
                    curNode = curNode.Next;
                }

                if (curNode == null)
                {
                    _unscaledTimerList.AddLast(timer);
                }
                else
                {
                    _unscaledTimerList.AddBefore(curNode, timer);
                }
            }
            else
            {
                if (_timerList.Count <= 0)
                {
                    _timerList.AddLast(timer);
                    return;
                }

                LinkedListNode<Timer> curNode = _timerList.First;
                while (curNode != null && curNode.Value.curTime <= timer.curTime)
                {
                    curNode = curNode.Next;
                }

                if (curNode == null)
                {
                    _timerList.AddLast(timer);
                }
                else
                {
                    _timerList.AddBefore(curNode, timer);
                }
            }
        }

        /// <summary>
        /// 暂停计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public void Stop(int timerId)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null) timer.isRunning = false;
        }

        /// <summary>
        /// 恢复计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public void Resume(int timerId)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null) timer.isRunning = true;
        }

        /// <summary>
        /// 计时器是否在运行中。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        /// <returns>否在运行中。</returns>
        public bool IsRunning(int timerId)
        {
            Timer timer = GetTimer(timerId);
            return timer is { isRunning: true };
        }

        /// <summary>
        /// 获得计时器剩余时间
        /// </summary>
        public float GetLeftTime(int timerId)
        {
            Timer timer = GetTimer(timerId);
            if (timer == null) return 0;
            return timer.curTime;
        }

        /// <summary>
        /// 重置计时器,恢复到开始状态。
        /// </summary>
        public void Restart(int timerId)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null)
            {
                timer.curTime = timer.time;
                timer.isRunning = true;
            }
        }

        public void ResetTimer(int timerId, TimerHandler callback, float time, bool isLoop = false, bool isUnscaled = false)
        {
            Reset(timerId, callback, time, isLoop, isUnscaled);
        }

        public void ResetTimer(int timerId, float time, bool isLoop, bool isUnscaled)
        {
            Reset(timerId, time, isLoop, isUnscaled);
        }

        /// <summary>
        /// 重置计时器。
        /// </summary>
        public void Reset(int timerId, TimerHandler callback, float time, bool isLoop = false, bool isUnscaled = false)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null)
            {
                timer.curTime = time;
                timer.time = time;
                timer.Handler = callback;
                timer.isLoop = isLoop;
                timer.isNeedRemove = false;
                timer.hasLoopCount = false;
                timer.loopCount = 0;
                if (timer.isUnscaled != isUnscaled)
                {
                    RemoveTimerImmediate(timerId);

                    timer.isUnscaled = isUnscaled;
                    InsertTimer(timer);
                }
            }
        }

        /// <summary>
        /// 重置计时器。
        /// </summary>
        public void Reset(int timerId, float time, bool isLoop, bool isUnscaled)
        {
            Timer timer = GetTimer(timerId);
            if (timer != null)
            {
                timer.curTime = time;
                timer.time = time;
                timer.isLoop = isLoop;
                timer.isNeedRemove = false;
                timer.hasLoopCount = false;
                timer.loopCount = 0;
                if (timer.isUnscaled != isUnscaled)
                {
                    RemoveTimerImmediate(timerId);

                    timer.isUnscaled = isUnscaled;
                    InsertTimer(timer);
                }
            }
        }

        /// <summary>
        /// 立即移除。
        /// </summary>
        /// <param name="timerId"></param>
        private void RemoveTimerImmediate(int timerId)
        {
            LinkedListNode<Timer> curNode = _timerList.First;
            while (curNode != null)
            {
                if (curNode.Value.timerId == timerId)
                {
                    _timerList.Remove(curNode);
                    return;
                }
                curNode = curNode.Next;
            }

            curNode = _unscaledTimerList.First;
            while (curNode != null)
            {
                if (curNode.Value.timerId == timerId)
                {
                    _unscaledTimerList.Remove(curNode);
                    return;
                }
                curNode = curNode.Next;
            }
        }

        /// <summary>
        /// 移除计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public void RemoveTimer(int timerId)
        {
            LinkedListNode<Timer> curNode = _timerList.First;
            while (curNode != null)
            {
                if (curNode.Value.timerId == timerId)
                {
                    curNode.Value.isNeedRemove = true;
                    return;
                }
                curNode = curNode.Next;
            }

            curNode = _unscaledTimerList.First;
            while (curNode != null)
            {
                if (curNode.Value.timerId == timerId)
                {
                    curNode.Value.isNeedRemove = true;
                    return;
                }
                curNode = curNode.Next;
            }
        }

        /// <summary>
        /// 移除所有计时器。
        /// </summary>
        public void RemoveAllTimer()
        {
            _timerList.Clear();
            _unscaledTimerList.Clear();
        }

        private Timer GetTimer(int timerId)
        {
            LinkedListNode<Timer> curNode = _timerList.First;
            while (curNode != null)
            {
                if (curNode.Value.timerId == timerId)
                {
                    return curNode.Value;
                }
                curNode = curNode.Next;
            }

            curNode = _unscaledTimerList.First;
            while (curNode != null)
            {
                if (curNode.Value.timerId == timerId)
                {
                    return curNode.Value;
                }
                curNode = curNode.Next;
            }

            return null;
        }

        private void HandleLoopBadFrame()
        {
            int checkCount = MaxBadFrameCheckCount;
            while (_hasBadFrame && checkCount > 0)
            {
                _hasBadFrame = false;
                LinkedListNode<Timer> curNode = _timerList.First;

                while (curNode != null && checkCount-- > 0)
                {
                    LinkedListNode<Timer> nextNode = curNode.Next;

                    if (curNode.Value.isLoop && curNode.Value.curTime <= 0
                        && !curNode.Value.isNeedRemove && curNode.Value.isRunning)
                    {
                        curNode.Value.Handler?.Invoke(curNode.Value.Args);

                        if (curNode.Value.hasLoopCount)
                        {
                            curNode.Value.loopCount -= 1;

                            if (curNode.Value.loopCount > 0)
                            {
                                curNode.Value.curTime += curNode.Value.time;

                                if (curNode.Value.curTime <= 0)
                                {
                                    _hasBadFrame = true;
                                }
                            }
                            else
                            {
                                _timerList.Remove(curNode);
                            }
                        }
                        else
                        {
                            curNode.Value.curTime += curNode.Value.time;

                            if (curNode.Value.curTime <= 0)
                            {
                                _hasBadFrame = true;
                            }
                        }
                    }
                    curNode = nextNode;
                }
            }
        }

        private void HandleUnscaledLoopBadFrame()
        {
            int checkCount = MaxBadFrameCheckCount;
            while (_hasUnscaledBadFrame && checkCount > 0)
            {
                _hasUnscaledBadFrame = false;
                LinkedListNode<Timer> curNode = _unscaledTimerList.First;

                while (curNode != null && checkCount-- > 0)
                {
                    LinkedListNode<Timer> nextNode = curNode.Next;

                    if (curNode.Value.isLoop && curNode.Value.curTime <= 0
                        && !curNode.Value.isNeedRemove && curNode.Value.isRunning)
                    {
                        curNode.Value.Handler?.Invoke(curNode.Value.Args);

                        if (curNode.Value.hasLoopCount)
                        {
                            curNode.Value.loopCount -= 1;

                            if (curNode.Value.loopCount > 0)
                            {
                                curNode.Value.curTime += curNode.Value.time;

                                if (curNode.Value.curTime <= 0)
                                {
                                    _hasUnscaledBadFrame = true;
                                }
                            }
                            else
                            {
                                _unscaledTimerList.Remove(curNode);
                            }
                        }
                        else
                        {
                            curNode.Value.curTime += curNode.Value.time;

                            if (curNode.Value.curTime <= 0)
                            {
                                _hasUnscaledBadFrame = true;
                            }
                        }
                    }
                    curNode = nextNode;
                }
            }
        }

        private void UpdateTimer(float elapseSeconds)
        {
            bool hasBadFrame = false;
            LinkedListNode<Timer> curNode = _timerList.First;

            while (curNode != null)
            {
                LinkedListNode<Timer> nextNode = curNode.Next;

                if (curNode.Value.isNeedRemove)
                {
                    _timerList.Remove(curNode);
                    curNode = nextNode;
                    continue;
                }

                if (!curNode.Value.isRunning)
                {
                    curNode = nextNode;
                    continue;
                }

                curNode.Value.curTime -= elapseSeconds;

                if (curNode.Value.curTime <= 0)
                {
                    curNode.Value.Handler?.Invoke(curNode.Value.Args);

                    if (curNode.Value.hasLoopCount)
                    {
                        curNode.Value.loopCount -= 1;

                        if (curNode.Value.loopCount > 0)
                        {
                            curNode.Value.curTime += curNode.Value.time;
                            if (curNode.Value.curTime <= 0)
                            {
                                hasBadFrame = true;
                            }
                        }
                        else
                        {
                            _timerList.Remove(curNode);
                        }
                    }
                    else
                    {
                        if (curNode.Value.isLoop)
                        {
                            curNode.Value.curTime += curNode.Value.time;

                            if (curNode.Value.curTime <= 0)
                            {
                                hasBadFrame = true;
                            }
                        }
                        else
                        {
                            _timerList.Remove(curNode);
                        }
                    }
                }

                curNode = nextNode;
            }

            _hasBadFrame = hasBadFrame;
        }

        private void UpdateUnscaledTimer(float realElapseSeconds)
        {
            bool hasBadFrame = false;
            LinkedListNode<Timer> curNode = _unscaledTimerList.First;

            while (curNode != null)
            {
                LinkedListNode<Timer> nextNode = curNode.Next;

                if (curNode.Value.isNeedRemove)
                {
                    _unscaledTimerList.Remove(curNode);
                    curNode = nextNode;
                    continue;
                }

                if (!curNode.Value.isRunning)
                {
                    curNode = nextNode;
                    continue;
                }

                curNode.Value.curTime -= realElapseSeconds;

                if (curNode.Value.curTime <= 0)
                {
                    curNode.Value.Handler?.Invoke(curNode.Value.Args);

                    if (curNode.Value.hasLoopCount)
                    {
                        curNode.Value.loopCount -= 1;

                        if (curNode.Value.loopCount > 0)
                        {
                            curNode.Value.curTime += curNode.Value.time;
                            if (curNode.Value.curTime <= 0)
                            {
                                hasBadFrame = true;
                            }
                        }
                        else
                        {
                            _unscaledTimerList.Remove(curNode);
                        }
                    }
                    else
                    {
                        if (curNode.Value.isLoop)
                        {
                            curNode.Value.curTime += curNode.Value.time;

                            if (curNode.Value.curTime <= 0)
                            {
                                hasBadFrame = true;
                            }
                        }
                        else
                        {
                            _unscaledTimerList.Remove(curNode);
                        }
                    }
                }

                curNode = nextNode;
            }

            _hasUnscaledBadFrame = hasBadFrame;
        }

        private readonly List<System.Timers.Timer> _ticker = new List<System.Timers.Timer>();

        public System.Timers.Timer AddSystemTimer(Action<object, System.Timers.ElapsedEventArgs> callBack)
        {
            int interval = 1000;
            var timerTick = new System.Timers.Timer(interval);
            timerTick.AutoReset = true;
            timerTick.Enabled = true;
            timerTick.Elapsed += new System.Timers.ElapsedEventHandler(callBack);

            _ticker.Add(timerTick);

            return timerTick;
        }

        private void DestroySystemTimer()
        {
            foreach (var ticker in _ticker)
            {
                if (ticker != null)
                {
                    ticker.Stop();
                    ticker.Dispose();
                }
            }
            _ticker.Clear();
        }

        public override void OnInit()
        {
        }

        public override void Shutdown()
        {
            RemoveAllTimer();
            _timerList.ClearCachedNodes();
            _unscaledTimerList.ClearCachedNodes();
            DestroySystemTimer();
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            UpdateTimer(elapseSeconds);
            HandleLoopBadFrame();
            UpdateUnscaledTimer(realElapseSeconds);
            HandleUnscaledLoopBadFrame();
        }
    }
}
