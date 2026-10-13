// RockeyArmManager.cs
// 作用：ROCKEY-ARM 客户端主逻辑（Windows / Linux 双平台）。
// 搭配文件：RockeyArmManager.Consts.cs（常量与协议）、RockeyArmManager.Platform.cs（平台 P/Invoke 网关）
// 平台：Windows 加载 Dongle_d.dll（StdCall）；Linux 加载 libRockeyARM.so（Cdecl，需先 root 运行 SDK ./inst 装 udev 规则）。
//       两平台 Dongle_* API 签名与错误码完全一致，主逻辑无平台分支；锁内程序（0x0002）与狗通用，不区分平台。

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// RockeyArmManager（锁内程序版 v2.1，零 PIN、零匿名读账本、UUID 会话、完整错误诊断）
    ///
    /// 架构一句话：密码和账本全锁在狗肚子里。
    /// 绑定 / 校验 / 防回退 / 到期 / 使用统计全部在锁内 exe（0x0002）完成，
    /// 软件只调 Dongle_RunExeFile 递数据收结果，全程零 PIN。
    ///
    /// v2.1：
    /// 1. 厂商 API 错误码完整翻译；
    /// 2. 新增 LicenseErrorInfo / OnLicenseErrorDetail / LastError；
    /// 3. 区分“Dongle API 调用失败”和“锁内程序返回内部错误”；
    /// 4. 支持锁内诊断包，能解析 0x6A83 这类锁内系统错误码。
    /// 
    /// 调用内部EXE前填写的数据
    /// 0~31  指纹 SHA256(deviceUniqueIdentifier)
    /// 32~35 UTC 秒，低字节在前
    /// 36~51 会话 UUID（软件启动时生成一次，全程固定）
    /// 52~55 APP_ID（写死，如 0xA101）
    /// </summary>
    public partial class RockeyArmManager
    {
        // 原生 API 声明与平台选择见 RockeyArmManager.Platform.cs（RockeyArmNative）。

        IntPtr hDongle = IntPtr.Zero;
        public bool isLicensed = false;

        /// <summary>狗发回来的通行证（32 字节），验证通过后才有值。</summary>
        public byte[] SessionToken { get; private set; }

        /// <summary>设备指纹缓存：CheckPermissions（主线程）算一次，之后线程池直接用。</summary>
        byte[] _fp;

        /// <summary>本次进程会话 UUID：CheckPermissions 时生成一次，整个运行期不变。</summary>
        byte[] _sessionUuid;

        readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        /// <summary>兼容旧逻辑：只发短文案。</summary>
        public delegate void LicenseErrorEvent(string error);

        public static event LicenseErrorEvent OnLicenseError;

        /// <summary>新诊断事件：给日志系统 / 客服排障界面用。</summary>
        public delegate void LicenseErrorDetailEvent(LicenseErrorInfo error);

        public static event LicenseErrorDetailEvent OnLicenseErrorDetail;

        /// <summary>最近一次授权错误，供任意界面随时读取。</summary>
        public static LicenseErrorInfo LastError { get; private set; }

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
            NotAuthorized, // 本产品未授权（锁内返回6）
            UnknownLockResult
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

        sealed class GuardResult
        {
            public int Ret;
            public byte[] Token;
            public uint Deadline = DEADLINE_PERMANENT;
            public LicenseErrorInfo Error;

            public static GuardResult FromError(int ret, LicenseErrorInfo error)
            {
                return new GuardResult { Ret = ret, Error = error };
            }
        }

        public async UniTaskVoid StartWatchdog(CancellationToken cancellationToken = default)
        {
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken, _lifetimeCts.Token))
            {
                while (!linkedCts.Token.IsCancellationRequested && hDongle != IntPtr.Zero)
                {
                    if (await UniTask.WaitForSeconds(2f, cancellationToken: linkedCts.Token)
                            .SuppressCancellationThrow())
                        break;

                    GuardResult result = await UniTask.RunOnThreadPool(
                        () => RunGuard("看门狗授权复查"), cancellationToken: linkedCts.Token);

                    if (result.Ret == RET_OK || result.Ret == RET_FIRST_BIND)
                    {
                        if (result.Token != null) SessionToken = result.Token;
                        isLicensed = true;
                        continue;
                    }

                    // 狗回答了但拒绝（2~6）：立即锁死
                    if (result.Ret >= RET_WRONG_DEVICE)
                    {
                        isLicensed = false;
                        ReportError(result.Error);
                        break;
                    }

                    // 调用失败（狗被拔/通信异常）：同一拍重扫一圈，找到就换句柄静默继续
                    GuardResult rescan = await UniTask.RunOnThreadPool(
                        () => FindMyDog("看门狗授权复查"), cancellationToken: linkedCts.Token);

                    if (rescan.Ret == RET_OK || rescan.Ret == RET_FIRST_BIND)
                    {
                        if (rescan.Token != null) SessionToken = rescan.Token;
                        isLicensed = true;
                        continue;
                    }

                    isLicensed = false;
                    ReportError(rescan.Error ?? MakeSimpleError(
                        RockeyErrorKind.DongleNotFound, "看门狗授权复查",
                        "检测到加密狗已拔出! 请插入加密狗后重启应用!",
                        "重扫后仍未找到本产品的加密锁"));
                    break;
                }
            }
        }
        
        public void CheckPermissions(Action onSuccess)
        {
            isLicensed = false;
            SessionToken = null;

            _fp = GetDeviceFingerprint();
            _sessionUuid = Guid.NewGuid().ToByteArray();

            GuardResult result = FindMyDog("启动授权验证");
            if (result.Ret != RET_OK && result.Ret != RET_FIRST_BIND)
            {
                isLicensed = false;
                SessionToken = null;
                ReportError(result.Error);
                return;
            }

            SessionToken = result.Token;
            isLicensed = true;

            if (result.Ret == RET_FIRST_BIND)
                Debug.Log("<color=green>首次使用，本机绑定成功</color>");

            Debug.Log("<color=green>ROCKEY-ARM 授权验证通过</color>");
            PrintLicenseDeadline(result.Deadline);
            onSuccess?.Invoke();
        }

        /// <summary>
        /// 扫圈找本软件的狗：逐只打开试 RunExeFile，返回 0/1 的记住句柄。
        /// 全部被拒时，优先报"非6"的严重错误（如到期/换机）；全是6报未授权；一只都没有报未找到。
        /// </summary>
        GuardResult FindMyDog(string stage)
        {
            int count = 0;
            uint enumRes = RockeyArmNative.Dongle_Enum(IntPtr.Zero, ref count);
            if (enumRes != DONGLE_SUCCESS)
                return GuardResult.FromError(RET_API_FAILED, MakeApiError(
                    RockeyErrorKind.DongleApiFailed, stage + "：枚举加密锁",
                    "Dongle_Enum", enumRes, "加密锁枚举失败，请检查加密锁连接"));

            if (count == 0)
                return GuardResult.FromError(RET_DONGLE_GONE, MakeSimpleError(
                    RockeyErrorKind.DongleNotFound, stage + "：枚举加密锁",
                    "未找到加密锁!", "Dongle_Enum 执行成功，但设备数 = 0"));

            LicenseErrorInfo firstSeriousError = null;

            for (int i = 0; i < count; i++)
            {
                IntPtr h = IntPtr.Zero;
                if (RockeyArmNative.Dongle_Open(out h, i) != DONGLE_SUCCESS || h == IntPtr.Zero)
                    continue;

                IntPtr oldHandle = hDongle;
                hDongle = h;
                GuardResult result = RunGuard(stage);

                if (result.Ret == RET_OK || result.Ret == RET_FIRST_BIND)
                {
                    if (oldHandle != IntPtr.Zero && oldHandle != h) RockeyArmNative.Dongle_Close(oldHandle);
                    return result; // 找到本软件的狗，句柄已记好
                }

                RockeyArmNative.Dongle_Close(h);
                hDongle = oldHandle; // 还原

                if (result.Ret != RET_NOT_AUTHORIZED && firstSeriousError == null)
                    firstSeriousError = result.Error; // 记下非6的错误，优先报它
            }

            if (firstSeriousError != null)
                return GuardResult.FromError(RET_API_FAILED, firstSeriousError);

            return GuardResult.FromError(RET_NOT_AUTHORIZED, MakeSimpleError(
                RockeyErrorKind.NotAuthorized, stage,
                "请插入本产品对应的加密锁!", $"已枚举到 {count} 只加密锁，均无本产品授权"));
        }

        void PrintLicenseDeadline(uint deadline)
        {
            if (deadline == DEADLINE_PERMANENT)
            {
                Debug.Log("<color=green>授权有效期: 永久</color>");
                return;
            }

            DateTime expiry = DateTimeOffset.FromUnixTimeSeconds(deadline).ToLocalTime().DateTime;
            TimeSpan remain = expiry - DateTime.Now;
            if (remain.TotalSeconds <= 0)
                Debug.Log($"<color=red>授权有效期至: {expiry:yyyy-MM-dd HH:mm:ss}（已到期）</color>");
            else
                Debug.Log($"<color=green>授权有效期至: {expiry:yyyy-MM-dd HH:mm:ss}（剩余 {remain.Days} 天 {remain.Hours} 小时）</color>");
        }

        /// <summary>
        /// 调锁内“登记员”：递指纹 + 时间 + 会话 UUID，收通行证 + 到期时间。
        /// 可能被看门狗从线程池调用，严禁在这里碰 Unity API。
        /// </summary>
        GuardResult RunGuard(string stage)
        {
            if (_fp == null || _fp.Length != FP_LEN)
            {
                return GuardResult.FromError(RET_API_FAILED, MakeSimpleError(
                    RockeyErrorKind.DongleApiFailed,
                    stage,
                    "授权校验初始化异常!",
                    "设备指纹缓存为空，必须先调用 CheckPermissions"));
            }

            if (_sessionUuid == null || _sessionUuid.Length != 16)
            {
                return GuardResult.FromError(RET_API_FAILED, MakeSimpleError(
                    RockeyErrorKind.DongleApiFailed,
                    stage,
                    "授权校验初始化异常!",
                    "会话 UUID 为空，必须先调用 CheckPermissions"));
            }

            if (hDongle == IntPtr.Zero)
            {
                return GuardResult.FromError(RET_API_FAILED, MakeSimpleError(
                    RockeyErrorKind.DongleApiFailed,
                    stage,
                    "加密锁句柄无效!",
                    "Dongle_Open 尚未成功或句柄已被关闭"));
            }

            byte[] buf = new byte[IO_LEN];
            Array.Copy(_fp, 0, buf, 0, FP_LEN);

            uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Array.Copy(BitConverter.GetBytes(now), 0, buf, OFF_TIME, 4);
            Array.Copy(_sessionUuid, 0, buf, OFF_UUID, 16);
            Array.Copy(BitConverter.GetBytes(APP_ID), 0, buf, OFF_APPID, 4);

            uint apiRes = RockeyArmNative.Dongle_RunExeFile(hDongle, EXE_FILE_ID, buf, IO_LEN, out int mainRet);
            if (apiRes != DONGLE_SUCCESS)
            {
                return GuardResult.FromError(RET_API_FAILED, MakeApiError(
                    RockeyErrorKind.DongleApiFailed,
                    stage,
                    "Dongle_RunExeFile",
                    apiRes,
                    "调用锁内授权程序失败!",
                    "厂商 API 没有成功执行锁内程序，常见原因：狗被拔出、句柄失效、通讯异常、锁内程序未烧录或运行权限配置错误"));
            }

            GuardResult result = new GuardResult();
            result.Ret = mainRet;
            result.Deadline = DEADLINE_PERMANENT;

            if (mainRet == RET_OK || mainRet == RET_FIRST_BIND)
            {
                result.Token = new byte[FP_LEN];
                Array.Copy(buf, 0, result.Token, 0, FP_LEN);
                result.Deadline = BitConverter.ToUInt32(buf, OFF_TIME);
                return result;
            }

            result.Error = MakeLockResultError(stage, mainRet, buf);
            return result;
        }

        static byte[] GetDeviceFingerprint()
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(SystemInfo.deviceUniqueIdentifier));
        }

        static LicenseErrorInfo MakeSimpleError(RockeyErrorKind kind, string stage, string userMessage, string detail = "")
        {
            return new LicenseErrorInfo
            {
                Kind = kind,
                Stage = stage,
                UserMessage = userMessage,
                Detail = detail
            };
        }

        static LicenseErrorInfo MakeApiError(RockeyErrorKind kind, string stage, string apiName, uint apiCode, string userMessage, string detail = "")
        {
            return new LicenseErrorInfo
            {
                Kind = kind,
                Stage = stage,
                UserMessage = userMessage,
                Detail = detail,
                HasApiCode = true,
                ApiName = apiName,
                ApiCode = apiCode,
                ApiMeaning = DescribeDongleApiError(apiCode)
            };
        }

        static LicenseErrorInfo MakeLockResultError(string stage, int mainRet, byte[] io)
        {
            RockeyErrorKind kind = RockeyErrorKind.UnknownLockResult;
            string userMessage = "加密锁数据异常，请联系供应商!";

            switch (mainRet)
            {
                case RET_WRONG_DEVICE:
                    kind = RockeyErrorKind.WrongDevice;
                    userMessage = "本加密锁已绑定其他电脑，无法在本机使用!";
                    break;
                case RET_TIME_BACK:
                    kind = RockeyErrorKind.TimeBack;
                    userMessage = "检测到系统时间异常，授权锁定!";
                    break;
                case RET_EXPIRED:
                    kind = RockeyErrorKind.Expired;
                    userMessage = "授权已到期!";
                    break;
                case RET_NOT_AUTHORIZED:
                    kind = RockeyErrorKind.NotAuthorized;
                    userMessage = "本加密锁未授权本产品，请插入本产品对应的加密锁!";
                    break;
                case RET_ERROR:
                    kind = RockeyErrorKind.LockInternalError;
                    userMessage = "锁内授权程序内部出错，请联系供应商!";
                    break;
            }

            LicenseErrorInfo info = new LicenseErrorInfo
            {
                Kind = kind,
                Stage = stage,
                UserMessage = userMessage,
                HasMainRet = true,
                MainRet = mainRet,
                MainRetMeaning = DescribeLockMainRet(mainRet)
            };

            if (mainRet == RET_ERROR)
            {
                byte lockStage;
                ushort lockCode;
                if (TryParseLockDiag(io, out lockStage, out lockCode))
                {
                    info.HasLockDiag = true;
                    info.LockStage = lockStage;
                    info.LockStageMeaning = DescribeLockStage(lockStage);
                    info.LockApiCode = lockCode;
                    info.LockApiMeaning = DescribeLockApiError(lockCode);
                    info.Detail = "锁内程序已回传具体系统错误码";
                }
                else
                {
                    info.Detail = "当前锁内程序未回传诊断码，只能确定是锁内内部错误；若需定位到具体 read_file/write_file 错误，请刷入带诊断回传的锁内程序";
                }
            }

            return info;
        }

        static bool TryParseLockDiag(byte[] buf, out byte lockStage, out ushort lockCode)
        {
            lockStage = 0;
            lockCode = 0;

            if (buf == null || buf.Length < OFF_DIAG_CODE + 2)
                return false;

            if (BitConverter.ToUInt32(buf, OFF_DIAG_MAGIC) != LOCK_DIAG_MAGIC)
                return false;

            lockStage = buf[OFF_DIAG_STAGE];
            lockCode = BitConverter.ToUInt16(buf, OFF_DIAG_CODE);
            return true;
        }

        /// <summary>厂商用户 API 错误码翻译。</summary>
        static string DescribeDongleApiError(uint code)
        {
            // 手册特殊说明：密码错误时 (错误码 & 0xFFFFFF00) == DONGLE_INCORRECT_PIN，低 8 位是剩余次数。
            if ((code & 0xFFFFFF00) == DONGLE_INCORRECT_PIN)
                return $"密码错误，剩余重试次数 {code & 0xFF}";

            switch (code)
            {
                case DONGLE_SUCCESS: return "操作成功";
                case DONGLE_NOT_FOUND: return "未找到指定的设备";
                case DONGLE_INVALID_HANDLE: return "无效的句柄";
                case DONGLE_INVALID_PARAMETER: return "参数错误";
                case DONGLE_COMM_ERROR: return "通讯错误";
                case DONGLE_INSUFFICIENT_BUFFER: return "缓冲区空间不足";
                case DONGLE_NOT_INITIALIZED: return "产品尚未初始化（未设置 PID）";
                case DONGLE_ALREADY_INITIALIZED: return "产品已经初始化（已设置 PID）";
                case DONGLE_ADMINPIN_NOT_CHECK: return "开发商密码没有验证";
                case DONGLE_USERPIN_NOT_CHECK: return "用户密码没有验证";
                case DONGLE_PIN_BLOCKED: return "PIN 码已锁死";
                case DONGLE_ACCESS_DENIED: return "访问被拒绝";
                case DONGLE_FILE_EXIST: return "文件已存在";
                case DONGLE_FILE_NOT_FOUND: return "未找到指定的文件";
                case DONGLE_READ_ERROR: return "读取数据错误";
                case DONGLE_WRITE_ERROR: return "写入数据错误";
                case DONGLE_FILE_CREATE_ERROR: return "创建文件错误";
                case DONGLE_FILE_READ_ERROR: return "读取文件错误";
                case DONGLE_FILE_WRITE_ERROR: return "写入文件错误";
                case DONGLE_FILE_DEL_ERROR: return "删除文件错误";
                case DONGLE_FAILED: return "操作失败";
                case DONGLE_CLOCK_EXPIRE: return "加密锁时钟到期";
                case DONGLE_ERROR_UNKNOWN: return "未知的错误";
                default: return "手册未定义的厂商 API 错误";
            }
        }

        static string DescribeLockMainRet(int ret)
        {
            switch (ret)
            {
                case RET_OK: return "通过";
                case RET_FIRST_BIND: return "首次绑定成功";
                case RET_WRONG_DEVICE: return "换电脑/设备指纹不匹配";
                case RET_ERROR: return "锁内程序内部出错";
                case RET_TIME_BACK: return "检测到系统时间回退";
                case RET_EXPIRED: return "授权已到期";
                case RET_NOT_AUTHORIZED: return "本产品未授权";
                default: return "未定义的锁内返回值";
            }
        }

        /// <summary>锁内系统 API 错误码翻译。</summary>
        static string DescribeLockApiError(ushort code)
        {
            switch (code)
            {
                case 0x0001: return "自定义：账本绑定标志非法";
                case 0x9000: return "操作成功";
                case 0x6C00: return "无效的 INS";
                case 0x6C01: return "无效的 P1";
                case 0x6C02: return "无效的 P2";
                case 0x6C03: return "无效的 LEN";
                case 0x6C04: return "无效的参数";
                case 0x6C05: return "操作失败";
                case 0x6B00: return "读数据错误";
                case 0x6B01: return "写数据错误";
                case 0x6A82: return "文件已存在";
                case 0x6A83: return "文件不存在";
                case 0x6A86: return "创建文件失败";
                case 0x6A87: return "读文件失败";
                case 0x6A88: return "写文件失败";
                case 0x6A89: return "删除文件失败";
                case 0x6A8A: return "创建文件夹失败";
                case 0x6A8B: return "删除文件夹失败";
                case 0x6980: return "尚未初始化";
                case 0x6981: return "已初始化过了";
                case 0x6982: return "管理员 PIN 没有校验";
                case 0x6983: return "用户 PIN 没有校验";
                case 0x6984: return "PIN 码已被锁定";
                case 0x6985: return "运行已受限（如私钥运算、种子码运算）";
                default: return "手册未定义的锁内系统 API 错误";
            }
        }

        /// <summary>锁内诊断阶段翻译。阶段号必须和锁内 main.c 补丁保持一致。</summary>
        static string DescribeLockStage(byte stage)
        {
            switch (stage)
            {
                case 1: return "读取绑定标志";
                case 2: return "首绑写入设备指纹";
                case 3: return "首绑写入绑定标志";
                case 4: return "读取已绑定设备指纹";
                case 5: return "读取到期时间";
                case 6: return "读取最晚时间/使用统计块";
                case 7: return "写入最晚时间/使用统计块";
                case 8: return "读取共享内存";
                case 9: return "写入共享内存";
                case 10: return "账本绑定标志内容异常";
                case 11: return "生成通行证";
                case 12: return "读取产品ID";
                default: return "未定义阶段";
            }
        }

        void ReportError(LicenseErrorInfo info)
        {
            if (info == null)
                info = MakeSimpleError(RockeyErrorKind.Unknown, "未知阶段", "未知授权错误!");

            LastError = info;

            // 先打印完整日志，确保就算 GameEvent 订阅方异常/没接，也能在日志里看到错误码。
            Debug.LogError("[ROCKEY-ARM 授权错误] " + info);

            // 再通知外部。旧事件只发短文案，新事件发结构化完整信息。
            OnLicenseError?.Invoke(info.UserMessage);
            OnLicenseErrorDetail?.Invoke(info);

            // 最后发游戏事件。用户界面只拿短提示；完整诊断已经先写入日志。
            string gameEventMessage = SEND_FULL_ERROR_TO_GAME_EVENT ? info.ToString() : info.UserMessage;
            GameEvent.Send<string>(RuntimeId.ToRuntimeId("SYSYTEM_VIKEYERROR"), gameEventMessage);
        }

        void ReportError(string msg)
        {
            ReportError(MakeSimpleError(RockeyErrorKind.Unknown, "未分类错误", msg));
        }

        public void OnApplicationQuit()
        {
            if (!_lifetimeCts.IsCancellationRequested) _lifetimeCts.Cancel();
            if (hDongle != IntPtr.Zero) RockeyArmNative.Dongle_Close(hDongle);
            hDongle = IntPtr.Zero;
            _lifetimeCts.Dispose();
        }
    }
}