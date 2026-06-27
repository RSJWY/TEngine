using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 场景业务管理器示例。
    /// </summary>
    /// <remarks>
    /// 这个脚本只作为实现参考，不承载具体场景业务。
    /// 实际项目里建议复制本类，改成 XxxManager，再按当前场景填写 TargetSceneType 和初始化逻辑。
    /// </remarks>
    public class ExampleSceneGameManager : SceneGameManagerBase<DynamicSceneSpawner>
    {
        /// <summary>
        /// 当前 Manager 负责的场景类型。
        /// 只有对应场景的 DynamicSceneSpawner 完成加载时，才会触发 OnSceneSpawnCompleted。
        /// </summary>
        protected override SceneType TargetSceneType => SceneType.MainScene;

        /// <summary>
        /// 动态场景对象全部加载完成后的场景初始化入口。
        /// 父类已处理 Spawner 查找、完成事件监听、轮询兜底和防重复调用。
        /// </summary>
        protected override void OnSceneSpawnCompleted()
        {
            Log.Info("[ExampleSceneGameManager] 动态场景对象加载完成，开始执行场景初始化。");

            // 示例：读取 DynamicSpawnPoint 上配置了 registerKey 的动态对象。
            // 使用时把 "PlayerSpawnRoot" 换成当前场景真实配置的 registerKey。
            GameObject playerSpawnRoot = GetSpawnedObject("PlayerSpawnRoot");
            if (playerSpawnRoot == null)
            {
                return;
            }

            // 在这里写当前场景自己的逻辑：
            // 1. 相机对准动态加载出的目标
            // 2. 开启交互组件
            // 3. 刷新 UI
            // 4. 播放入场动画
        }
    }
}
