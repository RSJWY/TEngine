# RockeyARM 加密狗授权模块

本文描述当前 Fork 内置的 ROCKEY-ARM USB 加密锁授权模块（锁内程序版 v2.3，零框架依赖核心 + TEngine 适配层）。模块提供启动验证、看门狗周期复查、设备绑定、防时间回退、到期控制与完整错误诊断，全部核心校验逻辑运行在加密锁硬件内部；核心实现不依赖任何游戏框架，可同时以“裸使用”方式接入其它工程（`Assets/RockeyARM` 目录可整体导出为 Unity 包）。

## 模块位置与程序集

| 项 | 值 |
| --- | --- |
| 代码位置 | `Assets/RockeyARM/` |
| 核心程序集 | `RockeyARM.asmdef`（`autoReferenced`，仅引用 `UniTask` + `Unity.TextMeshPro`） |
| TEngine 适配程序集 | `RockeyARM/TEngine/RockeyARM.TEngine.asmdef`（引用 `RockeyARM`、`TEngine.Runtime`、`UniTask`） |
| 核心命名空间 | `RockeyArm`（`RockeyArmManager` / `IRockeyArm` / `LicenseErrorInfo`） |
| 适配层命名空间 | `TEngine`（`RockeyArmModule` / `IRockeyArmModule`） |
| TEngine 访问门面 | `GameModule.RockeyArm`（`IRockeyArmModule`） |

模块位于主包（非热更域），因此授权验证可以安排在热更 DLL 加载之前执行，破解者无法通过跳过热更程序集绕过验证；同时热更域（`GameLogic`）通过 asmdef 引用可直接使用接口。

## 文件结构

| 文件 | 职责 |
| --- | --- |
| `IRockeyArm.cs` | 核心接口（零框架依赖）、`RockeyErrorKind` 错误分类、`LicenseErrorInfo` 结构化诊断 |
| `RockeyArmManager.cs` | 核心主逻辑（验证、看门狗、错误翻译），日志/错误事件走注入 |
| `RockeyArmManager.Consts.cs` | 协议常量、偏移、错误码 |
| `RockeyArmManager.Platform.cs` | Windows / Linux P/Invoke 网关（`RockeyArmNative`）与平台守卫 |
| `UI/RockeyUIBase.cs` / `UI/RockeyUIMgr.cs` | 零框架依赖轻量 UI 外壳（仿 `Assets/Launcher`：纯 C# UI 类 + `Resources.Load`） |
| `UI/ExitErrorUI.cs` | 授权错误弹窗（驱动 `Resources/UIWindow/ExitErrorUI.prefab`，prefab 根自带 Canvas） |
| `TEngine/IRockeyArmModule.cs` | TEngine 门面接口（等价 `RockeyArm.IRockeyArm`） |
| `TEngine/RockeyArmModule.cs` | TEngine 适配层：`Module` 生命周期桥、日志桥（`TEngine.Log`）、错误事件转 GameEvent |
| `UsageSamples/BareSample/` | 裸使用调用示例（直调核心，不依赖 TEngine） |
| `UsageSamples/TEngineSample/` | TEngine 使用调用示例（注册适配层模块） |

原生插件（不在本模块内，需自行部署）：

- Windows：`Assets/Plugins/ROCKEY/Windows/x86_64/Dongle_d.dll`（StdCall）
- Linux：`Assets/Plugins/ROCKEY/Linux/x86_64/libRockeyARM.so`（Cdecl）

## 分层设计（核心零框架依赖）

```
业务（主包 Procedure / 热更 GameApp）
        │  GameModule.RockeyArm / ModuleSystem
        ▼
TEngine 适配层  RockeyArmModule : Module, IRockeyArmModule   （RockeyARM.TEngine 程序集）
        │  组合持有，桥接生命周期 / TEngine.Log / GameEvent
        ▼
零框架核心      RockeyArmManager : IRockeyArm                 （RockeyArm 程序集）
        │  InfoLogger / WarningLogger / ErrorLogger 注入；LicenseError C# 事件
        ▼
原生库          RockeyArmNative（Dongle_* P/Invoke）
```

