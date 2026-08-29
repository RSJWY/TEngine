# 当前 Fork 定制功能

本文描述当前仓库相对上游 TEngine 的稳定能力。通用框架原理仍可参考 Wiki 其他章节；当旧页面与本文或当前代码冲突时，以当前代码和本文为准。

改动背景、迁移过程和时间线不在 Wiki 重复展开，统一查阅：

- [Fork 定制改动总览](../../../../../Books/Fork/README.md)
- [Fork 改动时间线](../../../../../Books/Fork/CHANGELOG.md)

## 当前技术基线

| 领域 | 当前基线 |
| --- | --- |
| 资源系统 | YooAsset 3.x 原生 API，不启用 `YOOASSET_LEGACY_API` |
| 热更新包 | 热更程序集位于独立程序集包，默认配置名为 `CodePackage`，实际名称通过 `UpdateSetting.GetAssemblyPackageName()` 获取 |
| 代码包构建 | 默认使用 `ArchiveFileBuildPipeline` 与 `EncryptionType.ChaCha20` |
| 轻量配置 | 使用 `GameModule.Config` 加载 TOML/JSON；项目默认不使用 Luban |
| 模块访问 | 热更业务通过 `GameModule.XXX` 访问模块 |
| 构建工具 | 使用 `Build/打包工具窗口`，运行时包配置与构建配置共用 `UpdateSetting.RuntimePackages` |

## 资源与热更新

### YooAsset 3

当前资源模块使用 YooAsset 3 的 `EditorSimulateModeOptions`、`OfflinePlayModeOptions`、`HostPlayModeOptions` 和 `WebPlayModeOptions`，并为不同运行模式配置 `FileSystemParameters`。

注意事项：

- `EPlayMode` 包含 `None = 0`，Editor 下拉框必须保存真实枚举值。
- `OfflinePlayMode` 只读取已构建并复制到 StreamingAssets 的资源，不访问远端。
- 下载器使用 `StartDownload()` 后等待 `Task`，不要使用旧版 `BeginDownload()`。
- 清单更新返回 `LoadPackageManifestOperation`，缓存清理返回 `ClearCacheOperation`。

### RuntimePackages 与 CodePackage

`UpdateSetting.RuntimePackages` 是运行时初始化和 Editor 构建共用的数据源。每个资源包可独立配置：

- 是否启用、启动时初始化和更新清单；
- 是否按需下载、是否保存版本；
- 构建管线与加密方式；
- 独立版本键和远端包目录。

程序集包目录分为 `AOT/`、`HotDll/` 和 `PDB/`。不要硬编码包名、子目录或版本键。

### Archive 二进制加载

ArchiveBundle 中的 DLL、PDB、AOT 元数据和 Obfuz 动态密钥返回 `RawFileObject`：

```csharp
var raw = await GameModule.Resource.LoadAssetAsync<RawFileObject>(
    location, cancellationToken, packageName);
byte[] bytes = raw.GetBytes();
GameModule.Resource.UnloadAsset(raw);
```

非 Archive 管线继续兼容 `TextAsset.bytes`。这项分流只属于热更新二进制加载链路，普通业务资源仍使用 `IResourceModule` 的类型化 API。

### HybridCLR 与 Obfuz

- 构建前同步 `AOTMetadataManifest`，缺少补充元数据程序集时中断构建。
- 静态 Obfuz 密钥在 `AfterAssembliesLoaded` 初始化，动态密钥在 `Assembly.Load` 前初始化。
- 多态热更 DLL 的构建顺序为：编译 -> 混淆 -> `GeneratePolymorphicDll` -> 拷贝 `.dll.bytes`。
- 首次发布多态 App 前必须执行 `HybridCLR/ObfuzExtension/GenerateAll`。
- 当前 `disableLoadStandardDll = 0`，热更 DLL 可使用多态格式，AOT 补充元数据可保持标准格式。

## Fork 运行时模块

