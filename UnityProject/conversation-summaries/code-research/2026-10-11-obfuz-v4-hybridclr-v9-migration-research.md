# Obfuz v4 + HybridCLR v9 升级影响研究（obfuz_v3-hybridclr_v8 分界）

## 背景

- 目的：`main` 追随最新（HybridCLR v9 + Obfuz v4），旧组合（HybridCLR v8 + Obfuz v3）用分支 `obfuz_v3-hybridclr_v8` 保留。
- 触发：Obfuz v4 移除了多态 DLL、禁用了代码水印；obfuz4hybridclr v4 仅支持 HybridCLR v9。
- 参考：<https://www.obfuz.com/docs/help/upgrade-notes>
- 实际对比源（临时克隆）：
  - `obfuz` v3.1.0 → v4.0.0
  - `obfuz4hybridclr` v3.0.1 → v4.0.0（main）
  - `hybridclr_unity` v8.15.0 → v9.0.1

## 一、Obfuz v3.1.0 → v4.0.0 改了什么

| 改动 | 具体表现 | 本项目影响 |
| --- | --- | --- |
| 移除多态 DLL | 删除 `Editor/Settings/PolymorphicDllSettings.cs`；`ObfuzSettings.polymorphicDllSettings` 字段整个删除（`Enable` / `Code Generation Secret Key` / `Disable Load Standard Dll` 选项全没了） | 所有访问 `ObfuzSettings.Instance.polymorphicDllSettings` 的代码编译失败 |
| 禁用代码水印 WaterMark | `ObfuscationPassType.WaterMark` 枚举值仍保留（0x200000），`WatermarkPass.Process()` 变成空实现；`WatermarkSettings` 字段保留但无效 | 引用还能编译，但开启不再生效 |
| 执行栈混淆失效（旧闻） | `EvalStackObfus` 自 v3.0.0 起已是空实现，v4 文档补充说明 | 同上，仅无效 |
| 内部重构 | `DefaultMetadataImporter`、`Obfuscator`、`ObfuscatorBuilder`、`ConstEncryptPass`、`DefaultConstEncryptor`、`SymbolObfus` 系列、`ObfuzSettingsProvider`、`PlatformUtil`、`Runtime/EncryptionService`、`EncryptorBase`、`IEncryptor`、`Plugins/dnlib.dll` 有改动 | 未见对外 API 破坏，属内部演进；dnlib 仍是 Obfuz 定制版（需保留单一副本） |

要点：**唯一对外破坏性变化就是 `polymorphicDllSettings` 被删**，其余是「保留字段但静默失效」。

## 二、HybridCLR v8.15.0 → v9.0.1 改了什么

官方改动日志：
- v9.0.0 移除对 Unity 2019/2020/2021 的支持；移除对 6000.5.x（非 LTS）支持；移除热更代码中定义 `extern` 函数的能力。
- **8.15.0 是最后一个支持 Unity 2019/2020/2021 的版本。**（本项目 Unity 2022.3.62f3，落在 v9 支持范围内）

代码层面（影响本项目引用的 API）：
- **命名空间整体重构**：`HybridCLR.Editor.*` 系列命名空间被取消，改为：
  - `HybridCLR`（`SettingsUtil` 等，原 `Editor/SettingsUtil.cs` → 现 `Editor/Utils/SettingsUtil.cs`，`namespace HybridCLR`）
  - `HybridCLR.Commands`（`CompileDllCommand` 等）
  - `HybridCLR.Settings`（`HybridCLRSettings`、`HybridCLRSettingProvider`）
  - `HybridCLR.Meta`、`HybridCLR.Mod`、`HybridCLR.Utils`、`HybridCLR.Installer`、`HybridCLR.CodeGen.*`
