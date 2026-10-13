# RockeyARM 加密狗授权模块

本文描述当前 Fork 内置的 ROCKEY-ARM USB 加密锁授权模块（锁内程序版 v2.2，TEngine 框架适配版）。模块提供启动验证、看门狗周期复查、设备绑定、防时间回退、到期控制与完整错误诊断，全部核心校验逻辑运行在加密锁硬件内部。

## 模块位置与程序集

| 项 | 值 |
| --- | --- |
| 代码位置 | `Assets/GameScripts/RockeyARM/` |
| 程序集 | `RockeyARM.asmdef`（独立 AOT 主包程序集，`autoReferenced`） |
| 引用 | `TEngine.Runtime`、`UniTask` |
| 命名空间 | `TEngine`（接口与实现同命名空间，与框架模块风格一致） |
| 访问门面 | `GameModule.RockeyArm`（`IRockeyArmModule`） |

模块位于主包（非热更域），因此授权验证可以安排在热更 DLL 加载之前执行，破解者无法通过跳过热更程序集绕过验证；同时热更域（`GameLogic`）通过 asmdef 引用可直接使用接口。

## 文件结构

| 文件 | 职责 |
| --- | --- |
| `IRockeyArmModule.cs` | 模块接口、`RockeyErrorKind` 错误分类、`LicenseErrorInfo` 结构化诊断 |
| `RockeyArmManager.cs` | 主逻辑（验证、看门狗、错误翻译） |
| `RockeyArmManager.Consts.cs` | 协议常量、偏移、错误码、GameEvent 事件 ID |
| `RockeyArmManager.Platform.cs` | Windows / Linux P/Invoke 网关（`RockeyArmNative`）与平台守卫 |

原生插件（不在本模块内，需自行部署）：

- Windows：`Assets/Plugins/ROCKEY/Windows/x86_64/Dongle_d.dll`（StdCall）
- Linux：`Assets/Plugins/ROCKEY/Linux/x86_64/libRockeyARM.so`（Cdecl）

## 安全架构（锁内程序）

密码和账本全部保存在加密锁内部，软件侧全程零 PIN：

- 绑定、校验、防时间回退、到期判断、使用统计均由锁内 exe（文件 ID `0x0002`）完成；
- 软件侧只调用 `Dongle_RunExeFile` 递数据、收结果，拿到 32 字节会话通行证（`SessionToken`）；
- 软件递入的数据布局（56 字节）：

| 偏移 | 内容 |
| --- | --- |
| 0~31 | 设备指纹 `SHA256(SystemInfo.deviceUniqueIdentifier)` |
| 32~35 | UTC 秒（低字节在前） |
| 36~51 | 会话 UUID（启动时生成一次，运行期不变） |
| 52~55 | 产品 APP_ID（`0x6ACDF146`，多产品共狗共用） |

锁内 main 返回值：`0` 通过、`1` 首次绑定、`2` 换机、`3` 锁内内部错误（可带诊断包）、`4` 时间回退、`5` 到期、`6` 产品未授权。软件侧内部另有 `-1` 枚举不到狗、`-2` 厂商 API 调用失败两个约定值。

## 接入步骤

验证时机由实际项目自行决定（主包 Procedure 流程或热更入口均可），模块只提供能力：

```csharp
// 1. 注册模块（如 GameApp.Entrance）
ModuleSystem.RegisterModule<IRockeyArmModule>(new RockeyArmManager());

// 2a. 同步验证（主包 FSM / Procedure 流程友好；onSuccess 仅在通过时回调）
GameModule.RockeyArm.CheckPermissions(onSuccess);

// 2b. 异步验证（锁内 IO 在线程池，await 后回主线程）
bool ok = await GameModule.RockeyArm.CheckPermissionsAsync();

// 3. 启动看门狗（每 2 秒复查一次）
GameModule.RockeyArm.StartWatchdog().Forget();
```

## 授权失败处理

所有失败（启动验证失败、狗被拔出、锁内拒绝、到期、时间回退）统一通过 GameEvent 广播：

```csharp
// 订阅（常驻层，如 GameApp）
GameEvent.AddEventListener<string>(
    RuntimeId.ToRuntimeId(RockeyArmManager.LICENSE_ERROR_EVENT_ID), OnLicenseError);

private static void OnLicenseError(string msg)
{
    // msg 为用户可读短提示；完整诊断随时读 GameModule.RockeyArm.LastError
    GameModule.UI.ShowUI<ExitErrorUI>(new ExitPanelData
    {
        title = "授权验证失败",
        contenet = msg,
        buttonText = "退出"
    });
}

// Release 时注销
GameEvent.RemoveEventListener<string>(
    RuntimeId.ToRuntimeId(RockeyArmManager.LICENSE_ERROR_EVENT_ID), OnLicenseError);
```