| 入口 | 作用 | 关键约束 |
| --- | --- | --- |
| `GameModule.Config` | TOML/JSON 轻量运行时配置 | 单项失败可跳过；消费方优先使用 `TryGet` |
| `GameModule.GameScene` | 业务场景切换与展示进度 | UI 只展示 `DisplayProgress`，不控制状态机 |
| `GameModule.Screen` | Windows 多显示器窗口布局 | 仅 Windows Standalone 生效 |
| `GameModule.GameObjectPool` | 基于 YooAsset location 的 GameObject 实例池 | 与逻辑对象 `ObjectPoolModule` 不同 |
| `GameModule.Anim` | 基于 PlayableGraph 的代码驱动 3D 动画 | 创建后必须显式销毁 `IAnimPlayable` |

`TimerModule` 保留旧 API，并新增 `AddLoopCountTimer`。坏帧补触发单帧最多执行 10 次，业务对象销毁时仍需主动移除计时器。

## 业务数据与 UI

- `DataBinding`：纯数据变化通知和代码生成，不依赖 `UIWindow` 或 `GameEvent`。
- `ClientSaveDataMgr`：支持 PlayerPrefs/JsonFile、版本升级、坏档备份和异步保存。
- `DataCenterSys`：管理当前玩家会话数据，不应由持久化存档对象替代。
- `FrameAnimModule`：支持 `SpriteRenderer`、UGUI `Image` 和 `RawImage` 三种序列帧代理。
- UGUI 扩展：`UIButton`、`UIImage`、`UIText`、`RichTextItem` 及常用布局、拖拽和效果组件。
- `Utility.Unity`：补充组件、子节点、Layer、EventTrigger、射线、材质和分辨率等工具。
- `Utility.Json.FromJsonOverwrite`：支持覆盖已有对象。
- `GameEvent.RemoveAllListeners`：支持按事件 ID 批量清理监听。

## 日志与运行时工具

- `UnityLoggerBridge` 将 Unity、Task、UniTask 和未观察异常统一写入持久化日志目录。
- TouchSocket 可通过 `AddUnityDebugLogger()` 接入 Unity Console。
- `GameTickWatcher` 位于独立 `RuntimeTools` 程序集，用于轻量逻辑耗时统计。
- GameObjectPool 调试窗口菜单为 `TEngine Tools/Debugger/GameObject Pool`。

## Editor 与发布流程

打包工具的主要流程为：

```text
编译热更 DLL -> 构建资源包 -> 发布整理 -> 最小包处理
-> 构建 Player -> 编译 Inno Setup 安装包
```

主要产物目录：

```text
Releases/
├── Bundles/
├── Windows/{setup.iss, setup.generated.iss, build/, setup/}
├── Linux/build/
└── Publish/{平台}/{包名}/
```

Android、iOS、MacOS 和 WebGL Player 仍使用 `Output/Player/{平台}/`。AssetBundle 或 Player 构建失败时，安装包阶段必须停止，不能复用旧产物。

场景枚举使用 `TEngine/场景枚举配置` 维护，通过 GUID 跟踪场景并生成 `SceneType.g.cs`、`SceneConstName.g.cs` 和 `SceneTypeMapping.g.cs`。

## 详细改动文档

| 主题 | 文档 |
| --- | --- |
| YooAsset 3 迁移 | [yooasset-3-migration.md](../../../../../Books/Fork/yooasset-3-migration.md) |
| 热更新 | [hot-update.md](../../../../../Books/Fork/hot-update.md) |
| 资源构建与发布 | [resource-build.md](../../../../../Books/Fork/resource-build.md) |
| 代码混淆 | [obfuscation.md](../../../../../Books/Fork/obfuscation.md) |
| 运行时配置 | [runtime-config.md](../../../../../Books/Fork/runtime-config.md) |
| 场景系统 | [scene-system.md](../../../../../Books/Fork/scene-system.md) |
| 窗口管理 | [window-management.md](../../../../../Books/Fork/window-management.md) |
| DataBinding | [data-binding.md](../../../../../Books/Fork/data-binding.md) |
| 存档与数据中心 | [save-data.md](../../../../../Books/Fork/save-data.md) |
| UI 扩展 | [ui-expansion.md](../../../../../Books/Fork/ui-expansion.md) |
| GameObject 对象池 | [game-object-pool.md](../../../../../Books/Fork/game-object-pool.md) |
| 动画模块 | [anim-module.md](../../../../../Books/Fork/anim-module.md) |
