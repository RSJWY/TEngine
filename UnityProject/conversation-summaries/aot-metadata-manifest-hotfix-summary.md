# AOT 元数据热更清单改造总结

## 背景

`UpdateSetting.AOTMetaAssemblies` 同时承担构建期拷贝列表和运行时加载列表。由于 `UpdateSetting.asset` 由基础包内的 `GameEntry.prefab` 序列化引用，旧基础包无法通过普通热更更新这个列表，导致 `AOTGenericReferences.PatchedAOTAssemblyList` 中新增但未配置的 AOT 元数据 DLL 可能无法后续补救。

## 本次改造

- 新增 `AOTMetadataManifest` ScriptableObject 类型，用于在 Unity Inspector 中维护 AOT 补充元数据程序集列表。
- 新增 `Assets/AssetRaw/DLL/AOTMetadataManifest.asset`，使 manifest 随 `CodePackage` 被 YooAsset 收集并支持热更。
- 改造 `BuildDLLCommand`：
  - 构建期优先读取 `AOTMetadataManifest.asset`。
  - manifest 不存在或为空时回退 `UpdateSetting.AOTMetaAssemblies`。
  - 解析 `AOTGenericReferences.PatchedAOTAssemblyList` 并自动合并缺失项。
  - 使用最终列表拷贝 AOT DLL 到 `Assets/AssetRaw/DLL/*.dll.bytes`。
  - 输出详细日志：manifest 来源、生成列表、最终列表、拷贝路径、文件大小、失败原因。
- 改造 `ProcedureLoadAssembly`：
  - 运行时优先从 `CodePackage` 加载 `AOTMetadataManifest`。
  - manifest 不存在、加载为空或列表为空时回退 `UpdateSetting.AOTMetaAssemblies`。
  - 使用最终列表加载 AOT 元数据 DLL 并调用 `HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly`。
  - 输出详细日志：manifest location、回退原因、最终列表、每个 DLL 的加载结果和 HybridCLR 返回码。

## 提交

- `732525b9`：支持 AOT 元数据热更清单。

## 注意事项

- 旧基础包仍无法享受这套 manifest 机制，必须发一次包含新逻辑的基础包后，后续才能通过 `CodePackage` 热更 manifest 和新增 AOT DLL。
- `Assets/AssetRaw/DLL` 目录下现有未跟踪的各类 `.dll.bytes` 文件没有纳入本次代码提交，需要按发布策略单独决定是否提交。
- 本次仅做了静态检查和 `git diff --check`，还未在 Unity 中完成编译和实际打包验证。
