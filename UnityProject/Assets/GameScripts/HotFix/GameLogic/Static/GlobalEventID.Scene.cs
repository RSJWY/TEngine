using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 全局事件 ID — 场景相关事件。
    /// </summary>
    public static partial class GlobalEventID
    {
        #region 场景事件

        /// <summary>
        /// 场景切换开始事件（LoadingUI 打开时触发）。
        /// </summary>
        /// <remarks>
        /// 发送方：<see cref="LoadingUI"/>，在 OnCreate 中发送，携带目标场景类型作为参数。
        /// 监听方：需要在场景切换开始时执行清理或准备逻辑的任意模块/UI。
        /// 与 <see cref="Event_SceneReady"/> 成对使用，分别标记场景切换的开始和完成。
        /// </remarks>
        public static readonly int Event_SceneLoadStart = RuntimeId.ToRuntimeId("SCENE_LOAD_START");

        /// <summary>
        /// 场景加载就绪事件。
        /// </summary>
        /// <remarks>
        /// 发送方：<see cref="LoadingUI"/>，在 progressCallBack 进度到达 0.9（场景资源加载完成、挂起待激活）时发送。
        /// 监听方：<see cref="GameSceneManager"/>（LoadScene/SetReplayLoadScene/JumpToMainScene 入口注册），
        /// 收到后直接调用 <c>GameModule.Scene.UnSuspend</c> 激活挂起的目标场景。
        /// </remarks>
        public static readonly int Event_LoadOver = RuntimeId.ToRuntimeId("LOADPAGE_LOADOVER");

        /// <summary>
        /// 场景动态对象全部加载完成事件。
        /// </summary>
        /// <remarks>
        /// 参数：<see cref="SceneType"/>（当前完成加载的场景类型，供监听方校验）。
        /// 发送方：<see cref="DynamicSceneSpawner"/>，在所有 DynamicSpawnPoint 对应的预制体分批加载完毕后发送。
        /// 监听方：需要等待场景内动态对象全部就位后再执行逻辑的任意模块（如过场动画、截图、开放操作等）。
        /// </remarks>
        public static readonly int Event_DynamicSpawnComplete = RuntimeId.ToRuntimeId("DYNAMIC_SPAWN_COMPLETE");

        /// <summary>
        /// 场景切换完成事件（LoadingUI 关闭后触发）。
        /// </summary>
        /// <remarks>
        /// 发送方：<see cref="GameSceneManager"/>，在 finishCallBack 执行完毕后发送。
        /// 监听方：新场景中需要在切换完成后执行初始化逻辑的任意模块/UI。
        /// 配合 <see cref="GameSceneManager.IsSceneReady"/> 使用：事件用于被动通知，属性用于主动查询。
        /// </remarks>
        public static readonly int Event_SceneReady = RuntimeId.ToRuntimeId("SCENE_READY");

        #endregion
    }
}