- 程序集名仍是 `HybridCLR.Editor`（asmdef 名未变，`rootNamespace` 改为 `HybridCLR`），所以 asmdef 引用不受影响，但 `using HybridCLR.Editor.Xxx` 全部编译失败。
- 类型重命名：`HybridCLR.Editor.MethodBridge.Analyzer` → `HybridCLR.CodeGen.MethodBridge.MethodBridgeAnalyzer`；`HybridCLR.Editor.AOT.Analyzer` → `HybridCLR.CodeGen.AOT.AOTReferenceAnalyzer`；`GenericReferenceWriter` → `AOTGenericReferenceWriter` 等。
- `MethodBridgeGeneratorCommand.GenerateMethodBridgeCppFile(...)` 改为 public（原通过反射调用私有方法）。
- 菜单重构为 `HybridCLR.MenuProvider`（`HybridCLR/Installer...`、`HybridCLR/Settings...` 等）。
- 移除了 2019/2020/2021 的 `Data~/ModifiedUnityAssemblies`、部分 Templates、`Editor/3rds/7zip`、`UnityFS`、`LZ4.dll` 等。

## 三、obfuz4hybridclr v3.0.1 → v4.0.0 改了什么

| 项 | v3.0.1 | v4.0.0 |
| --- | --- | --- |
| 命名空间 | `HybridCLR.Editor.*` | `HybridCLR.*`（`HybridCLR`、`HybridCLR.CodeGen.AOT`、`HybridCLR.CodeGen.MethodBridge`、`HybridCLR.Commands`、`HybridCLR.Installer`、`HybridCLR.Meta`、`HybridCLR.Mod`、`HybridCLR.Utils`） |
| 多态 DLL | `Editor/Polymorphic/PolymorphicCodeGenerator.cs`、`Templates~/Polymorphic*.tpl`、`ObfuscateUtil.GeneratePolymorphicDll`、`GeneratePolymorphicCodes`、菜单 `HybridCLR/ObfuzExtension/GeneratePolymorphicCodes` | 全部删除 |
| method bridge | 反射调 `MethodBridgeGeneratorCommand` 私有方法 + `PInvokeAnalyzer` | 改为 `MethodBridgeGeneratorCommand.GenerateMethodBridgeCppFile(...)` 公开 API，去掉 PInvoke 分析 |
| min HybridCLR | v8 | v9.0.0+ |

版本号严格耦合：**obfuz4hybridclr 大版本必须与 obfuz 大版本一致**（v4.x 配 obfuz v4.x），且 obfuz4hybridclr v4.x 配 HybridCLR v9.0.0+。因此「HybridCLR 升 v9」必然要求「obfuz + obfuz4hybridclr 升 v4」，三者需一起动。

## 四、本项目受影响的代码（Assets 侧）

| 文件 | 问题 | 迁移方向 |
| --- | --- | --- |
| `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs` | `using HybridCLR.Editor; .Commands; .Settings; .Installer;`；`HybridCLR.Editor.SettingsUtil`；`ObfuzSettings.Instance.polymorphicDllSettings.enable`；`ObfuscateUtil.GeneratePolymorphicDll`；`IsPolymorphicInjected`/`CleanupPolymorphicInjection` | `using HybridCLR; .Commands; .Settings; .Installer`；`HybridCLR.SettingsUtil`；删多态分支与清理逻辑 |
| `Assets/TEngine/Editor/Obfuz/ObfuzConfigWindow.cs` | 大量 `S.polymorphicDllSettings.*`、`PolymorphicEnable/PolymorphicKey/PolymorphicDisableStandardDll`、`RandomizePolymorphicKey`、`ShouldWarnPolymorphic*`；`HybridCLR.Editor.SettingsUtil.HotUpdateAssemblyNamesIncludePreserved` | 删除多态相关 UI 面板；`HybridCLR.SettingsUtil`；水印开关保留但提示「v4 起无效」 |
| `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs` | `polymorphicDllSettings.enable` 判断、`IsPolymorphicInjectedButDisabled`、`CleanupPolymorphicAndGenerateAll` | 删除多态注入相关 UI 与提示 |
| `Assets/TEngine/Editor/Utility/UpdateSettingEditor.cs` | `using HybridCLR.Editor.Settings;` | `using HybridCLR.Settings;` |
| `Assets/TEngine/Editor/TEngine.Editor.asmdef` | 无直接改动（versionDefine `OBFUZ_INSTALLED` 仍有效） | 无需改 |

`Packages/com.code-philosophy.*` 三个包均为 **git 跟踪的嵌入包**（embedded），同步脚本会直接改写被跟踪文件，可用 git 回退。

## 五、实测结果（HybridCLR 已升 v9.0.1 后）

