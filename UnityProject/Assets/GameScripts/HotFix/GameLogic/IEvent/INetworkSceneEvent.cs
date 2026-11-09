using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 联机场景协调事件（NetworkSceneCoordinator 派发）。
    /// <para>与 <see cref="IGameSceneEvent"/> 区分：本接口只描述「服务器权威加载」这一层，
    /// 场景就绪语义仍由 <see cref="IGameSceneEvent.OnSceneReady"/> 承担，避免下游（如 DynamicSceneSpawner）受影响。</para>
    /// </summary>
    [EventInterface(EEventGroup.GroupLogic)]
    public interface INetworkSceneEvent
    {
        /// <summary>服务器收到切场景指令并开始权威加载（含客户端请求转内部加载）。</summary>
        /// <param name="sceneType">目标场景类型。</param>
        void OnServerSceneLoadRequested(SceneType sceneType);

        /// <summary>
        /// 服务器权威加载完成（场景已激活并写回状态；不代表 LoadingWait 动态生成完成）。
        /// </summary>
        /// <param name="sceneType">已加载完成的场景类型。</param>
        void OnServerSceneLoadCompleted(SceneType sceneType);

        /// <summary>场景加载失败（服务器侧已回滚场景状态）。</summary>
        /// <param name="sceneType">目标场景类型。</param>
        /// <param name="reason">失败原因（句柄错误信息）。</param>
        void OnSceneLoadFailed(SceneType sceneType, string reason);
    }
}
