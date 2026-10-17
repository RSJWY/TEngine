# BuildCLI 与 TEngine 打包工具对齐核查

> 日期：2026-10-17
> 范围：`BuildCLI/tengine_build/`（Python GUI/CLI）↔ `Assets/TEngine/Editor/ReleaseTools/`（CLIBridge.cs / BuildConfig.cs / BuildPipelineSetting.cs / ReleaseTools.cs / InnoSetupBuilder.cs）↔ 本项目内嵌 YooAsset 3.x 枚举

## 核查结论

**核心协议完全对齐；4 处枚举映射不对齐，其中 2 处有实际构建风险（平台读错、管线/压缩读错）。**

## 一、已对齐项（逐条核实）

| 对齐面 | 证据 |
| --- | --- |
| 请求 DTO 字段一一对应 | `BuildFormState`（config_store.py:54）↔ `CLIBridge.BuildRequestDTO`（CLIBridge.cs:31）35 个下发字段全一致；本地专用字段 `unityExePath/projectDir/logKeepCount/logKeepDays/buildTimeoutMinutes/batchName` 由 `_LOCAL_ONLY_FIELDS`（dto.py:14）正确排除 |
| 10 个 action 一致 | `dto.ALL_ACTIONS` = C# `Execute` switch 全部分支：build/buildAb/buildPlayer/publish/hotfixDll/generateAll/syncAotManifest/copyAotDll/switchPlatform/buildInstaller |
| 域重载分段规则一致 | `DOMAIN_RELOAD_ACTIONS = {generateAll, switchPlatform}` 三处定义（dto.py/config_store.py/unity_runner.py）值一致，与 CLIBridge.cs:39 注释约定一致；`split_into_segments` 正确独占进程 |
| 结果 JSON 字段一致 | C# `BuildResultDTO`（success/error/durationSeconds/playerOutputPath/playerSizeBytes/installerOutputPath/packages/steps/executedActions）↔ Python `_write_result` 合并键完全匹配 |
| 10 组日志哨兵逐字匹配 | unity_runner.py `_SENTINELS` 与 CLIBridge.cs 各 action 的 Debug.Log 字符串逐字比对通过（含 buildPlayer/publish/buildInstaller 的失败哨兵） |
| C# 引用 API 全部存在且签名匹配 | `ReleaseTools.BuildWithConfig(config, buildPlayer, packageName)`(:117)、`BuildImp(group, target, path, cleanOutputDirectory)`(:830)、`PublishFromExistingBuild(config, version)`(:473)、`BuildDLLCommand.BuildAndCopyDlls(target)/GenerateAllForTarget(target)/SyncAOTMetadataManifest()/CopyAOTAssembliesToAssetPath(target)/ActivateBuildTarget(target)`、`InnoSetupBuilder.BuildInstaller(config, isccPathOverride)` + `IssInstallerConfig` 6 字段 |
| 退出码与调用约定 | C# batchmode 下 `EditorApplication.Exit(0/1)`；Python 命令行 `-batchmode -quit -executeMethod TEngine.CLIBridge.Run -logFile … -tengineConfig=… -tengineResult=…`；Player 构建未加 -nographics（注释正确） |
| COMPRESS_OPTIONS / BUNDLED_COPY_OPTIONS | 与本项目 YooAsset `ECompressOption`（Uncompressed/LZMA/LZ4）、`EBundledCopyOption`（None/ClearAndCopyAll/ClearAndCopyByTags/OnlyCopyAll/OnlyCopyByTags）完全一致 |
| 版本号自动生成语义 | Python `default_package_version()` 与 C# `BuildConfig.GetDefaultPackageVersion()` 同为 `yyyy-MM-dd-当日分钟数` |

## 二、不对齐项（问题清单）

### P1-1 BuildTarget int 映射大面积错乱（config_store.py:34-44）

`BUILD_TARGET_NAMES` 用于把 `BuildPipelineSetting.asset` 里序列化的枚举 int 读成名字。**实际正确值**（已用 Unity 官方参考源码 UnityCsReference `Editor/Mono/BuildPipeline/BuildTarget.cs` 核实，且与本项目 .asset 中 `BuildTarget: 19`=Windows64 相互印证）：

| int | 正确名字 | Python 现状 |
| --- | --- | --- |
| 2 | StandaloneOSX | ❌ StandaloneWindows64 |
| 9 | iOS | ❌ StandaloneOSX |
| 13 | Android | ✓ |
| 19 | StandaloneWindows64 | ✓ |
| 20 | WebGL | ❌ 未收录（25 被误标为 WebGL） |
| 24 | StandaloneLinux64 | ❌ "Windows"（非法名，C# ParseBuildTarget 兜底回 Windows64） |
| 25 | StandaloneLinuxUniversal（已移除平台） | ❌ WebGL |
| 8/4/11 | WebPlayerStreamed/StandaloneOSXIntel/XBOX360（均已移除） | ❌ Linux64/Android/iOS |

**风险**：打包窗口把目标切到 macOS/iOS/Linux64/WebGL 后，BuildCLI「从打包工具读取配置」会静默拿错平台（iOS→构建 OSX；OSX/Linux64/WebGL→构建 Windows64）。当前 .asset 存 19（Windows64）所以未暴露。

**正确映射应为**：`{2: StandaloneOSX, 9: iOS, 13: Android, 19: StandaloneWindows64, 20: WebGL, 24: StandaloneLinux64}`。

### P1-2 枚举 int→name 映射缺失（config_store.py:336-339 `_map_enum_name`）

