using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// GameObject 对象池调试快照。
    /// </summary>
    public sealed class GameObjectPoolDebugInfo
    {
        /// <summary>
        /// 池内对象明细列表。
        /// </summary>
        public readonly List<GameObjectPoolObjectDebugInfo> Objects = new List<GameObjectPoolObjectDebugInfo>();

        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public string Location;
        /// <summary>
        /// 对象池总容量。
        /// </summary>
        public int Count;
        /// <summary>
        /// 正在被使用的游戏对象个数。
        /// </summary>
        public int SpawnedCount;
        /// <summary>
        /// 没在使用的游戏对象个数。
        /// </summary>
        public int NoSpawnCount;
        /// <summary>
        /// 对象池最大容量。
        /// </summary>
        public int MaxCapacity;
        /// <summary>
        /// 自动销毁时间。
        /// </summary>
        public float AutoDestroyTime;
        /// <summary>
        /// 闲置时长。
        /// </summary>
        public float IdleTime;
        /// <summary>
        /// 是否持久化（切场景不销毁）。
        /// </summary>
        public bool DontDestroy;
        /// <summary>
        /// 是否被手动标记销毁。
        /// </summary>
        public bool MarkedForDestroy;
        /// <summary>
        /// 对象池是否已被销毁。
        /// </summary>
        public bool IsDestroyed;
        /// <summary>
        /// 是否可以自动销毁。
        /// </summary>
        public bool CanAutoDestroy;
    }

    /// <summary>
    /// GameObject 对象池内单个对象的调试信息。
    /// </summary>
    public sealed class GameObjectPoolObjectDebugInfo
    {
        /// <summary>
        /// 对象名称。
        /// </summary>
        public string Name;
        /// <summary>
        /// 所属对象池定位地址。
        /// </summary>
        public string PoolKey;
        /// <summary>
        /// 是否正在被使用。
        /// </summary>
        public bool Spawned;
        /// <summary>
        /// 是否激活。
        /// </summary>
        public bool ActiveSelf;
        /// <summary>
        /// 父节点。
        /// </summary>
        public Transform Parent;
        /// <summary>
        /// 游戏对象引用。
        /// </summary>
        public GameObject GameObject;
    }
}
