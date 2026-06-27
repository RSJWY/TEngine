using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

/// <summary>
/// DynamicSceneSpawner
/// 功能描述：场景动态加载抽象基类——收集占位点/列表项，分批异步加载预制体并对齐。
/// 创建时间：2026-06-25
/// 开发者：lzx
/// </summary>

namespace GameLogic
{
    /// <summary>
    /// 加载项数据：描述一个待加载的预制体及其放置信息。
    /// </summary>
    public struct SpawnItem
    {
        /// <summary>YooAsset 资源地址。</summary>
        public string Location;

        /// <summary>父节点（占位节点），为 null 时放在场景根。</summary>
        public Transform Parent;

        /// <summary>对齐模式。</summary>
        public SpawnAlignMode AlignMode;

        /// <summary>可选注册键。</summary>
        public string RegisterKey;

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器辅助：测试启动时（YooAsset 未初始化）的直接预制体引用，用于 Instantiate 回退。
        /// 仅编辑器下存在。
        /// </summary>
        public GameObject EditorPrefab;
#endif
    }

    /// <summary>
    /// 初始化模式：决定 Spawner 在哪个时机自动开始加载。
    /// </summary>
    public enum SpawnInitMode
    {
        /// <summary>Awake 时立即开始加载（最早，场景刚激活就跑）。</summary>
        [InspectorName("Awake（最早，场景激活即加载）")]
        Awake,

        /// <summary>Start 时开始加载（Awake 之后一帧）。</summary>
        [InspectorName("Start（Awake 后一帧）")]
        Start,

        /// <summary>OnEnable 时开始加载（每次激活都会触发）。</summary>
        [InspectorName("OnEnable（每次激活触发）")]
        OnEnable,

        /// <summary>收到 Event_SceneReady 时开始加载（LoadingUI 关闭后，最晚）。</summary>
        [InspectorName("SceneReady（加载页关闭后）")]
        SceneReady,

        /// <summary>不自动触发，由外部手动调用 SpawnAsync()。</summary>
        [InspectorName("手动（外部调用 SpawnAsync）")]
        Manual
    }

    /// <summary>
    /// 场景动态加载抽象基类。挂在场景空节点上，按指定时机分批加载预制体。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>初始化模式</b>：通过 <see cref="initMode"/> 选择触发时机（Awake/Start/OnEnable/SceneReady/Manual）。
    /// </para>
    /// <para>
    /// <b>释放</b>：Single 模式切场景时整场景卸载，<c>LoadGameObjectAsync</c> 引用计数自动归还，无需手动 UnloadAsset。
    /// </para>
    /// <para>
    /// <b>方案乙预留</b>：<see cref="SpawnAsync"/> 为 public，将来可由 GameSceneManager 在 finishCallBack 前 await 调用。
    /// </para>
    /// </remarks>
    public abstract class DynamicSceneSpawner : MonoBehaviour
    {
        /// <summary>每帧加载的最大数量（削峰）。</summary>
        [Tooltip("每帧加载的最大数量（削峰），0 表示不限")]
        [SerializeField] protected int batchSize = 3;

        /// <summary>初始化模式：决定什么时候自动开始加载。</summary>
        [Tooltip("Awake=最早 / Start=Awake后一帧 / OnEnable=每次激活 / SceneReady=LoadingUI关闭后 / Manual=手动调用")]
        [SerializeField] protected SpawnInitMode initMode = SpawnInitMode.Start;

        private CancellationTokenSource _cts;
        private bool _hasSpawned = false;
        private bool _isSpawnCompleted = false;

        /// <summary>注册表：registerKey → 已加载的 GameObject 实例。</summary>
        private readonly Dictionary<string, GameObject> _registry = new Dictionary<string, GameObject>();

        /// <summary>动态加载是否已经完成。</summary>
        public bool IsSpawnCompleted => _isSpawnCompleted;

        /// <summary>
        /// 通过注册键查找已加载的 GameObject。未找到或 key 为空时返回 null。
        /// </summary>
        public GameObject GetRegistered(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _registry.TryGetValue(key, out var go) ? go : null;
        }

        /// <summary>
        /// 尝试通过注册键查找已加载的 GameObject。
        /// </summary>
        public bool TryGetRegistered(string key, out GameObject go)
        {
            go = null;
            if (string.IsNullOrEmpty(key)) return false;
            return _registry.TryGetValue(key, out go) && go != null;
        }

        /// <summary>
        /// 获取当前注册表中所有已注册的键。
        /// </summary>
        public IReadOnlyCollection<string> RegisteredKeys => _registry.Keys;

