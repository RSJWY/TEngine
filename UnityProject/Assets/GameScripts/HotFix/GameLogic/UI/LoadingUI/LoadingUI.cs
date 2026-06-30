/*
 * 【临时保留·已废弃】逻辑已迁移至 SwitchUI + GameSceneModule。
 * 本文件整体注释，仅作功能逻辑参考；确认新逻辑无误后将整体删除整个 LoadingUI 目录。
 * 注：原实现引用的 LoadSceneDataBody 已删除、GameTipsData 类型仓库中不存在，故原代码已无法编译。
 *
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 场景加载页窗口（节点由 <c>LoadingUI_Gen.g.cs</c> 代码生成绑定）。
    /// </summary>
    /// <remarks>
    /// 经 <c>GameModule.UI.ShowUI&lt;LoadingUI&gt;(LoadSceneDataBody)</c> 直接打开（场景切换不再走 UIJump 路由）。
    /// <para>
    /// <b>三段式进度</b>（避免小场景加载过快导致 UI 闪过 / 进度条跳变）：
    /// <list type="number">
    /// <item><b>阶段 0 预热</b>（0→10%）：OnCreate 后立即动画，完成后再发起真实加载；</item>
    /// <item><b>阶段 1 加载</b>（10%→90%）：progressCallBack 把 YooAsset 的 0~0.9 映射到 10%~90%；</item>
    /// <item><b>阶段 2 收尾</b>（90%→100%）：在 90% 立即激活场景，用最后 10% 动画 + 100% 停留<b>遮盖激活卡顿</b>，走满后才 finishCallBack + Close。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>为何 90% 激活而非 100%</b>：激活会有一帧卡顿，留最后 10% 做视觉遮盖；若 100% 才激活，激活瞬间紧接 UI 关闭会暴露突兀弹出。
    /// </para>
    /// <para>
    /// <b>激活权归属</b>：本窗口不调用 <c>UnSuspend</c>，只在阶段 1→2 发 <see cref="GlobalEventID.Event_LoadOver"/>；
    /// 场景激活由 <see cref="GameSceneManager.OnLoadOver"/> 直接执行（基础设施层操作归基础设施层）。
    /// </para>
    /// <para>
    /// <b>陷阱 1</b>：suspendLoad=true + progressCallBack 时，<c>LoadSceneAsync</c> 内部 <c>while(!IsDone)</c> 一直 yield（SceneModule.cs:107-114），await 会死循环。故只 fire-and-forget，进度全由 progressCallBack 驱动。
    /// </para>
    /// <para>
    /// <b>陷阱 2</b>：suspendLoad 时 IsDone 永远 false，progressCallBack 每帧持续回调 value=0.9，会反复覆盖 <c>_targetProgress</c>。故 <see cref="OnLoadProgress"/> 在 <c>_currentPhase>=2</c> 后直接 return，保护收尾阶段的 target=1.0 不被打回 0.90（否则永远卡 90%）。
    /// </para>
    /// </remarks>
    [Window(UILayer.Top, location: "LoadingUI")]
    public partial class LoadingUI
    {
        // ===== 进度展示文本（OnUpdate 拼接后写入 m_tmpShowTips）=====
        private string _progress = "0";              // 进度百分比文本（0~100）
        private string _prefixText = "Loading";      // 文案前缀
        private string _dotText = "";                // 点点动画文本（. .. ... 循环）
        private string _tipsText = "正在加载资源..."; // Tips 文案（表加载前/失败时占位）

        private int _dotCount = 0;                   // 当前点点数量（0~3 循环）

        // ===== Tips 文案数据（OnCreate 从 GameTipsTable 加载）=====
        private readonly List<string> _tipsList = new List<string>(); // Tips 文案池

        // ===== 场景加载状态 =====
        private bool _isSendLoadOver = false;         // 防重复发 Event_LoadOver（收尾阶段发一次）
        private bool _isClosing = false;              // 防重复收尾关闭（100% 停留结束后关一次）
        private bool _isFinished = false;             // 防重复触发 finishCallBack（统一终结出口防重标志）
        private string _sceneName = "";               // 待加载并激活的场景资源地址
        private Action _finishCallBack = null;        // 场景激活后的完成回调（来自 LoadSceneDataBody）

        // ===== 三段式进度控制 =====
        private int _currentPhase = 0;                // 当前阶段（0=预热, 1=加载, 2=收尾）

        /// <summary>目标进度（OnUpdate 平滑追赶此值）：阶段0=0.10、阶段1=0.10~0.90、阶段2=1.0。</summary>
        private float _targetProgress = 0f;

        /// <summary>显示进度（平滑后驱动 UI）。</summary>
        private float _displayProgress = 0f;

        /// <summary>阶段 0 预热动画时长（0→10%），秒。</summary>
        private const float WarmupDuration = 0.7f;

        /// <summary>阶段 0 预热速度 = 0.10 / WarmupDuration，每秒前进的进度值。</summary>
        private const float WarmupSpeed = 0.10f / WarmupDuration;

        /// <summary>阶段 1 加载追赶速度：平滑跟随 YooAsset 进度，避免跳变。值 1.0 表示 10%→90% 约需 0.8s。</summary>
        private const float LoadingSpeed = 1.0f;

        /// <summary>阶段 2 收尾动画时长（90%→100%），秒。</summary>
        private const float FinishDuration =2f;

        /// <summary>阶段 2 收尾速度 = 0.10 / FinishDuration，每秒前进的进度值。</summary>
        private const float FinishSpeed = 0.10f / FinishDuration;

        /// <summary>到达 100% 后的停留时间（让用户看清 100%），秒。</summary>
        private const float HoldAt100Duration = 0.5f;

        private float _holdAt100Time = 0f;            // 已在 100% 停留的累计时间
        private bool _sceneLoadComplete = false;      // 真实加载已完成（YooAsset 进度到 0.9）
        private float _phase1ElapsedTime = 0f;        // 阶段 1 已持续时间（用于超时兜底）
        private bool _skipMode = false;               // 快速跳过模式（由 GameSceneManager.SkipLoadingAnimation 控制）

        // ===== 定时器 ID（OnDestroy 统一 RemoveTimer 清理）=====
        private int _timerDot;     // 点点动画（0.5s 循环）
        private int _timerTips;    // Tips 切换（3s 循环）

        /// <summary>窗口创建：解析数据、加载 Tips、启动动画定时器（场景加载推迟到预热完成后发起）。</summary>
        protected override void OnCreate()
        {
            base.OnCreate();

            LoadSceneDataBody data = UserData as LoadSceneDataBody;
            if (data == null)
            {
                Log.Error("[LoadingUI] UserData 不是 LoadSceneDataBody，加载页无法工作。");
                return;
            }

            _sceneName = data.sceneName;
            _finishCallBack = data.finishCallBack;
            _isSendLoadOver = false;
            _isClosing = false;
            _isFinished = false;
            _currentPhase = 0;
            _targetProgress = 0.10f; // 阶段 0 预热目标
            _displayProgress = 0f;
            _holdAt100Time = 0f;
            _sceneLoadComplete = false;
            _phase1ElapsedTime = 0f;
            _skipMode = GameSceneManager.SkipLoadingAnimation;

            // 快速跳过模式：跳过预热动画，直接进入阶段 1 发起加载
            if (_skipMode)
            {
                _currentPhase = 1;
                _displayProgress = 0.10f;
                _targetProgress = 0.10f;
                StartRealLoading();
            }

            // 加载 Tips 文案表（ScriptableObject，地址 "GameTipsTable"），回调式不持有句柄
            GameModule.Resource.LoadAsset<GameTipsData>("GameTipsTable", (tipsData) =>
            {
                if (tipsData != null && tipsData.TipsList != null && tipsData.TipsList.Count > 0)
                {
                    _tipsList.Clear();
                    _tipsList.AddRange(tipsData.TipsList);
                    _tipsText = _tipsList[UnityEngine.Random.Range(0, _tipsList.Count)];
                }
            });

            // 场景加载推迟到阶段 0 完成后发起（见 StartRealLoading / OnUpdate 阶段 0）

            // 广播场景切换开始事件：通知监听方 LoadingUI 已打开，场景切换流程启动
            SceneType? targetSceneType = GameSceneManager.GetSceneTypeFromName(_sceneName);
            if (targetSceneType.HasValue)
            {
                GameEvent.Send<SceneType>(GlobalEventID.Event_SceneLoadStart, targetSceneType.Value);
            }

            // 点点动画：每 0.5s 推进 . → .. → ... → 空（使用 unscaled time，避免时间暂停时定时器停止）
            _timerDot = GameModule.Timer.AddTimer((args) =>
            {
                _dotCount = (_dotCount + 1) % 4;
                _dotText = new string('.', _dotCount);
            }, 0.5f, isLoop: true, isUnscaled: true);

            // Tips 切换：每 3s 从池中随机刷新（池空保持占位文案）（使用 unscaled time，避免时间暂停时定时器停止）
            _timerTips = GameModule.Timer.AddTimer((args) =>
            {
                if (_tipsList.Count > 0)
                {
                    _tipsText = _tipsList[UnityEngine.Random.Range(0, _tipsList.Count)];
                }
            }, 3f, isLoop: true, isUnscaled: true);
        }

        /// <summary>每帧：按当前阶段驱动进度条平滑动画 + 刷新文本。</summary>
        protected override void OnUpdate()
        {
            base.OnUpdate();

            switch (_currentPhase)
            {
                case 0: // 阶段 0 预热（0→10%）
                    _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, WarmupSpeed * Time.unscaledDeltaTime);
                    if (_displayProgress >= 0.10f)
                    {
                        _currentPhase = 1;
                        StartRealLoading();
                    }
                    break;

                case 1: // 阶段 1 加载（10%→90%）
                    _phase1ElapsedTime += Time.unscaledDeltaTime;
                    _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, LoadingSpeed * Time.unscaledDeltaTime);

                    if (_sceneLoadComplete && _displayProgress >= 0.89f)
                    {
                        // 快速跳过模式：跳过收尾动画，直接激活场景并关闭
                        if (_skipMode)
                        {
                            _displayProgress = 1.0f;
                            _currentPhase = 2;
                            if (!_isSendLoadOver)
                            {
                                _isSendLoadOver = true;
                                GameEvent.Send(GlobalEventID.Event_LoadOver);
                            }
                            _isClosing = true;
                            TriggerFinish();
                            Close();
                        }
                        else
                        {
                            EnterFinishPhase();
                        }
                    }
                    else if (_phase1ElapsedTime >= 5.0f) // 兜底：超时强制进入收尾
                    {
                        Log.Warning($"[LoadingUI] 阶段 1→2（超时兜底）：已等待 {_phase1ElapsedTime:F1}s，强制进入收尾");
                        EnterFinishPhase();
                    }
                    break;

                case 2: // 阶段 2 收尾（90%→100% + 停留）：动画遮盖激活卡顿
                    // 钳制 deltaTime：激活那一帧 Time.unscaledDeltaTime 可能高达数百毫秒甚至秒级，
                    // 会把 90→100 动画和 100% 停留压缩成一帧瞬间完成（用户看不到 100%）。
                    // 用 0.05（约 20fps 步长）封顶，让收尾动画与停留按设定墙钟时长真实展开，确保用户看清 100%。
                    float clampedDelta = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                    _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, FinishSpeed * clampedDelta);
                    if (_displayProgress >= 1.0f)
                    {
                        _displayProgress = 1.0f;
                        _holdAt100Time += clampedDelta;
                        // 停留结束 → 回调并关闭（场景已在 90% 激活完毕）
                        if (!_isClosing && _holdAt100Time >= HoldAt100Duration)
                        {
                            _isClosing = true;
                            TriggerFinish();
                            Close();
                        }
                    }
                    break;
            }

            m_imgProgress.fillAmount = _displayProgress;
            _progress = Mathf.RoundToInt(_displayProgress * 100).ToString();

            if (m_tmpShowTips != null)
            {
                m_tmpShowTips.text = $"{_prefixText}<size=20>({_progress}%)</size>{_dotText}\n<size=20>tips：{_tipsText}</size>";
            }
        }

        /// <summary>发起真实场景加载（suspendLoad=true，fire-and-forget 规避陷阱 1）。</summary>
        private void StartRealLoading()
        {
            GameModule.Scene.LoadSceneAsync(_sceneName, LoadSceneMode.Single, suspendLoad: true, priority: 100,
                gcCollect: true, progressCallBack: OnLoadProgress);
        }

        /// <summary>场景加载进度回调：把 YooAsset 0~0.9 映射到 10%~90%，到 0.9 标记加载完成。</summary>
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

        /// <summary>进入收尾阶段：发 Event_LoadOver 请求激活场景，目标进度置 1.0，并停 Tips 防激活瞬间文案滚动干扰。</summary>
        /// <remarks>
        /// 场景激活由 <see cref="GameSceneManager"/> 收到 <see cref="GlobalEventID.Event_LoadOver"/> 后调
        /// <c>GameModule.Scene.UnSuspend</c> 完成；本窗口不再参与激活，仅继续播放 90%→100% 收尾动画遮盖激活卡顿。
        /// </remarks>
        private void EnterFinishPhase()
        {
            _currentPhase = 2;
            _targetProgress = 1.0f;
            GameModule.Timer.Stop(_timerTips);          // 进入收尾即停 Tips，防激活瞬间文案滚动干扰

            if (!_isSendLoadOver)
            {
                _isSendLoadOver = true;
                GameEvent.Send(GlobalEventID.Event_LoadOver);
            }
        }

        /// <summary>
        /// 统一终结出口：无论正常收尾、提前关闭还是异常，都经此方法触发 finishCallBack。
        /// </summary>
        /// <remarks>
        /// 防重标志 _isFinished 确保回调只触发一次。由 OnUpdate 阶段 2 正常流程或 OnDestroy 异常兜底调用。
        /// </remarks>
        private void TriggerFinish()
        {
            if (_isFinished) return;
            _isFinished = true;
            _finishCallBack?.Invoke();
        }

        /// <summary>销毁前清理定时器，防回调引用已销毁对象。</summary>
        protected override void OnDestroy()
        {
            GameModule.Timer.RemoveTimer(_timerDot);
            GameModule.Timer.RemoveTimer(_timerTips);

            // 兜底触发 finishCallBack：若窗口提前被 CloseAll 等外部调用关闭，确保回调仍然执行
            TriggerFinish();

            base.OnDestroy();
        }
    }
}

*/
