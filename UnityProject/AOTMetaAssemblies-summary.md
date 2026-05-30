# TEngine AOTMetaAssemblies 来源确认

## 结论

`UpdateSetting.asset` 中的 `AOTMetaAssemblies` 不是由 TEngine 脚本自动填充的，目前主要靠手动维护；新建 `UpdateSetting` 资产时会使用 `UpdateSetting.cs` 中声明的默认值。

## 已确认链路

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs` 定义了 `AOTMetaAssemblies` 字段，并标注为 `Need manual setting!`。
- `Assets/TEngine/Settings/UpdateSetting.asset` 保存了当前项目实际使用的序列化列表。
- `Assets/TEngine/Editor/Utility/UpdateSettingEditor.cs` 只做 `UpdateSetting.AOTMetaAssemblies` 到 `HybridCLRSettings.Instance.patchAOTAssemblies` 的同步。
- `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs` 打包复制 AOT DLL 时遍历 `TEngine.Settings.UpdateSetting.AOTMetaAssemblies`。
- `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs` 运行时加载补充元数据时遍历 `_setting.AOTMetaAssemblies`，并调用 `HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly`。

## 容易混淆的点

HybridCLR 生成的 `Assets/HybridCLRGenerate/AOTGenericReferences.cs` 中包含 `PatchedAOTAssemblyList`，但当前仓库没有发现脚本读取该列表并反向写回 `UpdateSetting.asset`。

因此，`AOTGenericReferences.cs` 可以作为补充元数据配置参考，但不会自动更新 TEngine 的 `AOTMetaAssemblies` 配置。
