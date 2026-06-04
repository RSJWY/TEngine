# AOT 元数据打包期校验 与 打包工具按钮

日期：2026-06-04
关联文件：
- `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/AssetRaw/DLL/AOTMetadataManifest.asset`
- `README.md`（「🔄 热更新」分类追加条目）

## 背景需求

用户要求在构建 AB 包时对 `Assets/AssetRaw/DLL/AOTMetadataManifest.asset` 做校验：
1. 确保 manifest 包含 `AOTGenericReferences.PatchedAOTAssemblyList` 列出的全部程序集。
2. 拷贝 AOT DLL 时若源文件不存在，改为 error 中断而非静默跳过。

## 方案选型

- **方案 A（采纳）**：严格校验模式。缺失即抛异常中断构建，移除原「自动合并写回」逻辑。
- 校验方向选 **A-1 单向校验**：`AOTMetadataManifest ⊇ AOTGenericReferences`。
  - 缺生成项 → 抛 `BuildFailedException` 中断。
  - manifest 含额外项（手动补充）→ 仅 `LogWarning`，不中断（允许 HybridCLR 未识别的边缘泛型手动补充）。

## 实现内容（5 处改动）

### BuildDLLCommand.cs
1. **新增 `SyncAOTMetadataManifest()` 菜单**（`HybridCLR/Build/Sync AOT Metadata Manifest`，优先级 89）
   - manifest 不存在时警告并提示两种创建方式与期望路径。
   - 同步逻辑：`新列表 = AOTGenericReferences 生成列表 + manifest 原有额外项`，去重排序后写回。保留手动添加项。
2. **改造 `GetResolvedAOTMetaAssemblies()`**：移除原「LogWarning + 自动合并 + SetDirty 写回」，改为单向校验（缺失抛异常、额外项警告）。
3. **改造 `CopyAOTAssembliesToAssetPath()`**：源 DLL 不存在时由 `LogError + continue` 改为 `throw BuildFailedException`。
4. **新增 `BuildFailedException`**（文件末尾，类外，Editor 构建专用异常）。

### BuildPipelineWindow.cs
5. 「热更DLL设置」折叠区新增按钮行：`[编译并拷贝热更DLL]`（调用 `BuildAndCopyDlls()`）+ `[同步 AOT 元数据清单]`（调用 `SyncAOTMetadataManifest()`），并加 HelpBox 说明校验规则。

## 验证

`dotnet build TEngine.Editor.csproj` 通过（0 错误，8 警告）。两个改动文件同属 `TEngine.Editor.csproj`，且 `ENABLE_HYBRIDCLR` 宏已在 csproj 的 DefineConstants 中定义，核心 `#if ENABLE_HYBRIDCLR` 代码确实参与编译。运行时校验行为（构建中断、菜单同步、Inspector 显示）需在 Unity 内实测，dotnet 无法覆盖。

## 关键澄清（三份 AOT 清单的真实关系）

用户记忆中「AOTGenericReferences 和 UpdateSetting 两者合并为 AOTMetadataManifest」——经代码核对，**当前及历史均不存在此逻辑**。三者是分层关系，非并列三份：

| 清单 | 层级 | 角色 |
|------|------|------|
| `UpdateSetting.AOTMetaAssemblies` | 源头/配置层 | 经 `UpdateSettingEditor`（第 59、98 行）反向写入 `HybridCLRSettings.patchAOTAssemblies`，决定 HybridCLR 做 AOT 泛型分析的范围；同时作为构建/运行时回退源 |
| `AOTGenericReferences.PatchedAOTAssemblyList` | 产物层 | HybridCLR 分析输出，manifest 的**校验基准** |
| `AOTMetadataManifest` | 分发层 | 构建期 `GetResolvedAOTMetaAssemblies` 与运行时 `ProcedureLoadAssembly` 实际使用，可随 CodePackage 热更 |

数据流：`UpdateSetting → HybridCLRSettings.patchAOTAssemblies → (HybridCLR Generate) → AOTGenericReferences → (Sync) → AOTMetadataManifest`。

### 结论
- **UpdateSetting.AOTMetaAssemblies 不能删**：它是 HybridCLR 分析范围的配置源头（写入 `patchAOTAssemblies`），删除会断掉 manifest 的上游数据来源。其「回退源」角色虽与 manifest 重复、意义不大，但配置职责不可替代。
- **manifest「缺项」判定基准是 AOTGenericReferences**，UpdateSetting 完全不参与缺项校验，仅在 manifest 文件整个不存在/为空时作为 if/else 二选一回退。
- 区分两种「缺失」：manifest 文件不存在 → 看文件系统；manifest 内容缺项 → 拿 AOTGenericReferences 比对。

## 提交

- `b6f93edf`：feat(hybridclr) AOT 元数据打包期校验与打包工具按钮（4 files，108+/15-）。
