
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>
    /// 通用动态场景加载脚本：从子节点的 DynamicSpawnPoint 收集加载项。
    /// </summary>
    public class SpawnPointSceneSpawner : DynamicSceneSpawner
    {
        protected override List<SpawnItem> CollectSpawnItems()
        {
            return CollectFromSpawnPoints();
        }
    }
}