        /// <summary>
        /// 获取当前注册表中的条目数。
        /// </summary>
        public int RegisteredCount => _registry.Count;

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器辅助：当前批量放置的预览实例列表（不序列化，不会保存到场景）。
        /// 由自定义 Inspector 在非运行态填充，用于快速调试整体布局。
        /// </summary>
        [System.NonSerialized]
        public List<GameObject> editorPreviewInstances = new List<GameObject>();

        /// <summary>
        /// 编辑器辅助：暴露给自定义 Inspector 的收集入口（protected 的 CollectSpawnItems 包装）。
        /// </summary>
        public List<SpawnItem> EditorCollectSpawnItems() => CollectSpawnItems();
#endif

        #region 生命周期

        protected virtual void Awake()
        {
            if (initMode == SpawnInitMode.SceneReady)
            {
                GameEvent.AddEventListener<SceneType>(GlobalEventID.Event_SceneReady, OnSceneReady);
            }

            if (initMode == SpawnInitMode.Awake)
            {
                TrySpawn();
            }
        }

        protected virtual void Start()
        {
            if (initMode == SpawnInitMode.Start)
            {
                TrySpawn();
            }
        }

        protected virtual void OnEnable()
        {
            if (initMode == SpawnInitMode.OnEnable)
            {
                TrySpawn();
            }
        }

        protected virtual void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (initMode == SpawnInitMode.SceneReady)
            {
                GameEvent.RemoveEventListener<SceneType>(GlobalEventID.Event_SceneReady, OnSceneReady);
            }
        }

        #endregion

        #region 触发入口

        /// <summary>
        /// 内部触发（防重复）。
        /// </summary>
        private void TrySpawn()
        {
            if (_hasSpawned) return;
            SpawnAsync().Forget();
        }

        /// <summary>
        /// Event_SceneReady 回调。
        /// </summary>
        private void OnSceneReady(SceneType sceneType)
        {
            TrySpawn();
        }

        /// <summary>
        /// 是否为正常流程启动（经过主场景 → GameSceneManager 记录了 CurrentSceneType）。
        /// 如果 CurrentSceneType 为 null，说明是编辑器下直接打开测试场景，YooAsset 未初始化。
        /// </summary>
        private static bool IsNormalBoot => GameSceneManager.CurrentSceneType != null;

        /// <summary>
        /// 公开异步加载入口（方案乙预留：将来可由 GameSceneManager await 调用）。
        /// </summary>
        public async UniTask SpawnAsync()
        {
            if (_hasSpawned) return;
            _hasSpawned = true;
            _isSpawnCompleted = false;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // 收集加载项
            var items = CollectSpawnItems();
            if (items == null || items.Count == 0)
            {
                Log.Info($"[DynamicSceneSpawner] {GetType().Name}: 无加载项，跳过。");
                CompleteSpawn();
                return;
            }

            bool useYooAsset = IsNormalBoot;
            var modeLabel = useYooAsset ? "YooAsset" : "编辑器直接实例化（测试启动）";
            Log.Info($"[DynamicSceneSpawner] {GetType().Name}: 开始加载 {items.Count} 个对象，batchSize={batchSize}，模式={modeLabel}");

            int count = 0;
            int successCount = 0;

            foreach (var item in items)
            {
                if (token.IsCancellationRequested) return;

                if (string.IsNullOrEmpty(item.Location))
                {
                    Log.Warning($"[DynamicSceneSpawner] 跳过空 location 项（parent={item.Parent?.name ?? "null"}）");
                    continue;
                }

                try
                {
                    GameObject go = null;

                    if (useYooAsset)
                    {
                        go = await GameModule.Resource.LoadGameObjectAsync(
                            item.Location, parent: item.Parent, cancellationToken: token);
                    }
                    else
                    {
                        // 测试启动：YooAsset 未初始化，通过编辑器引用直接实例化
                        go = InstantiateFromEditorRef(item);
                    }

                    if (go != null)
                    {
                        AlignInstance(go, item);
                        RegisterInstance(go, item);
                        successCount++;
                    }
                }
                catch (System.OperationCanceledException)
                {
                    return; // 正常取消（切场景）
                }
                catch (System.Exception e)
                {
                    Log.Error($"[DynamicSceneSpawner] 加载 \"{item.Location}\" 失败: {e.Message}");
                }

                // 分批削峰
                if (batchSize > 0)
                {
                    count++;
                    if (count >= batchSize)
                    {
                        count = 0;
                        await UniTask.Yield(PlayerLoopTiming.Update, token);
                    }
                }
            }

            if (token.IsCancellationRequested) return;

            Log.Info($"[DynamicSceneSpawner] {GetType().Name}: 加载完成，成功 {successCount}/{items.Count}");
            CompleteSpawn();
        }