已执行 `Packages/sync-hybridclr-local.sh v9.0.1`，`com.code-philosophy.hybridclr` `package.json` version 已为 `9.0.1`；Unity 2022.3.62f3 侧强制 `AssetDatabase.Refresh` 后，控制台错误 **17 条，全部来自 `com.code-philosophy.obfuz4hybridclr@3.0.1`**，形如：

```
Packages\com.code-philosophy.obfuz4hybridclr\Editor\PrebuildCommandExt.cs(21,17): error CS0234:
  The type or namespace name 'Editor' does not exist in the namespace 'HybridCLR'
```

结论：HybridCLR v9 自身编译通过，阻塞点只在 **obfuz4hybridclr v3.0.1 与 HybridCLR v9 不兼容**。

补充：`TEngine.Editor` 本轮未报错，原因是它 `#if OBFUZ_INSTALLED` 引用了 `Obfuz4HybridCLR` 程序集，而该程序集编译失败导致 `TEngine.Editor` 被跳过编译。**把 obfuz4hybridclr 升到 v4 后，`TEngine.Editor` 自身的新命名空间/多态引用错误才会浮现**，需要一并迁移。

## 六、执行结果（已完成）

三包同步：`hybridclr 9.0.1` + `obfuz 4.0.0` + `obfuz4hybridclr 4.0.0`（注意 obfuz4hybridclr 无 v4.0.0 tag，用 `main` 同步）。

代码迁移（4 个文件）：
- `BuildDLLCommand.cs`：`HybridCLR.Editor.*` → `HybridCLR.*`；删除 `Obfuz/多态注入清理` region、`GenerateAllForTarget` 的 `cleanupPolymorphicInjection` 参数、`CopyAOTHotUpdateDlls` 的多态分支、`GetPolymorphicHotUpdateAssemblyOutputPath` / `GeneratePolymorphicHotUpdateDll`；`HybridCLRSettings.Instance` → `HybridCLRSettings.instance`。
- `ObfuzConfigWindow.cs`：删除多态 DLL 面板（Enable / 密钥 / DisableLoadStandardDll / 注入提示 / 清理按钮）与 `RandomizePolymorphicKey`；`HybridCLR.Editor.SettingsUtil` → `HybridCLR.SettingsUtil`；水印开关与设置保留并标注「v4 起无效」。
- `BuildPipelineWindow.cs`：删除 Obfuz 多态注入区按钮与 `PolymorphicInjectionHint` 提示块。
- `UpdateSettingEditor.cs`：`using HybridCLR.Settings`；`HybridCLRSettings.instance`；删除 `patchAOTAssemblies` 赋值（v9 已移除该字段，AOT 列表以 `UpdateSetting.AOTMetaAssemblies` + `AOTMetadataManifest` 为准）。

残留清理：删除 `Library/Obfuz/StandaloneWindows64/PolymorphicHotUpdateAssemblies`（旧多态热更 DLL）；`HybridCLRData` 下未发现 `PolymorphicRawImage.*`。

**Unity MCP 复检：0 error / 0 warning**（此前 17 条 CS0234 已全部消除）。

## 七、dnlib 共存实验（结论：可以共存）

- Obfuz v3 的 `Plugins/dnlib.dll` 是 1,262,592 字节的自定义超集；**Obfuz v4 回归上游标准 dnlib（1,175,040 字节，md5 `6bc570a7...`），与 HybridCLR v9 的 `dnlib.dll` 逐字节一致**。
- 实验：恢复 HybridCLR v9 的 `Plugins/dnlib.dll`（不再移除），两包各持一份同名 dnlib.dll 同时存在。Unity 2022.3.62f3 导入 + 编译通过，**未出现 “Multiple precompiled assemblies with the same name dnlib.dll” 类报错**。
- 据此更新 `sync-hybridclr-local.sh`：移除 dnlib 的步骤改为保留；同步修正 `sync-obfuz-local.sh` 头部注释。

## 八、剩余待办

- **重装 HybridCLR il2cpp**：`HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp/libil2cpp/hybridclr/generated/libil2cpp-version.txt` 仍为 `8.15.0`，需走 `HybridCLR/Installer...` 将本地 il2cpp 升到与包匹配的 `v2022.3.x-9.0.1`。
- 真实构建验证（Player + YooAsset 资源 + 混淆产物链）。
