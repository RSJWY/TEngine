
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>
    /// 机库场景加载脚本
    /// </summary>
    public class HangarSceneSpawner:DynamicSceneSpawner
    {
        protected override List<SpawnItem> CollectSpawnItems()
        {
            return CollectFromSpawnPoints();
        }
    }
}