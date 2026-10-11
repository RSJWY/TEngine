# Obfuz 介入 HybridCLR 热更流程的时机与冗余分析

研究动机：整理打包工具「热更 DLL / AOT」按钮时，需要确认 Obfuz 到底在热更流程的哪一步介入，以便判断按钮调用链是否存在非必要重复，并兼顾工程的 PDB 策略。

## 一、官方文档结论（obfuz v4.x 与 HybridCLR 协同）

来源：<https://www.obfuz.com/docs/manual/hybridclr/work-with-hybridclr>

官方指出 HybridCLR 工作流未考虑混淆，直接混用会有三个问题：

1. **dnlib 冲突**（旧问题）：两包都带 dnlib.dll。v4 起 Obfuz 回归上游标准 dnlib，与 HybridCLR 逐字节一致，已可共存，无需再删。
2. **被混淆的 AOT 程序集裁剪问题**：HybridCLR 默认 `Generate/All` 基于**未混淆**程序集生成 link.xml，保留的是混淆前名字，导致混淆后的 AOT 类型/函数被误裁剪。
3. **热更 dll 未被混淆**：`HybridCLR/CompileDll/XXX` 产出的热更 dll 是原始代码，必须**额外混淆**。

官方解决办法（obfuz4hybridclr）：

- 把 `HybridCLR/Generate/All` → 替换为 `HybridCLR/ObfuzExtension/GenerateAll`（代码里 `PrebuildCommandExt.GenerateAll()`）。
- 把 `HybridCLR/CompileDll/XXX` → 替换为 `HybridCLR/ObfuzExtension/CompileAndObfuscateDll`（代码里 `PrebuildCommandExt.CompileAndObfuscateDll()`）。
- `obfuz4hybridclr v4` 要求 HybridCLR `v9.0.0+`。

## 二、Obfuz 介入热更/打包的两处入口（源码）

### 入口 A：Player 构建期混淆 AOT 程序集（Obfuz 本体）

`com.code-philosophy.obfuz/Editor/Unity/ObfuscationProcess.cs`

```csharp
public class ObfuscationProcess : IPostBuildPlayerScriptDLLs   // Unity 构建回调
{
    public void OnPostBuildPlayerScriptDLLs(BuildReport report) => RunObfuscate(...);
}
```

- 由 Unity 在 BuildPlayer 的 `PostBuildPlayerScriptDLLs` 阶段自动调用；
- 仅当 `ObfuzSettings.buildPipelineSettings.enable == true` 才执行；
- 混淆对象是**主包 AOT 程序集**（staging area 里的 managed dll），混淆后回写 staging 目录；
- 与 HybridCLR 无关，是 Obfuz 本体行为。

### 入口 B：热更 DLL 混淆（obfuz4hybridclr）

`com.code-philosophy.obfuz4hybridclr/Editor/ObfuscateUtil.cs`

```csharp
public static void ObfuscateHotUpdateAssemblies(BuildTarget target, string outputDir)
{
    string hotUpdateDllPath = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
    ObfuscationProcess.ValidateReferences(hotUpdateDllPath, ...);   // 校验引用跟随声明
    Obfuscate(target, assemblySearchPaths, outputDir);              // 复用 Obfuz 的 Obfuscator
    foreach 非混淆程序集: 拷贝到 outputDir                          // 产物目录同时含混淆/未混淆
}
```

- 产物目录：`{ObfuzRootDir}/{target}/ObfuscatedHotUpdateAssemblies`（本项目 `Library/Obfuz/<target>/...`）；
- 这是**独立于 Player 构建**的一步，需显式调用；
- `ObfuscateUtil.Obfuscate` 内部 `ObfuscatorBuilder.FromObfuzSettings(...)` + `Obfuscator.Run()`，**不检查 `buildPipelineSettings.enable`**（入口 B 不受 enable 开关控制，由调用方是否走 `ENABLE_OBFUZ` 链路决定）。

## 三、obfuz4hybridclr 的 `GenerateAll` 编排（介入点）

`com.code-philosophy.obfuz4hybridclr/Editor/PrebuildCommandExt.cs`

```
CompileDll(target)
Il2CppDefGeneratorCommand.GenerateIl2CppDef()
LinkGeneratorCommand.GenerateLinkXml(target)       // 注：仍用标准 LinkXml
StripAOTDllCommand.GenerateStripedAOTDlls(target)
ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath)   // ← 入口 B
GenerateMethodBridgeAndReversePInvokeWrapper(target, obfuscatedHotUpdateDllPath) // ← 基于混淆后 dll 分析
GenerateAOTGenericReference(target, obfuscatedHotUpdateDllPath)                  // ← 基于混淆后 dll 分析
```

关键点：**混淆必须夹在 `StripAOTDlls` 之后、`MethodBridge`/`AOTGenericReference` 之前**，因为后两者要读**混淆后**的热更 dll 才能得到正确的桥接签名与 AOT 泛型引用列表（含 `PatchedAOTAssemblyList`）。

