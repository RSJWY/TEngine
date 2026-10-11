# HybridCLR v8.15.0 → v9.0.1 API 变动清单（含重命名）

对比源：临时克隆的 `hybridclr_unity` `v8.15.0` 与 `v9.0.1`（`Editor/` + `Runtime/` 全量）。
统计口径：`public` 类型 / 公开成员。v8 公开类型 129 个，v9 75 个（并包 + 归档工具类大幅精简）。

## 一、程序集与运行时（无破坏性变更）

| 项 | v8 | v9 | 影响 |
| --- | --- | --- | --- |
| asmdef 名 | `HybridCLR.Editor` / `HybridCLR.Runtime` | 同左 | asmdef 引用不用改 |
| Editor asmdef `rootNamespace` | （空） | `HybridCLR` | 仅影响新建文件默认命名空间 |
| Runtime 命名空间 | `HybridCLR` | `HybridCLR` | 不变 |

**`Runtime` 公开 API 逐字段一致**（业务热更运行时零改动）：
`RuntimeApi`（`LoadMetadataForAOTAssembly`、`PreJitMethod`、`PreJitClass`、`Get/SetInterpreterThreadObjectStackSize`、`Get/SetInterpreterThreadFrameStackSize`、`Get/SetRuntimeOption`）、
`RuntimeOptionId`、`LoadImageErrorCode`、`HomologousImageMode`、`ReversePInvokeWrapperGenerationAttribute`。

## 二、命名空间重构（最大破坏性变更）

`HybridCLR.Editor.*` 前缀整体取消：

| v8 | v9 |
| --- | --- |
| `HybridCLR.Editor` | `HybridCLR`（`SettingsUtil` / `HashUtil` / `MenuProvider`） |
| `HybridCLR.Editor.Settings` | `HybridCLR.Settings` |
| `HybridCLR.Editor.Commands` | `HybridCLR.Commands` |
| `HybridCLR.Editor.Installer` | `HybridCLR.Installer` |
| `HybridCLR.Editor.HotUpdate` | `HybridCLR.HotUpdate` |
| `HybridCLR.Editor.Meta`（泛型/分析） | `HybridCLR.Meta` |
| `HybridCLR.Editor.Meta`（解析器/缓存） | `HybridCLR.Mod` |
| `HybridCLR.Editor.Meta.MetaUtil` | `HybridCLR.Utils` |
| `HybridCLR.Editor.ABI` | `HybridCLR.Meta`（`MethodDesc` / `TypeInfo` / `TypeCreator`） |
| `HybridCLR.Editor.AOT` | `HybridCLR.CodeGen.AOT` |
| `HybridCLR.Editor.Il2CppDef` | `HybridCLR.CodeGen.Il2CppDef` |
| `HybridCLR.Editor.Link` | `HybridCLR.CodeGen.Link` |
| `HybridCLR.Editor.MethodBridge` | `HybridCLR.CodeGen.MethodBridge` |
| `HybridCLR.Editor.BuildProcessors` | `HybridCLR.BuildProcessors`（Hooks → `.Hooks`） |
| `HybridCLR.MonoHook` | `HybridCLR.BuildProcessors.Hooks` |
| （新） | `HybridCLR.Utils`、`HybridCLR.CppCodeGen` |

**被删除的命名空间**（连同实现）：`HybridCLR.Editor.Template`、`HybridCLR.Editor.UnityBinFileReader`、`SevenZip.*`、`UnityFS`、`HybridCLR.MonoHook`。
即 v9 移除了 7zip/LZ4 归档与 Unity 二进制（`globalgamemanagers` / `data.unity3d`）改写这一整套旧工具链。

## 三、类型重命名

| v8 | v9 |
| --- | --- |
| `HybridCLR.Editor.MethodBridge.Analyzer` | `HybridCLR.CodeGen.MethodBridge.MethodBridgeAnalyzer` |
| `HybridCLR.Editor.AOT.Analyzer` | `HybridCLR.CodeGen.AOT.AOTReferenceAnalyzer` |
| `HybridCLR.Editor.AOT.GenericReferenceWriter` | `HybridCLR.CodeGen.AOT.AOTGenericReferenceWriter` |
| `HybridCLR.Editor.ABI.MethodDesc/TypeInfo/TypeCreator` | `HybridCLR.Meta.*`（同名迁包） |
| `HybridCLR.Editor.Il2CppDef.FileRegionReplace` | 删除（`Il2CppDefGenerator` 自行处理区域替换） |

## 四、v8 有、v9 已删除的公开类型

