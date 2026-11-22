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
        /// 加载页进度到达 100%（展示进度满格时刻，进入关页等待阶段前，每次加载会话广播一次）。
        /// <remarks>此时尚未关闭加载页、尚未派发 OnSceneReady，业务可在关页前介入（如收尾提示、埋点等）。</remarks>
        /// </summary>
        void OnSceneLoadProgressFull(SceneType sceneType);

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
