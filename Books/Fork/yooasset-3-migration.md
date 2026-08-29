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

## 关键文件

- `UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs`
- `UnityProject/Assets/Editor/ToolbarExtender/UnityToolbarExtenderRight/EditorPlayMode.cs`
- `UnityProject/Assets/Editor/ToolbarExtender/Unity6000_OR_New/MainToolbarExtender.cs`
- `UnityProject/Assets/TEngine/Editor/Inspector/ResourceModuleDriverInspector.cs`

## 相关记录

- [YooAsset 3.0.5 迁移会话总结](../../UnityProject/conversation-summaries/2026-08-29-yooasset-3-migration-summary.md)
- [运行模式差异与修复会话总结](../../UnityProject/conversation-summaries/2026-08-29-yooasset-2-vs-3-and-playmode-fix-summary.md)
