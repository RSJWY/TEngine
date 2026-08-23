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
- `PublishRoot` 默认值改为 `./Releases/Publish/`，与 AB 输出、Player 输出统一平铺到 `Releases/` 下。
- 发布目录扁平化：去掉原 `{项目名}` 一层，结构变为 `Releases/Publish/{平台}/{包名}/`。

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
  6. 编译 InnoSetup 安装包（仅 Windows + 勾选时）
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

## 清理 UpdateSetting 死配置字段

### 背景

`UpdateSetting` 原有 `BuildAddress` 与 `isAutoAssetCopeToBuildAddress` 两个字段，字面含义是「Player 打包后把内置资源复制到指定目录」。但经核对 YooAsset 2.3.19 源码，YooAsset 内置资源复制目标由 `BuildinFileRoot` 决定，而本 fork 将其设为 `AssetBundleBuilderHelper.GetStreamingAssetsRoot()` = `StreamingAssets + DefaultYooFolderName`（项目配置为 `package`），实际落地到 `Assets/StreamingAssets/package/{PackageName}/`。这两个字段从未被任何代码读取，属于误导性死配置：修改它们对打包行为无任何影响。

### 改动摘要

- 删除 `UpdateSetting.BuildAddress` 字段及 getter `GetBuildAddress()`。
- 删除 `UpdateSetting.isAutoAssetCopeToBuildAddress` 字段及 getter `IsAutoAssetCopeToBuildAddress()`。
- 原悬挂在它们上方的 `[Header("构建资源设置")]` 下移到保留字段 `ReplaceAssetPathWithAddress`，保持分组语义。
- 同步清理 `UpdateSetting.asset` 中对应的序列化行。
- YooAsset 内置资源复制机制（`DefaultYooFolderName=package` + `BuildinFileCopyOption`）保持不变，行为无任何回归。

### 注意事项

- YooAsset 内置资源复制目标仍由 `YooAssetSettings.asset` 的 `DefaultYooFolderName` 决定，与 `UpdateSetting` 无关。
- 若未来需要「打出 Player 后把 StreamingAssets 再复制到 Player 目录」的能力，需另行实现并校正默认路径（现走 `Releases/Windows/build` + Inno Setup 路线，原默认值 `../../Builds/Unity_Data/StreamingAssets` 已不适用）。

