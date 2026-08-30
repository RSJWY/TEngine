# 资源打包

本页记录 fork 中围绕 YooAsset 构建、发布整理和打包工具体验的改动。

## 按包构建管线

### 背景

多包架构下，不同资源包可能需要不同 YooAsset 构建管线。继续使用全局单一管线会限制代码包、普通资源包和 RawFile 包的独立配置。

### 改动摘要

- 资源包不再统一使用单一构建管线。
- 支持按包指定 YooAsset 构建管线。
- 保留 SBP 与 RawFile，并新增 ArchiveFile 管线。
- 移除 BBP（BuiltinBuildPipeline）。
- 打包工具页面直接读写运行时配置 `UpdateSetting.RuntimePackages`。
- 编辑器配置与运行时初始化配置共用同一数据源，避免双份维护。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-05-30-resource-package-pipeline-and-default-package-summary.md`

## ArchiveFileBuildPipeline 代码包构建

### 背景

`CodePackage` 由 DLL、PDB、AOT 元数据和动态密钥等原始文件组成。YooAsset 3.0.5 的 `ArchiveFileBuildPipeline` 可以按 BundleName 将这些文件合并为 `ArchiveBundle`，更适合代码包独立发布。

### 改动摘要

- 打包窗口支持选择 `ArchiveFileBuildPipeline`。
- `CodePackage` 默认使用 ArchiveFile 管线和 ChaCha20 加密。
- 构建参数使用 `ArchiveFileBuildParameters`，Bundle 类型为 `ArchiveBundle`，默认 4 字节对齐。
- 编辑器模拟模式按包选择 `VirtualArchiveBundle`。
- 原有 Scriptable 和 RawFile 管线保持可用。

### 注意事项

- ArchiveBundle 加密后只能使用内存解密器，整包解密会产生内存峰值。
- 修改密钥或 ChaCha20 实现后必须重新构建资源，旧加密包和缓存不能混用。
- TEngine 上游支持 YooAsset 3.x 后，应优先合并上游构建抽象并减少本地分支判断。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-08-29-yooasset-archive-code-package-summary.md`

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
- `setup.iss` 的 `OutputDir` 需指向 `Releases/Windows/setup`，`Source` 需指向 `Releases/Windows/build`（脚本内容由用户维护，本工具回写 `MyAppName`/`MyAppVersion`/`MyAppPublisher`/`MyAppExeName`/`MyAppPassword`/`BrandWatermark` 六项，`MyAppId` 不回写，详见下文「iss 变量输入补全」）。
- Linux 安装包方案待定，本次仅建好 `Releases/Linux/build/` 的 Player 输出。
- YooAsset 内置资源复制链路（`BuildinFileRoot = GetStreamingAssetsRoot()` → `Assets/StreamingAssets/package/`）不受影响，与 `OutputRoot` 相互独立。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineSetting.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`（新增，由 `Assets/Editor/Build/windows/FullReleaseBuilder.cs` 迁移重构）
- `UnityProject/.gitignore`

## iss 变量输入补全

### 背景

`setup.iss` 顶部用 `#define` 暴露了一组安装包元信息（应用名、版本、发布者、exe 名、AppId、安装密码、水印），并注释声明「打包工具构建时按窗口参数回写」。但 InnoSetup 集成初版只在窗口暴露了「安装包版本」「ISCC 路径」两项，`SyncIssDefines` 实际只回写 `MyAppExeName`/`MyAppVersion`；应用名、发布者、安装密码、水印等始终停留在 iss 模板写死的占位值（如「我的软件」「我的公司」），打出安装包后还得手动改 iss 再编译。本次把这部分变量补成窗口输入字段，让一键出包即出即用。

### 改动摘要

- `InnoSetupBuilder` 新增 `IssInstallerConfig` 结构承载窗口侧输入（`AppName`/`InstallerVersion`/`Publisher`/`ExeName`/`Password`/`Watermark`），`BuildInstaller` 改为接收该结构。
- `SyncIssDefines` 回写范围由 2 项扩展到 6 项：`MyAppName`/`MyAppPublisher`/`MyAppPassword`/`BrandWatermark` 与既有的 `MyAppExeName`/`MyAppVersion`（版本为空时沿用 iss 现值不回写，避免清空版本号）。
- **`MyAppId` 不回写**，始终以 `setup.iss` 文件内手填值为准——它决定升级覆盖 vs 并存的安装包身份，需稳定不随窗口参数漂移；iss 顶部注释同步标注此约束。
- `BuildConfig`/`BuildPipelineSetting` 新增 4 个持久化字段 `InstallerAppName`/`InstallerPublisher`/`InstallerPassword`/`InstallerWatermark`；默认值：应用名取 `PlayerSettings.productName`、发布者取 `PlayerSettings.companyName`、密码默认空、水印默认随发布者（为空时运行时回退用发布者值）。
- 打包窗口「安装包配置」tab 拆为两个 BoxGroup：**InnoSetup 安装包**（开关、版本、应用名、发布者、安装密码、发布者水印）与 **ISCC 编译**（ISCC 路径+浏览/打开、ISCC 状态、iss 脚本、安装包输出预览、构建按钮），参数与工具链分离；4 个新字段经 `LoadFromSetting`/`SaveSettings`/`ApplyConfigToFields`/`CreateConfig` 全链路同步，首次为空时从 `PlayerSettings` 补默认值并持久化。
- **新增软件英文名**（2026-08-24）：`setup.iss` 新增 `#define MyAppEnglishName`，`GetDefaultDir` 默认安装目录改用英文名而非中文 `MyAppName`（中文名仍用于显示/图标/安装包文件名）；`IssInstallerConfig` 增加 `AppEnglishName` 并回写，为空时回退用 `AppName`；`BuildConfig`/`BuildPipelineSetting`/`BuildPipelineWindow` 新增 `InstallerAppEnglishName` 字段与「软件英文名」UI（默认取 `PlayerSettings.productName`），全链路同步。

