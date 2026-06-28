using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 场景流程模块间事件。
    /// </summary>
    [EventInterface(EEventGroup.GroupLogic)]
    public interface IGameSceneEvent
    {
        /// <summary>
        /// 场景切换开始。
        /// </summary>
        void OnSceneLoadStart(SceneType sceneType);

        /// <summary>
        /// 场景资源加载到可激活状态。
        /// </summary>
        void OnSceneLoadOver();

        /// <summary>
        /// 场景动态对象全部加载完成。
        /// </summary>
        void OnDynamicSpawnComplete(SceneType sceneType);

        /// <summary>
        /// 场景切换完成。
        /// </summary>
        void OnSceneReady(SceneType sceneType);
    }
}