- ABI 支撑：`PlatformABI`、`ValueTypeSizeAligmentCalculator`、`CompatibleType`、`ParamInfo`、`ParamOrReturnType`、`ReturnInfo`
- 归档/压缩/UnityBin：`UnityBinUtils`、`BundleFileReader/Writer`、`BundleFileInfo/SubFile`、`ArchiveFlags`、`BinaryReaderExtensions`、`BinaryWriterExtensions`、`FileHeader`、`StorageBlock*`、`Dataunity3dPatcher`、`ComparessHelper`、`BinTree`、`Node`、`EndianBinaryReader/Writer`、`EndianType`、`SwitchForm/SwitchResult/SwitchType`、`ExternalInfo`、`ObjectInfo/ObjectType`、`ScriptID/ScriptsData/ScriptType/Hash/MetaData/Header/State`，以及整个 `SevenZip.*`（`Coder/Decoder/Encoder/Encoder2/InBuffer/OutBuffer/InWindow/OutWindow/CRC/ICoder/…`）
- 其他：`AssemblySorter`、`BuildProcessorUtil`、`MsvcStdextWorkaround`、`PInvokeAnalyzer`、`FileRegionReplace`、`StreamExtensions`、`StreamFile`

## 五、方法 / 签名变更

| 方法 | v8 | v9 |
| --- | --- | --- |
| `MethodBridgeGeneratorCommand.GenerateMethodBridgeCppFile` | `private`，签名带 `string tempFile, string outputFile` | **`public`**，去掉 `tempFile`，只剩 `string outputFile` |
| `MethodBridgeGeneratorCommand.CleanIl2CppBuildCache()` | 有 | **已删除** |
| method bridge 编译期分析 | `CalliAnalyzer` + `PInvokeAnalyzer` + `MonoPInvokeCallbackAnalyzer` | 去掉 `PInvokeAnalyzer`，只剩 `CalliAnalyzer` + `MonoPInvokeCallbackAnalyzer` |

> `GenerateMethodBridgeCppFile` 转 public 正是 obfuz4hybridclr v3 需反射调用、v4 可直接调用的原因。

**命令类公开方法基本未变**（仅命名空间改为 `HybridCLR.Commands`）：
`PrebuildCommand.GenerateAll`、`CompileDllCommand.CompileDll(...)` 全平台重载、`AOTReferenceGeneratorCommand.{CompileAndGenerateAOTGenericReference, GenerateAOTGenericReference, GeneratedAOTGenericReferenceExcludeExistsAOTClassAndMethods}`、`LinkGeneratorCommand.GenerateLinkXml`、`StripAOTDllCommand.GenerateStripedAOTDlls`、`Il2CppDefGeneratorCommand.GenerateIl2CppDef`。

## 六、`HybridCLRSettings`（`ScriptableObject` → `ScriptableSingleton`）

| 项 | v8 | v9 |
| --- | --- | --- |
| 基类 | `ScriptableObject` | `ScriptableSingleton<HybridCLRSettings>` |
| 访问器 | `HybridCLRSettings.Instance`（自定义大写属性） | 继承的 `HybridCLRSettings.instance`（小写） |
| 加载 | `LoadOrCreate()` | 移除（ScriptableSingleton 自动） |
| 字段 | 有 `hotUpdateDllCompileOutputRootDir`、`strippedAOTDllOutputRootDir`、`patchAOTAssemblies` | **三者全部移除** |
| 新增字段 | — | `linkPreserveLevel`（枚举 `LinkPreserveLevel`：`PreserveAllOfDeclaringType` 保整类型更安全 / `PreserveExactMember` 只保引用成员更小） |
| `outputLinkFile` 默认值 | `HybridCLRGenerate/link.xml` | `HybridCLR/Generate/link.xml` |
| `outputAOTGenericReferenceFile` 默认值 | `HybridCLRGenerate/AOTGenericReferences.cs` | `HybridCLR/Generate/AOTGenericReferences.cs` |
| `Save()` | 保留 | 保留 |

> 默认路径只是**默认值**变化；`ProjectSettings/HybridCLRSettings.asset` 里已写入的值不受影响。
> `patchAOTAssemblies` 被删是本项目 `UpdateSettingEditor` 需要改动的原因。

## 七、`SettingsUtil` 成员变更

- 访问器：`HybridCLRSettings => HybridCLRSettings.Instance` → `HybridCLRSettings.instance`
- **移除**：`AOTAssemblyNames`、`HotUpdateDllsRootOutputDir`、无参 `AssembliesPostIl2CppStripDir`、`Il2CppBuildCacheDir`、`GlobalgamemanagersBinFile`、`Dataunity3dBinFile`、`ScriptingAssembliesJsonFile`
- **保留不变**：`Enable`、`ProjectDir`、`PackageName`、`HybridCLRDataPathInPackage`、`TemplatePathInPackage`、`HybridCLRDataDir`、`LocalUnityDataDir`、`LocalIl2CppDir`、`GeneratedCppDir`、`GetHotUpdateDllsOutputDirByTarget(target)`、`GetAssembliesPostIl2CppStripDir(target)`、`HotUpdateAssemblyNamesExcludePreserved/IncludePreserved`、`HotUpdateAssemblyFiles*`

## 八、对本项目的实际影响

