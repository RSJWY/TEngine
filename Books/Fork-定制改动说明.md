# 本 Fork 定制改动说明

> 本仓库 fork 自上游 [ALEXTANGXIAO/TEngine](https://github.com/ALEXTANGXIAO/TEngine)，在其基础上围绕**热更新、资源打包、运行时配置、场景加载**做了一系列定制改造。本文档汇总相对上游新增/修改的能力，详细的设计与排查过程见 `UnityProject/conversation-summaries/` 下对应日期的会话总结。

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
  - [打包工具 Odin 化与卡顿优化](#3-打包工具-odin-化与卡顿优化)
- [场景系统](#-场景系统)
  - [DynamicSpawn 通用 Spawner 与场景 Manager 示例](#dynamicspawn)
  - [场景加载进度拆分到 GameSceneModule](#sceneprogress)
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

### 3. 部署配置控制调试器开关 (DeployConfig.DebuggerActiveWindow)

将 `Debugger`（GameEntry 场景内的运行时调试器）的激活策略接入部署配置，打包后改 JSON 即可控制是否弹出调试器，无需改 Inspector/Prefab 重新出包。

**工作方式**

- `DeployConfig` 新增 `DebuggerActiveWindow` 字段（string），取值为 `DebuggerActiveWindowType` 枚举名：`AlwaysOpen` / `OnlyOpenWhenDevelopment` / `OnlyOpenInEditor` / `AlwaysClose`，大小写不敏感。
- `Debugger` 将原 `Start()` 内的激活策略 switch 抽为公共方法 `ApplyActiveWindowType(DebuggerActiveWindowType)`：Start 仍按 Inspector 的 `activeWindow` 字段初始化，外部可二次覆盖。
- `ProcedureLaunch.LoadDeployConfigAsync` 在配置加载完成后调用 `ApplyDebuggerConfig()`，用 `Enum.TryParse` 解析字段并应用到 `Debugger.Instance`。
- 字段留空、值无法解析、或场景内无 `Debugger` 时回退 Inspector 默认行为，不会误开关。

**时序说明**

`Debugger` 是场景内 MonoBehaviour，`Start()` 早于 `JsonConfigModule` 加载完成，因此不能在 Start 内直接读配置。采用「Start 先按 Inspector 初始化 → 部署配置加载完成后由 `ProcedureLaunch` 二次覆盖」，与 `UpdateSetting` 消费 `ResDownloadPath` 的时机一致。

```json
// DeployConfig.json
{
  "ResDownloadPath": "http://127.0.0.1:80/ProjectHotupdate",
  "FallbackResDownloadPath": "http://127.0.0.1:80/ProjectHotupdate",
  "DebuggerActiveWindow": "OnlyOpenWhenDevelopment"
}
```

**关键文件**

- `Assets/TEngine/Runtime/Core/DeployConfig.cs`
- `Assets/TEngine/Runtime/Module/DebugerModule/Debugger.cs`
- `Assets/GameScripts/Procedure/ProcedureLaunch.cs`

> 详见 `conversation-summaries/2026-06-04-deployconfig-debugger-toggle-summary.md`

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

### 3. 打包工具 Odin 化与卡顿优化

将 `BuildPipelineWindow` 从传统 IMGUI 迁移为 Odin 声明式窗口，同时保留原有构建逻辑和本地偏好键，降低维护成本并改善编辑体验。

- 窗口继承 `OdinEditorWindow`，使用 `BoxGroup` / `TitleGroup` 组织基础设置、资源包列表、发布整理、最小包、高级设置、热更 DLL、Player 设置、构建流程预览、操作按钮与构建日志。
- 使用 `TableList` 展示 `UpdateSetting.RuntimePackages` 与构建流程步骤；通过窗口内的 `RuntimePackageView` 包装运行时配置，避免给运行时程序集引入 Odin 依赖。
- 使用 `ValueDropdown` 替代手写 Popup，统一平台、构建管线、压缩方式、包级加密、内置文件拷贝与文件名风格选项；继续隐藏/规避已废弃的 BBP 路径。
- 保留原有 `EditorPrefs` key、菜单路径与 `ReleaseTools` 构建入口，原有一键构建、仅构建 AB、仅构建 Player、仅发布整理、编译热更 DLL、同步 AOT 元数据清单等行为不变。
- 针对 Odin 表格编辑卡顿做性能收敛：资源包表格编辑先写内存并标脏，0.75 秒静默后统一 `AssetDatabase.SaveAssets()`；窗口关闭或点击保存时强制 flush。
- 状态栏、发布目录预览、构建流程预览改为配置变化时刷新缓存，避免每次 `OnImGUI` 绘制都重新计算包摘要；构建日志 `Repaint()` 增加 0.1 秒节流。
- 版本号、路径、保留 Tag、包名、版本键等文本字段使用 `DelayedProperty`，减少输入过程中反复触发同步。

**关键文件**

- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs.meta`

> 详见 `conversation-summaries/2026-06-27-odin-build-pipeline-window-summary.md`

---

## 🎬 场景系统

<a id="dynamicspawn"></a>

### DynamicSpawn 通用 Spawner 与场景 Manager 示例

将原本带机库命名、但实际没有机库专属逻辑的动态场景加载脚本整理为通用实现，降低新场景接入成本。

#### 背景

原先 `HangarSceneSpawner` 只是继承 `DynamicSceneSpawner` 并返回 `CollectFromSpawnPoints()`，实际职责是“从子节点的 `DynamicSpawnPoint` 收集加载项”，并不属于机库专属逻辑。继续让每个场景复制一个空派生类，会增加无意义脚本数量，也容易让使用者误以为必须为每个场景写加载器。

#### 改动

- `HangarSceneSpawner` 重命名并改造为 `SpawnPointSceneSpawner`，作为大多数场景可直接挂载的通用加载脚本。
- `SpawnPointSceneSpawner` 仍继承 `DynamicSceneSpawner`，只负责调用 `CollectFromSpawnPoints()`，保留原有批量异步加载、完成事件、注册表和 Editor 预览能力。
- `HangarManager` 改为 `ExampleSceneGameManager`，仅作为场景业务管理器示例，不再承载机库业务逻辑。
- `ExampleSceneGameManager` 继承 `SceneGameManagerBase<DynamicSceneSpawner>`，演示如何指定 `TargetSceneType`，以及如何在 `OnSceneSpawnCompleted()` 里通过 `GetSpawnedObject("PlayerSpawnRoot")` 获取动态加载出的对象。
- `DynamicSpawn` 使用教程同步更新：默认挂 `SpawnPointSceneSpawner`，只有需要额外收集规则或完成钩子时才写 `XxxSceneSpawner`。

#### 使用方式

大多数场景只需要：

1. 在场景中新建 `DynamicSpawnRoot`
2. 给 `DynamicSpawnRoot` 挂 `SpawnPointSceneSpawner`
3. 在其子节点挂 `DynamicSpawnPoint` 并填写 `location`
4. 如需业务初始化，复制 `ExampleSceneGameManager` 为自己的 `XxxManager`
5. 在 `DynamicSpawnPoint.registerKey` 填写 key 后，通过 `GetSpawnedObject("你的key")` 获取加载出的对象

只有在以下情况才建议写专属 Spawner：

- 加载项不完全来自 `DynamicSpawnPoint`
- 需要混合代码生成的 `SpawnItem`
- 需要 override `OnAllSpawned()` 做加载器层面的完成钩子

#### 关键文件

- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/DynamicSceneSpawner.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/DynamicSpawnPoint.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/Load/SpawnPointSceneSpawner.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/SceneGameManager/SceneGameManagerBase.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/SceneGameManager/ExampleSceneGameManager.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/README.md`

#### 验证

已执行：

```powershell
dotnet build GameLogic.csproj --no-restore
```

结果：0 错误，0 警告。

> 详见 `UnityProject/conversation-summaries/2026-06-27-dynamic-spawn-generalization-summary.md`

---

<a id="sceneprogress"></a>

### 场景加载进度拆分到 GameSceneModule

把场景切换加载页从“胖 UI”重构为“模块独占控制 + UI 纯展示”，进度状态机、资源加载、激活、回调与关闭全部归 `GameSceneModule`，`SwitchUI` 只负责渲染。

#### 背景

原 `LoadingUI` 是个“胖窗口”：三段式进度状态机（预热 0→10% / 加载 10→90% / 收尾 90→100%+停留）、`LoadSceneAsync(suspendLoad=true)` 资源加载、`UnSuspend` 激活、完成回调、Tips 文案、关闭时机全部塞在 `UIWindow` 内。问题：

- 进度与加载控制等基础设施逻辑混入表现层，UI 既掌数据又掌流程，职责越界。
- 该文件还引用了仓库中不存在的 `GameTipsData` 类型与已迁移的旧全局事件（`Event_LoadOver` / `Event_SceneLoadStart`），实际已无法编译。
- 激活采用“UI 发 `Event_LoadOver` → 模块自收再 `UnSuspend`”的自发自收事件回路，绕了一圈。

#### 改动

- **`GameSceneModule` 实现 `IUpdateModule`**：借 `ModuleSystem.Update`（`RootModule` 每帧调 `ModuleSystem.Update(GameTime.deltaTime, GameTime.unscaledDeltaTime)`）驱动状态机，无需 `Timer` 或 UI 内 `OnUpdate`。空闲期 `_isActive=false` 早退，避免每帧空转。
- **三段式进度原样迁入 `Update(elapse, realElapse)`**：用 `realElapseSeconds`（unscaled）驱动，暂停时加载页动画不冻结；phase 2 钳制 `delta ≤ 0.05` 防激活帧跳过 100%。
- **新增 `float DisplayProgress`**：暴露平滑后的展示进度（只读），供 UI 每帧读取渲染。
- **激活改直连**：`EnterFinishPhase` 在 90% 直接 `GameModule.Scene.UnSuspend(_sceneName)` 激活场景，并派发 `IGameSceneEvent.OnSceneLoadOver` 作对外通知（不再 UI 自发事件→模块自收）。
- **`SwitchUI` 降为纯展示**：仅 `OnUpdate` 读 `GameModule.GameScene.DisplayProgress` 写 `m_img_progress.fillAmount` 与百分比文本；不持任何加载状态、不主动关闭自身（由模块 `CloseUI<SwitchUI>` 关闭）；层级 `UILayer.UI → Top`（全屏遮罩）。
- **删除 `LoadSceneDataBody`**：模块自持 `_sceneName` / `_finishCallBack`，不再需要数据载体透传给 UI。
- **移除 `_eventMgr` 与 `OnSceneLoadOver` 自监听**：激活权归属模块，不再需要局部事件管理器。
- **去除 `GameTipsData`**：该类型仓库中不存在，且 `SwitchUI` prefab 无 tips 节点，未迁移。

#### 执行流程（运行时）

整体是“模块驱动 + UI 展示”的单向数据流，进度从模块单向流向 UI：

```
GameSceneModule.LoadScene(sceneType, finishCallBack)  /  JumpToMainScene()
  └─ StartSceneLoad:
       RecordScene()                   记录上一关/当前关 + 同步 GameValueStatic
       GameEvent.OnSceneLoadStart()    通知观察方
       重置状态机: _isActive=true, phase=0, display=0, target=0.10
                   （SkipLoadingAnimation=true 时: phase=1, display=0.10, 立即 StartRealLoading）
       GameModule.UI.ShowUI<SwitchUI>()  打开加载页（纯展示，不传 UserData）

ModuleSystem.Update 每帧 → GameSceneModule.Update(elapse, realElapse)
  ├─ phase 0 预热 (0→10%): MoveTowards 0.10；到位 → phase=1, StartRealLoading()
  │     └─ LoadSceneAsync(suspendLoad=true, cb=OnLoadProgress)  fire-and-forget
  │           OnLoadProgress(value): 0~0.9 → target 0.10~0.90；到 0.9 → _sceneLoadComplete=true
  ├─ phase 1 加载 (10%→90%): MoveTowards target；超时 5s 兜底进收尾
  │     到 _sceneLoadComplete && display≥0.89 → EnterFinishPhase（skip 模式直接 FinishAndClose）
  └─ phase 2 收尾 (90%→100%+停留0.5s): delta 钳制≤0.05
        EnterFinishPhase: target=1.0; UnSuspend(sceneName) 激活; 派发 OnSceneLoadOver
        到 100% 停留满 → FinishAndClose:
          finishCallBack() → CloseUI<SwitchUI> → OnSceneReady(sceneType) → _isActive=false
                                                              │
                                                              ▼ DynamicSceneSpawner 收到后开始生成

SwitchUI.OnUpdate（每帧，仅渲染）:
  progress = GameModule.GameScene.DisplayProgress
  m_img_progress.fillAmount = progress
  m_tmp_progressText.text = "{Round(progress*100)}%"
```

**终结顺序刻意 `回调 → 关加载页 → OnSceneReady`**，对齐 `DynamicSceneSpawner`「SwitchUI 关闭后才收 OnSceneReady」的契约。

#### 沿用的关键陷阱（代码注释保留）

- **陷阱 1**：`suspendLoad=true` + `progressCallBack` 时，`LoadSceneAsync` 内部 `while(!IsDone)` 一直 yield，`await` 会死循环 → 只 fire-and-forget，进度全由 `progressCallBack` 驱动。
- **陷阱 2**：suspendLoad 时 `IsDone` 永远 false，`progressCallBack` 每帧回调 `value=0.9` 会反复覆盖 target → `OnLoadProgress` 在 `phase≥2` 直接 return，保护收尾 `target=1.0` 不被打回 0.90（否则永远卡 90%）。
- **90% 激活而非 100%**：激活有一帧卡顿，留最后 10% 动画 + 100% 停留遮盖；100% 才激活会暴露突兀弹出。

#### 关键文件

- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/GameScene/IGameSceneModule.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/UI/SwitchUI/SwitchUI.cs`
- `UnityProject/Assets/AssetRaw/UI/SwitchUI.prefab`

> 详见 `UnityProject/conversation-summaries/2026-06-30-switchui-scene-progress-refactor-summary.md`

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
