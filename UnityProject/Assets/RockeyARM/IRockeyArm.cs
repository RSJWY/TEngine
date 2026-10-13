// IRockeyArm.cs
// 作用：ROCKEY-ARM 加密锁授权模块对外接口（零框架依赖），以及授权错误的结构化诊断类型。
// 搭配文件：RockeyArmManager.cs（主逻辑）、RockeyArmManager.Consts.cs（常量与协议）、RockeyArmManager.Platform.cs（平台 P/Invoke 网关）。
//
// 接入方式（验证时机由宿主自行决定，模块只提供能力）：
// 1. 创建并初始化：var arm = new RockeyArmManager(); arm.Init();
// 2. 接日志（可选，不接则静默）：arm.InfoLogger = Debug.Log; arm.WarningLogger = Debug.LogWarning; arm.ErrorLogger = Debug.LogError;
// 3. 订阅授权错误：arm.LicenseError += info => ...（UI 提示用 info.UserMessage，排障读 info 完整字段或 arm.LastError）；
// 4. 启动验证（二选一，均会先走 #if UNITY_EDITOR 短路，打包后恒真实校验）：
//        arm.CheckPermissions(onSuccess);                    // 同步版，阻塞调用线程
//        bool ok = await arm.CheckPermissionsAsync();        // 异步版，锁内 IO 在线程池
// 5. 通过后启动看门狗：arm.StartWatchdog().Forget();
// 6. 宿主销毁时释放（关狗句柄、停看门狗，幂等）：arm.Release();
//
// TEngine 工程不必直接使用本接口：请用 TEngine/RockeyArmModule.cs 适配层
//（继承 TEngine.Module，桥接 TEngine.Log 与 GameEvent，接口为 TEngine.IRockeyArmModule）。
// 完整调用示例见 UsageSamples/BareSample 与 UsageSamples/TEngineSample。

using System;
using System.Text;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace RockeyArm
{
    /// <summary>
    /// 加密锁授权模块接口（零框架依赖）。
    /// <remarks>绑定 / 校验 / 防回退 / 到期 / 看门狗复查均在锁内程序完成，软件侧只递数据收结果。</remarks>
    /// </summary>
    public interface IRockeyArm
    {
        /// <summary>当前是否已通过授权校验。看门狗复查失败或锁被拒绝时会翻回 false。</summary>
        bool IsLicensed { get; }

        /// <summary>狗发回来的通行证（32 字节），验证通过后才有值。</summary>
        byte[] SessionToken { get; }

        /// <summary>最近一次授权错误的完整诊断信息，供日志系统 / 客服排障界面随时读取。</summary>
        LicenseErrorInfo LastError { get; }

        /// <summary>授权错误事件（结构化诊断）；由宿主订阅，界面提示用 <see cref="LicenseErrorInfo.UserMessage"/>。</summary>
        event Action<LicenseErrorInfo> LicenseError;

        /// <summary>初始化（当前无前置资源，与 <see cref="Release"/> 配对）。</summary>
        void Init();

        /// <summary>释放：停止看门狗、关闭加密锁句柄。幂等，可安全重复调用。</summary>
        void Release();

        /// <summary>
        /// 启动授权验证（同步版）。
        /// <remarks>枚举与锁内 IO 在调用线程执行，会短暂阻塞主线程；适合启动 FSM / Procedure 流程。</remarks>
        /// </summary>
        /// <param name="onSuccess">验证通过后的回调，在调用线程执行。</param>
        void CheckPermissions(Action onSuccess);

        /// <summary>
        /// 启动授权验证（异步版）。
        /// <remarks>设备指纹在主线程计算（依赖 SystemInfo API），枚举与锁内 IO 在线程池执行，await 后回到主线程。</remarks>
        /// </summary>
        /// <returns>验证是否通过；失败时已通过 LicenseError 事件与 LastError 报告具体原因。</returns>
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
