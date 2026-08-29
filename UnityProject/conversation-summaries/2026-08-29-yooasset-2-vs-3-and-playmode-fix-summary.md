# YooAsset 2.x 与 3.x 差异及运行模式修复总结

> 日期：2026-08-29

## 背景

项目升级到 YooAsset 3.0.5 后，用户通过扩展工具选择 `OfflinePlayMode`，运行日志仍显示 `EditorSimulateMode`。

## 原因

YooAsset 3.x 的 `EPlayMode` 新增 `None = 0`。旧工具把下拉框索引直接写入 `EditorPrefs["EditorPlayMode"]`，选择 Offline 时写入 `1`，而 3.x 的 `1` 是 `EditorSimulateMode`。

## 修复

- 旧版 Unity 工具栏和 Unity 6 工具栏使用显式 `EPlayMode` 映射。
- `ResourceModuleDriverInspector` 保存实际枚举值，不再写入 UI 索引。
- 增加 `TEngine.EditorPlayModePrefsVersion`，首次加载时把旧索引转换为 3.x 枚举值。
- 修复 Unity 6 工具栏先生成文本、后读取偏好值造成的显示滞后。
- 将 `GameEntry.prefab` 中旧的 `playMode: 0` 更新为有效的 `EditorSimulateMode` 值 `1`。

## YooAsset 2.x 与 3.x 重点差异

- `InitializeAsync(InitializeParameters)` 改为 `InitializePackageAsync(InitializePackageOptions)`。
- `IRemoteServices` 改为 `IRemoteService`，并支持候选 URL 列表。
- `IEncryptionServices`/`IWebDecryptionServices` 改为 Bundle 加密、流式、偏移和内存解密接口。
- 下载、清单和缓存 API 从位置参数改为 Options 类型。
- `LoadRawFile` 改名为 `LoadBundleFile`，资源查询 API 统一重命名。
- WebGL 文件系统、ArchiveBundle、清单结构和多包缓存隔离能力增强。

## 验证

- `dotnet build UnityProject.sln --no-restore -m:1 -v:q`：通过，0 个错误。
- `git diff --check`：通过。
- 静态扫描确认编辑器工具不再直接用下拉索引读写 `EditorPlayMode`。

## 关键文件

- `UnityProject/Assets/Editor/ToolbarExtender/UnityToolbarExtenderRight/EditorPlayMode.cs`
- `UnityProject/Assets/Editor/ToolbarExtender/UnityToolbarExtenderRight/UnityToolbarExtenderRight.cs`
- `UnityProject/Assets/Editor/ToolbarExtender/Unity6000_OR_New/MainToolbarExtender.cs`
- `UnityProject/Assets/TEngine/Editor/Inspector/ResourceModuleDriverInspector.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs`
