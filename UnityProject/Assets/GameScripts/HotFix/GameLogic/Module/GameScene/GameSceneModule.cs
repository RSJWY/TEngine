using System;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 场景类型枚举：与具体场景资源地址（YooAsset location）一一对应。
    /// </summary>
    /// <remarks>
    /// Tips，往后追加，不要插入，以免顺序错误
    /// 更新后前往<see cref="GameSceneModule.RecordScene"/>和<see cref="GameSceneModule.GetSceneName"/>做正反向查询更新
    /// </remarks>
    public enum SceneType
    {
        /// <summary>
        /// 主场景（空，保留，未启用，仅仅做纯UI）
        /// </summary>
        MainScene
    }

    /// <summary>
    /// 场景名
    /// </summary>
    /// <remarks>
    /// 场景资源地址常量：与 SceneType 枚举一一对应，修改需同步。
    /// 更新后前往<see cref="GameSceneModule.RecordScene"/>和<see cref="GameSceneModule.GetSceneName"/>做正反向查询更新
    /// </remarks>
    public static class SceneConstName
    {
        public const string MainSceneName = "MainScene";

        public const string HangarUI = "机库";
    }

    /// <summary>
    /// 场景数据与跳转模块。
    /// </summary>
    /// <remarks>
    /// 职责：① 把 <see cref="SceneType"/> 映射成场景资源地址；② 提供统一的场景跳转入口，
    /// 经 <see cref="GameModule.UI"/> 直接打开 <see cref="SwitchUI"/> 加载页（场景切换不再走 UIJump，
    /// 避免过渡页被压入 UIJump 导航栈导致返回栈残留；SwitchUI 是过渡页不参与业务返回导航）；
    /// ③ 记录上一个关卡与当前关卡；④ 用 <see cref="IGameSceneEvent.OnSceneLoadOver"/> 协调"加载就绪 → 激活"。
    /// <para>
    /// <b>事件协调时序</b>：入口记录场景切换状态并注册 OnSceneLoadOver 监听 → GameModule.UI.ShowUI&lt;SwitchUI&gt; 打开加载页 →
    /// 加载页进度到 0.9 发 OnSceneLoadOver → 本模块 OnSceneLoadOver 直接 UnSuspend 激活场景；
    /// 加载页的 90%→100% 收尾动画 + 停留负责遮盖激活卡顿，走满后由加载页执行 finishCallBack 并关闭自身。
    /// </para>
    /// </remarks>
    public sealed class GameSceneModule : Module, IGameSceneModule
    {
        /// <summary>
        /// UI 跳转模块引用：仅用于场景激活后回到主菜单窗口（<see cref="UIJumpControl.JumpToMain"/>）。
        /// </summary>
        private IUIJumpControl _jumpControl;

        /// <summary>
        /// 事件管理器：统一管理场景切换流程中的临时事件监听，避免手动配对泄漏。
        /// </summary>
        private readonly GameEventMgr _eventMgr = new GameEventMgr();

        /// <summary>
        /// 调试用：跳过加载页动画（预热 + 收尾），场景加载完成后立即激活并关闭加载页。
        /// </summary>
        /// <remarks>
        /// 仅影响 SwitchUI 的三段式进度动画，不影响实际场景加载过程。
        /// Editor 调试时设为 true 可省去等待时间；发布时保持 false。
        /// </remarks>
        public bool SkipLoadingAnimation { get; set; } = false;

        /// <summary>
        /// 上一个关卡类型。
        /// </summary>
        public SceneType? PreviousSceneType { get; private set; }

        /// <summary>
        /// 当前关卡类型。
        /// </summary>
        public SceneType? CurrentSceneType { get; private set; }

        /// <summary>
        /// 上一个关卡资源名。
        /// </summary>
        public string PreviousSceneName => PreviousSceneType.HasValue ? GetSceneName(PreviousSceneType.Value) : string.Empty;

        /// <summary>
        /// 当前关卡资源名。
        /// </summary>
        public string CurrentSceneName => CurrentSceneType.HasValue ? GetSceneName(CurrentSceneType.Value) : string.Empty;

        public override void OnInit()
        {
            _jumpControl = GameModule.UIJumpControl;
        }

        public override void Shutdown()
        {
            _eventMgr.Clear();
            _jumpControl = null;
            PreviousSceneType = null;
            CurrentSceneType = null;
        }

        /// <summary>
        /// 记录场景切换前后的关卡状态。
        /// </summary>
        /// <param name="sceneType">即将进入的目标关卡类型。</param>
        private void RecordScene(SceneType sceneType)
        {
            PreviousSceneType = CurrentSceneType;
            CurrentSceneType = sceneType;

            // 同步到全局数据
            GameValueStatic.PreviousSceneType = PreviousSceneType;
            GameValueStatic.CurrentSceneType = CurrentSceneType;

            var prevInfo = PreviousSceneType.HasValue
                ? $"{PreviousSceneType.Value}:{GetSceneName(PreviousSceneType.Value)}"
                : "无";
            Debug.Log($"[场景切换] {prevInfo} → {sceneType}:{GetSceneName(sceneType)}");
        }

        /// <summary>
        /// 把场景类型枚举转为场景资源地址字符串。
        /// </summary>
        /// <param name="sceneType">场景类型。</param>
        /// <returns>场景资源地址；未匹配返回空串。</returns>
        public string GetSceneName(SceneType sceneType)
        {
            switch (sceneType)
            {
                case SceneType.MainScene:
                    return SceneConstName.MainSceneName;
                default:
                    Log.Error($"SceneType:[{sceneType}]未匹配资源地址");
                    return string.Empty;
            }
        }

        /// <summary>
        /// 把场景资源地址字符串逆转换为场景类型枚举。
        /// </summary>
        /// <param name="sceneName">场景资源地址（支持 SceneConstName 定义的常量值或 SceneType 枚举字符串）。</param>
        /// <returns>匹配的 SceneType；未匹配返回 null。</returns>
        /// <remarks>
        /// 支持两种输入格式：
        /// ① 实际资源名（如 "MainScene", "飞行测试"）；
        /// ② SceneType 枚举名（如 "Main", "FlyTest"）—— 用于解析回放文件名中的场景标识。
        /// </remarks>
        public SceneType? GetSceneTypeFromName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Log.Error("场景名为空，无法解析 SceneType");
                return null;
            }

            // 优先匹配实际资源名
            if (sceneName == SceneConstName.MainSceneName)
                return SceneType.MainScene;

            // 尝试解析枚举名（用于回放文件名格式：Replay_FlyTest_20260622.replay）
            if (System.Enum.TryParse<SceneType>(sceneName, true, out SceneType sceneType))
                return sceneType;

            Log.Error($"场景名:[{sceneName}]未匹配任何 SceneType");
            return null;
        }

        #region 场景跳转入口

        /// <summary>
        /// 通用场景加载入口：打开加载页，加载并激活目标场景。
        /// </summary>
        /// <param name="sceneType">目标场景类型。</param>
        /// <param name="finishCallBack">场景激活并显示后的完成回调（可为空）。</param>
        /// <remarks>
        /// 流程：记录上一个/当前关卡 → 监听 <see cref="IGameSceneEvent.OnSceneLoadOver"/> → 构造 <see cref="LoadSceneDataBody"/>
        /// → GameModule.UI.ShowUI&lt;SwitchUI&gt; 打开加载页；加载页加载就绪后发 OnSceneLoadOver，
        /// 本模块 OnSceneLoadOver 直接 UnSuspend 激活挂起的目标场景。
        /// 最后派发 <see cref="IGameSceneEvent.OnSceneReady"/> 通知场景内逻辑初始化。
        /// </remarks>
        public void LoadScene(SceneType sceneType, Action finishCallBack = null)
        {
            RecordScene(sceneType);
            GameEvent.Get<IGameSceneEvent>().OnSceneLoadStart(sceneType);

            // 清理旧监听，避免上次加载异常退出时残留
            _eventMgr.Clear();
            _eventMgr.AddEvent(IGameSceneEvent_Event.OnSceneLoadOver, OnSceneLoadOver);

            LoadSceneDataBody loadSceneData = new LoadSceneDataBody()
            {
                sceneName = GetSceneName(sceneType),
                finishCallBack = () =>
                {
                    finishCallBack?.Invoke();
                    GameEvent.Get<IGameSceneEvent>().OnSceneReady(sceneType);
                }
            };

            GameModule.UI.ShowUI<SwitchUI>(loadSceneData);
        }

        
        /// <summary>
        /// 回到主菜单场景：若正在录制回放则停止，加载 MainScene 后回到主菜单窗口。
        /// </summary>
        /// <remarks>
        /// finishCallBack 内执行 CloseAll + jumpControl.JumpToMain()，确保加载页关闭后主菜单干净弹出。
        /// </remarks>
        public void JumpToMainScene()
        {
            RecordScene(SceneType.MainScene);
            GameEvent.Get<IGameSceneEvent>().OnSceneLoadStart(SceneType.MainScene);

            // 清理旧监听，避免上次加载异常退出时残留
            _eventMgr.Clear();
            _eventMgr.AddEvent(IGameSceneEvent_Event.OnSceneLoadOver, OnSceneLoadOver);

            LoadSceneDataBody loadSceneData = new LoadSceneDataBody()
            {
                sceneName = GetSceneName(SceneType.MainScene),
                finishCallBack = () =>
                {
                    GameModule.UI.CloseAll();
                    _jumpControl?.JumpToMain();
                    GameEvent.Get<IGameSceneEvent>().OnSceneReady(SceneType.MainScene);
                }
            };

            GameModule.UI.ShowUI<SwitchUI>(loadSceneData);
        }

        /// <summary>
        /// 收到加载就绪事件后，激活挂起的目标场景。
        /// </summary>
        /// <remarks>
        /// 场景资源由 <see cref="SwitchUI"/> 以 suspendLoad=true 加载到 0.9 后发 <see cref="IGameSceneEvent.OnSceneLoadOver"/>；
        /// 本方法作为基础设施层入口，直接调用 <c>GameModule.Scene.UnSuspend</c> 激活场景。
        /// 激活时机仍为进度 90%，SwitchUI 的收尾动画（90%→100% + 停留）负责遮盖激活卡顿。
        /// 激活后清理监听；事件管理由 GameEventMgr 统一处理。
        /// </remarks>
        private void OnSceneLoadOver()
        {
            if (string.IsNullOrEmpty(CurrentSceneName))
            {
                Log.Warning("[GameScene] 当前场景名为空，跳过激活。");
                _eventMgr.Clear();
                return;
            }

            // 激活挂起的目标场景：sceneName 取自本类记录的当前关卡，数据归属与操作归属统一在管理器侧。
            GameModule.Scene.UnSuspend(CurrentSceneName);
            _eventMgr.Clear();
        }

        #endregion
    }
}