### 使用方式

在「打包工具窗口 → 安装包配置」tab 勾选「构建安装包」后：

- **应用名称**：填软件中文名（如「我的软件」），回写 `MyAppName`，影响开始菜单组、桌面/启动项图标名、安装包文件名（不再影响安装目录）。
- **软件英文名**：回写 `MyAppEnglishName`，**仅用于默认安装目录**（`DefaultDirName`→`GetDefaultDir`），中文名含非 ASCII 字符会污染路径，故目录改用英文名；为空时回退用「应用名称」。默认取 `PlayerSettings.productName`。
- **发布者**：回写 `MyAppPublisher`，仅用于安装向导显示（不影响路径）。
- **安装密码**：留空表示不加密；填入后 iss 的 `#if MyAppPassword != ""` 自动启用 `Password`+`Encryption`。
- **发布者水印**：安装向导左下角灰色文字，回写 `BrandWatermark`；留空时回退用「发布者」值。
- **AppId**：直接改 `Releases/Windows/setup.iss` 的 `#define MyAppId`，窗口不提供入口。

### 注意事项

- `MyAppId` 改动会影响旧版升级覆盖（同 AppId 覆盖、不同 AppId 并存），首次部署需在 iss 里填稳定值，勿随手改。
- 密码以明文写入 iss（Inno Setup 本身如此），iss 已入库则密码会随仓库泄露，敏感场景注意仓库可见范围。
- 老工程升级到本版后，`BuildPipelineSetting.asset` 无新字段，首次打开窗口会自动从 `PlayerSettings` 补应用名/发布者并保存；密码、水印默认空，需按需填写。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`（`IssInstallerConfig`、`BuildInstaller`、`SyncIssDefines`）
- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`（4 字段 + `CreateDefault` 默认值）
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineSetting.cs`（4 字段 + `ApplyDefaults`）
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`（4 个 UI 字段 + 双 Box 分区 + 全链路同步 + `ExecuteInstallerBuild` 组装 `IssInstallerConfig`）
- `Releases/Windows/setup.iss`（注释更新回写清单与 `MyAppId` 约束）

### 相关记录

- `UnityProject/conversation-summaries/2026-08-23-releases-unify-innosetup-integration-summary.md`

## Inno Setup 模板分离与构建可靠性增强

### 背景

原流程会直接回写受版本控制的 `setup.iss`，项目参数和安装密码容易污染框架模板；同时 AssetBundle 或 Player 构建失败后，安装包阶段可能继续使用旧产物。ISCC 失败时 stderr 管道和进度条清理也不完整，存在编辑器长时间卡住的风险。

### 改动摘要

- `Releases/Windows/setup.iss` 固定作为版本管理模板，构建面板不再修改它。
- 面板首次打开时自动从模板创建忽略版本管理的 `setup.generated.iss`；实际参数和密码只同步到生成脚本，ISCC 也只编译生成脚本。
- Inno Setup 页显示模板路径、实际编译脚本、生成状态和最后修改时间，并提供“打开模板”“打开生成脚本”“从模板重新生成”操作。
- `BuildWithConfig` 和 Player 构建返回明确成功状态；AssetBundle 或 Player 失败时立即阻断安装包阶段，避免打入旧产物。
- 安装包编译前校验固定输入目录 `Releases/Windows/build`、目录内容和主 EXE。
- Player 输出路径增加“规范化路径”，只替换平台目录段并保留后续子目录和文件名。
- ISCC stdout/stderr 改为并行读取，增加十分钟超时，并在所有异常路径通过 `finally` 清理 Unity 进度条。
- Windows64 模板增加 `ArchitecturesAllowed=x64compatible`；旧版本卸载器返回失败或等待超时时中止安装，不再兜底删除整个 `{app}`。
- 保留 `BuildPipelineSetting.asset` 中的安装密码持久化，也保留模板里的分卷配置，由实际项目按需修改。

### 使用方式

打开构建面板后，`setup.generated.iss` 不存在时会自动生成。日常修改应用名、版本、发布者、密码和水印后直接构建即可；需要继承最新模板结构时，点击“从模板重新生成”，确认后覆盖生成脚本并重新应用面板参数。

