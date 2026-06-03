# 本 Fork 定制改动说明

> 本仓库 fork 自上游 [ALEXTANGXIAO/TEngine](https://github.com/ALEXTANGXIAO/TEngine)，在其基础上围绕**热更新、资源打包、运行时配置**做了一系列定制改造。本文档汇总相对上游新增/修改的能力，详细的设计与排查过程见 `UnityProject/conversation-summaries/` 下对应日期的会话总结。

## 📚 目录

- [日志系统](#-日志系统)
  - [TouchSocket 日志桥接与落盘](#1-touchsocket-日志桥接与落盘)
  - [Editor 打开日志目录菜单](#2-editor-打开日志目录菜单)
  - [日志查看工具 LogViewer](#3-日志查看工具-logviewer)
- [运行时配置](#-运行时配置)
  - [轻量 JSON 配置模块](#1-轻量-json-配置模块-jsonconfigmodule)
  - [部署配置覆盖热更地址](#2-部署配置覆盖热更地址-deployconfig)
- [热更新](#-热更新)
  - [多包架构](#1-多包架构-codepackage)
  - [代码包 XXTEA 加密](#2-代码包-xxtea-加密)
  - [版本确认与下载流程](#3-版本确认与下载流程)
  - [AOT 元数据热更清单](#4-aot-元数据热更清单)
  - [PlayerPrefs 版本记录清理工具](#5-playerprefs-版本记录清理工具)
- [资源打包](#-资源打包)
  - [按包构建管线](#1-按包构建管线)
  - [发布整理流程](#2-发布整理流程)
- [窗口管理](#-窗口管理)
  - [窗口布局控制模块 ScreenModule](#screenmodule)

---

## 🧾 日志系统

### 1. TouchSocket 日志桥接与落盘

在 `TEngine.Runtime` 内基于 `TouchSocket.Core` 新增日志双向桥接：TouchSocket 日志可进入 Unity Console，Unity/Task/UniTask 日志与未观察异常统一落盘到本地文件。

**特性**

- `TouchSocketContainerUnityDebugLogger` 继承 `LoggerBase`，将 TouchSocket 日志按级别映射到 `Debug.Log` / `LogWarning` / `LogError`，统一带 `[TouchSocket]` 前缀。
- `AddUnityDebugLogger()` 扩展支持给 `IRegistrator` / `LoggerGroup` 注册 Unity Console 日志器，可指定日志级别。
- `UnityLoggerBridge` 通过 `[RuntimeInitializeOnLoadMethod(BeforeSplashScreen)]` 自动初始化，监听 `Application.logMessageReceivedThreaded`、`TaskScheduler.UnobservedTaskException`、`UniTaskScheduler.UnobservedTaskException`，经 TouchSocket `FileLogger` 落盘。
- 默认落盘到 `Application.persistentDataPath/Logs/yyyy-MM-dd/`，单文件 1 MB 滚动，保留最近 3 天日志目录。
- 加入线程级重入保护避免日志系统递归，并用 `SubsystemRegistration` 重置静态状态，兼容关闭 Domain Reload 的 Editor Play Mode。
- `DefaultLogHelper` 的 `LogRedirection` 过滤列表加入桥接脚本，避免 Console 双击日志时跳到桥接层。

**使用方式**

```csharp
// TouchSocket 配置处注册 Unity Console 日志器（可选指定级别）
registrator.AddUnityDebugLogger();
registrator.AddUnityDebugLogger(LogLevel.Trace);

// Unity/TEngine 普通日志无需额外注册，UnityLoggerBridge 自动落盘
```

**关键文件**

- `Assets/TEngine/Runtime/Core/Log/TouchSocketContainerUnityDebugLogger.cs`
- `Assets/TEngine/Runtime/Core/Log/TouchSocketUnityLoggerExtensions.cs`
- `Assets/TEngine/Runtime/Core/Log/UnityLoggerBridge.cs`
- `Assets/TEngine/Runtime/Core/Utility/DefaultHelper/DefaultLogHelper.cs`

> 依赖 `Assets/Packages/TouchSocket.Core.4.2.12/`（经 NuGetForUnity 入库）。
>
> 详见 `conversation-summaries/2026-06-02-touchsocket-logger-summary.md`

---

### 2. Editor 打开日志目录菜单

便于在编辑器下快速定位 `UnityLoggerBridge` 的落盘产物。

- 在 `OpenFolderHelper` 新增菜单入口：`TEngine/Open Folder/Log Files Path`，与现有 Data Path / Persistent Data Path 等菜单同组。
- 打开 `Application.persistentDataPath/Logs`；目录尚未生成时退回打开 `Persistent Data Path`，避免路径不存在报错。

**关键文件**

- `Assets/TEngine/Editor/Utility/OpenFolderHelper.cs`

---

### 3. 日志查看工具 LogViewer

仓库根 `Tools/LogViewer/` 下新增独立桌面工具，把 `UnityLoggerBridge` 落盘的 `.log` 文件以图形界面查看，免去直接翻阅满是富文本标签与堆栈的原始日志。

基于 **Go + [Wails v2](https://wails.io)** 构建，编译为单体 `exe`，无运行时依赖（Windows 10/11 自带 WebView2）。

**特性**

- 打开 / 拖入 `.log` 文件查看，深色主题界面。
- 按级别筛选（DEBUG / INFO / WARNING / ERROR），关键词实时检索并高亮。
- 自动剥离 Unity 富文本标签（`<color>` / `<b>` 等）与 `[INFO] ►` 冗余前缀。
- 堆栈默认折叠、点击展开；兼容编辑器（带 `at .../File.cs:行号`）与打包后（无路径）两种堆栈格式。
- 一键打开默认日志目录 `%LOCALAPPDATA%\DefaultCompany\hotUnity\Logs`。

**构建**

```bash
go install github.com/wailsapp/wails/v2/cmd/wails@latest
cd Tools/LogViewer
build.bat        # 或 ./build.sh / wails build -clean
```

产物为 `Tools/LogViewer/build/bin/LogViewer.exe`。

> `frontend/wailsjs/` 为 Wails 构建时自动生成的绑定，已被 `.gitignore` 忽略，clone 后首次 `wails build` 会重新生成。

**关键文件**

- `Tools/LogViewer/main.go`（Wails 入口与后端 API）
- `Tools/LogViewer/parser/parser.go`（日志解析：富文本剥离、堆栈分组、过滤）
- `Tools/LogViewer/frontend/`（index.html / style.css / app.js）

> 详见 `conversation-summaries/2026-06-03-logviewer-tool-summary.md`

---

## 🔧 运行时配置

### 1. 轻量 JSON 配置模块 (JsonConfigModule)

在 `TEngine.Runtime` 内新增的轻量 JSON 配置模块，从 `StreamingAssets/Configs` 读取配置，作为 Luban `ConfigSystem` 之外的补充（不替换、不移除原有配置表系统）。

**特性**

- 按 `config_manifest.json` 清单声明需要加载的 JSON 文件，统一加载并缓存。
- 支持强类型 `Get<T>` / `TryGet<T>`、原始文本 `GetJson` / `TryGetJson`、`Contains`、`Clear`、`ReloadAsync`。
- 内置对象缓存，缓存键为 `"配置名:类型全名"`，同名配置可按不同类型分别缓存。
- 远程/Android 路径（含 `://`）走 `UnityWebRequest`，本地路径切线程池用 `File` 同步读，读完切回主线程。
- JSON 序列化默认切换为 **Newtonsoft**（保留 `DefaultJsonHelper` 可回退）。

**访问方式**

```csharp
// 通过 GameModule 统一访问，DTO 由业务层定义
await GameModule.JsonConfig.LoadAllAsync();
var cfg = GameModule.JsonConfig.Get<DeployConfig>();
```

**目录结构**

```text
Assets/StreamingAssets/Configs/
├── config_manifest.json     # 声明需要加载的 JSON 文件列表
└── DeployConfig.json        # 业务配置示例
```

```json
// config_manifest.json
{
  "files": [
    "DeployConfig.json"
  ]
}
```

**关键文件**

- `Assets/TEngine/Runtime/Module/JsonConfigModule/IJsonConfigModule.cs`
- `Assets/TEngine/Runtime/Module/JsonConfigModule/JsonConfigModule.cs`
- `Assets/TEngine/Runtime/Module/JsonConfigModule/JsonConfigManifest.cs`
- `Assets/TEngine/Runtime/Extension/Json/NewtonsoftJsonHelper.cs`

> 详见 `conversation-summaries/2026-06-02-json-config-deploy-summary.md`

---

### 2. 部署配置覆盖热更地址 (DeployConfig)

打包后无需重新出包，即可在现场通过明文 JSON 覆盖 `UpdateSetting` 中的资源服务器地址，便于多端/多现场部署。

**工作方式**

- `StreamingAssets/Configs/DeployConfig.json` 提供 `ResDownloadPath` / `FallbackResDownloadPath`。
- `UpdateSetting.GetResDownLoadPath()` / `GetFallbackResDownLoadPath()` 优先读取部署配置；为空、模块未加载或解析异常时回退 Inspector 默认值。
- `ProcedureLaunch` 在资源初始化**之前**加载部署配置，确保获取远程地址时已读到现场地址。

```json
// DeployConfig.json
{
  "ResDownloadPath": "http://127.0.0.1:8081",
  "FallbackResDownloadPath": "http://127.0.0.1:8082"
}
```

**关键文件**

- `Assets/TEngine/Runtime/Core/DeployConfig.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/GameScripts/Procedure/ProcedureLaunch.cs`

> 详见 `conversation-summaries/2026-06-02-json-config-deploy-summary.md`、`2026-05-30-runtime-config-management-summary.md`

---

## 🔄 热更新

### 1. 多包架构 (CodePackage)

将热更新流程从“默认资源包内混装 DLL”演进为可扩展的多包架构。

- 热更程序集从 `DefaultPackage` 拆出为独立 `CodePackage`，DLL/AOT 元数据独立发布与更新。
- `UpdateSetting` 引入运行时资源包列表 `RuntimePackages`，可按包配置：是否启用、启动时是否初始化、是否更新清单、是否参与下载检查、是否保存版本记录、`VersionKey` 等。
- 运行时初始化、清单更新、下载器创建流程均改为按包执行，并支持远端不可用时回退到本地已缓存版本。
- 远端目录由统一平台目录改为每包独立子目录：`{host}/{project}/{platform}/{packageName}/...`。
- 程序集包判定收敛为依赖 `AssemblyPackageName` 与包名推断，移除了 `IsAssemblyPackage` 字段。

**关键文件**

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`、`ResourceModule.Services.cs`
- `Assets/GameScripts/Procedure/ProcedureInitPackage.cs`、`ProcedureInitResources.cs`、`ProcedureLoadAssembly.cs`

> 详见 `conversation-summaries/2026-05-28-hotfix-multipackage-summary.md`

---

### 2. 代码包 XXTEA 加密

仅对代码包加密，不全局加密所有资源包。

- 新增 `EncryptionType.XXTEA`，以及打包加密 `XXTEAEncryption`、运行时解密 `XXTEADecryption`、Web 解密 `XXTEAWebDecryption`。
- 构建期与运行时按 `RuntimePackageEntry.EncryptionType` 逐包判断，构建窗口移除全局加密选项、改为每包选择加密方式。
- 默认配置：`DefaultPackage` 不加密，`CodePackage` 使用 XXTEA。

> ⚠️ XXTEA 解密为整包读入内存后 `AssetBundle.LoadFromMemory`，适合代码包/DLL，大资源包会增加峰值内存。

**关键文件**

- `Assets/TEngine/Runtime/Module/ResourceModule/EncryptionType.cs`、`ResourceModule.Services.cs`

> 详见 `conversation-summaries/2026-05-30-xxtea-hotfix-update-summary.md`

---

### 3. 版本确认与下载流程

恢复“有本地版本可取消、无本地版本强制更新”的可选更新提示流程。

- `ProcedureCreateDownloader` 检测到待下载内容后：
  - `UpdateStyle.Optional` 且所有待下载包都有本地版本记录、且本地与远端版本不同时，弹出确认/取消提示（不操作则按 `AutoStartDownloadDelaySeconds` 倒计时自动确认）。
  - 无本地版本记录时只显示确认按钮，强制更新。
  - `UpdateNotice.NoNotice` 时跳过更新直接进入本地资源流程。
- 用户取消更新时回退待下载包到本地版本清单，并设置跳过标记；`ProcedureDownloadOver` 据此不写入远端版本记录，避免误把远端版本记成已更新。

**关键文件**

- `Assets/GameScripts/Procedure/ProcedureCreateDownloader.cs`、`ProcedureDownloadOver.cs`、`ProcedureBase.cs`

> 详见 `conversation-summaries/2026-05-30-xxtea-hotfix-update-summary.md`、`2026-05-30-hotfix-update-confirm-flow-summary.md`

---

### 4. AOT 元数据热更清单

将 AOT 元数据列表从基础包序列化引用中解耦，支持后续热更补充。

- 新增 `AOTMetadataManifest` ScriptableObject 与 `Assets/AssetRaw/DLL/AOTMetadataManifest.asset`，随 `CodePackage` 被 YooAsset 收集并热更。
- 构建期 `BuildDLLCommand` 优先读取 manifest（为空回退 `UpdateSetting.AOTMetaAssemblies`），并自动合并 `AOTGenericReferences.PatchedAOTAssemblyList` 的缺失项。
- 运行时 `ProcedureLoadAssembly` 优先从 `CodePackage` 加载 manifest，再调用 `HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly`。

> ⚠️ 旧基础包无法享受该机制，需先发一次包含新逻辑的基础包，后续才能通过 `CodePackage` 热更 manifest 与新增 AOT DLL。

> 详见 `conversation-summaries/aot-metadata-manifest-hotfix-summary.md`、`AOTMetaAssemblies-summary.md`

---

### 5. PlayerPrefs 版本记录清理工具

便于反复测试热更时清理“上次成功更新版本号”。

- 新增 Editor 窗口，菜单入口：`TEngine/HotUpdate/Package Version PlayerPrefs`。
- 自动读取 `UpdateSetting.RuntimePackages` 中各包的 `VersionKey`，展示包名、启用状态、`SaveVersion`、`VersionKey`、当前 PlayerPrefs 值。
- 支持刷新、选中有记录、全选、清理选中、清理全部、单行清理。
- 仅对展示的 `VersionKey` 执行 `DeleteKey` 后 `Save`，不调用 `DeleteAll`，不操作注册表，不影响其它 PlayerPrefs。

**关键文件**

- `Assets/TEngine/Editor/Utility/HotUpdatePlayerPrefsTool.cs`

> 详见 `conversation-summaries/2026-06-01-hotupdate-playerprefs-tool-summary.md`

---

## 📦 资源打包

### 1. 按包构建管线

资源包不再统一使用单一构建管线。

- 支持按包指定 YooAsset 构建管线，保留 SBP 与 RawFile，移除 BBP（BuiltinBuildPipeline）。
- 打包工具页面直接读写运行时配置 `UpdateSetting.RuntimePackages`，编辑器配置与运行时初始化配置共用同一数据源，避免双份维护。

**关键文件**

- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`、`ReleaseTools.cs`、`BuildPipelineWindow.cs`

> 详见 `conversation-summaries/2026-05-30-resource-package-pipeline-and-default-package-summary.md`

---

### 2. 发布整理流程

构建后自动整理产物到发布目录，减少手工拷贝。

- `BuildConfig` 新增 `EnablePublishCopy` / `PublishRoot` / `CleanPublishPackageDirectory`，打包窗口新增“发布整理”面板。
- 新增 `GetRemotePlatformName(BuildTarget)`，发布目标目录统一使用运行时远端平台名（如 `Windows64` / `MacOS` / `IOS`），解决构建目录名（`StandaloneWindows64` 等）与运行时远端平台名不一致导致的 404，并补齐运行时 `Linux` 分支。
- 支持“仅执行发布整理”，对历史已构建版本重新整理上传，只允许整理所有启用包都存在的“公共版本”。

**关键文件**

- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`、`BuildPipelineWindow.cs`、`ReleaseTools.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`

> 详见 `conversation-summaries/2026-05-30-resource-package-publish-workflow-summary.md`

---

## 🖥️ 窗口管理

<a id="screenmodule"></a>

### 窗口布局控制模块 ScreenModule

在 `TEngine.Runtime`（AOT 层）新增的窗口布局控制模块，参考自 [RSJWYFamework 的 Screen 模块](https://github.com/RSJWY/RSJWYFamework/tree/main/Assets/RSJWYFamework/Runtime/Screen)，在本项目中按 TEngine `Module` 规范重写。

#### 功能

Windows Standalone 下控制 Unity 多屏（Display）窗口的：

- 位置与大小
- 强制置顶（`HWND_TOPMOST`）
- 无边框模式（去除 `WS_CAPTION | WS_THICKFRAME`）

> Unity 应用本身无法创建多个独立 OS 窗口；这里的“多窗口”指 **多显示器（multi-display）**：激活副屏后，每块屏幕对应一个 Unity 窗口（窗口类名 `UnityWndClass`，同进程同线程），本模块分别控制它们。

#### 设计要点

- **放在 AOT 层（`TEngine.Runtime`），而非热更层**：`DllImport` 原生互操作在 HybridCLR 解释域中调用不稳定，因此整个模块（含 Win32 封装）放到 AOT 程序集，由 IL2CPP 直接编译。热更层仅通过 `GameModule.Screen`（`TEngine.IScreenModule`）调用。
- **被动启动，由热更入口触发**：在 `GameApp.StartGameLogic()` 首次访问 `GameModule.Screen` 时，`ModuleSystem.GetModule` 自动创建模块并执行 `OnInit`（模块在 AOT 层，`Type.GetType` 可正常解析，无需 `RegisterModule`）。
- **基于 TEngine `Module` 生命周期**：`OnInit` 读配置并按需应用，`Shutdown` 清缓存。
- **Win32 全正向 P/Invoke**：句柄发现用 `FindWindowEx` 循环枚举顶层 `UnityWndClass` 窗口 + `GetWindowThreadProcessId` 按进程过滤，**不使用任何 native→managed 回调委托**；样式读写用 `GetWindowLongPtr` / `SetWindowLongPtr`（兼容 64 位）。
- **应用前自动切窗口化**：全屏模式（默认 `FullScreenWindow`）下 `SetWindowPos` 会被 Unity/OS 覆盖而不生效，模块在应用布局前先 `Screen.SetResolution(..., FullScreenMode.Windowed)` 并等待数帧。这是打包后“看不到效果”的常见根因。
- **平台隔离**：底层 `WindowsScreenNative` 整文件 `#if UNITY_STANDALONE_WIN || UNITY_EDITOR` 包裹（其他平台提供安全空实现）；`ScreenModule` 通过 `IsSupported` 分支；非 Windows 平台不编译任何 user32 调用，调用 API 仅输出警告。
- **全程诊断日志**：配置读取、显示器激活、窗口发现与映射、布局应用逐条 `Log.Info`，失败带 `Win32Error`，便于打包后定位。

#### 位置

- 模块：`Assets/TEngine/Runtime/Module/ScreenModule/`
  - `IScreenModule.cs` — 模块接口（`namespace TEngine`）
  - `ScreenModule.cs` — 模块实现（生命周期 + 多屏编排 + 窗口化切换）
  - `ScreenConfig.cs` — 配置模型
  - `WindowsScreenNative.cs` — user32 / kernel32 P/Invoke 封装
- 配置：`Assets/StreamingAssets/Configs/ScreenConfig.json`（已登记进 `config_manifest.json`）

#### 使用方式

```csharp
// 已在 GameApp.StartGameLogic() 首次访问时自动创建并按配置应用，无需手动初始化。
// 运行时动态调整：
GameModule.Screen.ApplyAll();              // 重新应用全部配置
GameModule.Screen.ApplyScreen(0);          // 重新应用主屏配置
GameModule.Screen.SetTopmost(1, true);     // 副屏（DisplayIndex=1）强制置顶
bool ok = GameModule.Screen.IsSupported;   // 当前平台是否支持
```

#### 配置说明

```json
{
  "ApplyOnInit": true,
  "Screens": [
    {
      "DisplayIndex": 0,
      "Activate": true,
      "X": 0,
      "Y": 0,
      "Width": 1920,
      "Height": 1080,
      "Topmost": false,
      "Borderless": false
    }
  ]
}
```

| 字段 | 含义 |
|------|------|
| `ApplyOnInit` | 模块初始化时是否自动应用配置 |
| `DisplayIndex` | Unity Display 索引（0=主屏，1/2/… 为副屏） |
| `Activate` | 是否激活该 Display（副屏必须激活才会创建窗口） |
| `X` / `Y` | 窗口位置（屏幕坐标系） |
| `Width` / `Height` | 窗口宽 / 高（像素） |
| `Topmost` | 是否强制置顶 |
| `Borderless` | 是否去除边框与标题栏 |

#### 容错机制

- **未配置或配置为空**：输出警告并使用主显示器默认分辨率（`Screen.currentResolution`，居中、保留边框、不置顶）。无论何种情况都至少有一个主显示器可用。
- **`DisplayIndex` 越界**：跳过该项并告警，继续处理其余有效配置。
- **非 Windows 平台**：仅输出警告，不执行任何窗口操作。

#### 已知限制

- 仅 Windows Standalone 真实生效；Editor 下 `GetActiveWindow` 拿到的是编辑器/Game 窗口，多屏行为无法在 Editor 完整验证，需打 Windows 包多显示器实测。
- **必须为窗口化才生效**：全屏（`FullScreenWindow` / `ExclusiveFullScreen`）下 `SetWindowPos` 被覆盖，模块已在应用前自动切 `Windowed`；若 Player Settings 强制全屏或外部又切回全屏，位置/大小会失效。
- `DisplayIndex → 窗口句柄` 映射：主屏取当前激活窗口，副屏按窗口发现顺序与已激活的副屏配置（按索引升序）依次配对。多副屏场景该顺序可能需打包后实测校正（必要时可改用 `MonitorFromWindow` 按显示器矩形精确匹配）。看日志 `映射 Display=x -> hWnd=y` 核对。
- Display 激活必须在运行早期、渲染前进行，且激活后不可关闭（Unity 限制）。

#### 排查（打包后无效果时）

按日志定位：

- `当前可用显示器数量 Display.displays.Length=N` —— N 是否符合预期。
- `窗口发现：FindUnityWindows 命中 X 个` —— X=0 表示没找到 Unity 窗口（类名/进程过滤异常）。
- `切换为 Windowed` —— 是否成功从全屏切窗口化。
- `已应用：Display=... Rect=...` 或 `SetWindowPos 失败 ... Win32Error=...` —— 是否真正下发成功。

---

> 📝 维护提示：每完成一项新的定制改动，请在本文档对应主题下追加条目，并在根 `README.md` 的「🛠️ 本 Fork 的定制改动」章节同步精简概述。