业务/工具代码真正用到的入口全部已完成迁移：
- `HybridCLR.Editor.SettingsUtil` → `HybridCLR.SettingsUtil`
- `HybridCLR.Editor.Commands.{CompileDllCommand, PrebuildCommand}` → `HybridCLR.Commands.*`
- `HybridCLR.Editor.Settings.HybridCLRSettings` → `HybridCLR.Settings.HybridCLRSettings`（`Instance` → `instance`，去掉 `patchAOTAssemblies`）
- `HybridCLR.Editor.Installer.InstallerController` → `HybridCLR.Installer.InstallerController`
- `RuntimeApi` 无变化 → 热更运行时无需改动

结论：**v8→v9 的破坏面集中在「Editor 侧命名空间 + 少量类改名 + 设置字段删减」，Runtime 与命令 API 基本稳定**；受影响的多是自研编辑器工具与第三方扩展（如 obfuz4hybridclr）而非运行时业务代码。

## 九、本项目实际引用面（全量实测）

对 `Assets/` 全量扫描后，本项目真正消费的 HybridCLR API 只有下列几处：

### A. 用到了、且 v9 有变更（= 本次迁移的 4 个文件）

| 项目引用 | v8 → v9 变化 |
| --- | --- |
| `HybridCLR.Editor.SettingsUtil.{Enable, HotUpdateAssemblyNamesIncludePreserved, GetHotUpdateDllsOutputDirByTarget, GetAssembliesPostIl2CppStripDir, HotUpdateAssemblyFiles*}` | 命名空间 `HybridCLR.Editor` → `HybridCLR` |
| `HybridCLR.Editor.Commands.{CompileDllCommand.CompileDll, PrebuildCommand.GenerateAll}` | → `HybridCLR.Commands` |
| `HybridCLR.Editor.Settings.HybridCLRSettings.Instance.{hotUpdateAssemblies, outputAOTGenericReferenceFile}` | → `HybridCLR.Settings.HybridCLRSettings.instance` |
| `HybridCLRSettings.patchAOTAssemblies` | v9 删除 |
| `HybridCLR.Editor.Installer.InstallerController.{HasInstalledHybridCLR, InstallDefaultHybridCLR}` | → `HybridCLR.Installer`（方法本身不变） |

对应文件：`BuildDLLCommand.cs`、`UpdateSettingEditor.cs`、`ObfuzConfigWindow.cs`、`BuildPipelineWindow.cs`。

### B. Runtime 用到、但 v9 无变化（零改动）

- `HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(byte[], HomologousImageMode)`
- `HybridCLR.LoadImageErrorCode`
- 使用点：`GameScripts/Procedure/ProcedureLoadAssembly.cs`

### C. 从未引用过的 v9 变动（因此毫无影响）

- **ABI 全套**：`PlatformABI`、`CompatibleType`、`ParamInfo`、`ParamOrReturnType`、`ReturnInfo`、`ValueTypeSizeAligmentCalculator`
- **归档/压缩工具链**：`SevenZip.*`、`UnityFS`、`UnityBinUtils`、`BundleFile*`、`Endian*`、`StorageBlock*`、`Dataunity3dPatcher`、`BinTree` 等
- **被改名的分析器/写入器**：`Editor.MethodBridge.Analyzer`、`Editor.AOT.Analyzer`、`GenericReferenceWriter`（项目只消费 `AOTGenericReferences.cs` 文本，不引用这些类型）
- **包内部/第三方扩展专用**：`MethodBridgeGeneratorCommand.GenerateMethodBridgeCppFile`（v3 的 obfuz4hybridclr 用反射调用）、`CleanIl2CppBuildCache`、`PInvokeAnalyzer`
- **SettingsUtil 被删成员**：`AOTAssemblyNames`、`HotUpdateDllsRootOutputDir`、`Il2CppBuildCacheDir`、`GlobalgamemanagersBinFile`、`Dataunity3dBinFile`、`ScriptingAssembliesJsonFile`
- **HybridCLRSettings 被删成员**：`LoadOrCreate()`、`hotUpdateDllCompileOutputRootDir`、`strippedAOTDllOutputRootDir`
- **InstallerController 被删方法**：`GetCompatibleType()`、`GetCurrentUnityVersionMinCompatibleVersionStr()`

> 实测备注：全量扫描里 `BundleFile`/`LoadOrCreate` 等关键词的其余命中均为无关项（YooAsset 的 `*.bundle`、项目自有 `BuildPipelineSetting.LoadOrCreate`/`EditorScriptableSingleton.LoadOrCreate`、`ResourceModule.ClearAllBundleFiles`）。
>
> 该设计（AOT 列表走 `AOTMetadataManifest.asset` + `UpdateSetting.AOTMetaAssemblies`、`AOTGenericReferences` 走文本解析）刻意避开了包内 ABI/分析器类型，因此才把 v9 的破坏面压到最小。
