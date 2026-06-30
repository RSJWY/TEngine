# 资源打包

本页记录 fork 中围绕 YooAsset 构建、发布整理和打包工具体验的改动。

## 按包构建管线

### 背景

多包架构下，不同资源包可能需要不同 YooAsset 构建管线。继续使用全局单一管线会限制代码包、普通资源包和 RawFile 包的独立配置。

### 改动摘要

- 资源包不再统一使用单一构建管线。
- 支持按包指定 YooAsset 构建管线。
- 保留 SBP 与 RawFile。
- 移除 BBP（BuiltinBuildPipeline）。
- 打包工具页面直接读写运行时配置 `UpdateSetting.RuntimePackages`。
- 编辑器配置与运行时初始化配置共用同一数据源，避免双份维护。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-05-30-resource-package-pipeline-and-default-package-summary.md`

## 发布整理流程

### 背景

构建后手工整理产物到发布目录容易漏包、错平台名或生成 404。发布整理流程用于把构建产物按运行时实际访问路径归档。

### 改动摘要

- `BuildConfig` 新增 `EnablePublishCopy`。
- `BuildConfig` 新增 `PublishRoot`。
- `BuildConfig` 新增 `CleanPublishPackageDirectory`。
- 打包窗口新增“发布整理”面板。
- 新增 `GetRemotePlatformName(BuildTarget)`。
- 发布目标目录统一使用运行时远端平台名，如 `Windows64`、`MacOS`、`IOS`。
- 解决构建目录名 `StandaloneWindows64` 等与运行时远端平台名不一致导致的 404。
- 补齐运行时 `Linux` 分支。
- 支持“仅执行发布整理”，可对历史已构建版本重新整理上传。
- 仅允许整理所有启用包都存在的“公共版本”。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-05-30-resource-package-publish-workflow-summary.md`

## 打包工具构建流程预览

### 背景

原打包窗口中折叠区域顺序和实际执行顺序不完全一致，容易让使用者误判构建流程。

### 改动摘要

- 打包工具窗口新增「构建流程预览」面板。
- 按实际执行顺序动态展示步骤：
  1. 编译热更 DLL
  2. 构建 AB
  3. 发布整理
  4. 最小包处理
  5. 构建 Player
- 启用步骤递增编号。
- 未启用步骤灰显跳过。
- 随配置实时刷新。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`

## 打包工具 Odin 化与卡顿优化

### 背景

原 `BuildPipelineWindow` 使用传统 IMGUI，随着资源包、发布整理、热更 DLL、Player 设置和构建日志都集中在一个窗口中，维护成本和编辑卡顿都开始明显。

### 改动摘要

- `BuildPipelineWindow` 迁移为 `OdinEditorWindow`。
- 使用 `BoxGroup` / `TitleGroup` 组织基础设置、资源包列表、发布整理、最小包、高级设置、热更 DLL、Player 设置、构建流程预览、操作按钮与构建日志。
- 使用 `TableList` 展示 `UpdateSetting.RuntimePackages` 与构建流程步骤。
- 通过窗口内的 `RuntimePackageView` 包装运行时配置，避免给运行时程序集引入 Odin 依赖。
- 使用 `ValueDropdown` 替代手写 Popup，统一平台、构建管线、压缩方式、包级加密、内置文件拷贝与文件名风格选项。
- 继续隐藏和规避已废弃的 BBP 路径。
- 保留原有 `EditorPrefs` key、菜单路径与 `ReleaseTools` 构建入口。
- 原有一键构建、仅构建 AB、仅构建 Player、仅发布整理、编译热更 DLL、同步 AOT 元数据清单等行为不变。

### 性能处理

- 资源包表格编辑先写内存并标脏。
- 0.75 秒静默后统一 `AssetDatabase.SaveAssets()`。
- 窗口关闭或点击保存时强制 flush。
- 状态栏、发布目录预览、构建流程预览改为配置变化时刷新缓存。
- 避免每次 `OnImGUI` 绘制都重新计算包摘要。
- 构建日志 `Repaint()` 增加 0.1 秒节流。
- 版本号、路径、保留 Tag、包名、版本键等文本字段使用 `DelayedProperty`，减少输入过程中反复触发同步。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs.meta`

### 相关记录

- `UnityProject/conversation-summaries/2026-06-27-odin-build-pipeline-window-summary.md`
