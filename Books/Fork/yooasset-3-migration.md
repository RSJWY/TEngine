# YooAsset 3.0.5 迁移

本 fork 将 YooAsset 从 2.3.x 升级到 3.0.5，并关闭 `YOOASSET_LEGACY_API`，让运行时代码和编辑器工具直接使用 3.x 原生接口。

## 迁移范围

- 初始化改用 `InitializePackageAsync` 和 `InitializePackageOptions`。
- 远端服务改用 `IRemoteService`，文件系统参数按模式显式创建。
- 加解密改用 `IBundleEncryptor`、`IBundleStreamDecryptor`、`IBundleOffsetDecryptor` 和 `IBundleMemoryDecryptor`。
- 版本、清单、下载和缓存清理改用 `RequestPackageVersionOptions`、`LoadPackageManifestOptions`、`ResourceDownloaderOptions` 和 `ClearCacheOptions`。
- 资源包、场景和 Collector 查询改用 `ResourcePackage` 及 3.x 类型命名。
- 编辑器构建参数切换到 YooAsset 3.x 的 Bundle Builder API，移除旧 BBP 配置引用。

## 编辑器运行模式

YooAsset 3.x 在 `EPlayMode` 中新增 `None = 0`，因此枚举值变为：

| 模式 | 3.x 值 | 说明 |
| --- | ---: | --- |
| `None` | 0 | 未指定 |
| `EditorSimulateMode` | 1 | 使用编辑器模拟清单和源资源 |
| `OfflinePlayMode` | 2 | 从 `StreamingAssets` 的已构建资源包加载 |
| `HostPlayMode` | 3 | 使用内置资源并从远端下载 |
| `WebPlayMode` | 4 | WebGL/小游戏资源加载 |

工具栏和 Inspector 必须保存实际枚举值，不能把下拉框索引直接写入 `EditorPrefs["EditorPlayMode"]`。本 fork 已增加旧索引的一次性自动迁移。

### OfflinePlayMode 要求

`OfflinePlayMode` 不使用编辑器模拟资源，也不访问远端服务器。运行前需要：

1. 使用 YooAsset 构建 `DefaultPackage`。
2. 确认 Bundle、清单和内置资源已复制到 `StreamingAssets`。
3. 在工具栏选择 `OfflinePlayMode` 后重新进入 Play Mode。

## 兼容性边界

- 不启用 `YOOASSET_LEGACY_API`，旧版兼容包装器不作为项目 API 使用。
- 业务层部分 TEngine 方法名保留不变，但内部实现和 YooAsset 返回类型已切换到 3.x。
- 远端下载、加密 Bundle、WebGL 和小游戏目标仍需在对应环境中单独验证。

## ArchiveFileBuildPipeline 与 CodePackage

### 背景

YooAsset 3.0.5 新增 `ArchiveFileBuildPipeline`，可把同一 BundleName 下的多个原始文件合并为 `ArchiveBundle`。`CodePackage` 主要包含 DLL、PDB、AOT 元数据和 Obfuz 动态密钥，适合通过归档减少零散文件，并保持独立发布和加密。

### 改动摘要

- 新增 `RuntimePackageBuildPipeline.ArchiveFileBuildPipeline`，打包窗口支持全局和按包选择。
- `CodePackage` 默认使用归档管线，构建类型为 `EBundleType.ArchiveBundle`，编辑器模拟类型为 `VirtualArchiveBundle`。
- `ReleaseTools` 使用 `ArchiveFileBuildParameters`，默认 `FileAlignment = 4`。
- Builtin、Sandbox、WebServer、WebNetwork 文件系统均注册 `ArchiveBundleDecryptor`。
- ArchiveBundle 只支持 `IBundleMemoryDecryptor`；FileOffset、XOR、ChaCha20 均补充归档内存解密器。
- DLL、PDB、AOT 元数据和 Obfuz 动态密钥在归档包下使用 `RawFileObject.GetBytes()`；SBP 等旧管线继续兼容 `TextAsset`。
- ArchiveBundle 不直接还原 `ScriptableObject`，因此归档 CodePackage 下的 AOT 元数据列表使用 `UpdateSetting.AOTMetaAssemblies`。

### 加密修复

- `CryptoKeyConfig.MarkDirty()` 仅存在于 `UNITY_EDITOR`，密钥重新生成按钮的调用同步增加条件编译，避免 Player 构建失败。
- 修复 ChaCha20 变换实现：输出必须是 `input ^ keystream`，不能只对全零输出数组执行 XOR。
- 旧错误算法生成的加密资源包不可复用，修复后必须重新构建 CodePackage，并清理 StreamingAssets、沙盒缓存或远端旧版本。

### 后续上游对齐

TEngine 上游正式支持 YooAsset 3.x 后，继续进行以下优化：

- 对比并优先采用上游 `ResourceModule`、初始化、下载、缓存与构建工具实现，删除重复迁移代码。
- 将 `RawFileObject`/`TextAsset` 差异封装到统一的二进制资源加载 API，避免热更新流程判断构建管线。
- 增加 ArchiveBundle 构建后校验和加密往返测试，覆盖归档头、DLL/PDB、动态密钥及各运行模式。
- 评估归档整包内存解密峰值，必要时拆分 CodePackage 分组或调整加密策略。

## 关键文件

- `UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs`
- `UnityProject/Assets/Editor/ToolbarExtender/UnityToolbarExtenderRight/EditorPlayMode.cs`
- `UnityProject/Assets/Editor/ToolbarExtender/Unity6000_OR_New/MainToolbarExtender.cs`
- `UnityProject/Assets/TEngine/Editor/Inspector/ResourceModuleDriverInspector.cs`
- `UnityProject/Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `UnityProject/Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Services.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/Crypto/ChaCha20Util.cs`
- `UnityProject/Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`
- `UnityProject/Assets/GameScripts/ObfuzRuntimeInitializer.cs`

## 相关记录

- [YooAsset 3.0.5 迁移会话总结](../../UnityProject/conversation-summaries/2026-08-29-yooasset-3-migration-summary.md)
- [运行模式差异与修复会话总结](../../UnityProject/conversation-summaries/2026-08-29-yooasset-2-vs-3-and-playmode-fix-summary.md)
- [ArchiveFileBuildPipeline 与 CodePackage 适配总结](../../UnityProject/conversation-summaries/2026-08-29-yooasset-archive-code-package-summary.md)
