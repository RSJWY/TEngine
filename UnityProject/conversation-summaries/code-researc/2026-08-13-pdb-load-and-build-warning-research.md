# 支持加载 pdb 文件 + 打包弹窗提醒 - 调研记录

> 对应 issue: RSJWY/TEngine#3「支持加载pdb文件」
> 调研日期: 2026-08-13
> 关键词: HybridCLR、pdb 符号、Assembly.Load(dll,pdb)、真机调试、ProcedureLoadAssembly、BuildDLLCommand、CopyHotUpdateAssembliesToAssetPath、CompilePlayerScripts、DevelopmentBuild、CollectAll、EditorUtility.DisplayDialog、打包弹窗、.gitignore

---

## 一、Issue 需求

> 支持加载 pdb 文件，同时如果开启了以及没有清理 pdb 时，构建和 yooassets 打包时，弹窗提示用户。
> 参考：https://www.hybridclr.cn/docs/basic/runhotupdatecodes#加载更新assembly

两个诉求：
1. **加载 pdb**：热更 Assembly 加载时同时加载对应 pdb 符号，用于真机调试（拿到行号堆栈）。
2. **安全弹窗**：若开启了 pdb 且构建/YooAsset 打包前未清理，弹窗警告（pdb 体积大、泄露源码符号信息，不应进正式包）。

---

## 二、可行性结论（先行）

- **HybridCLR 版本**：`Library/PackageCache/com.code-philosophy.hybridclr@4df417e56a/package.json` → **v8.8.0**。
- 官方自 **v6.4.0** 起支持 `Assembly.Load(byte[] dll, byte[] pdb)` 双参数重载（见 hybridclr.cn 文档）。→ **当前版本完全支持**。
- **现状**：代码库**完全没有任何 pdb 加载/拷贝逻辑**。
  - 运行时用单参数 `Assembly.Load(byte[])`，未加载 pdb。
  - Editor 拷贝逻辑只拷 `.dll → .dll.bytes`，不拷 pdb。
  - `.gitignore` 全局忽略 `*.pdb`。
- pdb 由谁产生：HybridCLR 编译热更 dll 时走 Unity 的 `PlayerBuildInterface.CompilePlayerScripts`，当 `developmentBuild=true`（`ScriptCompilationOptions.DevelopmentBuild`）时会在 `HybridCLRData/HotUpdateDlls/<Target>` 目录同时输出 `.dll` 和 `.pdb`。是否生成 pdb **完全取决于该 development 开关**，HybridCLRSettings 本身没有 pdb 开关。

---

## 三、关键代码点（file:line）

### 3.1 运行时加载入口
文件：`Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`

| 位置 | 内容 |
|------|------|
| `:21` | `_enableAddressable = true`（硬编码，YooAsset addressable 地址=文件名） |
| `:52-109` | `LoadAssembly()` 主流程；`:79-95` 遍历 `HotUpdateAssemblies` 逐个 `LoadAssetAsync<TextAsset>` |
| `:93` | `await _resourceModule.LoadAssetAsync<TextAsset>(assetLocation, default, _assemblyPackageName)` |
| **`:196`** | **`var assembly = Assembly.Load(textAsset.bytes);`** ← 单参数，**需改为带 pdb 重载** |
| `:182-215` | `LoadAssetSuccess(TextAsset)`，加载完 `UnloadAsset` |
| `:217-246` | `LoadMetadataForAOTAssembly()`（AOT 补充元数据，与 pdb 无关） |
| `:310-314` | `RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, HomologousImageMode.SuperSet)` |

命名/路径约定（`Assets/TEngine/Runtime/Core/UpdateSetting.cs`）：
- `AssemblyTextAssetPath = "AssetRaw/DLL"`（相对 Assets/），`:155`
- `AssemblyTextAssetExtension = ".bytes"`，`:150`（实际热更文件名 `xxx.dll.bytes`）
- `AssemblyPackageName = "CodePackage"`，`:160`
- `LogicMainDllName = "GameLogic.dll"`，`:145`
- `HotUpdateAssemblies = { GameProto.dll, GameLogic.dll }`，`:137`

### 3.2 Editor 拷贝热更 dll（需新增 pdb 拷贝）
文件：`Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`

| 位置 | 内容 |
|------|------|
| `:354-372` | `CopyHotUpdateAssembliesToAssetPath(BuildTarget)` |
| **`:364-369`** | `foreach dll: File.Copy(dllPath, "{dll}.bytes")` ← **只拷 dll，需同时拷 `{dll}.pdb → {name}.pdb.bytes`** |
| `:179-189` | Obfuz 加密分支拷贝（同样只处理 .dll） |
| `:200-228` | `CopyAOTAssembliesToAssetPath`（AOT dll 拷贝） |
| `:95-141` | `SyncAOTMetadataManifest` |
| `:143-161` | `BuildAndCopyDlls` 统一入口：Sync → `CompileDllCommand.CompileDll` → `CopyAOTHotUpdateDlls` |

pdb 产物来源：`Library/PackageCache/com.code-philosophy.hybridclr@.../Editor/Commands/CompileDllCommand.cs:15-29`，`CompilePlayerScripts` + `DevelopmentBuild` 选项。

### 3.3 打包/出包入口（弹窗提醒插入点）
- GUI 窗口：`Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
  - 菜单 `TEngine/Build/打包工具窗口` `:17` `:438-445`
  - 三按钮 → `ExecuteBuild`（`:484-536`）/`ExecuteBuildPlayerOnly`
- **核心参数化入口**：`Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
  - **`BuildWithConfig` `:120-175`** ← 所有 GUI/菜单最终汇聚点；`:135-141` 内部触发 `BuildDLLCommand.BuildAndCopyDlls()`
  - 菜单一键打包 `:69-111`（都走 `BuildWithConfig`）
  - CLI 静默入口 `BuildAssetBundle` `:37-63`（走 `BuildInternal`，**不经过 BuildWithConfig**，不编译热更 dll）
  - YooAsset 管线执行 `BuildInternalWithConfig` `:181-233`（`pipeline.Run` `:232`）