.asset 中枚举字段一律序列化为 int，而 `_map_enum_name` 只接受 str，int 一律落 fallback。受影响四项：`BuildPipeline / CompressOption / BuildinFileCopyOption / FileNameStyle`——从 .asset 导入表单时**永远等于默认值**。当前 .asset 值（BuildPipeline:2、CompressOption:2、BuildinFileCopyOption:1、FileNameStyle:2）恰好等于 fallback（ScriptableBuildPipeline/LZ4/ClearAndCopyAll/BundleName_HashName）所以碰巧正确；用户一旦在打包窗口改成 ArchiveFileBuildPipeline(5)/Uncompressed(0)/HashName(0) 等，BuildCLI 静默拿到旧默认值。

**修法**：补 int→name 映射表（YooAsset 枚举值见下）或直接 `Enum.GetNames` 顺序推 name。
本项目 YooAsset 枚举 int 定义（内嵌包源码核实）：
- `EBuildPipeline`：0 EditorSimulateBuildPipeline、1 LegacyBuildPipeline、2 ScriptableBuildPipeline、3 RawFileBuildPipeline、4 InstantAssetBuildPipeline、5 ArchiveFileBuildPipeline
- `ECompressOption`：0 Uncompressed、1 LZMA、2 LZ4
- `EBundledCopyOption`：0 None、1 ClearAndCopyAll、2 ClearAndCopyByTags、3 OnlyCopyAll、4 OnlyCopyByTags
- `EFileNameStyle`：0 HashName、1 BundleName、2 BundleName_HashName

### P2-1 BUILD_PIPELINES 含不存在的枚举名（unity_locator.py:34）

列表含 `"BuiltinBuildPipeline"`——YooAsset 2.x 的名字，本项目 3.x 枚举中不存在。GUI 可选中，下发后 C# `ParseEnum` TryParse 失败静默回退 ScriptableBuildPipeline。TEngine 窗口实际只提供 ScriptableBuildPipeline/RawFileBuildPipeline/ArchiveFileBuildPipeline 三项（BuildPipelineWindow.cs:2396-2398），BuildCLI 应与窗口对齐（fork 红线推荐 ArchiveFileBuildPipeline）。

### P2-2 FILE_NAME_STYLES 含不存在的枚举名（unity_locator.py:37）

列表含 `"None"`、`"BundleName_HashName2"`，本项目 `EFileNameStyle` 只有 HashName/BundleName/BundleName_HashName；两者下发同样静默回退，且缺 `"BundleName"` 选项。

## 三、附带小问题（不影响对齐结论）

- `read_last_package_versions`（config_store.py:350）`output_root.lstrip("./")` 会把 `../` 前缀也剥掉（相对父路径场景），建议改 `Path(output_root)` 规范化处理。
- `dto.dump_request` 的 if/elif/else 三分支行为等价，可简化为无差别写 `action`+`actions`。
- C# `ExecuteInstaller` 的 exe 名固定取 `GetDefaultPlayerOutputPath(StandaloneWindows64)`（productName 推导），与自定义 `PlayerOutputPath` 文件名不同时会找不到 exe；本项目两者恰好一致（hotUnitydemo.exe）。

## 四、核实方法备忘

- Unity 2022.3.62f3 BuildTarget 数值：UnityCsReference 官方仓库 `Editor/Mono/BuildPipeline/BuildTarget.cs`（本机反射加载 UnityEngine.CoreModule.dll 因 netstandard 引用程序集限制失败，改用官方源码 + 项目 .asset 实际序列化值 19 双重印证）。
- YooAsset 枚举：`Packages/com.tuyoogame.yooasset/Editor/BundleBuilder/EBuildPipeline.cs`、`ECompressOption.cs`、`EBundledCopyOption.cs`、`Runtime/ResourcePackage/EFileNameStyle.cs`。

## 五、修复记录（2026-10-17 同日完成）

- `config_store.py`：
  - `BUILD_TARGET_NAMES` 修正为官方枚举值 `{2: OSX, 9: iOS, 13: Android, 19: Win64, 20: WebGL, 24: Linux64}`；
  - 新增 `BUILD_PIPELINE_NAMES` / `COMPRESS_OPTION_NAMES` / `BUNDLED_COPY_OPTION_NAMES` / `FILE_NAME_STYLE_NAMES` 四张 int→name 映射表；
  - `_map_enum_name` 增加 `int_names` 查表与 `valid_names` 校验：int 落表、字符串原样、非法名（含 Legacy/InstantAsset 等不在下拉表的管线）兜底默认值——Legacy 管线的强制回退 SBP 与打包窗口 `BuildPipelineWindow.cs:1620-1624` 行为一致。
- `unity_locator.py`：`BUILD_PIPELINES` 移除不存在的 `BuiltinBuildPipeline`（对齐打包窗口三项 SBP/RawFile/ArchiveFile）；`FILE_NAME_STYLES` 改为 `HashName/BundleName/BundleName_HashName`（移除 None、BundleName_HashName2，补 BundleName）。
- 验证：用 `BuildCLI/.venv` Python 跑 28 项断言全部通过——真实 `.asset` 读取、3 组合成 `.asset` 变体（iOS+ArchiveFile+Uncompressed+OnlyCopyByTags+HashName、OSX+Legacy 强制回退、Linux64+RawFile）、五张下拉项与 YooAsset/Unity 枚举逐一比对、`dump_request` 本地字段隔离与 `actions` 双写。
- 未动项：`read_last_package_versions` 的 `lstrip("./")` 边缘问题与 `dto.dump_request` 分支冗余（行为正确，避免无关重构）。
