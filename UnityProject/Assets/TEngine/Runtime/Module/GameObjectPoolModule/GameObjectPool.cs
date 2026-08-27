using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TEngine
{
    /// <summary>
    /// 对象池对象身份标记。挂在每个被池管理的 GameObject 上，用于回收时反查所属池。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class GameObjectPoolIdentity : MonoBehaviour
    {
        /// <summary>
        /// 所属对象池定位地址。
        /// </summary>
        public string PoolKey;
    }

    /// <summary>
    /// GameObject 对象池。按 YooAsset location 维护一池预制体实例，支持预热、回收、自动销毁、容量上限与超容复用。
    /// <para>本身是 <see cref="MemoryObject"/>，由 <see cref="GameObjectPoolModule"/> 通过内存池获取/归还。</para>
    /// </summary>
    public sealed class GameObjectPool : MemoryObject
    {
        private Queue<GameObject> _goPool;
        private readonly LinkedList<GameObject> _spawnedPool = new LinkedList<GameObject>();
        private readonly Dictionary<GameObject, LinkedListNode<GameObject>> _spawnedNodeDict =
            new Dictionary<GameObject, LinkedListNode<GameObject>>();
        private GameObject _parent;
        private int _initCapacity;
        private int _maxCapacity;
        private float _autoDestroyTime;
        private float _lastRecycleTime = -1f;
        private bool _allowMultiSpawn;
        private IResourceModule _resourceModule;
        private CancellationTokenSource _destroyCancellationTokenSource;

        /// <summary>
        /// 自动销毁时间。
        /// </summary>
        public float AutoDestroyTime { get => _autoDestroyTime; set => _autoDestroyTime = value; }

        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public string Location { get; private set; }

        /// <summary>
        /// 对象池总容量。
        /// </summary>
        public int Count => _goPool != null && _spawnedPool != null ? _goPool.Count + _spawnedPool.Count : 0;

        /// <summary>
        /// 正在被使用的游戏对象个数。
        /// </summary>
        public int SpawnedCount => _spawnedPool?.Count ?? 0;

        /// <summary>
        /// 没在使用的游戏对象个数。
        /// </summary>
        public int NoSpawnCount => _goPool?.Count ?? 0;

        /// <summary>
        /// 持久化对象池（切场景不销毁）。
        /// </summary>
        public bool DontDestroy { get; set; }

        /// <summary>
        /// 手动标记销毁。
        /// </summary>
        public bool MarkedForDestroy { get; set; }

        /// <summary>
        /// 对象池最大容量。
        /// </summary>
        public int MaxCapacity { get => _maxCapacity; set => _maxCapacity = value; }

        /// <summary>
        /// 对象池是否被销毁。
        /// </summary>
        public bool IsDestroyed { get; private set; }

        private CancellationToken DestroyToken
            => _destroyCancellationTokenSource != null ? _destroyCancellationTokenSource.Token : CancellationToken.None;

        /// <summary>
        /// 从内存池获取时回调。
        /// </summary>
        public override void InitFromPool()
        {
            IsDestroyed = false;
        }

        public static GameObjectPool Create(Transform poolRoot, string location, int initCapacity = 0,
            int maxCapacity = int.MaxValue, float autoDestroyTime = -1f, bool dontDestroy = false,
            bool allowMultiSpawn = false)
        {
            NormalizeCapacity(ref initCapacity, ref maxCapacity);
            GameObjectPool pool = MemoryPool.Alloc<GameObjectPool>();
            pool._destroyCancellationTokenSource = new CancellationTokenSource();
            pool._parent = new GameObject($"POOL-[{location}]");
            pool._parent.transform.SetParent(poolRoot, false);
            pool.Location = location;
            pool._initCapacity = initCapacity;
            pool._maxCapacity = maxCapacity;
            pool._autoDestroyTime = autoDestroyTime;
            pool.DontDestroy = dontDestroy;
            pool.MarkedForDestroy = false;
            pool._goPool = new Queue<GameObject>(initCapacity);
            pool._resourceModule = ModuleSystem.GetModule<IResourceModule>();
            pool._allowMultiSpawn = allowMultiSpawn;
            return pool;
        }

        public async UniTask<bool> CreatePoolAsync(CancellationToken ct = default)
            => await EnsureCapacityAsync(_initCapacity, ct);

        public async UniTask<bool> ConfigureAsync(int initCapacity, int maxCapacity, float autoDestroyTime,
            bool dontDestroy, bool allowMultiSpawn, CancellationToken ct = default)
        {
            NormalizeCapacity(ref initCapacity, ref maxCapacity);

            var oldInitCapacity = _initCapacity;
            var oldMaxCapacity = _maxCapacity;
            var oldAutoDestroyTime = _autoDestroyTime;
            var oldDontDestroy = DontDestroy;
            var oldAllowMultiSpawn = _allowMultiSpawn;

            _initCapacity = initCapacity;
            _maxCapacity = maxCapacity;
            _autoDestroyTime = autoDestroyTime;
            DontDestroy = dontDestroy;
            _allowMultiSpawn = allowMultiSpawn;

            TrimInactiveObjectsToCapacity();

            var success = await EnsureCapacityAsync(_initCapacity, ct);
            if (success)
            {
                return true;
            }

            _initCapacity = oldInitCapacity;
            _maxCapacity = oldMaxCapacity;
            _autoDestroyTime = oldAutoDestroyTime;
            DontDestroy = oldDontDestroy;
            _allowMultiSpawn = oldAllowMultiSpawn;
            TrimInactiveObjectsToCapacity();
            return false;
        }

        private async UniTask<bool> EnsureCapacityAsync(int targetCapacity, CancellationToken ct)
        {
            if (IsDestroyed || MarkedForDestroy)
            {
                return false;
            }

            targetCapacity = Mathf.Clamp(targetCapacity, 0, _maxCapacity);

            while (Count < targetCapacity)
            {
                var go = await LoadPoolGameObjectAsync(ct);
                if (go == null)
                {
                    Log.Warning($"对象池初始化未完全成功: {Location}。目前对象池容量: {Count}/{targetCapacity}");
                    return false;
                }

                go.SetActive(false);
                _goPool.Enqueue(go);
            }
            MarkIdleTimeIfNeeded();
            return true;
        }

        public async UniTask<GameObject> SpawnAsync(Transform parent, Vector3 position,
            Quaternion rotation, CancellationToken ct = default)
        {
            if (IsDestroyed || MarkedForDestroy)
            {
                return null;
            }

            GameObject go = null;

            if (_goPool.Count > 0)
            {
                go = _goPool.Dequeue();
            }
            else if (Count >= _maxCapacity)
            {
                if (_allowMultiSpawn && _spawnedPool.Count > 0)
                {
                    go = _spawnedPool.First.Value;
                    _spawnedPool.RemoveFirst();
                    _spawnedNodeDict.Remove(go);
                    Log.Warning($"强制复用正在使用的对象: {go.name}");
                }
                else
                {
                    Log.Warning($"对象池容量已满，无法继续生成对象: {Location}");
                    return null;
                }
            }

            if (go == null)
            {
                go = await LoadPoolGameObjectAsync(ct);
            }

            if (go == null || IsDestroyed || MarkedForDestroy)
            {
                if (go != null)
                {
                    DestroyGameObject(go);
                }

                return null;
            }

            go.transform.SetParent(parent, false);
            if (parent == null)
            {
                SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            }
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.SetActive(true);
            _spawnedNodeDict[go] = _spawnedPool.AddLast(go);
            return go;
        }

        /// <summary>
        /// 回收对象
        /// </summary>
        /// <param name="go"></param>
        public void Recycle(GameObject go)
        {
            if (go == null)
            {
                Log.Warning("尝试回收 null 对象");
                return;
            }
            if (IsDestroyed)
            {
                DestroyGameObject(go);
                return;
            }

            if (!_spawnedNodeDict.Remove(go, out var node))
            {
                Log.Warning($"对象不在已生成列表中，可能已回收: {go.name}");
                return;
            }

            _spawnedPool.Remove(node);

            if (!MarkedForDestroy && Count < _maxCapacity && _parent != null)
            {
                go.SetActive(false);
                go.transform.SetParent(_parent.transform, false);
                go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                _goPool.Enqueue(go);
            }
            else
            {
                DestroyGameObject(go);
            }
            MarkIdleTimeIfNeeded();
        }

        /// <summary>
        /// 丢弃对象
        /// </summary>
        /// <param name="go"></param>
        public void Remove(GameObject go)
        {
            if (go == null)
            {
                Log.Warning("尝试丢弃 null 对象");
                return;
            }
            if (IsDestroyed)
            {
                DestroyGameObject(go);
                return;
            }

            if (!_spawnedNodeDict.Remove(go, out var node))
            {
                Log.Warning($"对象不在已生成列表中，可能已丢弃或回收: {go.name}");
                return;
            }

            _spawnedPool.Remove(node);
            MarkIdleTimeIfNeeded();
            DestroyGameObject(go);
        }

        private void MarkIdleTimeIfNeeded()
        {
            if (SpawnedCount <= 0)
            {
                _lastRecycleTime = Time.realtimeSinceStartup;
            }
        }

        /// <summary>
        /// 是否可以进行自动销毁对象池
        /// </summary>
        /// <returns></returns>
        public bool CanAutoDestroy()
        {
            if (DontDestroy)
            {
                return false;
            }

            if (MarkedForDestroy)
            {
                return SpawnedCount <= 0;
            }

            if (_autoDestroyTime <= 0f)
            {
                return false;
            }

            if (_lastRecycleTime > 0 && SpawnedCount <= 0)
            {
                return Time.realtimeSinceStartup - _lastRecycleTime > _autoDestroyTime;
            }
            return false;
        }

        internal void FillDebugInfo(GameObjectPoolDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            info.Location = Location;
            info.Count = Count;
            info.SpawnedCount = SpawnedCount;
            info.NoSpawnCount = NoSpawnCount;
            info.MaxCapacity = _maxCapacity;
            info.AutoDestroyTime = _autoDestroyTime;
            info.IdleTime = _lastRecycleTime > 0f && SpawnedCount <= 0
                ? Time.realtimeSinceStartup - _lastRecycleTime
                : 0f;
            info.DontDestroy = DontDestroy;
            info.MarkedForDestroy = MarkedForDestroy;
            info.IsDestroyed = IsDestroyed;
            info.CanAutoDestroy = CanAutoDestroy();
            info.Objects.Clear();

            if (_goPool != null)
            {
                foreach (var go in _goPool)
                {
                    AddObjectDebugInfo(info.Objects, go, false);
                }
            }

            foreach (var go in _spawnedPool)
            {
                AddObjectDebugInfo(info.Objects, go, true);
            }
        }

        private static void AddObjectDebugInfo(List<GameObjectPoolObjectDebugInfo> results, GameObject go, bool spawned)
        {
            if (go == null)
            {
                return;
            }

            var objectInfo = new GameObjectPoolObjectDebugInfo
            {
                Name = go.name,
                Spawned = spawned,
                ActiveSelf = go.activeSelf,
                Parent = go.transform.parent,
                GameObject = go,
            };

            if (go.TryGetComponent<GameObjectPoolIdentity>(out var identity))
            {
                objectInfo.PoolKey = identity.PoolKey;
            }

            results.Add(objectInfo);
        }

        /// <summary>
        /// 销毁对象池
        /// </summary>
        public void Destroy()
        {
            if (IsDestroyed)
            {
                return;
            }

            IsDestroyed = true;
            CancelDestroyToken();
            MemoryPool.Dealloc(this);
        }

        private async UniTask<GameObject> LoadPoolGameObjectAsync(CancellationToken externalToken)
        {
            var operationToken = CreateOperationToken(externalToken, out var linkedTokenSource);
            try
            {
                var go = await _resourceModule.LoadGameObjectAsync(Location, _parent.transform, operationToken);
                if (go == null)
                {
                    if (operationToken.IsCancellationRequested || IsDestroyed)
                    {
                        return null;
                    }

                    Log.Error($"创建对象池失败: {Location}");
                    return null;
                }

                if (operationToken.IsCancellationRequested || IsDestroyed)
                {
                    DestroyGameObject(go);
                    return null;
                }

                MarkPooledObject(go);
                return go;
            }
            finally
            {
                linkedTokenSource?.Dispose();
            }
        }

        private CancellationToken CreateOperationToken(CancellationToken externalToken,
            out CancellationTokenSource linkedTokenSource)
        {
            linkedTokenSource = null;

            if (!externalToken.CanBeCanceled)
            {
                return DestroyToken;
            }

            linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalToken, DestroyToken);
            return linkedTokenSource.Token;
        }

        private void CancelDestroyToken()
        {
            if (_destroyCancellationTokenSource == null)
            {
                return;
            }

            if (!_destroyCancellationTokenSource.IsCancellationRequested)
            {
                _destroyCancellationTokenSource.Cancel();
            }
        }

        private void DestroyGameObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            UnityEngine.Object.Destroy(go);
        }

        private void DestroyAllGameObject()
        {
            if (_goPool == null)
            {
                _spawnedPool.Clear();
                _spawnedNodeDict.Clear();
                return;
            }

            while (_goPool.Count > 0)
            {
                var go = _goPool.Dequeue();
                DestroyGameObject(go);
            }

            var curNode = _spawnedPool.First;

            while (curNode != null)
            {
                var nextNode = curNode.Next;
                DestroyGameObject(curNode.Value);
                curNode = nextNode;
            }

            _goPool.Clear();
            _goPool = null;
            _spawnedPool.Clear();
            _spawnedNodeDict.Clear();
        }

        private void MarkPooledObject(GameObject go)
        {
            if (!go.TryGetComponent<GameObjectPoolIdentity>(out var identity))
            {
                identity = go.AddComponent<GameObjectPoolIdentity>();
            }
            identity.PoolKey = Location;
        }

        private void TrimInactiveObjectsToCapacity()
        {
            if (_goPool == null)
            {
                return;
            }

            int targetInactiveCount = Mathf.Max(0, _maxCapacity - SpawnedCount);
            while (_goPool.Count > targetInactiveCount)
            {
                DestroyGameObject(_goPool.Dequeue());
            }
        }

        private static void NormalizeCapacity(ref int initCapacity, ref int maxCapacity)
        {
            initCapacity = Mathf.Max(0, initCapacity);
            maxCapacity = Mathf.Max(0, maxCapacity);

            if (initCapacity > maxCapacity)
            {
                initCapacity = maxCapacity;
            }
        }

        /// <summary>
        /// 回收到内存池时清理状态。
        /// </summary>
        public override void RecycleToPool()
        {
            var cts = _destroyCancellationTokenSource;
            _destroyCancellationTokenSource = null;
            cts?.Dispose();
            DestroyAllGameObject();
            UnityEngine.Object.Destroy(_parent);
            _parent = null;
            _initCapacity = 0;
            _autoDestroyTime = 0f;
            _lastRecycleTime = -1f;
            _maxCapacity = 0;
            _resourceModule = null;
            _allowMultiSpawn = false;
            Location = null;
            DontDestroy = false;
            MarkedForDestroy = false;
        }
    }
}
