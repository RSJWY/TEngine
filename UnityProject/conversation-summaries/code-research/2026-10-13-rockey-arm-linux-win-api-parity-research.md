# ROCKEY-ARM Linux SDK 与 Win API 一致性研究及双平台接入实施

> 日期：2026-10-13
> 背景：项目已有 Win 平台加密狗接入（RockeyArmManager 锁内程序版 v2.1，Dongle_d.dll），需要 Linux 平台（libRockeyARM.so）支持，并规范化 RockeyARM 脚本目录。

## 一、研究结论

### 1. Linux API 与 Win 完全一致

对照来源：
- Linux SDK 头文件：`E:\飞天诚信-Rockey\ROCKEY-ARM_SDK_LINUX\X64_Rockey-ARM_SDK_LINUX_V2.00_20260410\Rockey-ARM_SDK_LINUX\base\include\Dongle_API.h`
- Win 手册总结：`E:\飞天诚信-Rockey\授权程序\ROCKEY-ARM开发手册总结-完整API参考.md`

逐条核对结果：**49 个 `Dongle_*` 用户 API、全部错误码（0xF0000001~0xF0000017 / 0xF000FF00 / 0xFFFFFFFF）、全部结构体（DONGLE_INFO / RSA / ECCSM2 / 文件属性结构）在两个平台完全相同**。`Dongle_RunExeFile` 跑锁内程序（0x0002 登记员）的协议也一致——锁内可执行程序烧录在狗的芯片里，与 PC 端操作系统无关，**Win/Linux 可共用同一批狗、同一套锁内授权协议与返回值语义（0~6）**。

### 2. 平台差异仅三点

| 项 | Windows | Linux |
|---|---|---|
| 库文件 | `Dongle_d.dll` | `libRockeyARM.so`（SDK `base/api/api64/`） |
| 调用约定 | StdCall | **Cdecl** |
| 部署要求 | dll 放 Plugins 即可 | 必须先在目标机以 **root 运行 SDK `base/inst`** 安装 udev 规则，否则 `Dongle_Enum` 返回 NOT_FOUND / COMM_ERROR |

DllImport 提示：Linux 传 `"RockeyARM"`，Mono 运行时自动尝试 `libRockeyARM.so`（lib 前缀 + .so 后缀）。

### 3. 旧 RockeyARM_Linux.cs 的问题（已删除）

- 引用了工程中不存在的 `ARMLicenseData` 类型，**TEngine.Runtime 程序集当时编译不过**。
- 它是旧的"8K 数据区明文账本 + adminPin 硬编码在代码里"方案，与 RockeyArmManager 的"锁内程序版 v2.1（零 PIN，账本全在锁内）"互斥。安全性差（PIN 明文、账本匿名可读），已被锁内程序版取代。
- 用户决策：删除，统一走锁内程序版。

## 二、实施内容

### 1. 插件目录重组（`Assets/Plugins/ROCKEY/`）

```
Assets/Plugins/ROCKEY/
├── Windows/x86_64/Dongle_d.dll      # meta：仅 Editor(Win, OS=Windows) + Standalone Win64 启用
└── Linux/x86_64/libRockeyARM.so     # meta：仅 Standalone Linux64 (CPU=x86_64) 启用
```

- 原 `ROCKEY/` 根下两文件混放且 meta 为"Any 全平台启用"，已按平台收紧并移动（guid 保留不变）。
- 两文件 MD5 与 SDK 原版一致，未替换内容。

### 2. 脚本规范化（`Assets/TEngine/Runtime/RockeyARM/`）

| 文件 | 职责 |
|---|---|
| `RockeyArmManager.cs` | 授权主逻辑（跨平台，无 DllImport，调用 RockeyArmNative 网关） |
| `RockeyArmManager.Consts.cs` | 常量 / 协议偏移 / 错误码（双平台通用） |
| `RockeyArmManager.Platform.cs`（新增） | `RockeyArmNative` 静态网关：`UNITY_EDITOR_WIN \|\| UNITY_STANDALONE_WIN` → Dongle_d(StdCall)；`UNITY_EDITOR_LINUX \|\| UNITY_STANDALONE_LINUX` → RockeyARM(Cdecl)；其余平台抛 NotSupportedException |
| `RockeyARM_Linux.cs` | 已删除（含 .meta） |

### 3. 验证

- Unity 2022.3.51（非 Unity 6，CLI 工具不可用）；系统 Python 3.10 < workflow.py 要求的 3.11，workflow 无法运行。
- 改用 `MSBuild.exe`（VS2022）直接构建：`TEngine.Runtime.csproj` 与 `UnityProject.sln` 全量 **0 error**（仅 TMP 包自身 CS0618 过时警告）。
- csproj 中已删除的 `RockeyARM_Linux.cs` Compile 条目已手动移除（Unity 编辑器运行中，后续自动重新生成时会自我修正）。

## 三、Linux 交付注意事项

1. 目标机先以 root 执行 `ROCKEY-ARM_SDK_LINUX/base/inst`（卸载用 `./uninst`）。
2. 打包 Linux(x86_64) 时 libRockeyARM.so 按 meta 自动随包；无需复制 SDK 其它文件。
3. 狗内无需任何平台差异处理：Win/Linux 用同一批狗、同一个锁内 0x0002 登记员程序。
4. 业务调用方式与 Win 完全相同：`CheckPermissions` / `StartWatchdog` / `OnApplicationQuit`。
