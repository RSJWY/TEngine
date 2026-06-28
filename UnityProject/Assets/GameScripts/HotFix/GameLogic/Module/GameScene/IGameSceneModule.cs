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

        string GetSceneName(SceneType sceneType);

        SceneType? GetSceneTypeFromName(string sceneName);

        void LoadScene(SceneType sceneType, Action finishCallBack = null);

        void JumpToMainScene();
    }
}