- **建议插入点**：`ReleaseTools.BuildWithConfig` 开头 + `BuildPipelineWindow.ExecuteBuild`，检测 `Assets/AssetRaw/DLL` 下是否残留 `*.pdb.bytes`（或 pdb 开关开启但目录残留），`EditorUtility.DisplayDialog` 提示。
- 配置类：`Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`（可加 pdb 开关字段）

现有 DisplayDialog 参考：
- `Assets/Editor/TEngineSettingsProvider/TEngineSettingsProvider.cs:54/66`（启用/禁用 HybridCLR）
- `Assets/Editor/ToolbarExtender/EditorSceneTransitionUtility.cs:26`（`DisplayDialogComplex` 三选一）
- `Assets/TEngine/Editor/Utility/HotUpdatePlayerPrefsTool.cs:340/345/351`

### 3.4 资源收集规则（pdb 如何进包）
文件：`Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset`（`:174-196`）
- `CodePackage / DLL` 组：`CollectPath=Assets/AssetRaw/DLL`，`AddressByFileName`，`FilterRuleName=CollectAll`
- `CollectAll` 实现：`Library/PackageCache/com.tuyoogame.yooasset@2.3.19/.../DefaultFilterRule.cs:10-21`，`IsCollectAsset` 恒 `true`
- **结论**：把 pdb 以 `xxx.pdb.bytes`（TextAsset）放进 `Assets/AssetRaw/DLL`，`CollectAll` 会**自动收集打包，无需改收集规则**；运行时可用 addressable 名（如 `GameLogic.pdb`）加载。

### 3.5 .gitignore 影响
- `.gitignore:43 *.pdb`、`:51 *.pdb.meta`、`:106-107 Assets/AssetRaw/DLL/*.dll.bytes(.meta)`
- 若新增 `*.pdb.bytes` 符号资产入库，需在 `.gitignore` 放行；否则不入库/不入包（但本地 CollectAll 仍会收集）。

---

## 四、改造方案（四处联动）

1. **运行时加载 pdb**（`ProcedureLoadAssembly.cs`）
   - 在 `LoadAssembly()` 逐个 dll 时，尝试加载对应 `.pdb.bytes` 的 TextAsset（可能不存在 → 容错）。
   - `:196` 改为 `Assembly.Load(dllBytes, pdbBytes)`（pdb 存在时）/ `Assembly.Load(dllBytes)`（回退）。
   - dll 与 pdb 需配对加载（注意异步顺序与计数）。仅在存在 pdb 资产时才带 pdb 加载。

2. **Editor 拷贝 pdb**（`BuildDLLCommand.CopyHotUpdateAssembliesToAssetPath`）
   - `:364-369` 循环中，若源目录存在 `{name}.pdb`，`File.Copy` 到 `{name}.pdb.bytes`；无则跳过。
   - 需保证编译热更 dll 时用了 development（否则无 pdb 产物）。

3. **打包弹窗提醒**（`ReleaseTools.BuildWithConfig` + `BuildPipelineWindow.ExecuteBuild`）
   - 出包前检测 `Assets/AssetRaw/DLL/*.pdb.bytes` 是否存在。
   - 存在 → `DisplayDialog`：警告 pdb 会打进正式包（体积+符号泄露），提供「清理并继续 / 仍然继续 / 取消」。
   - 可加 `BuildConfig` 开关（如 `IncludePdb` / `WarnOnPdb`）。

4. **.gitignore**：视是否要把 pdb 入库决定（建议**不入库**，保持忽略；pdb 仅本地/开发包生成）。

---

## 五、待与用户确认的决策点

1. **pdb 开关放哪**：新增 `BuildConfig.IncludePdb` 字段，还是复用「development / 是否编译 development dll」？
2. **弹窗行为**：检测到残留 pdb 时，三选一（清理并继续 / 仍然继续 / 取消）还是仅二选一警告？
3. **pdb 生成时机**：只在 development 构建产 pdb？还是提供独立「生成带 pdb 的热更 dll」菜单？
4. **是否入库**：`*.pdb.bytes` 是否加入 git（默认建议不入库，保持 `.gitignore` 忽略）。
5. **运行时加载策略**：pdb 缺失时静默回退单参数加载（推荐），还是打日志提示？
6. **正式包防呆**：Release 打包是否强制拒绝带 pdb（而不仅是弹窗），做硬保护？

---

## 六、风险与红线

1. **Editor 引用热更红线（CLAUDE.md 红线6）**：拷贝/弹窗逻辑都在 Editor 程序集，不被热更代码引用；运行时加载逻辑在 `ProcedureLoadAssembly`（Assembly-CSharp 主包，非热更），合规。
2. **pdb 与 dll 版本必须匹配**：加载不配对的 pdb 会报错，拷贝与加载须严格按 dll 名配对。
3. **正式包泄露风险**：pdb 含类型/方法/行号符号，进正式包等于泄露热更代码结构 → 弹窗/硬保护的核心动机。
4. **体积**：pdb 通常与 dll 同量级甚至更大，进包显著增大热更体积。
5. **CLI 静默打包路径**（`BuildAssetBundle :37-63`）不经过 `BuildWithConfig`，若要覆盖需单独处理；但该路径本就不编译热更 dll，风险较低。
6. **异步计数**：`LoadAssembly` 现有 `_loadAssetCount` 完成判定逻辑，新增 pdb 加载不要破坏计数与 `_loadAssemblyComplete` 判定。