### 关键文件

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/TEngine/Settings/UpdateSetting.asset`

### 相关记录

- `UnityProject/conversation-summaries/code-research/2026-08-22-updatesetting-buildaddress-yooasset-research.md`

## 统一产物目录到 Releases/ 与 InnoSetup 集成

### 背景

原仓库存在两套并行、互不相交的打包产物目录约定：`ReleaseTools`/`BuildPipelineWindow` 体系默认输出到 `UnityProject/Output/`（AB→`Output/Bundles`、Player→`Output/Player/{平台}`、发布整理→`Output/Publish/{项目名}/{平台}/{包名}`）；而独立的 `FullReleaseBuilder` 脚本输出到 `UnityProject/Releases/`，且自行重复实现了一遍 YooAsset 构建（与 ReleaseTools 的 `BuildInternalWithConfig` 逻辑重复并不同步，连 AB 输出根都不同），还以全局命名空间落在 `Assets/Editor/Build/` 下，跨程序集引用不便。`setup.iss` 也从不入库，一键打包实际跑不起来。

### 目录结构

所有构建产物统一平铺到 `UnityProject/Releases/` 下：

```
Releases/
├── Bundles/                       # yooasset 资源输出根（内部由 YooAsset 拼 {平台}/{包名}/{版本}/）
├── Windows/
│   ├── setup.iss                  # InnoSetup 脚本（用户自行放入）
│   ├── build/                     # Unity Player 产物（<productName>.exe + _Data/）
│   └── setup/                     # InnoSetup ISCC 编译输出的安装包
├── Linux/
│   └── build/                     # Unity Player 产物（安装包方式 TBD）
└── Publish/                       # 发布整理产物（内部 {平台}/{包名}/）
```

仅 Windows/Linux 的 Player 归 `Releases/{平台}/build/`；Android/iOS/MacOS/WebGL 的 Player 输出仍走 `Output/Player/{平台}/`，本次不动。

### 改动摘要

- `BuildConfig` 的 `OutputRoot`/`PublishRoot` 默认值由 `./Output/Bundles/`、`./Output/Publish/` 改为 `./Releases/Bundles/`、`./Releases/Publish/`；`BuildPipelineSetting`、`BuildPipelineWindow` 常量与 `ReleaseTools` 回退默认值、`MenuItem` 预设路径同步。
- `BuildConfig.GetDefaultPlayerOutputPath`：Windows→`Releases/Windows/build/<name>.exe`，Linux→`Releases/Linux/build/<name>`；其它平台分支保持 `Output/Player/{平台}/`。
- `ReleaseTools.PublishBuiltPackage` 去掉 `{项目名}` 一层，发布目录扁平化为 `Releases/Publish/{平台}/{包名}/`；窗口发布预览文本同步去项目名。
- 已入库 `BuildPipelineSetting.asset` 的旧 `Output/` 路径自动迁移到 `Releases/`：扩展 `LoadFromSetting` 的 legacy 判定，把 `./Output/Bundles/`、`./Output/Publish/`、`Output/Player/` 前缀也纳入迁移（保留更早的 `./Builds/`、`./Publish/` 历史兼容）。
- `FullReleaseBuilder` 迁移到 `Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`，纳入 `TEngine` 命名空间；删除其重复的 YooAsset 构建实现，仅保留 InnoSetup 专属逻辑（`FindIscc`/`CompileSetup`/`GetIssDefine`/`SyncIssDefines`），AB 与 Player 构建复用 `ReleaseTools.BuildWithConfig`。
- `FindIscc` 去除硬编码 `D:\Program Files\...`，改为注册表 `HKLM\SOFTWARE\...\Inno Setup <ver>` → PATH → ProgramFiles 三级查找。
- 新增 `IsccPath` 字段与 UI「ISCC 路径」输入框（浏览/打开），作为自动查找失败的兜底；`InnoSetupBuilder.ResolveIscc` 优先用用户指定路径，其次自动查找，并在窗口显示「ISCC 状态」只读指示。
- `BuildConfig`/`BuildPipelineSetting`/`BuildPipelineWindow` 新增 `BuildInstaller` + `InstallerVersion` 字段与 UI（「InnoSetup 安装包」分组，仅 Windows Player 下显示）；`ExecuteBuild` 在 Player 构建成功后按需调用 `InnoSetupBuilder.BuildInstaller` 回写 iss 并编译安装包。
- 删除独立窗口 `FullReleaseBuilderWindow` 及菜单 `Build/一键出安装包`，InnoSetup 步骤并入 `Build/打包工具窗口` 的「一键构建 (AB + Player)」流程。
- `.gitignore` 补 `Releases/` 产物忽略（`/Releases/Bundles/`、`/Releases/*/build/`、`/Releases/*/setup/`、`/Releases/Publish/`），保留 `setup.iss` 可跟踪；删去已废弃的 `/Publish/`，保留 `/Output/`（其它平台 Player 仍用）。

### 注意事项

- `setup.iss` 需用户自行放入 `Releases/Windows/setup.iss` 后 InnoSetup 流程才可用；缺失时 `InnoSetupBuilder.BuildInstaller` 抛 `FileNotFoundException` 并提示路径。
- `setup.iss` 的 `OutputDir` 需指向 `Releases/Windows/setup`，`Source` 需指向 `Releases/Windows/build`（脚本内容由用户维护，本工具仅回写 `MyAppExeName`/`MyAppVersion`）。
- Linux 安装包方案待定，本次仅建好 `Releases/Linux/build/` 的 Player 输出。
- YooAsset 内置资源复制链路（`BuildinFileRoot = GetStreamingAssetsRoot()` → `Assets/StreamingAssets/package/`）不受影响，与 `OutputRoot` 相互独立。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineSetting.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`（新增，由 `Assets/Editor/Build/windows/FullReleaseBuilder.cs` 迁移重构）
- `UnityProject/.gitignore`