### 注意事项

- `MyAppId` 仍直接维护在模板中，不由面板覆盖。
- `setup.generated.iss` 可供实际项目手工定制；普通打开面板和普通构建不会用模板覆盖它。
- Inno Setup 输入目录继续固定为 `Releases/Windows/build`，一键安装包要求 Windows Player 输出与该约定一致。
- `DiskSpanning`、`DiskSliceSize` 和 `SlicesPerDisk` 等分卷设置继续由模板维护。

### 关键文件

- `UnityProject/Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`
- `UnityProject/Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `UnityProject/Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `UnityProject/Releases/Windows/setup.iss`
- `UnityProject/.gitignore`

### 相关记录

- `UnityProject/conversation-summaries/2026-08-24-innosetup-build-hardening-plan.md`

## 资源清单加密（Manifest Encryption）

### 背景

YooAsset 3.x 的资源清单（Manifest）默认是明文二进制，包含全部资源路径、依赖关系、Hash 和 Bundle 元数据，是离线包里最先被抓取的高价值元数据。YooAsset 原生提供 `IManifestEncryptor` / `IManifestDecryptor` 两个对称接口，但 fork 之前未接入运行时解密链路，构建端选了加密器也会因运行时缺解密器导致清单加载失败。

Bundle 加密与清单加密在 fork 中独立配置：Bundle 加密按 `RuntimePackageEntry.EncryptionType` 走 `FileOffSet`/`FileStream`/`ChaCha20` 三档，清单加密固定为 ChaCha20，密钥与 Bundle 隔离，避免从清单解密链路逆向到 Bundle 密钥。

### 改动摘要

- `RuntimePackageEntry` 新增 `bool ManifestEncrypted`（per-package，默认 false，向后兼容）。
- 清单加密固定为 ChaCha20（RFC 7539），算法不暴露为枚举，未来更换算法为 breaking change。
- 新增 `ManifestChaCha20KeyConfig`（`CryptoKeyConfig<T>` 子类，32B key + 12B nonce 单资产承载），密钥资产路径 `Resources/EncryptConfigs/ManifestChaCha20KeyConfig.asset`，与 Bundle 用的 `ChaCha20KeyConfig` 独立。
- 新增 `ManifestChaCha20Encryptor` / `ManifestChaCha20Decryptor`，复用 `ChaCha20Util`，不引入新加密实现。
- `ResourceModule` 四个 `Create*FileSystemParameters`（Builtin/Sandbox/WebServer/WebNetwork）及微信小游戏分支统一经 `AddManifestDecryptor` 按 `ManifestEncrypted` 注入 `IManifestDecryptor`。
- `ReleaseTools` 构建端按 `ManifestEncrypted` 同时设置 `BuildParameters.ManifestEncryptor` 与 `ManifestDecryptor`：前者用于序列化加密清单，后者供 `TaskCreateCatalog` 反序列化生成首包 Catalog（缺解密器会导致 Catalog 生成时 `FileMagic` 校验失败）。
- `NormalizeRuntimePackageEntry` 同步 `ManifestEncrypted` 字段。

### 使用方式

在 `UpdateSetting` 的 `RuntimePackages` 列表中，对需要清单加密的资源包勾选 `ManifestEncrypted`。首次启用时编辑器会自动创建 `ManifestChaCha20KeyConfig.asset` 并生成随机密钥；如需自定义密钥，在 Project 视图选中该资产，用 Inspector 的 Hex 输入框或「重新生成密钥」按钮修改。

Editor 模拟模式（`EditorSimulateMode`）不经过真实构建清单，清单加密对它不生效，与 Bundle 加密在 EditorSimulate 下的行为一致。

### 注意事项

- 修改密钥或 nonce 后必须重新构建资源，并清理 StreamingAssets、沙盒缓存和远端旧版本，旧加密清单不能与新密钥混用。
- `ManifestEncrypted` 是 per-package 配置：可以为 `CodePackage` 启用而对 `DefaultPackage` 关闭，互不影响。
- 清单加密不与 Bundle 加密联动：一个包可以 Bundle 用 `None` 而清单用 ChaCha20，反之亦可。
- 算法更换（如换 AES-GCM）属于 breaking change，不做运行时自动识别或兼容迁移——清单版本与 Bundle 版本绑定，不存在线上旧清单需兼容的场景。

### 关键文件

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`（`RuntimePackageEntry.ManifestEncrypted`、`NormalizeRuntimePackageEntry`）
- `Assets/TEngine/Runtime/Module/ResourceModule/Crypto/ManifestChaCha20KeyConfig.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.ManifestCrypto.cs`（`ManifestChaCha20Encryptor`/`Decryptor`）
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`（`AddManifestDecryptor`、`GetManifestEncrypted`、四个 `Create*FileSystemParameters`）
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`（构建端注入）

### 相关记录

- `UnityProject/conversation-summaries/2026-08-30-yooasset-manifest-encryption-summary.md`