三条处理通道：

| 通道 | 用途 |
| --- | --- |
| GameEvent `ROCKEY_ARM_LICENSE_ERROR` | 唯一的通知总线，同步/异步/看门狗失败都走这里 |
| `GameModule.RockeyArm.LastError` | 结构化完整诊断（`LicenseErrorInfo`：错误分类、阶段、厂商 API 码、锁内返回值、锁内诊断包），供日志上报与客服排障 |
| `CheckPermissionsAsync` 返回值 / `CheckPermissions` 的 `onSuccess` | 调用方主动分支 |

`RockeyErrorKind` 分类：`DongleNotFound`（没插狗）、`WrongDevice`（狗绑定了别的电脑）、`TimeBack`（时间回退锁定）、`Expired`（到期）、`NotAuthorized`（非本产品狗）、`LockInternalError`（锁内内部错误）、`DongleApiFailed`（厂商 API 调用失败）、`UnsupportedPlatform`（平台不支持）等。

看门狗行为：复查通过则静默续期通行证；狗被拔出（API 调用失败）同一拍重扫一圈，找到即换句柄继续；锁内明确拒绝（返回值 ≥ 2）或重扫失败则 `IsLicensed = false`、广播错误事件并停止循环。

## Editor 跳过与平台守卫

- Editor 下默认跳过真实校验（开发机通常无狗）：`RockeyArmManager.SkipValidationInEditor = true`。需要联调真狗时运行时置为 `false`。该字段仅在 `UNITY_EDITOR` 下编译，打包后恒为真实校验。
- 平台守卫：原生库仅支持 Windows / Linux x86_64。其余平台（如 Android、iOS 打包目标）下 `CheckPermissions` / `CheckPermissionsAsync` / `StartWatchdog` 在入口即返回结构化 `UnsupportedPlatform` 错误，不会进入线程池抛异常。

## 生命周期

模块继承 `TEngine.Module`，生命周期由 `ModuleSystem` 接管：

- `OnInit`：无操作；
- `Shutdown`：取消看门狗（CTS）、关闭狗句柄、复位 `IsLicensed` / `SessionToken`。幂等，可安全重复调用，也可挂 `Utility.Unity.AddDestroyListener`。

注意：`Shutdown` 只取消不 `Dispose` CTS，避免与在途的看门狗 `await` 产生竞态。

## 从旧版迁移（v2.1 → v2.2）

| 旧版 | 现状 |
| --- | --- |
| `Assets/TEngine/Runtime/RockeyARM/`（框架程序集内） | 迁至 `Assets/GameScripts/RockeyARM/`（主包独立程序集） |
| 静态事件 `OnLicenseError` / `OnLicenseErrorDetail` | 已移除，统一 GameEvent + `LastError`（实例属性） |
| GameEvent ID `"SYSYTEM_VIKEYERROR"`（ViKey 时代拼写错误） | 废弃，改用 `RockeyArmManager.LICENSE_ERROR_EVENT_ID`（`"ROCKEY_ARM_LICENSE_ERROR"`） |
| `OnApplicationQuit()` 手动清理方法（实际永不生效） | `Module.Shutdown()` 承担，框架自动调用 |
| `isLicensed` 公开字段 | `IRockeyArmModule.IsLicensed` 属性 |
| 同步阻塞式验证 | 保留同步版，另增 `CheckPermissionsAsync` |
| `Debug.Log` 直写 | `TEngine.Log`（TouchSocket 桥接 + 落盘） |

## 常见排查

| 现象 | 原因与处理 |
| --- | --- |
| `未找到加密锁` | 狗未插入或驱动未识别；Linux 下还需以 root 运行 SDK 的 `./inst` 安装 udev 规则，否则 `Dongle_Enum` 返回 `NOT_FOUND / COMM_ERROR` |
| `本加密锁已绑定其他电脑` | 锁内绑定标志指向其他设备指纹，属硬件级防盗，需供应商处理 |
| `检测到系统时间异常` | 锁内记录的最晚时间大于当前时间（时间回退），按设计锁定 |
| `调用锁内授权程序失败` | 狗被拔出、句柄失效、锁内程序未烧录或运行权限配置错误；看 `LastError` 中厂商 API 错误码 |
| Editor 下没插狗也想运行 | 默认 `SkipValidationInEditor = true` 已跳过；打包前确认发布机配置 |