        /// <summary>
        /// 测试启动回退：从编辑器下的 prefabReference 直接实例化。
        /// </summary>
        private GameObject InstantiateFromEditorRef(SpawnItem item)
        {
#if UNITY_EDITOR
            if (item.EditorPrefab == null)
            {
                Log.Warning($"[DynamicSceneSpawner] 测试模式下 \"{item.Location}\" 无 EditorPrefab 引用，跳过。");
                return null;
            }

            var parent = item.Parent != null ? item.Parent : transform;
            var go = UnityEngine.Object.Instantiate(item.EditorPrefab, parent);
            go.name = item.EditorPrefab.name; // 去掉 (Clone) 后缀
            return go;
#else
            Log.Error($"[DynamicSceneSpawner] 非编辑器环境下不支持测试启动回退，\"{item.Location}\" 加载失败。");
            return null;
#endif
        }

        private void CompleteSpawn()
        {
            _isSpawnCompleted = true;

            // 发送完成事件（携带当前场景类型，供监听方校验）
            GameEvent.Send<SceneType>(GlobalEventID.Event_DynamicSpawnComplete,
                GameSceneManager.CurrentSceneType ?? SceneType.Main);

            // 子类钩子
            OnAllSpawned();
        }

        #endregion

        #region 子类接口

        /// <summary>
        /// 子类实现：收集本场景需要动态加载的所有项。
        /// </summary>
        /// <remarks>
        /// 可混用占位节点法（遍历场景内 DynamicSpawnPoint）和代码列表法。
        /// 基类提供 <see cref="CollectFromSpawnPoints"/> 辅助方法，子类可直接调用。
        /// </remarks>
        protected abstract List<SpawnItem> CollectSpawnItems();

        /// <summary>
        /// 子类钩子：所有对象加载完毕后调用（可 override 做定制逻辑）。
        /// </summary>
        protected virtual void OnAllSpawned() { }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 从指定根节点下收集所有 DynamicSpawnPoint 组件，转为 SpawnItem 列表。
        /// </summary>
        /// <param name="root">搜索根节点，为 null 时搜索本 GameObject 下。</param>
        /// <param name="includeInactive">是否包含未激活的节点。</param>
        protected List<SpawnItem> CollectFromSpawnPoints(Transform root = null, bool includeInactive = false)
        {
            var searchRoot = root != null ? root : transform;
            var points = searchRoot.GetComponentsInChildren<DynamicSpawnPoint>(includeInactive);
            var items = new List<SpawnItem>(points.Length);

            foreach (var point in points)
            {
                if (string.IsNullOrEmpty(point.location))
                {
                    Log.Warning($"[DynamicSceneSpawner] 占位节点 \"{point.name}\" 的 location 为空，已跳过。");
                    continue;
                }

                var item = new SpawnItem
                {
                    Location = point.location,
                    Parent = point.transform,
                    AlignMode = point.alignMode,
                    RegisterKey = point.registerKey
                };

#if UNITY_EDITOR
                point.MigrateLegacyReferenceIfNeeded();
                item.EditorPrefab = point.EditorPrefab;
#endif

                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// 对齐实例到占位节点。
        /// </summary>
        private void AlignInstance(GameObject instance, SpawnItem item)
        {
            if (item.AlignMode == SpawnAlignMode.AlignToPlaceholder)
            {
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
            }
            // KeepPrefabLocal：LoadGameObjectAsync 已将实例放在 parent 下，保留预制体自带 local TRS 即可
        }

        /// <summary>
        /// 将有 registerKey 的实例注册到内部字典。重复键会覆盖并打警告。
        /// </summary>
        private void RegisterInstance(GameObject instance, SpawnItem item)
        {
            if (string.IsNullOrEmpty(item.RegisterKey)) return;

            if (_registry.TryGetValue(item.RegisterKey, out var existing) && existing != null)
            {
                Log.Warning($"[DynamicSceneSpawner] 注册键 \"{item.RegisterKey}\" 重复，旧实例 \"{existing.name}\" 将被覆盖为 \"{instance.name}\"");
            }

            _registry[item.RegisterKey] = instance;
        }

        #endregion
    }
}