## 四、本项目原有流程的冗余（已修）

按钮「编译并拷贝热更DLL」= `BuildAndCopyDlls(target)`：

```
ActivateBuildTarget(target)
SyncAOTMetadataManifest()
CompileDll(target, WillGeneratePdb)          ← 编译①
CopyAOTHotUpdateDlls(target)
    CopyAOTAssembliesToAssetPath(target)     → AOT dll .bytes; Refresh
    CopyHotUpdateAssembliesToAssetPath(target) → 原始热更 dll .bytes; pdb 处理; Refresh
    #if OBFUZ:
        CompileDll(target, WillGeneratePdb)  ← 编译②  ❌ 与编译① 完全相同（冗余）
        ObfuscateHotUpdateAssemblies(...)    → 入口 B
        foreach Include: 再拷一遍 dll .bytes  ❌ 覆盖了上面 CopyHotUpdate 的拷贝（冗余）
```

发现三处冗余：

| # | 冗余 | 性质 | 处理 |
| --- | --- | --- | --- |
| 1 | 同一 target/development 下 `CompileDll` 连编两次 | 昂贵、纯冗余 | **已删第二次编译** |
| 2 | Obfuz 开启时热更 dll 被拷两次（原始 + 混淆/重复） | 纯冗余（后者覆盖前者） | **已合并为单次混淆链路** |
| 3 | `AssetDatabase.Refresh()` 调两次 | 轻微 | 保留（两个 Copy 各自会被独立复用，合并会破坏独立性） |

`CopyAOTHotUpdateDlls` 全项目**只被 `BuildAndCopyDlls` 调用**，`CompileDll` 已由调用方先行完成，故第二次编译可安全删除。

### 修复后的结构

```csharp
public static void CopyAOTHotUpdateDlls(BuildTarget target)
{
    CopyAOTAssembliesToAssetPath(target);
#if ENABLE_HYBRIDCLR && ENABLE_OBFUZ
    CopyObfuscatedHotUpdateDlls(target);      // 混淆 + 单次落地 + pdb 清理
#elif ENABLE_HYBRIDCLR
    CopyHotUpdateAssembliesToAssetPath(target);
#endif
}
```

`CopyObfuscatedHotUpdateDlls` 内部：`ObfuscateHotUpdateAssemblies` → 拷 `Include` 集合（混淆程序集取混淆目录，其余取原始目录）→ `CopyPdbToAssetPath` → `Refresh`。最终产物集合与原实现**逐字节等价**（原实现被覆盖后的结果即此），因此是行为保持的精简。

## 五、PDB 策略的耦合

工程约定 **pdb 与 Obfuz 互斥**（`BuildDLLCommand.SetObfuzSafeConfirm` / `SetPdbEnabledConfirm` 弹框强制）：

| 状态 | 热更 dll | pdb |
| --- | --- | --- |
| dev + pdb 开 | 原始 dll | `CopyPdbToAssetPath` 拷贝 `.pdb.bytes` |
| release / Obfuz 开 | 混淆或原始 dll | `CleanStalePdbFiles` 清理残留 |

因此两条路径天然不重叠：
- Obfuz 关闭 → `CopyHotUpdateAssembliesToAssetPath`（原始 dll + pdb 拷贝/清理）；
- Obfuz 开启 → `CopyObfuscatedHotUpdateDlls`（混淆 dll + pdb 清理，因 Obfuz 开时 pdb 必关）。

修复后每条路径各自只在需要时执行一次拷贝与一次 pdb 处理，无重复。

## 六、其余按钮的调用链（确认无冗余）

| 按钮 | 调用 | 结论 |
| --- | --- | --- |
| GenerateAll | `PrebuildCommandExt.GenerateAll`（Obfuz）/ `PrebuildCommand.GenerateAll` | `CompileDll` 仅 1 次；内部 `LinkXml`/`AOTGenericReference` 用 target 重载不再重编 |
| ① 生成 AOT DLL | `StripAOTDllCommand.GenerateStripedAOTDlls` | 单次 |
| ② 生成 AOT 列表 | `CompileDll` + `ObfuscateHotUpdateAssemblies` + `PrebuildCommandExt.GenerateAOTGenericReference` | 单次，且基于混淆后 dll |
| ③ 同步清单 | `SyncAOTMetadataManifest` | 单次 |
| ④ 拷贝 AOT DLL | `CopyAOTAssembliesToAssetPath` | 单次 |

## 七、相关官方文档

- 与 HybridCLR 协同工作：<https://www.obfuz.com/docs/manual/hybridclr/work-with-hybridclr>
- 构建过程中执行混淆：<https://www.obfuz.com/docs/manual/build-pipeline>
- 单独执行混淆：<https://www.obfuz.com/docs/manual/run-obfuscation-standalonely>
