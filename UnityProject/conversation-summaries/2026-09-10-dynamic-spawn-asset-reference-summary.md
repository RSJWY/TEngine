# 2026-09-10 DynamicSpawn 接入资源弱引用（GUID 寻址）会话总结

## 背景

用户提出评估 YooAsset 3.0 扩展示例的 `AssetReference`（资源弱引用：序列化"包裹名 + GUID"，运行时再定位加载），判断 `DynamicSceneSpawner` 是否应升级。讨论结论是两者为共存关系：`AssetReference` 是字段级弱引用类型，`DynamicSceneSpawner` 是场景加载编排；最终采用"融合"方案——占位点引入 GUID 弱引用作为主通道，`location` 字符串保留为回落与代码列表法通道。

原方案的运行时风险：`location` 按预制体文件名寻址（`AddressByFileName`），预制体改名后引用失效；不同目录同名预制体寻址冲突。编辑器侧虽已有 `prefabGuid`，但仅用于预览与测试启动回退，不随包发布。

## 本次改动

### 1. 引入 AssetReference 弱引用

- 新增 `GameLogic.AssetReference` 基类与 `AssetReferenceGameObject`（源自 YooAsset Extension Sample，置于 `Scenes/DynamicSpawn/AssetReference/`）：序列化 `_packageName` + `_assetGUID`，提供 `RuntimeKeyIsValid()` / `LoadAssetAsync()` / `ReleaseAsset()`，另加编辑器专用 `EditorSetAssetGUID` 供迁移。
- 新增 `AssetReferenceDrawer`（`Assets/Editor/SceneTools/DynamicSpawn/`）：包裹名 + 资源拖拽框 + 只读 GUID 三行绘制。

### 2. 占位点与加载管线改造

- `DynamicSpawnPoint` 新增 `prefabRef` 字段；`prefabGuid` 降级为迁移中转；`MigrateToAssetReferenceIfNeeded()` 完成 `prefabGuid` → `prefabRef` 搬迁，与旧 PPtr 迁移（`MigrateLegacyReferenceIfNeeded`）串成链；退役"填充 Location"右键菜单。
- `DynamicSceneSpawner`：`SpawnItem` 增加 `PrefabRef`；新增 `TryResolveAddress()`——GUID 有效则 `GetAssetInfoByGuid` 解析地址后走原有 `GameModule.Resource.LoadGameObjectAsync`（保留分批削峰、取消、TEngine 引用计数释放语义），GUID 无效打警告回落 `location`；未使用 `AssetReference` 自带的 handle 生命周期。
- 编辑器工具：`DynamicSpawnPointInspector` 弱引用拖拽字段置顶、自动填 registerKey；`DynamicSpawnPointManager` 校验 GUID 优先、新增"预制体"只读列、快速添加/一键转换不再写 `location`、迁移按钮升级为全链路；`DynamicSceneSpawnerInspector` 批量预览 GUID 优先。

### 3. 收集器配置

- `DefaultPackage` 开启 `IncludeAssetGUID`（`AssetBundleCollectorConfig.xml` + `AssetBundleCollectorSetting.asset`）——清单记录 GUID 映射是 `GetAssetInfoByGuid` 的前提，缺失时运行时报 "Package manifest does not include asset GUID" 并回落 location。真机/离线图需重新构建资源包生效。

### 4. 测试启动回归修复

- 地址解析原在 `useYooAsset` 分流之前执行：YooAsset 未初始化时 `YooAssets.GetPackage` 直接抛异常，且纯 GUID 点 `location` 为空被跳过，导致编辑器直接打开场景的脱离 YooAsset 加载失效。修复：解析挪入 YooAsset 分支内部；编辑器回退 `InstantiateFromEditorRef` 经 `AssetDatabase` 按 GUID 解析实例化。
- `CompleteSpawn` NRE 修复：测试启动时 `IGameSceneEvent` 未注册，`GameEvent.Get` 返回 null，发送完成事件前判空（此为改造前已存在的隐患）。

## 验证

- `dotnet build GameLogic.csproj` / `Assembly-CSharp-Editor.csproj`：0 错误。
- Unity 实测：正常流程 GUID 寻址通过；测试启动直接实例化通过；MainScene 迁移后 `prefabGuid` 清空、GUID 统一入 `prefabRef`（场景 diff 确认）。

## 提交

- `55cd1adc`：DynamicSpawn 接入弱引用主体改造 + 收集器配置 + MainScene 迁移。
- `6f4ef08f`：测试启动回退失效与 `CompleteSpawn` NRE 修复。

## 关键文件

- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/AssetReference/AssetReference.cs`
- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/AssetReference/AssetReferenceGameObject.cs`
- `Assets/Editor/SceneTools/DynamicSpawn/AssetReferenceDrawer.cs`
- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/DynamicSpawnPoint.cs`
- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/DynamicSceneSpawner.cs`
- `Assets/Editor/SceneTools/DynamicSpawn/DynamicSpawnPointInspector.cs`
- `Assets/Editor/SceneTools/DynamicSpawn/DynamicSpawnPointManager.cs`
- `Assets/Editor/SceneTools/DynamicSpawn/DynamicSceneSpawnerInspector.cs`
- `Assets/AssetBundleCollectorConfig.xml`