- 核心生命周期为 `Init()` / `Release()`（幂等），不继承 `Module`；日志通过 `InfoLogger` / `WarningLogger` / `ErrorLogger` 三个委托注入；授权错误通过 `event Action<LicenseErrorInfo> LicenseError` 抛出。
- TEngine 工程一律注册适配层 `new RockeyArmModule()`（`OnInit` 自动接好桥，`Shutdown` 自动 `Release`）；不要直接注册核心 `RockeyArmManager`。
- 裸工程直接 `new RockeyArmManager()`，日志接 `Debug.Log*`，错误订阅 `LicenseError`，销毁时自行 `Release()`（完整代码见 `UsageSamples/BareSample/RockeyArmBareUsage.cs`）。

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

## TEngine 接入步骤

验证时机由实际项目自行决定（主包 Procedure 流程或热更入口均可），模块只提供能力。当前工程已在 `ProcedureLaunch` 完成注册与验证：

```csharp
// 1. 注册适配层模块（只做一次；如主包 ProcedureLaunch）
ModuleSystem.RegisterModule<IRockeyArmModule>(new RockeyArmModule());

// 2a. 同步验证（主包 FSM / Procedure 流程友好；onSuccess 仅在通过时回调）
GameModule.RockeyArm.CheckPermissions(onSuccess);

// 2b. 异步验证（锁内 IO 在线程池，await 后回主线程）
bool ok = await GameModule.RockeyArm.CheckPermissionsAsync();

// 3. 启动看门狗（每 2 秒复查一次）
GameModule.RockeyArm.StartWatchdog().Forget();
```

## 授权失败处理

所有失败（启动验证失败、狗被拔出、锁内拒绝、到期、时间回退）在核心层统一走 `LicenseError` 事件；TEngine 适配层把它转 GameEvent 广播（事件 ID `RockeyArmModule.LICENSE_ERROR_EVENT_ID`）：

```csharp
// 订阅（常驻层，如 GameApp）
GameEvent.AddEventListener<string>(
    RuntimeId.ToRuntimeId(RockeyArmModule.LICENSE_ERROR_EVENT_ID), OnLicenseError);

private static void OnLicenseError(string msg)
{
    // msg 为用户可读短提示；完整诊断随时读 GameModule.RockeyArm.LastError
    // ExitErrorUI 为零框架依赖弹窗（RockeyARM 包自带，prefab 位于其 Resources/UIWindow）
    RockeyUIMgr.ShowUI<ExitErrorUI>(new ExitPanelData
    {
        title = "授权验证失败",
        contenet = msg,
        buttonText = "退出"
    });
}

// Release 时注销
GameEvent.RemoveEventListener<string>(
    RuntimeId.ToRuntimeId(RockeyArmModule.LICENSE_ERROR_EVENT_ID), OnLicenseError);
```

三条处理通道：

| 通道 | 用途 |
| --- | --- |
| GameEvent `ROCKEY_ARM_LICENSE_ERROR` | TEngine 侧唯一通知总线，同步/异步/看门狗失败都走这里 |
| `GameModule.RockeyArm.LastError` | 结构化完整诊断（`LicenseErrorInfo`：错误分类、阶段、厂商 API 码、锁内返回值、锁内诊断包），供日志上报与客服排障 |
| `CheckPermissionsAsync` 返回值 / `CheckPermissions` 的 `onSuccess` | 调用方主动分支 |

`RockeyErrorKind` 分类：`DongleNotFound`（没插狗）、`WrongDevice`（狗绑定了别的电脑）、`TimeBack`（时间回退锁定）、`Expired`（到期）、`NotAuthorized`（非本产品狗）、`LockInternalError`（锁内内部错误）、`DongleApiFailed`（厂商 API 调用失败）、`UnsupportedPlatform`（平台不支持）等。

看门狗行为：复查通过则静默续期通行证；狗被拔出（API 调用失败）同一拍重扫一圈，找到即换句柄继续；锁内明确拒绝（返回值 ≥ 2）或重扫失败则 `IsLicensed = false`、广播错误事件并停止循环。

## Editor 跳过与平台守卫

