# 2026-05-30 资源包构建管线与默认包来源整理会话总结

## 背景

本次会话围绕资源包配置与编辑器配置收敛展开，目标包括：

1. 资源包构建不再统一使用单一构建管线，而是支持按包指定 YooAsset 构建管线。
2. 移除已不再需要的 BBP（BuiltinBuildPipeline）选项，仅保留 SBP 与 RawFile。
3. 收敛 `ResourceModuleDriver` 的默认包来源，不再依赖 inspector 中的单包配置。
4. 清理 `ResourceModuleDriver` 和对应自定义 Inspector 中的失效单包字段，并修复由此引发的 Inspector 空引用。

项目规则要求 L2-L4 任务先查询 `tengine-dev` skill。本会话已查询资源加载、热更工作流与排障相关规范。

## 本次改动

### 1. 资源包支持按包指定构建管线

主要内容：

- 在 `RuntimePackageEntry` 中新增 `BuildPipeline` 字段。
- 新增 `RuntimePackageBuildPipeline` 枚举：
  - `UseGlobal`
  - `ScriptableBuildPipeline`
  - `BuiltinBuildPipeline`（仅兼容旧数据）
  - `RawFileBuildPipeline`
- 构建时优先使用包级配置；若为 `UseGlobal`，则回退到打包窗口中的默认构建管线。
- 构建窗口资源包列表增加“构建管线”选择。
- 构建日志显示每个包的实际构建管线。

关键文件：

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`

### 2. 移除 BBP 作为实际可选构建管线

主要内容：

- 打包窗口全局默认管线只保留：
  - `ScriptableBuildPipeline`
  - `RawFileBuildPipeline`
- 包级构建管线选择只保留：
  - `UseGlobal`
  - `ScriptableBuildPipeline`
  - `RawFileBuildPipeline`
- `ReleaseTools` 中删除 `BuiltinBuildPipeline` / `BuiltinBuildParameters` 的实际构建分支。
- 历史 BBP 配置读取后自动收敛为 `ScriptableBuildPipeline`，避免旧配置误映射为 RawFile。
- 全局默认构建管线的 EditorPrefs 存储由旧的整型索引改为字符串枚举名，避免历史索引污染。

关键文件：

- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`

### 3. 收敛默认包来源到 UpdateSetting.RuntimePackages

主要内容：

- 在 `UpdateSetting` 中新增 `GetDefaultPackageName()`。
- 默认包名改为取 `GetEnabledRuntimePackages()` 的第一个包。
- `ResourceModuleDriver.Start()` 启动时不再使用自身的 `PackageName` 字段，而是使用 `Settings.UpdateSetting.GetDefaultPackageName()`。
- 这使得：
  - 初始化哪些包：由 `UpdateSetting.RuntimePackages` 决定。
  - 未显式传包名时的默认包回退：也由 `UpdateSetting.RuntimePackages` 决定。

关键文件：

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs`

### 4. 清理失效的 ResourceModuleDriver 单包字段

主要内容：

- 删除 `ResourceModuleDriver` 中已不再生效的：
  - `packageName`
  - `PackageName`
- 这样可以避免 Inspector 继续给出误导性的单包配置入口。

关键文件：

- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs`

### 5. 修复 ResourceModuleDriverInspector 序列化显示空引用

问题现象：

- 删除 `ResourceModuleDriver.packageName` 后，`ResourceModuleDriverInspector` 仍然通过 `FindProperty("packageName")` 和 `m_packageName.stringValue` 访问该字段。
- 导致 Inspector 打开时报错：
  - `NullReferenceException`
  - `TEngine.Editor.Inspector.ResourceModuleDriverInspector.DrawBasicSettings`

修复内容：

- 删除 Inspector 中对已删除字段的全部引用：
  - `m_packageName`
  - `m_packageNameIndex`
  - `m_packageNames`
  - `FindProperty("packageName")`
  - `GetBuildPackageNames()`
- 基础设置中不再显示“资源包名”下拉，而是显示说明：
  - 默认包名来自 `UpdateSetting.RuntimePackages`
- 统计区域改为显示 `GetDefaultPackageName()`。
- 新增 Inspector 辅助方法 `GetDefaultPackageName()`。

关键文件：

- `Assets/TEngine/Editor/Inspector/ResourceModuleDriverInspector.cs`

## 影响与结论

### 关于初始化流程

`ResourceModuleDriver` inspector 中原有的单包配置，在本次收敛后：

- **不再影响** 多包初始化流程。
- **不再影响** 启动时的默认包来源。
- 默认包与资源包列表统一来源于 `UpdateSetting.RuntimePackages`。

### 关于未显式传包名的调用

以下行为仍会使用默认包：

- `GetPackageVersion(string customPackageName = "")`
- `RequestPackageVersionAsync(..., string customPackageName = "")`
- `UpdatePackageManifestAsync(..., string customPackageName = "")`
- `CreateResourceDownloader(string customPackageName = "")`
- 以及其他未显式传 `packageName` 的资源查询/加载入口

因此，`RuntimePackages` 的**第一个启用包**现在就是这些默认回退调用的实际目标包。

## 验证情况

已做：

- 检查 `ProcedureInitPackage`、`ResourceModuleDriver`、`ResourceModule`、`UpdateSetting` 的默认包与初始化路径。
- 检查 `ReleaseTools` 中 BBP 分支已删除，RawFile 分支使用 `EBuildBundleType.RawBundle`。
- 检查 `ResourceModuleDriverInspector` 已无：
  - `m_packageName`
  - `m_packageNameIndex`
  - `m_packageNames`
  - `FindProperty("packageName")`
  - `GetBuildPackageNames()`
- 多次执行 `git diff --check`，未发现本次目标文件的格式问题。

未做：

- 没有运行 Unity 完整编译。
- 没有在编辑器内进行真实打包验证。
- 没有对 HostPlay / OfflinePlay / WebGL 做完整运行验证。

## 用户协作偏好

用户明确要求：

- 下次修改类似运行时组件/序列化字段时，要同步确认是否存在对应的 Editor/Inspector 显示优化与字段引用。
- 不能只改运行时代码而忽略自定义 Inspector、PropertyDrawer 或 Editor 脚本。

## 当前工作区注意事项

本次会话期间工作区还存在一些未纳入本次功能修改的其他改动，例如：

- `Assets/AssetRaw/UI/BattleMainUI.prefab`
- `Assets/GameScripts/HotFix/GameLogic/GameApp.cs`
- `Assets/Scenes/main.unity`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/HybridCLRSettings.asset`
- `Assets/AssetRaw/DLL/`

后续如果提交，应继续只挑选与本次资源包/默认包/Inspector 收敛相关的文件，避免误带入其他 Unity 场景或资源变更。
