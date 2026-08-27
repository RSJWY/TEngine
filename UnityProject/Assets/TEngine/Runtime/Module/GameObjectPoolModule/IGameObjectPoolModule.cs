using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// GameObject 对象池模块接口。
    /// <para>提供基于 YooAsset location 的异步实例化、预热、回收、自动销毁等能力。</para>
    /// </summary>
    public interface IGameObjectPoolModule
    {
        /// <summary>
        /// 对象池根节点。
        /// </summary>
        GameObject PoolRoot { get; }

        /// <summary>
        /// 异步预创建 GameObjectPool。
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="initCapacity">初始容量</param>
        /// <param name="maxCapacity">最大容量</param>
        /// <param name="autoDestroyTime">自动销毁时间（&lt;=0 不自动销毁）</param>
        /// <param name="dontDestroy">持久化（切场景不销毁）</param>
        /// <param name="allowMultiSpawn">是否允许超容复用正在使用的对象</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>创建的对象池；失败返回 null。</returns>
        UniTask<GameObjectPool> CreateGameObjectPoolAsync(string location, int initCapacity = 0,
            int maxCapacity = int.MaxValue, float autoDestroyTime = -1f, bool dontDestroy = false,
            bool allowMultiSpawn = false, CancellationToken ct = default);

        /// <summary>
        /// 异步实例化一个游戏对象。
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="ct">取消令牌</param>
        UniTask<GameObject> SpawnAsync(string location, CancellationToken ct = default);

        /// <summary>
        /// 异步实例化一个游戏对象。
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="parent">父物体</param>
        /// <param name="ct">取消令牌</param>
        UniTask<GameObject> SpawnAsync(string location, Transform parent, CancellationToken ct = default);

        /// <summary>
        /// 异步实例化一个游戏对象。
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="parent">父物体</param>
        /// <param name="position">本地坐标</param>
        /// <param name="rotation">本地角度</param>
        /// <param name="ct">取消令牌</param>
        UniTask<GameObject> SpawnAsync(string location, Transform parent, Vector3 position,
            Quaternion rotation, CancellationToken ct = default);

        /// <summary>
        /// 回收对象到所属池。
        /// </summary>
        /// <param name="gameObject"></param>
        void Recycle(GameObject gameObject);

        /// <summary>
        /// 丢弃对象（不归还池，直接销毁）。
        /// </summary>
        /// <param name="gameObject"></param>
        void Remove(GameObject gameObject);

        /// <summary>
        /// 获取指定 location 的对象池。
        /// </summary>
        GameObjectPool GetGameObjectPool(string location);

        /// <summary>
        /// 尝试获取指定 location 的对象池。
        /// </summary>
        bool TryGetGameObjectPool(string location, out GameObjectPool pool);

        /// <summary>
        /// 获取对象池调试快照。
        /// </summary>
        /// <param name="results">输出列表，由调用方负责复用。</param>
        void GetDebugInfos(List<GameObjectPoolDebugInfo> results);

        /// <summary>
        /// 销毁指定对象池。
        /// </summary>
        /// <param name="location">资源定位地址</param>
        void DestroyPool(string location);

        /// <summary>
        /// 销毁所有对象池。
        /// </summary>
        /// <param name="includeAll">是否包括常驻对象池</param>
        void DestroyAllPool(bool includeAll);
    }
}