- Editor 下跳过真实校验由 `UNITY_EDITOR` **编译宏**直接控制（方法体内二分，无运行时变量）：Editor 编译只有放行路径、打包编译只有真实校验路径，不可运行时篡改。需要编辑器联调真狗时，临时注释三处入口（`CheckPermissions` / `CheckPermissionsAsync` / `StartWatchdog`）内的 `#if UNITY_EDITOR` 短路段即可。
- 平台守卫：原生库仅支持 Windows / Linux x86_64。其余平台（如 Android、iOS 打包目标）下 `CheckPermissions` / `CheckPermissionsAsync` / `StartWatchdog` 在入口即返回结构化 `UnsupportedPlatform` 错误，不会进入线程池抛异常。

## 生命周期

TEngine 侧：适配层继承 `TEngine.Module`，生命周期由 `ModuleSystem` 接管：

- `OnInit`（注册时触发）：接线日志桥（核心三个 Logger 接到 `TEngine.Log`）、订阅核心 `LicenseError` 转 GameEvent、调用核心 `Init()`；
- `Shutdown`：注销桥接并调用核心 `Release()`——取消看门狗（CTS）、关闭狗句柄、复位 `IsLicensed` / `SessionToken`。幂等，可安全重复调用。

裸使用侧：无框架托管，宿主销毁时必须自行调用 `Release()`（示例见 `RockeyArmBareBootstrap.OnDestroy`）。

注意：`Release` 只取消不 `Dispose` CTS，避免与在途的看门狗 `await` 产生竞态。

## 从旧版迁移

| 旧版 | 现状 |
| --- | --- |
| `Assets/GameScripts/RockeyARM/` 内 `IRockeyArmModule.cs` 同时放接口与诊断类型 | 拆分为核心 `RockeyArm/IRockeyArm.cs`（命名空间 `RockeyArm`）+ 适配层 `TEngine/IRockeyArmModule.cs` |
| `ModuleSystem.RegisterModule<IRockeyArmModule>(new RockeyArmManager())` | 改为 `new RockeyArmModule()`（适配层）；核心 `RockeyArmManager` 不再继承 `Module` |
| `RockeyArmManager.LICENSE_ERROR_EVENT_ID` / `SEND_FULL_ERROR_TO_GAME_EVENT` | 移至 `RockeyArmModule`（TEngine 侧 GameEvent 策略由适配层决定） |
| `RockeyArmManager` 直用 `TEngine.Log` / `GameEvent` | 核心改为注入式日志 + `LicenseError` C# 事件；TEngine 桥接在 `RockeyArmModule` |
| `ExitErrorUI` 走 `GameModule.UI.ShowUI`（UIWindow + UIBindComponent） | 改为零框架 `RockeyUIMgr.ShowUI<ExitErrorUI>`（纯 C# 类 + 路径绑定，prefab 移入 `Resources/UIWindow/` 并移除 UIBindComponent） |
| `Module.OnInit / Shutdown` | 核心改为 `Init() / Release()`；适配层 `OnInit / Shutdown` 桥接之 |
| GameEvent ID `"SYSYTEM_VIKEYERROR"`（ViKey 时代拼写错误） | 废弃，改用 `RockeyArmModule.LICENSE_ERROR_EVENT_ID`（`"ROCKEY_ARM_LICENSE_ERROR"`） |
| `Debug.Log` 直写 | TEngine 侧统一 `TEngine.Log`（TouchSocket 桥接 + 落盘）；核心统一橙色 `[ROCKEY-ARM]` 彩色标签，通过/有效期正文绿色、已到期红色（富文本，Console 与 LogViewer 渲染，落盘保留标签原文） |

## 常见排查

| 现象 | 原因与处理 |
| --- | --- |
| `未找到加密锁` | 狗未插入或驱动未识别；Linux 下还需以 root 运行 SDK 的 `./inst` 安装 udev 规则，否则 `Dongle_Enum` 返回 `NOT_FOUND / COMM_ERROR` |
| `本加密锁已绑定其他电脑` | 锁内绑定标志指向其他设备指纹，属硬件级防盗，需供应商处理 |
| `检测到系统时间异常` | 锁内记录的最晚时间大于当前时间（时间回退），按设计锁定 |
| `调用锁内授权程序失败` | 狗被拔出、句柄失效、锁内程序未烧录或运行权限配置错误；看 `LastError` 中厂商 API 错误码 |
| Editor 下没插狗也想运行 | `UNITY_EDITOR` 宏短路已放行；打包前确认发布机配置 |
