# 桌面多开缓存隔离

本页记录 fork 接入 YooAsset 桌面多开客户端缓存隔离方案的改动。

参考：[YooAsset 官方文档 - 面向桌面的多开客户端](https://www.yooasset.com/docs/solution/DesktopMultiInstance)。

## 背景

同一台桌面设备上同时运行多个客户端进程（多开调试、专用服务器 DS 多实例等）时，各进程默认访问相同的 YooAsset 缓存目录，下载、解包、清理和清单写入会互相冲突。YooAsset 3.0.5 的方案是为每个进程显式传入独立的 `packageRoot`，做纯路径隔离（不加文件锁）。

## 改动摘要

- `ResourceModule` / `IResourceModule` 新增 `InstanceId` 属性，默认空串表示不隔离。
- `InitPackage` 内根据 `InstanceId` 计算隔离根目录 `{DefaultCacheRoot}/instance-{InstanceId}/{PackageName}`，非空时：
  - Sandbox 文件系统改用带 `packageRoot` 的重载（下载缓存隔离）；
  - Builtin 文件系统追加 `UnpackFileSystemRoot`（内置解包可写数据隔离；StreamingAssets 只读源不隔离）。
- 新增 `CacheRootHelper`，用公开的 `YooAssetConfiguration.GetYooFolderName()` 按平台复刻默认缓存根目录（内部 `GetDefaultCacheRoot()` 不可访问）。
- 新增 `MultiInstanceLauncher` 解析命令行参数 `--yoo-instance <id>`，`ProcedureLaunch.OnEnter` 在资源包初始化前注入 `InstanceId`。
- 不传参数时所有文件系统创建路径与改动前完全一致，单开行为零变化。

## 使用方式

由启动器为每个进程分配稳定且唯一的实例标识：

```bash
Game.exe --yoo-instance client-1
Game.exe --yoo-instance client-2
```

编辑器下验证：用命令行带参启动 Unity（自定义参数会透传到 `Environment.GetCommandLineArgs()`），PlayMode 切到 HostPlayMode 后缓存落在 `Library/appdata/instance-{id}/{PackageName}/`。

```powershell
& "Unity.exe" -projectPath <工程路径> --yoo-instance client-1
```

同一客户端应尽量复用原有实例标识，以继续使用已有缓存。

## 注意事项

- 纯路径隔离，无跨进程文件锁；两个进程使用相同实例标识仍会冲突。
- `packageRoot` 在 Package 初始化时确定，初始化后不可更改。
- 每个实例独立下载资源，磁盘占用按实例数倍增；删除某个 `instance-{id}` 目录只清理该实例缓存。
- Windows/Linux 默认缓存根目录在游戏安装目录（`Application.dataPath`）下，Mac/移动端在 `persistentDataPath`；游戏安装到 `Program Files` 等需管理员权限的目录时写入会失败。
- 仅在 `UNITY_STANDALONE || UNITY_EDITOR` 下解析命令行；移动端恒不启用。EditorSimulateMode 与 WebGL 分支不参与隔离。
- 当前未启用 `CopyBuiltinPackageManifest`；若以后开启，需把 `CopyBuiltinPackageManifestDestRoot` 对齐到 `{isolatedRoot}/{CacheRootHelper.ManifestFolderName}`。

## 关键文件

- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/CacheRootHelper.cs`
- `Assets/Launcher/Scripts/MultiInstanceLauncher.cs`
- `Assets/GameScripts/Procedure/ProcedureLaunch.cs`
