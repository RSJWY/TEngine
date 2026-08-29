# YooAsset ArchiveFileBuildPipeline 与 CodePackage 适配总结

> 日期：2026-08-29

## 背景

项目已提前升级到 YooAsset 3.0.5，而 TEngine 上游尚未完成 3.x 资源模块迁移。本次继续接入 YooAsset 新增的 `ArchiveFileBuildPipeline`，用于独立 `CodePackage`，并补齐构建、运行时加载和加密解密链路。

## 完成内容

- 新增 `RuntimePackageBuildPipeline.ArchiveFileBuildPipeline`，打包窗口支持全局和按包选择归档管线。
- `ReleaseTools` 使用 `ArchiveFileBuildParameters` 和 `ArchiveFileBuildPipeline`，构建类型设为 `EBundleType.ArchiveBundle`，文件对齐设为 4 字节。
- `CodePackage` 默认使用归档管线和 ChaCha20 加密；编辑器模拟模式对应 `VirtualArchiveBundle`。
- `ResourceModule` 在 Builtin、Sandbox、WebServer、WebNetwork 文件系统中注册 `ArchiveBundleDecryptor`。
- ArchiveBundle 只接受内存解密器，因此 FileOffset、XOR、ChaCha20 均提供对应内存解密实现。
- DLL、PDB、AOT 元数据和 Obfuz 动态密钥在归档包下改用 `RawFileObject.GetBytes()`；非归档管线继续兼容 `TextAsset`。
- ArchiveBundle 无法直接还原 `ScriptableObject`，AOT 元数据清单在归档 CodePackage 下回退使用 `UpdateSetting.AOTMetaAssemblies`。

## 问题修复

- 修复密钥配置运行时编译失败：`MarkDirty()` 调用仅在 `UNITY_EDITOR` 下执行。
- 修复 ChaCha20 变换错误：原实现对全零输出数组执行 XOR，实际只生成 keystream；现改为正确的 `input ^ keystream`。
- 旧错误算法生成的加密包不可继续使用，修复后必须重新构建并清理旧缓存。

## 验证

- `dotnet build TEngine.Runtime.csproj --no-restore -m:1` 通过。
- `dotnet build GameLogic.csproj --no-restore -m:1` 通过。
- `dotnet build TEngine.Editor.csproj --no-restore -m:1` 通过。
- `dotnet build UnityProject.sln --no-restore -m:1` 通过。
- 实际构建和运行已进入 ArchiveBundle 解密加载路径；ChaCha20 归档头错误已定位并修复。

## 后续上游对齐

TEngine 上游正式支持 YooAsset 3.x 后，应优先比较并合并上游 `ResourceModule`、初始化流程、下载/缓存 API 与构建工具实现，减少 fork 自维护代码。重点检查：

- 是否可删除本地 2.x 到 3.x 迁移桥接和枚举兼容逻辑。
- 是否可复用上游 ArchiveBundle 文件系统配置、原生文件加载 API 和多包初始化模型。
- 将 CodePackage 的 `RawFileObject` 加载封装下沉到统一资源 API，减少业务流程感知构建管线类型。
- 为 ArchiveBundle 增加构建后自动校验与加密往返测试，覆盖归档头、DLL/PDB、动态密钥和各运行模式。
- 评估大归档包整包内存解密成本，必要时拆分 CodePackage 分组或采用更适合归档格式的加密策略。

## 关键文件

- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Services.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/Crypto/ChaCha20Util.cs`
- `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`
- `Assets/GameScripts/ObfuzRuntimeInitializer.cs`
