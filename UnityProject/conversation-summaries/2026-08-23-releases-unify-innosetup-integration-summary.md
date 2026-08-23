# 统一构建产物到 Releases/ + InnoSetup 集成进 BuildPipelineWindow

## 背景

仓库原存在两套并行、互不相交的打包体系：
- **Odin 体系**（`ReleaseTools` + `BuildPipelineWindow`，`TEngine/Editor/ReleaseTools/`）：默认输出到 `UnityProject/Output/`（AB→`Output/Bundles`、Player→`Output/Player/{平台}`、发布整理→`Output/Publish/{项目名}/{平台}/{包名}`）。
- **InnoSetup 体系**（`FullReleaseBuilder` + `FullReleaseBuilderWindow`，`Assets/Editor/Build/windows/`，全局命名空间、git 未跟踪）：输出到 `UnityProject/Releases/`，且**自己重复实现了一遍 YooAsset 构建**（`BuildYooAssetBundle`），与 ReleaseTools 的 `BuildInternalWithConfig` 逻辑重复并不同步（连 AB 输出根都不同：前者 `Bundles/`，后者 `Output/Bundles/`）；`setup.iss` 从不入库，一键打包实际跑不起来。

目标：把所有构建产物统一平铺到 `UnityProject/Releases/` 下，InnoSetup 步骤集成进 `BuildPipelineWindow`，`FullReleaseBuilder` 迁移到 `TEngine/Editor` 下并复用 ReleaseTools（消除重复实现）。

## 最终目录结构

```
UnityProject/Releases/
├── Bundles/                       # yooasset 资源输出根（内部由 YooAsset 拼 {平台}/{包名}/{版本}/）
├── Windows/
│   ├── setup.iss                  # InnoSetup 脚本（用户自行放入）
│   ├── build/                     # Unity Player 产物（<productName>.exe + _Data/）
│   └── setup/                     # InnoSetup ISCC 编译输出的安装包
├── Linux/
│   └── build/                     # Unity Player 产物（安装包方式 TBD）
└── Publish/                       # 发布整理产物（内部扁平化：{平台}/{包名}/）
```

仅 Windows/Linux 的 Player 归 `Releases/{平台}/build/`；Android/iOS/MacOS/WebGL 的 Player 输出仍走 `Output/Player/{平台}/`，本次不动。

## 关键改动

### 1. 默认路径常量统一到 Releases/

三处默认值同步（`BuildConfig` 字段 + `CreateDefault()` + `BuildPipelineWindow` 常量 + `BuildPipelineSetting` 字段）：
- `OutputRoot`：`./Output/Bundles/` → `./Releases/Bundles/`
- `PublishRoot`：`./Output/Publish/` → `./Releases/Publish/`
- `BuildConfig.GetDefaultPlayerOutputPath`：Windows→`Releases/Windows/build/<name>.exe`、Linux→`Releases/Linux/build/<name>`；其它平台分支保持 `Output/Player/{平台}/`（按平台分流，避免影响 Android/iOS/MacOS/WebGL）
- `ReleaseTools` 三个 MenuItem 预设（`AutomationBuild`/`Android`/`IOS`）的 `OutputRoot` 硬编码 `Output/Bundles` → `Releases/Bundles`；`GetResolvedOutputRoot`/`GetPublishOutputRoot` 回退默认值同步

### 2. Publish 扁平化去项目名层

`ReleaseTools.PublishBuiltPackage`（原 L443-446）去掉 `projectName` 一层：
- 原：`publishRoot/{项目名}/{平台}/{包名}/`
- 现：`publishRoot/{平台}/{包名}/` = `Releases/Publish/{平台}/{包名}/`
- 同步删除 `BuildPipelineWindow` 三处预览文本中的 `GetPreviewProjectName()` 引用（`PublishRuleText`、`RefreshCachedTexts`、`RebuildFlowSteps`），`GetPreviewProjectName` 方法本身删除

### 3. 已入库 BuildPipelineSetting.asset 旧路径自动迁移

`BuildPipelineSetting.asset` 已入库，持久化了旧 `Output/` 路径。利用现有迁移机制扩展：
- `BuildPipelineWindow` 新增 `LegacyOutputRootV2 = "./Output/Bundles/"`、`LegacyPublishRootV2 = "./Output/Publish/"` 常量
- `LoadFromSetting` 迁移块：`IsLegacyDefaultPath` 同时判定更早的 `./Builds/`、`./Publish/` 和上一版 `./Output/Bundles/`、`./Output/Publish/`，命中即重置为新默认 `Releases/`
- Player 旧目录前缀迁移：`./Build/` 与 `./Output/Player/` 都视为旧默认，重置为当前默认路径

### 4. FullReleaseBuilder 迁移 → InnoSetupBuilder + 集成进 BuildPipelineWindow

**迁移**：`Assets/Editor/Build/windows/FullReleaseBuilder.cs` → `Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`，纳入 `TEngine` 命名空间；删除 `FullReleaseBuilderWindow.cs` 及菜单 `Build/一键出安装包`；删空 `Assets/Editor/Build/` 目录。

**重构（消除重复 YooAsset 实现）**：
- 删除 `BuildYooAssetBundle`、`GetBuiltinShaderBundleName`、`CreateEncryptionInstance`、`GetBuildPackageVersion`（ReleaseTools 已有等价物）
- 保留 InnoSetup 专属：`FindIscc`、`CompileSetup`、`GetIssDefine`/`SyncIssDefines`/`WriteIssDefine`
- 路径常量对齐：`ReleasesDir`=`UnityProject/Releases`、`WindowsDir`=`Releases/Windows`、`IssPath`=`Releases/Windows/setup.iss`、`PlayerBuildDir`=`Releases/Windows/build`、`InstallerOutputDir`=`Releases/Windows/setup`
- `BuildInstaller(installerVersion, exeName, isccPathOverride)`：只做"回写 iss → ISCC 编译"两步，AB/Player 由 `ReleaseTools.BuildWithConfig` 完成

