using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameLogic
{
    // SceneType 枚举、SceneConstName 常量、SceneTypeMapping 双向映射已移至自动生成文件：
    // SceneType.g.cs / SceneConstName.g.cs / SceneTypeMapping.g.cs
    // 由 Assets/Editor/SceneTools/SceneEnumGenerator/SceneEnumConfig.asset 生成，勿手改。
    // 新增/删除/改名场景请通过该配置工具同步，无需手动维护本文件。

    /// <summary>
    /// 场景数据与跳转模块。
    /// <remarks>
    /// 职责：① 把 <see cref="SceneType"/> 映射成场景资源地址；② 提供统一的场景跳转入口，
    /// 经 <see cref="GameModule.UI"/> 直接打开 <see cref="SwitchUI"/> 加载页（场景切换不再走 UIJump，
    /// 避免过渡页被压入 UIJump 导航栈导致返回栈残留；SwitchUI 是过渡页不参与业务返回导航）；
    /// ③ 记录上一个关卡与当前关卡；④ <b>统一管理场景加载进度与激活控制</b>——多阶段进度状态机、
    /// 资源加载（suspendLoad）、激活（UnSuspend）、完成回调与关闭加载页全部由本模块负责，
    /// <see cref="SwitchUI"/> 仅做展示（每帧读取 <see cref="DisplayProgress"/> 渲染）。
    /// <para>
    /// <b>多阶段进度</b>（避免小场景加载过快导致 UI 闪过 / 进度条跳变，并分散性能峰值）：
    /// <list type="number">
    /// <item><b>阶段 0 预热</b>（0→10%）：打开加载页后立即动画，完成后再发起真实加载；</item>
    /// <item><b>阶段 1 加载</b>（10%→80%）：progressCallBack 把 YooAsset 的 0~0.9 映射到 10%~80%；</item>
    /// <item><b>阶段 2「80%」停帧检查点</b>：进度在 80% 停留数帧（用户看清阶段、峰值分帧），随后激活场景
    /// （UnSuspend 仅解除挂起，激活与首帧渲染仍异步）；</item>
    /// <item><b>阶段 3 激活</b>（80%→90%）：动画遮盖激活卡顿，<b>等待场景真实激活完成</b>（句柄 IsDone，带绝对超时兜底）；</item>
    /// <item><b>阶段 4「90%」停帧检查点</b>：进度在 90% 停留数帧后进入业务就绪段；</item>
    /// <item><b>阶段 5 业务就绪</b>（90%→100%）：由注册的初始化任务进度驱动（<see cref="RegisterInitTask"/> 按任务数平分，
    /// 如 <see cref="DynamicSceneSpawner"/> 的 LoadingWait 模式上报 已加载数/总数）；无任务时快速爬过；带绝对超时兜底；</item>
    /// <item><b>阶段 6 100% 帧等待</b>：激活后多渲染数帧 + 100% 停留 + 任务全部完成，全部满足后才执行完成回调并关闭加载页。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>为何 80% 激活</b>：激活会有一帧卡顿，用 80%→90% 动画做视觉遮盖；加载完成、激活、业务加载分属不同帧，避免性能峰值集中。
    /// </para>
    /// <para>
    /// <b>激活权归属</b>：本模块即加载方，<see cref="EnterFinishPhase"/> 在阶段 2「80%」停帧检查点结束后直接调用
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
        /// 调试用：跳过加载页动画（预热 + 收尾 + 100% 停留）。
        /// </summary>
        /// <remarks>
        /// 仅影响分段进度动画与停帧检查点停留，不影响实际场景加载过程；仍会等待场景真实激活完成再关闭加载页，
        /// 保证调试模式与发布行为一致。Editor 调试时设为 true 可省去等待时间；发布时保持 false。
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

        // ===== 加载页文案配置（SceneLoadTipsConfig） =====

        /// <summary>SO 资源地址（YooAsset location，文件名 = SceneLoadTipsConfig）。</summary>
        private const string TipsConfigAssetLocation = "SceneLoadTipsConfig";

        /// <summary>SO 配置缓存（首次访问时通过 ResourceModule 同步加载）。</summary>
        private SceneLoadTipsConfig _tipsConfig;

        /// <summary>
        /// 懒加载并缓存 <see cref="SceneLoadTipsConfig"/>；加载失败静默返回 null。
        /// </summary>
        /// <remarks>仅在加载会话首次调用，避免空闲期空转。</remarks>
        private SceneLoadTipsConfig GetTipsConfig()
        {
            if (_tipsConfig != null)
            {
                return _tipsConfig;
            }

            try
            {
                _tipsConfig = GameModule.Resource.LoadAsset<SceneLoadTipsConfig>(TipsConfigAssetLocation);
            }
            catch (Exception e)
            {
                Log.Error($"[GameScene] {TipsConfigAssetLocation} 加载失败：{e.Message}");
                _tipsConfig = null;
            }

            return _tipsConfig;
        }

        /// <summary>
        /// 当前加载阶段富文本：阶段 5/6（业务就绪与收尾等待）返回初始化文案，其余按 <see cref="DisplayProgress"/> 查 <see cref="SceneLoadTipsConfig"/> 返回。
        /// </summary>
        /// <remarks>未加载 SO 或表为空返回空串；非加载会话期间返回最后一次终值。</remarks>
        public string DisplayPhaseText
        {
            get
            {
                if (_currentPhase == 5 || (_currentPhase == 6 && HasPendingInitTasks()))
                {
                    var initCfg = _isActive ? GetTipsConfig() : _tipsConfig;
                    var initText = initCfg != null ? initCfg.InitPhaseText : null;
                    return string.IsNullOrEmpty(initText) ? DefaultInitPhaseText : initText;
                }

                var cfg = _isActive ? GetTipsConfig() : _tipsConfig;
                if (cfg == null)
                {
                    return string.Empty;
                }

                return cfg.GetPhaseText(_displayProgress);
            }
        }

        /// <summary>
        /// 从 <see cref="SceneLoadTipsConfig.Tips"/> 随机取一条小贴士。
        /// </summary>
        /// <returns>小贴士文本；配置为空返回空串。</returns>
        public string GetRandomTip()
        {
            var cfg = _isActive ? GetTipsConfig() : _tipsConfig;
            if (cfg == null || cfg.Tips.Count == 0)
            {
                return string.Empty;
            }

            return cfg.Tips[UnityEngine.Random.Range(0, cfg.Tips.Count)];
        }

        public override void OnInit()
        {
            _jumpControl = GameModule.UIJumpControl;
        }

        public override void Shutdown()
        {
            // 模块关闭：停止进度驱动，清理引用。不触发业务完成回调（避免在关闭流程中执行 CloseAll/JumpToMain 等副作用）。
            _isActive = false;
            _jumpControl = null;
            _tipsConfig = null;
            PreviousSceneType = null;
            CurrentSceneType = null;
            _initTasks.Clear();
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
            string name = SceneTypeMapping.GetSceneName(sceneType);
            if (string.IsNullOrEmpty(name))
            {
                Log.Error($"SceneType:[{sceneType}]未匹配资源地址");
            }
            return name;
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

            // 由 SceneTypeMapping 统一查询：同时匹配资源地址与枚举名（枚举名用于回放文件名格式 Replay_FlyTest_20260622.replay）
            SceneType? result = SceneTypeMapping.GetSceneTypeFromName(sceneName);
            if (result == null)
            {
                Log.Error($"场景名:[{sceneName}]未匹配任何 SceneType");
            }
            return result;
        }

        #region 场景加载进度控制（多阶段状态机）

        /// <summary>当前展示进度（平滑后驱动 UI 渲染），由 <see cref="Update"/> 每帧推进，UI 只读。</summary>
        /// <remarks>未在加载会话中时保留上一次的终值；新会话开始时在 <see cref="StartSceneLoad"/> 重置。</remarks>
        public float DisplayProgress => _displayProgress;

        // ===== 当前加载会话上下文 =====
        private bool _isActive = false;              // 是否处于加载会话（false 时 Update 直接 return）
        private SceneType _sceneType;                // 当前加载的目标场景类型（用于派发 OnSceneReady）
        private string _sceneName = "";              // 待加载并激活的场景资源地址
        private Action _finishCallBack = null;       // 场景激活后的完成回调（来自跳转入口）
        private bool _skipMode = false;              // 快速跳过模式（由 SkipLoadingAnimation 决定）

        // ===== 多阶段进度控制 =====
        private int _currentPhase = 0;               // 当前阶段（0=预热, 1=加载, 2=80%停帧, 3=激活, 4=90%停帧, 5=业务就绪, 6=100%帧等待）

        /// <summary>目标进度（Update 平滑追赶此值）：阶段0=0.10、阶段1=0.10~0.80、阶段3=0.90、阶段5=0.90~1.0。</summary>
        private float _targetProgress = 0f;

        /// <summary>展示进度（平滑后驱动 UI）。</summary>
        private float _displayProgress = 0f;

        // ===== 分段时长默认值（常量基线，会话级字段会按 LoadScene 传参覆盖） =====

        /// <summary>阶段 0 预热动画时长（0→10%）默认值，秒。</summary>
        private const float DefaultWarmupDuration = 0.7f;

        /// <summary>阶段 3 激活段动画时长（80%→90%）默认值，秒。</summary>
        private const float DefaultFinishDuration = 2f;

        /// <summary>到达 100% 后的停留时间（让用户看清 100%）默认值，秒。</summary>
        private const float DefaultHoldAt100Duration = 0.5f;

        // ===== 固定常量（不对外暴露，不由会话覆盖） =====

        /// <summary>阶段 0 预热速度 = 0.10 / 当前预热时长，每秒前进的进度值（按会话时长动态计算）。</summary>
        private float WarmupSpeed => 0.10f / _warmupDuration;

        /// <summary>阶段 1 加载追赶速度：平滑跟随 YooAsset 进度，避免跳变。</summary>
        private const float LoadingSpeed = 1.0f;

        /// <summary>阶段 2/4 停帧检查点停留帧数：让用户看清 80%/90% 阶段，并把各阶段性能峰值分到不同帧。</summary>
        private const int StageCheckpointFrames = 2;

        /// <summary>阶段 5 业务就绪段平滑追赶速度：跟随初始化任务上报进度，避免跳变。</summary>
        private const float InitSegmentSpeed = 1.0f;

        /// <summary>跳过模式下的动画速度（近似瞬时推进，等待逻辑不变）。</summary>
        private const float SkipSpeed = 100f;

        /// <summary>阶段 1 停滞超时：真实进度已 >0 且连续无提升才判定异常，秒。
        /// 取 60s 给大 bundle 串行解压留足时间（5s 固定超时会误杀大场景冷启动慢加载）。
        /// 仅当 <c>_lastLoadProgress > 0</c> 才累计，冷启动解压期 progress 长期为 0 不计入停滞。</summary>
        private const float Phase1StallTimeout = 60f;

        /// <summary>阶段 1 绝对超时：总时长上限，防彻底卡死，秒。无论进度是否在爬升都兜底。</summary>
        private const float Phase1AbsoluteTimeout = 180f;

        /// <summary>阶段 3 等待场景激活的绝对超时：超过则强制进入下一阶段，防激活异常卡死，秒。</summary>
        private const float Phase2AbsoluteTimeout = 30f;

        /// <summary>阶段 6 关页前需在场景激活完成后渲染的帧数：遮盖首帧 shader 编译/资源上传卡顿。</summary>
        private const int PostActivateFrames = 2;

        /// <summary>阶段 5/6 等待业务初始化任务的绝对超时：超过则强制收尾关页，防业务异常卡死玩家，秒。</summary>
        private const float Phase3AbsoluteTimeout = 30f;

        /// <summary>初始化等待阶段的默认文案（<see cref="SceneLoadTipsConfig.InitPhaseText"/> 未配置时使用）。</summary>
        private const string DefaultInitPhaseText = "正在初始化场景资源…";

        /// <summary>阶段 3 激活段速度 = 0.10 / 当前激活段时长，每秒前进的进度值（按会话时长动态计算）。</summary>
        private float FinishSpeed => 0.10f / _finishDuration;

        // ===== 会话级时长（每次 LoadScene 时设置，null 走默认值） =====

        private float _warmupDuration = DefaultWarmupDuration;       // 本次会话阶段 0 时长
        private float _finishDuration = DefaultFinishDuration;       // 本次会话阶段 3 激活段时长
        private float _holdAt100Duration = DefaultHoldAt100Duration;  // 本次会话 100% 停留时长

        private float _holdAt100Time = 0f;           // 已在 100% 停留的累计时间
        private bool _sceneLoadComplete = false;     // 真实加载已完成（YooAsset 进度到 0.9）
        private float _phase1ElapsedTime = 0f;       // 阶段 1 已持续时间（用于绝对超时兜底）
        private float _lastLoadProgress = 0f;        // 最近一次 YooAsset 原始进度（用于停滞判定）
        private float _phase1StallElapsed = 0f;      // 阶段 1 进度无提升累计时间（仅当真实进度已 >0 才累计）
        private bool _sceneActivated = false;        // 场景真实激活完成（UnSuspend 后句柄 IsDone）
        private int _postActivateFrames = 0;         // 场景激活后已渲染的帧数（遮盖首帧卡顿）
        private float _phase2Elapsed = 0f;           // 阶段 3 已持续时间（真实墙钟，用于等待激活超时兜底）
        private int _checkpointFrames = 0;           // 阶段 2/4 停帧检查点已停留帧数（每次进入检查点清零）
        private float _phase3Elapsed = 0f;           // 阶段 5 已持续时间（真实墙钟，用于业务等待超时兜底）
        private float _lastInitTarget = 0.90f;       // 阶段 5 目标进度单调钳制值（进度条只进不退）
        private bool _initTimeoutHit = false;        // 业务初始化等待是否已超时（超时后阶段 6 不再等任务）

        // ===== 阶段 5 业务就绪（初始化资源门闩） =====

        /// <summary>初始化任务条目：任务 + 可选进度源（0~1，驱动阶段 5 的 90→100%）。</summary>
        private readonly struct InitTaskEntry
        {
            public readonly UniTask Task;
            public readonly Func<float> Progress;

            public InitTaskEntry(UniTask task, Func<float> progress)
            {
                Task = task;
                Progress = progress;
            }
        }

        private readonly List<InitTaskEntry> _initTasks = new List<InitTaskEntry>(); // 已包装的初始化任务（关页前等待其完成，会话开始时清空）

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

            // 场景激活完成后每帧累计渲染帧数（遮盖首帧 shader 编译/资源上传卡顿），供阶段 6 关页判定
            if (_sceneActivated)
            {
                _postActivateFrames++;
            }

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

                case 1: // 阶段 1 加载（10%→80%）
                    _phase1ElapsedTime += dt;
                    // 仅当真实进度已 >0 后才累计停滞时间（冷启动解压时 progress 可能长期停在 0，不能按停滞误杀）
                    if (_lastLoadProgress > 0f && !_sceneLoadComplete)
                    {
                        _phase1StallElapsed += dt;
                    }
                    _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, LoadingSpeed * dt);

                    if (_sceneLoadComplete && _displayProgress >= 0.79f)
                    {
                        _displayProgress = 0.80f;
                        _currentPhase = 2;
                        _checkpointFrames = 0;
                    }
                    else if ((_lastLoadProgress > 0f && _phase1StallElapsed >= Phase1StallTimeout)
                             || _phase1ElapsedTime >= Phase1AbsoluteTimeout) // 兜底：停滞或绝对超时强制进入激活段
                    {
                        Log.Warning($"[GameScene] 阶段 1→2（超时兜底）：scene={_sceneName}, elapsed={_phase1ElapsedTime:F1}s, stall={_phase1StallElapsed:F1}s, rawProgress={_lastLoadProgress:F3}, display={_displayProgress:F3}, complete={_sceneLoadComplete}，强制进入激活段");
                        _displayProgress = 0.80f;
                        _currentPhase = 2;
                        _checkpointFrames = 0;
                    }
                    break;

                case 2: // 阶段 2 「80%」停帧检查点：让用户看清该阶段，并把「加载完成」与「激活」的性能峰值分到不同帧
                    _checkpointFrames++;
                    if (_checkpointFrames >= (_skipMode ? 0 : StageCheckpointFrames))
                    {
                        EnterFinishPhase(); // 派发 OnSceneLoadOver 并 UnSuspend 激活目标场景
                        _currentPhase = 3;
                        _targetProgress = 0.90f;
                        _phase2Elapsed = 0f;
                    }
                    break;

                case 3: // 阶段 3 激活（80%→90%）：动画遮盖激活卡顿，等待场景句柄 IsDone
                    // 钳制 deltaTime：激活那一帧 realElapse 可能高达数百毫秒甚至秒级，会把动画压缩成一帧瞬间完成
                    float clampedDelta = Mathf.Min(dt, 0.05f);
                    _phase2Elapsed += dt; // 激活等待超时兜底按真实流逝时间累计（不钳制），激活大卡顿帧计入真实墙钟
                    if (!_sceneActivated)
                    {
                        // UnSuspend 只是把 allowSceneActivation 置 true，
                        // 场景整合、Awake/OnEnable、首帧渲染仍异步进行，句柄 IsDone 才代表激活完毕
                        _sceneActivated = GameModule.Scene.IsSceneLoadDone(_sceneName);
                        if (!_sceneActivated && _phase2Elapsed >= Phase2AbsoluteTimeout)
                        {
                            Log.Warning($"[GameScene] 阶段 3 等待场景激活超时：scene={_sceneName}, elapsed={_phase2Elapsed:F1}s，强制进入下一阶段");
                            _sceneActivated = true;
                        }
                    }
                    _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, (_skipMode ? SkipSpeed : FinishSpeed) * clampedDelta);
                    if (_displayProgress >= 0.899f && _sceneActivated)
                    {
                        _displayProgress = 0.90f;
                        _currentPhase = 4;
                        _checkpointFrames = 0;
                    }
                    break;

                case 4: // 阶段 4 「90%」停帧检查点：区分「激活完成」与「业务就绪」两阶段
                    _checkpointFrames++;
                    if (_checkpointFrames >= (_skipMode ? 0 : StageCheckpointFrames))
                    {
                        _currentPhase = 5;
                        _phase3Elapsed = 0f;
                        _lastInitTarget = 0.90f;
                    }
                    break;

                case 5: // 阶段 5 业务就绪（90%→100%）：由注册的初始化任务进度驱动，无任务时快速爬过
                    float clampedDelta5 = Mathf.Min(dt, 0.05f);
                    _phase3Elapsed += dt; // 业务等待超时兜底按真实流逝时间累计（不钳制）
                    if (!_initTimeoutHit && _phase3Elapsed >= Phase3AbsoluteTimeout)
                    {
                        Log.Warning($"[GameScene] 阶段 5 等待初始化任务超时：scene={_sceneName}, elapsed={_phase3Elapsed:F1}s，强制收尾");
                        _initTimeoutHit = true;
                    }

                    if (_skipMode)
                    {
                        // 跳过模式：进度直接到 100%（任务完成与否仍由阶段 6 把关，保证调试与发布等待一致）
                        _displayProgress = 1.0f;
                    }
                    else
                    {
                        float initProgress = _initTimeoutHit ? 1f : ComputeInitProgress();
                        float target = 0.90f + 0.10f * initProgress;
                        if (target > _lastInitTarget)
                        {
                            _lastInitTarget = target; // 单调保护：进度条只进不退
                        }
                        _targetProgress = _lastInitTarget;
                        _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, InitSegmentSpeed * clampedDelta5);
                    }

                    if (_displayProgress >= 1.0f)
                    {
                        _displayProgress = 1.0f;
                        _currentPhase = 6;
                        _holdAt100Time = 0f;
                    }
                    break;

                case 6: // 阶段 6 100% 帧等待（正常收尾）：激活后渲染帧数 + 100% 停留 + 任务全部完成 → 关页
                    float clampedDelta6 = Mathf.Min(dt, 0.05f);
                    _holdAt100Time += clampedDelta6;

                    bool tasksDone = _initTimeoutHit || !HasPendingInitTasks();
                    if (!_isClosing && tasksDone && _postActivateFrames >= PostActivateFrames
                        && _holdAt100Time >= (_skipMode ? 0f : _holdAt100Duration))
                    {
                        FinishAndClose();
                    }
                    break;
            }
        }

        #region 阶段 5 业务就绪任务门闩

        /// <summary>
        /// 注册场景初始化任务（无进度源重载）：加载页在业务就绪段等待其完成，进度条该任务完成前不推进。
        /// </summary>
        /// <remarks>须在加载会话期间注册（场景激活阶段，即阶段 3 之后关页前）；会话未激活时任务包装后直接 Forget 后台执行，不参与等待。</remarks>
        public void RegisterInitTask(UniTask task, string label = null)
        {
            RegisterInitTask(task, null, label);
        }

        /// <summary>
        /// 注册场景初始化任务（带进度源）：加载页 90%→100% 段按任务数平分驱动（各任务进度求和 ÷ 任务数）。
        /// </summary>
        /// <param name="task">初始化任务。</param>
        /// <param name="progressProvider">进度源（0~1，应单调递增，如 已加载数/总数）；为 null 时该任务完成前按 0 进度计。</param>
        /// <param name="label">任务标签（用于日志定位）。</param>
        public void RegisterInitTask(UniTask task, Func<float> progressProvider, string label = null)
        {
            label = string.IsNullOrEmpty(label) ? "未命名初始化任务" : label;

            if (!_isActive)
            {
                WrapInitTask(task, label).Forget();
                return;
            }

            _initTasks.Add(new InitTaskEntry(WrapInitTask(task, label), progressProvider));
            Log.Info($"[GameScene] 注册初始化任务：{label}（当前 {_initTasks.Count} 项，{(progressProvider != null ? "带进度源" : "无进度源")}）");
        }

        /// <summary>
        /// 包装初始化任务：异常只记录一次并转为正常完成，使阶段 5/6 轮询只需判断 Pending 状态。
        /// </summary>
        private async UniTask WrapInitTask(UniTask task, string label)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                Log.Error($"[GameScene] 初始化任务 {label} 异常：{e.Message}（按已完成处理，不阻塞加载页关闭）");
            }
        }

        /// <summary>是否存在未完成的初始化任务。</summary>
        private bool HasPendingInitTasks()
        {
            for (int i = 0; i < _initTasks.Count; i++)
            {
                if (_initTasks[i].Task.Status == UniTaskStatus.Pending)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 初始化任务综合进度（0~1）：按任务数量平分业务就绪段（各任务进度求和 ÷ 任务数），
        /// 每完成一项任务进度条实进一格；已完成任务一律按满进度计（防进度源少报卡段）；
        /// 无进度源的任务完成前按 0 计、完成后按 1 计；无任务时返回 1（90→100% 快速爬过）。
        /// </summary>
        private float ComputeInitProgress()
        {
            if (_initTasks.Count == 0)
            {
                return 1f;
            }

            float sum = 0f;
            for (int i = 0; i < _initTasks.Count; i++)
            {
                var entry = _initTasks[i];
                if (entry.Task.Status != UniTaskStatus.Pending)
                {
                    sum += 1f; // 已完成按满进度计
                }
                else if (entry.Progress != null)
                {
                    sum += Mathf.Clamp01(entry.Progress());
                }
                // 无进度源且未完成 → 计 0
            }
            return sum / _initTasks.Count;
        }

        #endregion

        #endregion

        #region 场景跳转入口

        /// <summary>
        /// 通用场景加载入口：打开加载页，加载并激活目标场景。
        /// </summary>
        /// <param name="sceneType">目标场景类型。</param>
        /// <param name="finishCallBack">场景激活并显示后的完成回调（可为空）。</param>
        /// <param name="warmupDuration">阶段 0 预热动画时长（0→10%），秒。传 null 用默认 0.7s，传 0 等同 SkipLoadingAnimation。</param>
        /// <param name="finishDuration">阶段 3 激活段动画时长（80%→90%），秒。传 null 用默认 2s，传 0 瞬间推进（仍等激活完成）。</param>
        /// <param name="holdAt100Duration">到达 100% 后停留时间，秒。传 null 用默认 0.5s。</param>
        /// <remarks>
        /// 流程：记录上一个/当前关卡 → 派发 <see cref="IGameSceneEvent.OnSceneLoadStart"/> → 重置多阶段进度状态机 →
        /// <c>GameModule.UI.ShowUI&lt;SwitchUI&gt;</c> 打开加载页；本模块 <see cref="Update"/> 每帧推进进度，
        /// 加载就绪后经「80% 停帧检查点」由 <see cref="EnterFinishPhase"/> UnSuspend 激活场景，
        /// 依次经过「激活段（80→90，等句柄 IsDone）→ 90% 停帧检查点 → 业务就绪段（90→100，初始化任务进度驱动）→
        /// 100% 帧等待（激活后数帧 + 停留 + 任务全部完成）」，
        /// <see cref="FinishAndClose"/> 执行完成回调、派发 <see cref="IGameSceneEvent.OnSceneReady"/> 并关闭加载页。
        /// </remarks>
        public void LoadScene(SceneType sceneType, Action finishCallBack = null,
            float? warmupDuration = null, float? finishDuration = null, float? holdAt100Duration = null)
        {
            StartSceneLoad(sceneType, finishCallBack, warmupDuration, finishDuration, holdAt100Duration);
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
        /// <param name="warmupDuration">阶段 0 时长，秒。null 用默认，0 触发跳过模式。</param>
        /// <param name="finishDuration">阶段 3 激活段时长，秒。null 用默认，0 瞬间推进。</param>
        /// <param name="holdAt100Duration">100% 停留时长，秒。null 用默认。</param>
        private void StartSceneLoad(SceneType sceneType, Action finishCallBack,
            float? warmupDuration = null, float? finishDuration = null, float? holdAt100Duration = null)
        {
            if (_isActive)
            {
                // 硬保护：上一次加载尚未结束直接拒绝新请求，避免并发加载进度互相干扰、旧回调被静默丢弃、
                // 以及两个 Single 模式场景加载在 Unity SceneManager 下行为不可可靠。
                // 不触发 finishCallBack：场景并未激活，调用方按成功语义执行回调反而会误操作错误场景。
                Log.Error($"[GameScene] 上一次加载尚未结束（{_sceneType}:{_sceneName}），拒绝新的 {sceneType}:{GetSceneName(sceneType)} 加载请求。");
                return;
            }

            RecordScene(sceneType);
            GameEvent.Get<IGameSceneEvent>().OnSceneLoadStart(sceneType);

            // 重置当前加载会话上下文
            _sceneType = sceneType;
            _sceneName = GetSceneName(sceneType);
            _finishCallBack = finishCallBack;
            _isSendLoadOver = false;
            _isClosing = false;
            _isFinished = false;
            _sceneLoadComplete = false;
            _phase1ElapsedTime = 0f;
            _lastLoadProgress = 0f;
            _phase1StallElapsed = 0f;
            _holdAt100Time = 0f;
            _sceneActivated = false;
            _postActivateFrames = 0;
            _phase2Elapsed = 0f;
            _checkpointFrames = 0;
            _phase3Elapsed = 0f;
            _lastInitTarget = 0.90f;
            _initTimeoutHit = false;
            _initTasks.Clear();
            _isActive = true;

            // 会话级时长：传参覆盖默认值
            _warmupDuration = warmupDuration ?? DefaultWarmupDuration;
            _finishDuration = finishDuration ?? DefaultFinishDuration;
            _holdAt100Duration = holdAt100Duration ?? DefaultHoldAt100Duration;

            // 跳过模式判定：全局开关 OR 预热时长传 0（显式要求跳过预热动画）
            _skipMode = SkipLoadingAnimation || _warmupDuration <= 0f;

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
            GameModule.UI.ShowUI<SwitchSceneUI>();
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
        /// 场景加载进度回调：把 YooAsset 0~0.9 映射到 10%~80%，到 0.9 标记加载完成。
        /// </summary>
        private void OnLoadProgress(float value)
        {
            // 阶段 2（80% 检查点）后拒绝更新（陷阱 2：防止覆盖后续阶段的目标进度）
            if (_currentPhase >= 2)
            {
                return;
            }

            // 进度有实质提升（严格大于，YooAsset progress 基于字节数单调递增）时重置停滞计时
            if (value > _lastLoadProgress)
            {
                _phase1StallElapsed = 0f;
            }
            _lastLoadProgress = value;

            _targetProgress = 0.10f + Mathf.Clamp01(value / 0.9f) * 0.70f;

            if (value >= 0.9f && !_sceneLoadComplete)
            {
                _targetProgress = 0.80f;
                _sceneLoadComplete = true;
            }
        }

        /// <summary>
        /// 激活挂起的目标场景：派发 <see cref="IGameSceneEvent.OnSceneLoadOver"/> 通知观察方，并解除场景挂起。
        /// </summary>
        /// <remarks>
        /// 在阶段 2「80%」停帧检查点结束时调用（峰值分帧：与加载完成、业务加载不在同一帧）。
        /// 本模块即加载方，激活由 <c>GameModule.Scene.UnSuspend</c> 直接完成；随后 80%→90% 激活段动画遮盖激活卡顿，
        /// 阶段 3 通过 <c>GameModule.Scene.IsSceneLoadDone</c> 轮询等待激活真正完成（带绝对超时兜底）。
        /// </remarks>
        private void EnterFinishPhase()
        {
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
        /// 防重标志 <c>_isFinished</c> 确保回调只触发一次。仅由 <see cref="Update"/> 阶段 6 在
        /// 「初始化任务全部完成 + 场景激活后数帧 + 100% 停留结束」全部满足时调用（skip 模式同样走此出口，仅无动画与停留）。
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
                GameModule.UI.CloseUI<SwitchSceneUI>();
            }
            // 3. 通知场景内逻辑初始化（DynamicSceneSpawner 等监听方据此开始生成；须在 SwitchUI 关闭后派发）
            GameEvent.Get<IGameSceneEvent>().OnSceneReady(_sceneType);
            // 4. 结束加载会话，Update 不再推进
            _isActive = false;
        }

        #endregion
    }
}
