# 2026-05-28 热更新多包与打包工具改造总结

## 背景
本轮工作的目标，是把 TEngine 现有的 YooAsset + HybridCLR 热更新流程，从默认资源包内混装 DLL 的模式，演进为可扩展的多包架构，并让编辑器打包工具与运行时配置使用同一份资源包数据源。

## 本次完成的核心阶段

### 1. 拆分热更 DLL 为独立 `CodePackage`
- 将热更程序集从默认包中拆分出来，单独进入 `CodePackage`。
- 运行时程序集加载流程改为优先从程序集包读取 DLL/AOT 元数据。
- 保留 `DefaultPackage` 负责常规资源，程序集资源独立发布与更新。

### 2. 运行时支持多包初始化、更新与下载
- 在 `UpdateSetting` 中引入运行时资源包列表配置 `RuntimePackages`。
- 支持按包控制：
  - 是否启用
  - 启动时是否初始化
  - 启动时是否更新清单
  - 是否参与下载检查
  - 是否保存版本记录
  - 版本键 `VersionKey`
- 运行时初始化流程、清单更新流程、下载器创建流程都已改成按包执行。
- 若远端资源服务器不可用，已补齐“回退到本地已缓存版本初始化”的能力。

### 3. 远端资源目录改为每包独立子目录
- 远端地址规则由原先统一平台目录，调整为：
  - `{host}/{project}/{platform}/{packageName}/...`
- 这样 `DefaultPackage`、`CodePackage` 以及后续扩展包，都可以独立发布到自己的远端目录。
- 运行时 `RemoteServices` 已按包生成各自的主地址与备用地址。

### 4. 打包工具页面直接复用 `UpdateSetting.RuntimePackages`
- 原先打包工具尝试维护独立的包列表配置，后改为直接读写运行时配置 `UpdateSetting.RuntimePackages`。
- 这样编辑器打包配置与运行时初始化配置完全统一，避免双份维护。
- 打包工具页面现在可直接增删资源包，并编辑每个包的基础运行参数。
- 构建日志中也会显示当前参与构建的资源包列表。

### 5. 去掉 `IsAssemblyPackage` 字段
- 起初为明确程序集包职责，给 `RuntimePackageEntry` 增加过 `IsAssemblyPackage`。
- 后续改为只依赖 `AssemblyPackageName` 与包名推断程序集包，不再维护额外布尔字段。
- 已同步清理：
  - 运行时代码字段与判定逻辑
  - 编辑器打包工具页面中的对应开关
  - `UpdateSetting.asset` 内旧序列化字段

## 关键文件改动

### 运行时配置与流程
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/TEngine/Settings/UpdateSetting.asset`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Services.cs`

### 启动流程与下载流程
- `Assets/GameScripts/Procedure/ProcedureInitPackage.cs`
- `Assets/GameScripts/Procedure/ProcedureInitResources.cs`
- `Assets/GameScripts/Procedure/ProcedureCreateDownloader.cs`
- `Assets/GameScripts/Procedure/ProcedureDownloadOver.cs`
- `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`

### 编辑器打包工具
- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`

## 本次关键提交
- `fa7ffd45` 拆分热更 DLL 为独立 CodePackage
- `2fac2269` 完善多包远端更新与离线回退。
- `5c4e473e` 统一打包工具与运行时资源包配置。
- `af6f87bf` 移除资源包配置中的程序集包标记。

## 当前结果
- 热更 DLL 已独立成包。
- 运行时已支持多包初始化、更新、下载与断网回退。
- 远端目录已支持按包独立发布。
- 打包工具已直接复用运行时包列表配置。
- 程序集包判定已收敛为单一来源，不再使用 `IsAssemblyPackage`。

## 说明
- 本仓库当前仍存在一些与本轮任务无关的本地改动，它们未被混入上述提交。
- 本总结文件单独存放于 `conversation-summaries/` 目录，便于后续追溯本轮改造过程。
