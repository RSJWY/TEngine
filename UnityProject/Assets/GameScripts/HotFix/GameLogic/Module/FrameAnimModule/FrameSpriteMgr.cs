using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 帧动画资源池与播放更新管理器。
    /// <para>统一调度所有 <see cref="FrameAnimatorAgent"/> 与 <see cref="UIFrameAnimatorAgent"/>，
    /// 按 scaled / unscaled 时间域分组，用 <see cref="ITimerModule"/> 循环计时器驱动。</para>
    /// </summary>
    public sealed class FrameSpriteMgr : Singleton<FrameSpriteMgr>
    {
        /// <summary>
        /// 帧动画调度采样间隔，沿用原单代理计时器的八倍采样率。
        /// </summary>
        private const float ANIMATOR_TICK_INTERVAL = 0.015625f;

        /// <summary>
        /// 已加载的帧动画资源池缓存，键为资源定位地址。
        /// </summary>
        private readonly Dictionary<string, FrameSpritePool> _frameSpritePools = new Dictionary<string, FrameSpritePool>();

        /// <summary>
        /// 使用缩放时间的场景帧动画代理列表。
        /// </summary>
        private readonly List<FrameAnimatorAgent> _scaledFrameAnimators = new List<FrameAnimatorAgent>();

        /// <summary>
        /// 使用非缩放时间的场景帧动画代理列表。
        /// </summary>
        private readonly List<FrameAnimatorAgent> _unscaledFrameAnimators = new List<FrameAnimatorAgent>();

        /// <summary>
        /// 使用缩放时间的UI帧动画代理列表。
        /// </summary>
        private readonly List<UIFrameAnimatorAgent> _scaledUIAnimators = new List<UIFrameAnimatorAgent>();

        /// <summary>
        /// 使用非缩放时间的UI帧动画代理列表。
        /// </summary>
        private readonly List<UIFrameAnimatorAgent> _unscaledUIAnimators = new List<UIFrameAnimatorAgent>();

        /// <summary>
        /// 使用缩放时间的 RawImage 帧动画代理列表。
        /// </summary>
        private readonly List<UIFrameRawAnimatorAgent> _scaledUIRawAnimators = new List<UIFrameRawAnimatorAgent>();

        /// <summary>
        /// 使用非缩放时间的 RawImage 帧动画代理列表。
        /// </summary>
        private readonly List<UIFrameRawAnimatorAgent> _unscaledUIRawAnimators = new List<UIFrameRawAnimatorAgent>();

        /// <summary>
        /// 缩放时间调度计时器 Id；0 表示未创建。
        /// </summary>
        private int _scaledTimerId;

        /// <summary>
        /// 非缩放时间调度计时器 Id；0 表示未创建。
        /// </summary>
        private int _unscaledTimerId;

        /// <summary>
        /// 注册场景帧动画代理到对应时间域的调度列表。
        /// </summary>
        /// <param name="agent">待注册的场景帧动画代理。</param>
        internal void RegisterAnimator(FrameAnimatorAgent agent)
        {
            if (agent == null || agent.UpdateIndex >= 0)
            {
                return;
            }

            var animators = agent.IsUnscaledTime ? _unscaledFrameAnimators : _scaledFrameAnimators;
            agent.UpdateIndex = animators.Count;
            animators.Add(agent);
            EnsureTickTimer(agent.IsUnscaledTime);
        }

        /// <summary>
        /// 注册UI帧动画代理到对应时间域的调度列表。
        /// </summary>
        /// <param name="agent">待注册的UI帧动画代理。</param>
        internal void RegisterAnimator(UIFrameAnimatorAgent agent)
        {
            if (agent == null || agent.UpdateIndex >= 0)
            {
                return;
            }

            var animators = agent.IsUnscaledTime ? _unscaledUIAnimators : _scaledUIAnimators;
            agent.UpdateIndex = animators.Count;
            animators.Add(agent);
            EnsureTickTimer(agent.IsUnscaledTime);
        }

        /// <summary>
        /// 注册 RawImage 帧动画代理到对应时间域的调度列表。
        /// </summary>
        /// <param name="agent">待注册的 RawImage 帧动画代理。</param>
        internal void RegisterAnimator(UIFrameRawAnimatorAgent agent)
        {
            if (agent == null || agent.UpdateIndex >= 0)
            {
                return;
            }

            var animators = agent.IsUnscaledTime ? _unscaledUIRawAnimators : _scaledUIRawAnimators;
            agent.UpdateIndex = animators.Count;
            animators.Add(agent);
            EnsureTickTimer(agent.IsUnscaledTime);
        }

        /// <summary>
        /// 从调度列表注销场景帧动画代理。
        /// </summary>
        /// <param name="agent">待注销的场景帧动画代理。</param>
        internal void UnregisterAnimator(FrameAnimatorAgent agent)
        {
            var animators = agent != null && agent.IsUnscaledTime
                ? _unscaledFrameAnimators
                : _scaledFrameAnimators;
            RemoveAnimator(animators, agent);
        }

        /// <summary>
        /// 从调度列表注销UI帧动画代理。
        /// </summary>
        /// <param name="agent">待注销的UI帧动画代理。</param>
        internal void UnregisterAnimator(UIFrameAnimatorAgent agent)
        {
            var animators = agent != null && agent.IsUnscaledTime
                ? _unscaledUIAnimators
                : _scaledUIAnimators;
            RemoveAnimator(animators, agent);
        }

        /// <summary>
        /// 从调度列表注销 RawImage 帧动画代理。
        /// </summary>
        /// <param name="agent">待注销的 RawImage 帧动画代理。</param>
        internal void UnregisterAnimator(UIFrameRawAnimatorAgent agent)
        {
            var animators = agent != null && agent.IsUnscaledTime
                ? _unscaledUIRawAnimators
                : _scaledUIRawAnimators;
            RemoveAnimator(animators, agent);
        }

        /// <summary>
        /// 确保指定时间域的调度计时器已经创建。
        /// </summary>
        /// <param name="isUnscaledTime">是否使用不受时间缩放影响的时间。</param>
        private void EnsureTickTimer(bool isUnscaledTime)
        {
            if (isUnscaledTime)
            {
                if (_unscaledTimerId == 0)
                {
                    _unscaledTimerId = GameModule.Timer.AddTimer(TickUnscaled, ANIMATOR_TICK_INTERVAL, true, true);
                }
                return;
            }

            if (_scaledTimerId == 0)
            {
                _scaledTimerId = GameModule.Timer.AddTimer(TickScaled, ANIMATOR_TICK_INTERVAL, true, false);
            }
        }

        /// <summary>
        /// 缩放时间计时器回调，驱动所有使用缩放时间的帧动画代理。
        /// </summary>
        /// <param name="args">计时器透传参数，当前未使用。</param>
        private void TickScaled(object[] args)
        {
            float gameTime = Time.time;
            TickAnimators(_scaledFrameAnimators, gameTime);
            TickAnimators(_scaledUIAnimators, gameTime);
            TickAnimators(_scaledUIRawAnimators, gameTime);
        }

        /// <summary>
        /// 非缩放时间计时器回调，驱动所有使用非缩放时间的帧动画代理。
        /// </summary>
        /// <param name="args">计时器透传参数，当前未使用。</param>
        private void TickUnscaled(object[] args)
        {
            float unscaledTime = Time.unscaledTime;
            TickAnimators(_unscaledFrameAnimators, unscaledTime);
            TickAnimators(_unscaledUIAnimators, unscaledTime);
            TickAnimators(_unscaledUIRawAnimators, unscaledTime);
        }

        /// <summary>
        /// 轮询场景帧动画代理列表，并移除已经无效或播放结束的代理。
        /// </summary>
        /// <param name="animators">待轮询的场景帧动画代理列表。</param>
        /// <param name="currentTime">当前时间域下的时间戳。</param>
        private void TickAnimators(List<FrameAnimatorAgent> animators, float currentTime)
        {
            for (int i = animators.Count - 1; i >= 0; i--)
            {
                var agent = animators[i];
                if (!agent.Tick(currentTime))
                {
                    UnregisterAnimator(agent);
                }
            }
        }

        /// <summary>
        /// 轮询UI帧动画代理列表，并移除已经无效或播放结束的代理。
        /// </summary>
        /// <param name="animators">待轮询的UI帧动画代理列表。</param>
        /// <param name="currentTime">当前时间域下的时间戳。</param>
        private void TickAnimators(List<UIFrameAnimatorAgent> animators, float currentTime)
        {
            for (int i = animators.Count - 1; i >= 0; i--)
            {
                var agent = animators[i];
                if (!agent.Tick(currentTime))
                {
                    UnregisterAnimator(agent);
                }
            }
        }

        /// <summary>
        /// 轮询 RawImage 帧动画代理列表，并移除已经无效或播放结束的代理。
        /// </summary>
        /// <param name="animators">待轮询的 RawImage 帧动画代理列表。</param>
        /// <param name="currentTime">当前时间域下的时间戳。</param>
        private void TickAnimators(List<UIFrameRawAnimatorAgent> animators, float currentTime)
        {
            for (int i = animators.Count - 1; i >= 0; i--)
            {
                var agent = animators[i];
                if (!agent.Tick(currentTime))
                {
                    UnregisterAnimator(agent);
                }
            }
        }

        /// <summary>
        /// 以尾部交换方式从场景帧动画代理列表中移除代理。
        /// </summary>
        /// <param name="animators">目标场景帧动画代理列表。</param>
        /// <param name="agent">待移除的场景帧动画代理。</param>
        private static void RemoveAnimator(List<FrameAnimatorAgent> animators, FrameAnimatorAgent agent)
        {
            int index = agent?.UpdateIndex ?? -1;
            if ((uint)index >= (uint)animators.Count || !ReferenceEquals(animators[index], agent))
            {
                return;
            }

            int lastIndex = animators.Count - 1;
            if (index != lastIndex)
            {
                var lastAgent = animators[lastIndex];
                animators[index] = lastAgent;
                lastAgent.UpdateIndex = index;
            }

            animators.RemoveAt(lastIndex);
            agent.UpdateIndex = -1;
        }

        /// <summary>
        /// 以尾部交换方式从UI帧动画代理列表中移除代理。
        /// </summary>
        /// <param name="animators">目标UI帧动画代理列表。</param>
        /// <param name="agent">待移除的UI帧动画代理。</param>
        private static void RemoveAnimator(List<UIFrameAnimatorAgent> animators, UIFrameAnimatorAgent agent)
        {
            int index = agent?.UpdateIndex ?? -1;
            if ((uint)index >= (uint)animators.Count || !ReferenceEquals(animators[index], agent))
            {
                return;
            }

            int lastIndex = animators.Count - 1;
            if (index != lastIndex)
            {
                var lastAgent = animators[lastIndex];
                animators[index] = lastAgent;
                lastAgent.UpdateIndex = index;
            }

            animators.RemoveAt(lastIndex);
            agent.UpdateIndex = -1;
        }

        /// <summary>
        /// 以尾部交换方式从 RawImage 帧动画代理列表中移除代理。
        /// </summary>
        /// <param name="animators">目标 RawImage 帧动画代理列表。</param>
        /// <param name="agent">待移除的 RawImage 帧动画代理。</param>
        private static void RemoveAnimator(List<UIFrameRawAnimatorAgent> animators, UIFrameRawAnimatorAgent agent)
        {
            int index = agent?.UpdateIndex ?? -1;
            if ((uint)index >= (uint)animators.Count || !ReferenceEquals(animators[index], agent))
            {
                return;
            }

            int lastIndex = animators.Count - 1;
            if (index != lastIndex)
            {
                var lastAgent = animators[lastIndex];
                animators[index] = lastAgent;
                lastAgent.UpdateIndex = index;
            }

            animators.RemoveAt(lastIndex);
            agent.UpdateIndex = -1;
        }

        /// <summary>
        /// 获取 <see cref="FrameSpritePool"/> 资源。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>帧动画资源池；加载失败时返回null。</returns>
        public async UniTask<FrameSpritePool> GetFrameSpritePool(string location, CancellationToken ct)
        {
            if (!_frameSpritePools.TryGetValue(location, out var pool))
            {
                var goCfg = await GameModule.Resource.LoadAssetAsync<GameObject>(location, ct);
                if (goCfg != null)
                {
                    pool = goCfg.GetComponent<FrameSpritePool>();
                    _frameSpritePools[location] = pool;
                }
            }
            return pool;
        }

        /// <summary>
        /// 清空所有 <see cref="FrameSpritePool"/> 缓存资源。
        /// </summary>
        private void ClearAll()
        {
            foreach (var pool in _frameSpritePools.Values)
            {
                if (pool != null)
                {
                    GameModule.Resource.UnloadAsset(pool.gameObject);
                }
            }
            _frameSpritePools.Clear();
        }

        /// <summary>
        /// 销毁帧动画资源池管理器，停止调度并释放缓存的帧动画资源池。
        /// </summary>
        protected override void OnRelease()
        {
            if (_scaledTimerId != 0)
            {
                GameModule.Timer.RemoveTimer(_scaledTimerId);
                _scaledTimerId = 0;
            }

            if (_unscaledTimerId != 0)
            {
                GameModule.Timer.RemoveTimer(_unscaledTimerId);
                _unscaledTimerId = 0;
            }

            for (int i = 0; i < _scaledFrameAnimators.Count; i++)
            {
                _scaledFrameAnimators[i].UpdateIndex = -1;
            }
            _scaledFrameAnimators.Clear();

            for (int i = 0; i < _unscaledFrameAnimators.Count; i++)
            {
                _unscaledFrameAnimators[i].UpdateIndex = -1;
            }
            _unscaledFrameAnimators.Clear();

            for (int i = 0; i < _scaledUIAnimators.Count; i++)
            {
                _scaledUIAnimators[i].UpdateIndex = -1;
            }
            _scaledUIAnimators.Clear();

            for (int i = 0; i < _unscaledUIAnimators.Count; i++)
            {
                _unscaledUIAnimators[i].UpdateIndex = -1;
            }
            _unscaledUIAnimators.Clear();

            for (int i = 0; i < _scaledUIRawAnimators.Count; i++)
            {
                _scaledUIRawAnimators[i].UpdateIndex = -1;
            }
            _scaledUIRawAnimators.Clear();

            for (int i = 0; i < _unscaledUIRawAnimators.Count; i++)
            {
                _unscaledUIRawAnimators[i].UpdateIndex = -1;
            }
            _unscaledUIRawAnimators.Clear();

            ClearAll();
        }
    }
}
