using System;
using Cysharp.Threading.Tasks;

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

        /// <summary>
        /// 当前加载阶段富文本（按 <see cref="DisplayProgress"/> 查 SO 配置返回），供 <see cref="SwitchSceneUI"/> 顶部大字体显示。
        /// </summary>
        string DisplayPhaseText { get; }

        /// <summary>
        /// 随机获取一条小贴士文本（从 <see cref="SceneLoadTipsConfig.Tips"/> 中取）。
        /// </summary>
        /// <returns>小贴士文本；配置为空返回空串。</returns>
        string GetRandomTip();

        /// <summary>
        /// 注册场景初始化任务（无进度源）：加载页业务就绪段等待其完成，该任务完成前进度条不推进。
        /// </summary>
        /// <remarks>
        /// 供场景激活后需要加载初始化资源的一方调用（如 <see cref="DynamicSceneSpawner"/> 的 LoadingWait 模式、
        /// 场景内预加载逻辑）。须在加载会话期间（场景激活阶段，即加载页 80% 检查点之后、关页前）注册；
        /// 会话未激活时任务直接 Forget 后台执行，不等待。
        /// </remarks>
        /// <param name="task">初始化任务。</param>
        /// <param name="label">任务标签（用于日志定位）。</param>
        void RegisterInitTask(UniTask task, string label = null);

        /// <summary>
        /// 注册场景初始化任务（带进度源）：加载页 90%→100% 业务就绪段按任务数平分驱动（各任务进度求和 ÷ 任务数）。
        /// </summary>
        /// <param name="task">初始化任务。</param>
        /// <param name="progressProvider">进度源（0~1，应单调递增，如 已加载数/总数）；null 时该任务完成前按 0 计。</param>
        /// <param name="label">任务标签（用于日志定位）。</param>
        void RegisterInitTask(UniTask task, Func<float> progressProvider, string label = null);

        string GetSceneName(SceneType sceneType);

        SceneType? GetSceneTypeFromName(string sceneName);

        /// <summary>
        /// 通用场景加载入口：打开加载页，加载并激活目标场景。
        /// 三个时长参数传 null 走默认值，传 0 跳过对应阶段的动画。
        /// </summary>
        /// <param name="sceneType">目标场景类型。</param>
        /// <param name="finishCallBack">场景激活并显示后的完成回调（可为空）。</param>
        /// <param name="warmupDuration">阶段 0 预热时长（0→10%），秒。null=默认 0.7s，0=跳过预热。</param>
        /// <param name="finishDuration">阶段 3 激活段时长（80%→90%），秒。null=默认 2s，0=瞬间推进（仍等激活完成）。</param>
        /// <param name="holdAt100Duration">100% 停留时长，秒。null=默认 0.5s。</param>
        void LoadScene(SceneType sceneType, Action finishCallBack = null,
            float? warmupDuration = null, float? finishDuration = null, float? holdAt100Duration = null);

        void JumpToMainScene();
    }
}
