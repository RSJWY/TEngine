using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 场景内业务管理器抽象基类。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 职责：等待当前场景中的 <see cref="DynamicSceneSpawner"/> 完成动态对象加载后，
    /// 再执行场景自己的业务初始化，避免相机、交互、UI 或引导逻辑提前访问尚未实例化的场景对象。
    /// </para>
    /// <para>
    /// 完成判定采用“双保险”：
    /// ① 被动监听 <see cref="IGameSceneEvent.OnDynamicSpawnComplete"/>；
    /// ② 主动轮询 <see cref="DynamicSceneSpawner.IsSpawnCompleted"/>。
    /// 这样可以兼容 Manager 和 Spawner 在场景中的不同 Awake/Start 执行顺序，避免事件先发出后监听方才注册导致漏初始化。
    /// </para>
    /// </remarks>
    /// <typeparam name="TSpawner">当前场景使用的动态加载器具体类型。</typeparam>
    public abstract class SceneGameManagerBase<TSpawner> : MonoBehaviour where TSpawner : DynamicSceneSpawner
    {
        /// <summary>
        /// 主动轮询间隔：只用于兜底判断 Spawner 是否已完成，完成后会立即停止计时器。
        /// </summary>
        private const float PollInterval = 0.1f;

        /// <summary>
        /// 当前场景中的动态加载器缓存。
        /// </summary>
        /// <remarks>
        /// 优先从当前 Manager 子节点查找，便于把 Manager 与 Spawner 父子化放在同一个场景根节点下；
        /// 找不到时再退回到全场景查找，兼容旧场景结构。
        /// </remarks>
        private TSpawner _spawner;

        /// <summary>
        /// 主动轮询计时器 ID。大于 0 表示计时器已注册。
        /// </summary>
        private int _pollTimerId;

        /// <summary>
        /// 是否已经执行过场景初始化，防止事件通知和主动轮询同时命中时重复初始化。
        /// </summary>
        private bool _hasInitialized;

        /// <summary>
        /// 当前管理器负责的目标场景类型。
        /// </summary>
        /// <remarks>
        /// 用于过滤全局动态加载完成事件，避免其它场景或测试流程发出的事件误触发本 Manager。
        /// </remarks>
        protected abstract SceneType TargetSceneType { get; }

        /// <summary>
        /// 动态对象全部加载完成后的场景业务初始化入口。
        /// </summary>
        /// <remarks>
        /// 子类只需要在这里写本场景自己的逻辑，例如：打开场景 UI、相机就位、启用交互、播放入场动画等。
        /// 基类保证该方法在本 Manager 生命周期内最多只会调用一次。
        /// </remarks>
        protected abstract void OnSceneSpawnCompleted();

        /// <summary>
        /// 按注册键获取当前场景动态加载器中已注册的对象。
        /// </summary>
        /// <param name="registerKey">动态加载点上配置的注册键。</param>
        /// <returns>找到的对象；Spawner 不存在或未注册该键时返回 null（并打印警告日志）。</returns>
        /// <remarks>
        /// 封装了 <see cref="DynamicSceneSpawner.TryGetRegistered"/>，子类一行即可获取，无需关心 Spawner 引用与判空。
        /// </remarks>
        public virtual GameObject GetSpawnedObject(string registerKey)
        {
            TSpawner spawner = GetSpawner();
            if (spawner == null)
            {
                Log.Warning($"[{GetType().Name}] 获取注册对象 \"{registerKey}\" 失败：当前场景未找到 {typeof(TSpawner).Name}。");
                return null;
            }

            if (spawner.TryGetRegistered(registerKey, out GameObject go))
            {
                return go;
            }

            Log.Warning($"[{GetType().Name}] 获取注册对象失败：注册键 \"{registerKey}\" 未在 {typeof(TSpawner).Name} 中注册（检查动态加载点是否配置了该 registerKey 且已加载完成）。");
            return null;
        }

        /// <summary>
        /// Unity Awake：注册被动完成事件，并启动一次主动完成检查。
        /// </summary>
        protected virtual void Awake()
        {
            GameEvent.AddEventListener<SceneType>(IGameSceneEvent_Event.OnDynamicSpawnComplete, OnSpawnComplete);
            TryInitializeIfSpawnerCompleted();
            StartPollingSpawnerCompletion();
        }

        /// <summary>
        /// Unity Start：再做一次主动完成检查，覆盖 Spawner 在 Awake/Start 中先一步完成的情况。
        /// </summary>
        protected virtual void Start()
        {
            TryInitializeIfSpawnerCompleted();
        }

        /// <summary>
        /// Unity OnDestroy：注销事件与轮询计时器，避免场景卸载后仍回调已销毁对象。
        /// </summary>
        protected virtual void OnDestroy()
        {
            GameEvent.RemoveEventListener<SceneType>(IGameSceneEvent_Event.OnDynamicSpawnComplete, OnSpawnComplete);
            StopPollingSpawnerCompletion();
        }

        /// <summary>
        /// DynamicSceneSpawner 完成事件回调。
        /// </summary>
        /// <param name="sceneType">发出完成事件的场景类型。</param>
        private void OnSpawnComplete(SceneType sceneType)
        {
            if (sceneType != TargetSceneType)
            {
                return;
            }

            InitializeOnce();
        }

        /// <summary>
        /// 启动主动轮询，作为事件通知之外的兜底完成检测。
        /// </summary>
        private void StartPollingSpawnerCompletion()
        {
            if (_pollTimerId > 0 || _hasInitialized)
            {
                return;
            }

            _pollTimerId = GameModule.Timer.AddTimer(OnPollSpawnerCompletion, PollInterval, true);
        }

        /// <summary>
        /// 停止主动轮询计时器。
        /// </summary>
        private void StopPollingSpawnerCompletion()
        {
            if (_pollTimerId <= 0)
            {
                return;
            }

            GameModule.Timer.RemoveTimer(_pollTimerId);
            _pollTimerId = 0;
        }

        /// <summary>
        /// 主动轮询回调：周期性检查 Spawner 是否已经完成动态加载。
        /// </summary>
        /// <param name="args">TEngine Timer 透传参数，本场景轮询不需要使用。</param>
        private void OnPollSpawnerCompletion(object[] args)
        {
            TryInitializeIfSpawnerCompleted();
        }

        /// <summary>
        /// 主动查询 Spawner 当前完成状态；若已完成则触发一次性初始化。
        /// </summary>
        private void TryInitializeIfSpawnerCompleted()
        {
            if (_hasInitialized)
            {
                StopPollingSpawnerCompletion();
                return;
            }

            TSpawner spawner = GetSpawner();
            if (spawner == null || !spawner.IsSpawnCompleted)
            {
                return;
            }

            InitializeOnce();
        }

        /// <summary>
        /// 获取当前场景的动态加载器实例。
        /// </summary>
        /// <returns>找到的动态加载器；如果场景中尚不存在则返回 null。</returns>
        /// <remarks>
        /// 查找顺序：
        /// 1. 当前 Manager 的子节点，推荐把具体 Spawner 父子化挂到 Manager 下；
        /// 2. 全场景兜底查找，兼容历史场景中 Manager 与 Spawner 平级或分散放置的结构。
        /// </remarks>
        private TSpawner GetSpawner()
        {
            if (_spawner != null)
            {
                return _spawner;
            }

            _spawner = GetComponentInChildren<TSpawner>(true);
            if (_spawner != null)
            {
                return _spawner;
            }

            _spawner = FindObjectOfType<TSpawner>(true);
            return _spawner;
        }

        /// <summary>
        /// 执行一次性场景初始化。
        /// </summary>
        /// <remarks>
        /// 该方法是事件通知与主动轮询的统一收口，通过 <see cref="_hasInitialized"/> 防重，
        /// 并在初始化前关闭轮询计时器，避免后续无意义回调。
        /// </remarks>
        private void InitializeOnce()
        {
            if (_hasInitialized)
            {
                return;
            }

            _hasInitialized = true;
            StopPollingSpawnerCompletion();
            OnSceneSpawnCompleted();
        }
    }
}
