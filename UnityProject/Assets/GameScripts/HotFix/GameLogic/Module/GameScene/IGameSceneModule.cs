using System;

namespace GameLogic
{
    /// <summary>
    /// 游戏业务场景模块接口。
    /// </summary>
    public interface IGameSceneModule
    {
        bool SkipLoadingAnimation { get; set; }

        SceneType? PreviousSceneType { get; }

        SceneType? CurrentSceneType { get; }

        string PreviousSceneName { get; }

        string CurrentSceneName { get; }

        /// <summary>
        /// 当前场景加载展示进度（0~1，已平滑）。供 <see cref="SwitchUI"/> 每帧读取渲染，加载控制由模块独占。
        /// </summary>
        float DisplayProgress { get; }

        string GetSceneName(SceneType sceneType);

        SceneType? GetSceneTypeFromName(string sceneName);

        void LoadScene(SceneType sceneType, Action finishCallBack = null);

        void JumpToMainScene();
    }
}
