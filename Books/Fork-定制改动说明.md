# 本 Fork 定制改动说明

> 本仓库 fork 自上游 [ALEXTANGXIAO/TEngine](https://github.com/ALEXTANGXIAO/TEngine)，在其基础上围绕**热更新、资源打包、运行时配置**做了一系列定制改造。本文档汇总相对上游新增/修改的能力，详细的设计与排查过程见 `UnityProject/conversation-summaries/` 下对应日期的会话总结。

## 📚 目录

- [日志系统](#-日志系统)
  - [TouchSocket 日志桥接与落盘](#1-touchsocket-日志桥接与落盘)
  - [Editor 打开日志目录菜单](#2-editor-打开日志目录菜单)
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

> 📝 维护提示：每完成一项新的定制改动，请在本文档对应主题下追加条目，并在根 `README.md` 的「🛠️ 本 Fork 的定制改动」章节同步精简概述。
