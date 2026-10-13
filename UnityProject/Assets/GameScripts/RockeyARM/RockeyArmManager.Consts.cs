// RockeyArmManager.Consts.cs
// 作用：只放 RockeyArmManager 用到的常量、协议偏移、错误码（Windows / Linux 双平台通用，错误码同厂商 Dongle_API.h）。
// 搭配文件：IRockeyArmModule.cs（接口与诊断类型）、RockeyArmManager.cs（主逻辑）、RockeyArmManager.Platform.cs（平台 P/Invoke 网关）。

namespace TEngine
{
    public sealed partial class RockeyArmManager
    {
        // ==================== 对外事件 ====================

        /// <summary>
        /// 授权错误 GameEvent 事件 ID。
        /// 订阅：GameEvent.AddEventListener&lt;string&gt;(RuntimeId.ToRuntimeId(RockeyArmManager.LICENSE_ERROR_EVENT_ID), handler)。
        /// 注：旧 ViKey 时代的 "SYSYTEM_VIKEYERROR" 已废弃。
        /// </summary>
        public const string LICENSE_ERROR_EVENT_ID = "ROCKEY_ARM_LICENSE_ERROR";

        // ==================== 基础协议 ====================

        const uint DONGLE_SUCCESS = 0x00000000;
        const ushort EXE_FILE_ID = 0x0002;          // 锁内“登记员”程序 ID

        const uint DEADLINE_PERMANENT = 0xFFFFFFFF; // 到期时间：永久授权

        // v3 协议长度：32 指纹 + 4 时间 + 16 会话 UUID + 4 产品ID
        const int IO_LEN = 56;
        const int FP_LEN = 32;
        const int OFF_TIME = 32;
        const int OFF_UUID = 36;
        const int OFF_APPID = 52;

        const uint APP_ID = 0x6ACDF146;  // 本产品ID，写死；多产品共狗就共用同一个ID

        // ==================== 锁内诊断包 ====================

        // 锁内 main 返回 3 时，如果支持诊断包：
        // InOutBuf[32..35] = 0xD1A60001
        // InOutBuf[36]     = 出错阶段
        // InOutBuf[37]     = 保留
        // InOutBuf[38..39] = 锁内系统 API 错误码，低字节在前
        const uint LOCK_DIAG_MAGIC = 0xD1A60001;
        const int OFF_DIAG_MAGIC = 32;
        const int OFF_DIAG_STAGE = 36;
        const int OFF_DIAG_CODE = 38;

        // ==================== 锁内 main 返回值 ====================

        const int RET_OK = 0;           // 通过
        const int RET_FIRST_BIND = 1;   // 首次绑定成功
        const int RET_WRONG_DEVICE = 2; // 换电脑了
        const int RET_ERROR = 3;        // 锁内程序内部出错
        const int RET_TIME_BACK = 4;    // 时间回退
        const int RET_EXPIRED = 5;      // 已到期
        const int RET_NOT_AUTHORIZED = 6; // 本产品未授权

        // 本类内部约定：不是锁内 main 的返回值
        const int RET_DONGLE_GONE = -1; // 枚举不到狗
        const int RET_API_FAILED = -2;  // Dongle_* API 调用本身失败

        // ==================== 厂商用户 API 错误码 ====================
        // 来源：《ROCKEY-ARM 开发手册》“用户 API 错误码”

        const uint DONGLE_NOT_FOUND = 0xF0000001;             // 未找到指定的设备
        const uint DONGLE_INVALID_HANDLE = 0xF0000002;        // 无效的句柄
        const uint DONGLE_INVALID_PARAMETER = 0xF0000003;     // 参数错误
        const uint DONGLE_COMM_ERROR = 0xF0000004;            // 通讯错误
        const uint DONGLE_INSUFFICIENT_BUFFER = 0xF0000005;   // 缓冲区空间不足
        const uint DONGLE_NOT_INITIALIZED = 0xF0000006;       // 产品尚未初始化（没设置 PID）
        const uint DONGLE_ALREADY_INITIALIZED = 0xF0000007;   // 产品已经初始化（已设置 PID）
        const uint DONGLE_ADMINPIN_NOT_CHECK = 0xF0000008;    // 开发商密码没有验证
        const uint DONGLE_USERPIN_NOT_CHECK = 0xF0000009;     // 用户密码没有验证
        const uint DONGLE_INCORRECT_PIN = 0xF000FF00;         // 密码错（低 8 位为剩余次数）
        const uint DONGLE_PIN_BLOCKED = 0xF000000A;           // PIN 码已锁死
        const uint DONGLE_ACCESS_DENIED = 0xF000000B;         // 访问被拒绝
        const uint DONGLE_FILE_EXIST = 0xF000000E;            // 文件已存在
        const uint DONGLE_FILE_NOT_FOUND = 0xF000000F;        // 未找到指定的文件
        const uint DONGLE_READ_ERROR = 0xF0000010;            // 读取数据错误
        const uint DONGLE_WRITE_ERROR = 0xF0000011;           // 写入数据错误
        const uint DONGLE_FILE_CREATE_ERROR = 0xF0000012;     // 创建文件错误
        const uint DONGLE_FILE_READ_ERROR = 0xF0000013;       // 读取文件错误
        const uint DONGLE_FILE_WRITE_ERROR = 0xF0000014;      // 写入文件错误
        const uint DONGLE_FILE_DEL_ERROR = 0xF0000015;        // 删除文件错误
        const uint DONGLE_FAILED = 0xF0000016;                // 操作失败
        const uint DONGLE_CLOCK_EXPIRE = 0xF0000017;          // 加密锁时钟到期
        const uint DONGLE_ERROR_UNKNOWN = 0xFFFFFFFF;         // 未知的错误

        // ==================== 本地配置 ====================

        // 用户只看简短提示，完整诊断留给 Console / OnLicenseErrorDetail / LastError。
        // true：GameEvent 发完整诊断字符串；false：GameEvent 只发用户可读短提示。
        const bool SEND_FULL_ERROR_TO_GAME_EVENT = false;
    }
}