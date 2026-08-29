# YooAsset 3.0.5 无兼容层迁移会话总结

> 日期：2026-08-29
> 背景：项目包从 YooAsset 2.3.19 升级到 3.0.5，但业务和框架代码仍使用 2.x API。按官方 MigrationGuide 完成迁移，明确不启用 `YOOASSET_LEGACY_API`。

## 结论

- 运行时资源模块、更新下载流程、场景加载、编辑器构建工具、Collector 和报告分析工具已迁移到 YooAsset 3.x 原生 API。
- 全仓扫描未发现 `YOOASSET_LEGACY_API` 或本次迁移涉及的旧 API 残留。
- `UnityProject.sln` 编译通过，Unity Editor 已完成脚本域重载。

## 主要迁移内容

### 资源包初始化与文件系统

- 使用 `InitializePackageAsync` 和 3.x 的 `EditorSimulateModeOptions`、`OfflinePlayModeOptions`、`HostPlayModeOptions`、`WebPlayModeOptions`。
- 通过 `YooAssets.TryGetPackage` 获取包，使用包实例执行资源查询和加载，不再依赖默认包静态代理。
- 文件系统参数改为 `CreateDefaultBuiltinFileSystemParameters`、`CreateDefaultSandboxFileSystemParameters`、`CreateDefaultWebServerFileSystemParameters` 和 `CreateDefaultWebNetworkFileSystemParameters`。
- 远端服务实现改为 `IRemoteService`，返回 `IReadOnlyList<string>`。

### 加解密

- 迁移到 `IBundleEncryptor`、`IBundleStreamDecryptor`、`IBundleOffsetDecryptor` 和 `IBundleMemoryDecryptor`。
- 保留桌面端流式/偏移解密，并补充 WebGL 所需的内存解密接口。

### 更新、下载与缓存

- 使用 `RequestPackageVersionOptions`、`LoadPackageManifestOptions` 和 `ResourceDownloaderOptions`。
- 下载事件改为 3.x 的 `StartDownload`、`DownloadProgressChanged`、`DownloadError` 及对应事件参数，成功状态使用 `Succeeded`。
- 缓存清理改为 `ClearCacheOptions`，对外保留 TEngine 原有方法名以减少调用方改动。

### 场景与资源扩展

- 场景通过 `ResourcePackage.LoadSceneAsync` 加载和卸载。
- 挂起加载时按 3.x 语义设置 `allowSceneActivation = !suspendLoad`，恢复时调用 `AllowSceneActivation`。
- 子 Sprite 查询和加载改用 `ResourcePackage` API，并检查 `AssetInfo.Error`。

### 编辑器工具

- 构建参数、拷贝选项、加密器和 Bundle 类型切换到 3.x 命名空间与类型。
- Collector 使用 `BundleCollectorSetting`、`BundleCollectorPackage`、`BundleCollectorGroup`、`BundleCollector`。
- 场景打包规则使用 `IBundlePackRule`、`BundlePackRuleData`、`BundlePackRuleResult`。
- 资源使用分析改用 `ReportAssetInfo.AssetGuid`。
- 已移除旧的内置构建流水线引用；不支持的旧配置值归一到 Scriptable 构建流水线。

## 关键决策

| 决策 | 原因 |
| --- | --- |
| 不使用 `YOOASSET_LEGACY_API` | 让编译器直接暴露并清理所有 2.x 依赖，避免兼容层长期存在 |
| 保留部分 TEngine 公共方法名 | 降低业务调用方改动范围，方法内部和返回类型已切换到 3.x |
| 同时实现流、偏移和内存解密接口 | 覆盖桌面平台与 WebGL 的不同加载路径 |
| 旧构建流水线值归一为 Scriptable | YooAsset 3.x 已移除对应旧流水线，避免旧配置导致编辑器异常 |

## 验证结果

- `dotnet build TEngine.Runtime.csproj --no-restore -v:minimal`：通过。
- `dotnet build TEngine.Editor.csproj --no-restore`：通过。
- `dotnet build Launcher.csproj`：通过。
- `dotnet build GameLogic.csproj`：通过。
- `dotnet build UnityProject.sln --no-restore -m:1 -v:minimal`：通过。
- `git diff --check`：通过。
- 全仓旧 API 扫描：未发现迁移范围内的旧类型和 legacy define。
- Unity `Editor.log`：脚本程序集成功重新加载，未发现相关 `error CS`。

## 未覆盖的运行时验证

- 尚未连接实际远端资源服务器执行完整版本请求、清单更新和断点下载。
- 各种 Bundle 加密模式尚未用真实构建包逐一运行验证。
- WebGL/微信等目标平台尚未执行完整构建与运行测试；对应 3.x 接口已按包源码完成适配。

## 主要改动文件

- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Services.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs`
- `Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs`
- `Assets/GameScripts/Procedure/ProcedureDownloadFile.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/Editor/AssetBundleCollector/SceneFilePackRule.cs`
- `Assets/Editor/SceneTools/SceneEnumGenerator/YooAssetCollectorReader.cs`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
