using System;
using TEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    }

    /// <summary>
    /// 场景数据与跳转模块。
    /// </summary>
    /// <remarks>
    /// 职责：① 把 <see cref="SceneType"/> 映射成场景资源地址；② 提供统一的场景跳转入口，
    /// 经 <see cref="GameModule.UI"/> 直接打开 <see cref="SwitchUI"/> 加载页（场景切换不再走 UIJump，
    /// 避免过渡页被压入 UIJump 导航栈导致返回栈残留；SwitchUI 是过渡页不参与业务返回导航）；
    /// ③ 记录上一个关卡与当前关卡；④ <b>统一管理场景加载进度与激活控制</b>——三段式进度状态机、
    /// 资源加载（suspendLoad）、激活（UnSuspend）、完成回调与关闭加载页全部由本模块负责，
    /// <see cref="SwitchUI"/> 仅做展示（每帧读取 <see cref="DisplayProgress"/> 渲染）。
    /// <para>
    /// <b>三段式进度</b>（避免小场景加载过快导致 UI 闪过 / 进度条跳变）：
    /// <list type="number">
    /// <item><b>阶段 0 预热</b>（0→10%）：打开加载页后立即动画，完成后再发起真实加载；</item>
    /// <item><b>阶段 1 加载</b>（10%→90%）：progressCallBack 把 YooAsset 的 0~0.9 映射到 10%~90%；</item>
    /// <item><b>阶段 2 收尾</b>（90%→100%）：在 90% 立即激活场景，用最后 10% 动画 + 100% 停留<b>遮盖激活卡顿</b>，走满后才执行完成回调并关闭加载页。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>为何 90% 激活而非 100%</b>：激活会有一帧卡顿，留最后 10% 做视觉遮盖；若 100% 才激活，激活瞬间紧接加载页关闭会暴露突兀弹出。
    /// </para>
    /// <para>
    /// <b>激活权归属</b>：本模块即加载方，<see cref="EnterFinishPhase"/> 在阶段 1→2 直接调用
    /// <c>GameModule.Scene.UnSuspend</c> 激活场景，并派发 <see cref="IGameSceneEvent.OnSceneLoadOver"/> 通知观察方
    /// （基础设施层操作归基础设施层，不再走 UI 自发自收事件）。
    /// </para>
    /// <para>
    /// <b>陷阱 1</b>：suspendLoad=true + progressCallBack 时，<c>LoadSceneAsync</c> 内部 <c>while(!IsDone)</c> 一直 yield（SceneModule.cs:107-114），await 会死循环。故只 fire-and-forget，进度全由 progressCallBack 驱动。
    /// </para>
    /// <para>
    /// <b>陷阱 2</b>：suspendLoad 时 IsDone 永远 false，progressCallBack 每帧持续回调 value=0.9，会反复覆盖 <c>_targetProgress</c>。故 <see cref="OnLoadProgress"/> 在 <c>_currentPhase>=2</c> 后直接 return，保护收尾阶段的 target=1.0 不被打回 0.90（否则永远卡 90%）。
    /// </para>
    /// </remarks>
    public sealed class GameSceneModule : Module, IGameSceneModule, IUpdateModule
    {
        /// <summary>
        /// UI 跳转模块引用：仅用于场景激活后回到主菜单窗口（<see cref="UIJumpControl.JumpToMain"/>）。
        /// </summary>
        private IUIJumpControl _jumpControl;

        /// <summary>
        /// 调试用：跳过加载页动画（预热 + 收尾），场景加载完成后立即激活并关闭加载页。
        /// </summary>
        /// <remarks>
        /// 仅影响三段式进度动画，不影响实际场景加载过程。
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
            // 模块关闭：停止进度驱动，清理引用。不触发业务完成回调（避免在关闭流程中执行 CloseAll/JumpToMain 等副作用）。
            _isActive = false;
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

        #region 场景加载进度控制（三段式状态机）

        /// <summary>当前展示进度（平滑后驱动 UI 渲染），由 <see cref="Update"/> 每帧推进，UI 只读。</summary>
        /// <remarks>未在加载会话中时保留上一次的终值；新会话开始时在 <see cref="StartSceneLoad"/> 重置。</remarks>
        public float DisplayProgress => _displayProgress;

        // ===== 当前加载会话上下文 =====
        private bool _isActive = false;              // 是否处于加载会话（false 时 Update 直接 return）
        private SceneType _sceneType;                // 当前加载的目标场景类型（用于派发 OnSceneReady）
        private string _sceneName = "";              // 待加载并激活的场景资源地址
        private Action _finishCallBack = null;       // 场景激活后的完成回调（来自跳转入口）
        private bool _skipMode = false;              // 快速跳过模式（由 SkipLoadingAnimation 决定）

        // ===== 三段式进度控制 =====
        private int _currentPhase = 0;               // 当前阶段（0=预热, 1=加载, 2=收尾）

        /// <summary>目标进度（Update 平滑追赶此值）：阶段0=0.10、阶段1=0.10~0.90、阶段2=1.0。</summary>
        private float _targetProgress = 0f;

        /// <summary>展示进度（平滑后驱动 UI）。</summary>
        private float _displayProgress = 0f;

        /// <summary>阶段 0 预热动画时长（0→10%），秒。</summary>
        private const float WarmupDuration = 0.7f;

        /// <summary>阶段 0 预热速度 = 0.10 / WarmupDuration，每秒前进的进度值。</summary>
        private const float WarmupSpeed = 0.10f / WarmupDuration;

        /// <summary>阶段 1 加载追赶速度：平滑跟随 YooAsset 进度，避免跳变。值 1.0 表示 10%→90% 约需 0.8s。</summary>
        private const float LoadingSpeed = 1.0f;

        /// <summary>阶段 2 收尾动画时长（90%→100%），秒。</summary>
        private const float FinishDuration = 2f;

        /// <summary>阶段 2 收尾速度 = 0.10 / FinishDuration，每秒前进的进度值。</summary>
        private const float FinishSpeed = 0.10f / FinishDuration;

        /// <summary>到达 100% 后的停留时间（让用户看清 100%），秒。</summary>
        private const float HoldAt100Duration = 0.5f;

        private float _holdAt100Time = 0f;           // 已在 100% 停留的累计时间
        private bool _sceneLoadComplete = false;     // 真实加载已完成（YooAsset 进度到 0.9）
        private float _phase1ElapsedTime = 0f;       // 阶段 1 已持续时间（用于超时兜底）

        // ===== 防重标志 =====
        private bool _isSendLoadOver = false;        // 防重复激活场景 + 派发 OnSceneLoadOver（收尾阶段一次）
        private bool _isClosing = false;             // 防重复关闭加载页
        private bool _isFinished = false;            // 防重复触发完成回调（统一终结出口防重标志）

        /// <summary>
        /// 框架每帧轮询：按当前阶段驱动进度条平滑动画。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（scaled）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（unscaled，与原 LoadingUI 一致，暂停时加载页动画不冻结）。</param>
        /// <remarks>未处于加载会话（<c>_isActive=false</c>）时直接 return，避免空闲期空转。</remarks>
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (!_isActive)
            {
                return;
            }

            float dt = realElapseSeconds;

            switch (_currentPhase)
            {
                case 0: // 阶段 0 预热（0→10%）
                    _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, WarmupSpeed * dt);
                    if (_displayProgress >= 0.10f)
                    {
                        _currentPhase = 1;
                        StartRealLoading();
                    }
                    break;

                case 1: // 阶段 1 加载（10%→90%）
                    _phase1ElapsedTime += dt;
                    _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, LoadingSpeed * dt);

                    if (_sceneLoadComplete && _displayProgress >= 0.89f)
                    {
                        // 快速跳过模式：跳过收尾动画，直接激活场景并关闭
                        if (_skipMode)
                        {
                            _displayProgress = 1.0f;
                            EnterFinishPhase();
                            FinishAndClose();
                        }
                        else
                        {
                            EnterFinishPhase();
                        }
                    }
                    else if (_phase1ElapsedTime >= 5.0f) // 兜底：超时强制进入收尾
                    {
                        Log.Warning($"[GameScene] 阶段 1→2（超时兜底）：已等待 {_phase1ElapsedTime:F1}s，强制进入收尾");
                        EnterFinishPhase();
                    }
                    break;

                case 2: // 阶段 2 收尾（90%→100% + 停留）：动画遮盖激活卡顿
                    // 钳制 deltaTime：激活那一帧 realElapse 可能高达数百毫秒甚至秒级，
                    // 会把 90→100 动画和 100% 停留压缩成一帧瞬间完成（用户看不到 100%）。
                    // 用 0.05（约 20fps 步长）封顶，让收尾动画与停留按设定墙钟时长真实展开，确保用户看清 100%。
                    float clampedDelta = Mathf.Min(dt, 0.05f);
                    _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, FinishSpeed * clampedDelta);
                    if (_displayProgress >= 1.0f)
                    {
                        _displayProgress = 1.0f;
                        _holdAt100Time += clampedDelta;
                        // 停留结束 → 执行完成回调并关闭加载页（场景已在 90% 激活完毕）
                        if (!_isClosing && _holdAt100Time >= HoldAt100Duration)
                        {
                            FinishAndClose();
                        }
                    }
                    break;
            }
        }

        #endregion

        #region 场景跳转入口

        /// <summary>
        /// 通用场景加载入口：打开加载页，加载并激活目标场景。
        /// </summary>
        /// <param name="sceneType">目标场景类型。</param>
        /// <param name="finishCallBack">场景激活并显示后的完成回调（可为空）。</param>
        /// <remarks>
        /// 流程：记录上一个/当前关卡 → 派发 <see cref="IGameSceneEvent.OnSceneLoadStart"/> → 重置三段式进度状态机 →
        /// <c>GameModule.UI.ShowUI&lt;SwitchUI&gt;</c> 打开加载页；本模块 <see cref="Update"/> 每帧推进进度，
        /// 加载就绪后 <see cref="EnterFinishPhase"/> 直接 UnSuspend 激活场景，
        /// 收尾动画走满后 <see cref="FinishAndClose"/> 执行完成回调、派发 <see cref="IGameSceneEvent.OnSceneReady"/> 并关闭加载页。
        /// </remarks>
        public void LoadScene(SceneType sceneType, Action finishCallBack = null)
        {
            StartSceneLoad(sceneType, finishCallBack);
        }

        /// <summary>
        /// 回到主菜单场景：加载 MainScene 后回到主菜单窗口。
        /// </summary>
        /// <remarks>
        /// finishCallBack 内执行 CloseAll + jumpControl.JumpToMain()，确保加载页关闭后主菜单干净弹出。
        /// </remarks>
        public void JumpToMainScene()
        {
            StartSceneLoad(SceneType.MainScene, () =>
            {
                GameModule.UI.CloseAll();
                _jumpControl?.JumpToMain();
            });
        }

        /// <summary>
        /// 统一的场景加载启动器：记录状态、重置进度状态机、打开加载页。
        /// </summary>
        /// <param name="sceneType">目标场景类型。</param>
        /// <param name="finishCallBack">场景激活后的完成回调（可为空）。</param>
        private void StartSceneLoad(SceneType sceneType, Action finishCallBack)
        {
            if (_isActive)
            {
                // 上一次加载尚未结束又发起新加载：丢弃旧回调（旧场景可能未激活，强行触发会引发副作用），仅告警。
                Log.Warning($"[GameScene] 上一次加载尚未结束（{_sceneType}:{_sceneName}），被新的 {sceneType}:{GetSceneName(sceneType)} 抢占。");
            }

            RecordScene(sceneType);
            GameEvent.Get<IGameSceneEvent>().OnSceneLoadStart(sceneType);

            // 重置当前加载会话上下文
            _sceneType = sceneType;
            _sceneName = GetSceneName(sceneType);
            _finishCallBack = finishCallBack;
            _skipMode = SkipLoadingAnimation;
            _isSendLoadOver = false;
            _isClosing = false;
            _isFinished = false;
            _sceneLoadComplete = false;
            _phase1ElapsedTime = 0f;
            _holdAt100Time = 0f;
            _isActive = true;

            // 快速跳过模式：跳过预热动画，直接进入阶段 1 发起加载
            if (_skipMode)
            {
                _currentPhase = 1;
                _displayProgress = 0.10f;
                _targetProgress = 0.10f;
                StartRealLoading();
            }
            else
            {
                _currentPhase = 0;
                _displayProgress = 0f;
                _targetProgress = 0.10f; // 阶段 0 预热目标
            }

            // 打开加载页（纯展示，不传 UserData；进度由本模块 Update 驱动，UI 每帧读取 DisplayProgress）
            GameModule.UI.ShowUI<SwitchUI>();
        }

        /// <summary>
        /// 发起真实场景加载（suspendLoad=true，fire-and-forget 规避陷阱 1）。
        /// </summary>
        private void StartRealLoading()
        {
            GameModule.Scene.LoadSceneAsync(_sceneName, LoadSceneMode.Single, suspendLoad: true, priority: 100,
                gcCollect: true, progressCallBack: OnLoadProgress);
        }

        /// <summary>
        /// 场景加载进度回调：把 YooAsset 0~0.9 映射到 10%~90%，到 0.9 标记加载完成。
        /// </summary>
        private void OnLoadProgress(float value)
        {
            // 阶段 2 后拒绝更新（陷阱 2：防止覆盖收尾阶段的 target=1.0）
            if (_currentPhase >= 2)
            {
                return;
            }

            _targetProgress = 0.10f + Mathf.Clamp01(value / 0.9f) * 0.80f;

            if (value >= 0.9f && !_sceneLoadComplete)
            {
                _targetProgress = 0.90f;
                _sceneLoadComplete = true;
            }
        }

        /// <summary>
        /// 进入收尾阶段：派发 <see cref="IGameSceneEvent.OnSceneLoadOver"/> 通知观察方，并直接激活挂起的目标场景。
        /// </summary>
        /// <remarks>
        /// 本模块即加载方，激活由 <c>GameModule.Scene.UnSuspend</c> 直接完成；90%→100% 收尾动画 + 停留负责遮盖激活卡顿。
        /// </remarks>
        private void EnterFinishPhase()
        {
            _currentPhase = 2;
            _targetProgress = 1.0f;

            if (!_isSendLoadOver)
            {
                _isSendLoadOver = true;
                // 通知观察方：场景资源已加载到可激活状态
                GameEvent.Get<IGameSceneEvent>().OnSceneLoadOver();
                // 激活挂起的目标场景（数据归属与操作归属统一在本模块）
                if (!string.IsNullOrEmpty(_sceneName))
                {
                    GameModule.Scene.UnSuspend(_sceneName);
                }
            }
        }

        /// <summary>
        /// 统一终结出口：执行完成回调、派发 <see cref="IGameSceneEvent.OnSceneReady"/>、关闭加载页并结束会话。
        /// </summary>
        /// <remarks>
        /// 防重标志 <c>_isFinished</c> 确保回调只触发一次。由 <see cref="Update"/> 阶段 2 正常流程或跳过模式调用。
        /// </remarks>
        private void FinishAndClose()
        {
            if (_isFinished)
            {
                return;
            }
            _isFinished = true;

            // 1. 业务完成回调（如 JumpToMainScene 的 CloseAll + JumpToMain，CloseAll 会顺带关闭 SwitchUI）
            _finishCallBack?.Invoke();
            // 2. 确保加载页已关闭（finishCallBack 已通过 CloseAll 关闭则此次为 no-op；通用 LoadScene 无回调时由此关闭）
            if (!_isClosing)
            {
                _isClosing = true;
                GameModule.UI.CloseUI<SwitchUI>();
            }
            // 3. 通知场景内逻辑初始化（DynamicSceneSpawner 等监听方据此开始生成；须在 SwitchUI 关闭后派发）
            GameEvent.Get<IGameSceneEvent>().OnSceneReady(_sceneType);
            // 4. 结束加载会话，Update 不再推进
            _isActive = false;
        }

        #endregion
    }
}