**FindIscc 去硬编码 + D 盘扫描**：
- 原 `FindIscc` 含硬编码 `D:\Program Files\Inno Setup 7\ISCC.exe`，被以"去硬编码"删掉后反而漏掉 D 盘安装
- 改为三级查找：①注册表 `HKLM\SOFTWARE\...\Inno Setup <ver>` 的 `InstallPath`/`InstallDir` → ②PATH 环境变量 → ③**扫描所有固定驱动器**的 `\Program Files\Inno Setup 6/7\ISCC.exe` 和 `\Program Files (x86)\...`（覆盖装在非系统盘 D:\ 的情况，.NET `SpecialFolder` 只指向 C 盘无法覆盖）
- 实测用户装在 `D:\Program Files\Inno Setup 7\ISCC.exe`，注册表无记录、PATH 无记录，靠第③步扫描命中

**ISCC 路径手动兜底**：
- `BuildConfig`/`BuildPipelineSetting`/`BuildPipelineWindow` 新增 `IsccPath` 字段（用户手动填，持久化）
- `InnoSetupBuilder.ResolveIscc(isccPathOverride)`：优先用户指定（需文件存在）→ 否则自动查找
- UI「InnoSetup 安装包」分组：`构建安装包`开关 + `安装包版本` + `ISCC 路径`（浏览/打开）+ `ISCC 状态`只读指示（显示找到的实际路径，如"已就绪：D:\Program Files\Inno Setup 7\ISCC.exe"）

**集成进 BuildPipelineWindow**：
- `BuildConfig`/`BuildPipelineSetting` 新增 `BuildInstaller` + `InstallerVersion` + `IsccPath`
- UI 分组「InnoSetup 安装包」仅 Windows Player 下显示（`IsWindowsPlayerPlatform`/`IsInstallerEnabled`）
- `ExecuteBuild`（一键构建 AB+Player）和 `ExecuteBuildPlayerOnly`（单独构建 Player）在 Player 成功后按需调用 `InnoSetupBuilder.BuildInstaller(config.InstallerVersion, exeName, config.IsccPath)`
- 流程预览新增第 6 步「编译 InnoSetup 安装包」
- `LoadFromSetting`/`SaveSettings`/`ApplyConfig`/`CreateConfig` 全链路同步三个新字段

### 5. 编译错误修复

- `InnoSetupBuilder.cs` 因 `using System.Diagnostics;` 导致 `Debug` 在 `System.Diagnostics.Debug` 与 `UnityEngine.Debug` 间歧义 → 加 `using Debug = UnityEngine.Debug;` 别名
- `BuildPipelineWindow.cs` 插入 `ChooseIsccPath`/`OpenIsccPath` 时 Edit 误删 `ToAbsolutePath(string path)` 方法签名，导致方法体成孤立块 → 补回签名

### 6. .gitignore

- 删去已废弃的 `/Publish/`（已迁 `Releases/Publish/`）
- 保留 `/Output/`（其它平台 Player 仍用 `Output/Player/`）
- 新增：`/Releases/Bundles/`、`/Releases/*/build/`、`/Releases/*/setup/`、`/Releases/Publish/`（忽略产物，保留 `setup.iss` 可跟踪）

## 关键文件

- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`（默认值 + Player 路径 + 新字段）
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`（回退默认值 + Menu 预设 + PublishBuiltPackage 扁平化）
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineSetting.cs`（字段默认值 + 新字段 + ApplyDefaults）
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`（默认常量 + 迁移判定 + InnoSetup UI + ExecuteBuild 串调 + 流程预览）
- `Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`（新增，由 FullReleaseBuilder 迁移重构）
- `UnityProject/.gitignore`
- `Books/Fork/resource-build.md`、`Books/Fork/CHANGELOG.md`

## 复用的现有实现（避免重复造轮子）

- AB 构建：`ReleaseTools.BuildInternalWithConfig`（经 `BuildWithConfig` 调用），不在 InnoSetupBuilder 重写 `ScriptableBuildPipeline`
- Player 构建：`ReleaseTools.BuildImp`（由 `BuildWithConfig` 在 `buildPlayer=true` 时调用）
- 默认路径迁移：复用 `BuildPipelineWindow` 现有 `IsLegacyDefaultPath`/`MigrateLegacyExecutableName` 框架，只加新 legacy 值
- iss 读写：`InnoSetupBuilder.GetIssDefine`/`SyncIssDefines`（从 FullReleaseBuilder 原样搬，改路径常量）
- Publish 拷贝：`ReleaseTools.PublishBuiltPackage`/`PublishFromExistingBuild`/`CopyDirectory` 原样复用，只改拼接去项目名

## 待办 / 注意事项

- `setup.iss` 需用户自行放入 `Releases/Windows/setup.iss` 后 InnoSetup 流程才可用；缺失时 `BuildInstaller` 抛 `FileNotFoundException` 并提示路径。iss 的 `OutputDir` 需指向 `Releases/Windows/setup`，`Source` 需指向 `Releases/Windows/build`（脚本内容由用户维护，本工具仅回写 `MyAppExeName`/`MyAppVersion`）。
- Linux 安装包方案待定，本次仅建好 `Releases/Linux/build/` 的 Player 输出。
- YooAsset 内置资源复制链路（`BuildinFileRoot = GetStreamingAssetsRoot()` → `Assets/StreamingAssets/package/`）不受影响，与 `OutputRoot` 相互独立。
