// RockeyArmModule.cs
// 作用：RockeyArmManager（零框架依赖核心）的 TEngine 适配层。
// 搭配文件：IRockeyArmModule.cs（TEngine 门面接口）、../RockeyArmManager.cs（核心实现）。
//
// 桥接内容：
// - 生命周期：OnInit（注册时由 ModuleSystem 触发）接线日志并转发 Init；Shutdown 转发 Release（关狗句柄、停看门狗）；
// - 日志：核心注入的 InfoLogger / WarningLogger / ErrorLogger 接到 TEngine.Log（TouchSocket 桥接 + 落盘）；
// - 授权错误：订阅核心 LicenseError 事件，转 GameEvent 广播（事件 ID 见 LICENSE_ERROR_EVENT_ID），
//   便于热更域用 GameEvent.AddEventListener<string> 订阅；需要结构化诊断可读 LastError。
// - 用户只看简短提示，完整诊断留给日志 / LastError。
//   true：GameEvent 发完整诊断字符串；false：GameEvent 只发用户可读短提示。

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RockeyArm;

namespace TEngine
{
    /// <summary>
    /// ROCKEY-ARM 加密锁授权的 TEngine 模块适配层（核心逻辑全在 RockeyArmManager，本类只做桥接）。
    /// </summary>
    public sealed class RockeyArmModule : Module, IRockeyArmModule
    {
        /// <summary>
        /// 授权错误 GameEvent 事件 ID。
        /// 订阅：GameEvent.AddEventListener&lt;string&gt;(RuntimeId.ToRuntimeId(RockeyArmModule.LICENSE_ERROR_EVENT_ID), handler)。
        /// 注：旧 ViKey 时代的 "SYSYTEM_VIKEYERROR" 已废弃。
        /// </summary>
        public const string LICENSE_ERROR_EVENT_ID = "ROCKEY_ARM_LICENSE_ERROR";

        const bool SEND_FULL_ERROR_TO_GAME_EVENT = false;

        readonly RockeyArmManager _core = new RockeyArmManager();
        bool _wired;

        /// <inheritdoc />
        public bool IsLicensed => _core.IsLicensed;

        /// <inheritdoc />
        public byte[] SessionToken => _core.SessionToken;

        /// <inheritdoc />
        public LicenseErrorInfo LastError => _core.LastError;

        /// <inheritdoc />
        public event Action<LicenseErrorInfo> LicenseError
        {
            add => _core.LicenseError += value;
            remove => _core.LicenseError -= value;
        }

        /// <summary>注册进 ModuleSystem 时由框架调用：接日志、订阅核心错误事件并转发核心初始化。</summary>
        public override void OnInit()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;
            // TEngine.Log 带 Conditional 特性，不能直接转委托，用 lambda 包一层。
            _core.InfoLogger = msg => Log.Info(msg);
            _core.WarningLogger = msg => Log.Warning(msg);
            _core.ErrorLogger = msg => Log.Error(msg);
            _core.LicenseError += OnCoreLicenseError;
            _core.Init();
        }

        /// <summary>框架关闭时调用：注销桥接并释放核心（幂等）。</summary>
        public override void Shutdown()
        {
            if (_wired)
            {
                _wired = false;
                _core.LicenseError -= OnCoreLicenseError;
            }

            _core.Release();
        }

        /// <inheritdoc />
        public void Init() => _core.Init();

        /// <inheritdoc />
        public void Release() => _core.Release();

        /// <inheritdoc />
        public void CheckPermissions(Action onSuccess) => _core.CheckPermissions(onSuccess);

        /// <inheritdoc />
        public UniTask<bool> CheckPermissionsAsync(CancellationToken cancellationToken = default)
            => _core.CheckPermissionsAsync(cancellationToken);

        /// <inheritdoc />
        public UniTaskVoid StartWatchdog(CancellationToken cancellationToken = default)
            => _core.StartWatchdog(cancellationToken);

        void OnCoreLicenseError(LicenseErrorInfo info)
        {
            string message = SEND_FULL_ERROR_TO_GAME_EVENT ? info?.ToString() : info?.UserMessage;
            GameEvent.Send<string>(RuntimeId.ToRuntimeId(LICENSE_ERROR_EVENT_ID), message);
        }
    }
}
