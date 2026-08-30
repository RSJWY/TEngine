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

        /// <summary>
        /// 通用场景加载入口：打开加载页，加载并激活目标场景。
        /// 三个时长参数传 null 走默认值，传 0 跳过对应阶段的动画。
        /// </summary>
        /// <param name="sceneType">目标场景类型。</param>
        /// <param name="finishCallBack">场景激活并显示后的完成回调（可为空）。</param>
        /// <param name="warmupDuration">阶段 0 预热时长（0→10%），秒。null=默认 0.7s，0=跳过预热。</param>
        /// <param name="finishDuration">阶段 2 收尾时长（90%→100%），秒。null=默认 2s，0=跳过收尾。</param>
        /// <param name="holdAt100Duration">100% 停留时长，秒。null=默认 0.5s。</param>
        void LoadScene(SceneType sceneType, Action finishCallBack = null,
            float? warmupDuration = null, float? finishDuration = null, float? holdAt100Duration = null);

        void JumpToMainScene();
    }
}
