// IRockeyArmModule.cs
// 作用：ROCKEY-ARM 加密锁授权模块对外接口，以及授权错误的结构化诊断类型。
// 搭配文件：RockeyArmManager.cs（主逻辑）、RockeyArmManager.Consts.cs（常量与协议）、RockeyArmManager.Platform.cs（平台 P/Invoke 网关）。
//
// 接入方式（验证时机由实际项目自行决定，模块只提供能力）：
// 1. 注册模块（建议在热更入口 GameApp.Entrance，或主包 Procedure 流程中按需 new）：
//        ModuleSystem.RegisterModule<IRockeyArmModule>(new RockeyArmManager());
// 2. 启动验证（二选一）：
//        GameModule.RockeyArm.CheckPermissions(onSuccess);                    // 同步版，主包 FSM 流程友好
//        bool ok = await GameModule.RockeyArm.CheckPermissionsAsync();        // 异步版，锁内 IO 在线程池
// 3. 启动看门狗（每 2 秒复查，狗被拔出自动重扫换句柄；被锁拒绝时广播错误事件并停止）：
//        GameModule.RockeyArm.StartWatchdog().Forget();
// 4. 订阅授权错误（用户界面只拿短提示；完整诊断读 LastError）：
//        GameEvent.AddEventListener<string>(RuntimeId.ToRuntimeId(RockeyArmManager.LICENSE_ERROR_EVENT_ID), OnLicenseError);

using System;
using System.Text;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace TEngine
{
    /// <summary>
    /// 加密锁授权模块接口。
    /// <remarks>绑定 / 校验 / 防回退 / 到期 / 看门狗复查均在锁内程序完成，软件侧只递数据收结果。</remarks>
    /// </summary>
    public interface IRockeyArmModule
    {
        /// <summary>当前是否已通过授权校验。看门狗复查失败或锁被拒绝时会翻回 false。</summary>
        bool IsLicensed { get; }

        /// <summary>狗发回来的通行证（32 字节），验证通过后才有值。</summary>
        byte[] SessionToken { get; }

        /// <summary>最近一次授权错误的完整诊断信息，供日志系统 / 客服排障界面随时读取。</summary>
        LicenseErrorInfo LastError { get; }

        /// <summary>
        /// 启动授权验证（同步版）。
        /// <remarks>枚举与锁内 IO 在调用线程执行，会短暂阻塞主线程；适合主包 Procedure / FSM 流程。</remarks>
        /// </summary>
        /// <param name="onSuccess">验证通过后的回调，在主线程执行。</param>
        void CheckPermissions(Action onSuccess);

        /// <summary>
        /// 启动授权验证（异步版）。
        /// <remarks>设备指纹在主线程计算（依赖 SystemInfo API），枚举与锁内 IO 在线程池执行，await 后回到主线程。</remarks>
        /// </summary>
        /// <returns>验证是否通过；失败时已通过错误事件与 LastError 报告具体原因。</returns>
        UniTask<bool> CheckPermissionsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 启动看门狗：每 2 秒复查一次授权；狗被拔出时同拍重扫一圈换句柄静默继续，
        /// 锁内拒绝（换机 / 到期 / 时间回退 / 未授权）时立即锁死并广播错误事件后退出循环。
        /// </summary>
        UniTaskVoid StartWatchdog(CancellationToken cancellationToken = default);
    }

    /// <summary>授权错误归类，用于界面按类型分支处理（如区分"没插狗"和"狗被绑定到别的机器"）。</summary>
    public enum RockeyErrorKind
    {
        Unknown = 0,
        DongleNotFound,
        DongleOpenFailed,
        DongleApiFailed,
        LockInternalError,
        WrongDevice,
        TimeBack,
        Expired,
        NotAuthorized,          // 本产品未授权（锁内返回 6）
        UnknownLockResult,
        UnsupportedPlatform     // 当前平台不支持加密锁（仅 Windows / Linux x86_64）
    }

    /// <summary>授权错误完整诊断信息。</summary>
    public sealed class LicenseErrorInfo
    {
        public RockeyErrorKind Kind = RockeyErrorKind.Unknown;
        public string Stage = string.Empty;
        public string UserMessage = string.Empty;
        public string Detail = string.Empty;

        public bool HasApiCode;
        public string ApiName = string.Empty;
        public uint ApiCode;
        public string ApiMeaning = string.Empty;

        public bool HasMainRet;
        public int MainRet;
        public string MainRetMeaning = string.Empty;

        public bool HasLockDiag;
        public byte LockStage;
        public string LockStageMeaning = string.Empty;
        public ushort LockApiCode;
        public string LockApiMeaning = string.Empty;

        public DateTime UtcTime = DateTime.UtcNow;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(string.IsNullOrEmpty(UserMessage) ? "未知授权错误" : UserMessage);

            if (!string.IsNullOrEmpty(Stage))
                sb.Append(" [阶段: ").Append(Stage).Append(']');

            if (HasApiCode)
            {
                sb.Append(" [厂商API: ").Append(ApiName)
                    .Append(" = 0x").Append(ApiCode.ToString("X8"))
                    .Append(' ').Append(ApiMeaning).Append(']');
            }

            if (HasMainRet)
            {
                sb.Append(" [锁内返回值: ").Append(MainRet)
                    .Append(' ').Append(MainRetMeaning).Append(']');
            }

            if (HasLockDiag)
            {
                sb.Append(" [锁内诊断: 阶段 ").Append(LockStage)
                    .Append('(').Append(LockStageMeaning).Append(')')
                    .Append(", 系统API = 0x").Append(LockApiCode.ToString("X4"))
                    .Append(' ').Append(LockApiMeaning).Append(']');
            }

            if (!string.IsNullOrEmpty(Detail))
                sb.Append(" [说明: ").Append(Detail).Append(']');

            sb.Append(" [UTC: ").Append(UtcTime.ToString("yyyy-MM-dd HH:mm:ss")).Append(']');
            return sb.ToString();
        }
    }
}
