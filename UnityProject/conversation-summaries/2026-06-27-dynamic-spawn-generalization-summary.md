# 2026-06-27 DynamicSpawn 通用化与示例脚本会话总结

## 背景

本次会话围绕 `DynamicSceneSpawner` 的派生脚本使用方式整理展开。原项目中存在 `HangarSceneSpawner`，但该脚本只做了一件事：调用 `CollectFromSpawnPoints()` 收集场景中的 `DynamicSpawnPoint`，没有机库专属逻辑。

用户要求将 `HangarSceneSpawner` 改为示例，或者改成通用脚本使用；随后进一步要求将 `HangarManager` 也改成示例脚本，仅作为用户实现参考。

## 本次改动

### 1. `HangarSceneSpawner` 改为通用 Spawner

将：

- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/Load/HangarSceneSpawner.cs`

重命名为：

- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/Load/SpawnPointSceneSpawner.cs`

新类名为 `SpawnPointSceneSpawner`，职责明确为：

- 从自身子节点下的 `DynamicSpawnPoint` 收集加载项
- 直接复用 `DynamicSceneSpawner.CollectFromSpawnPoints()`
- 作为大多数场景可直接挂载的通用实现

对应 `.meta` 文件一并重命名并保留原 GUID，降低 Unity 场景中已有脚本引用丢失的风险。

### 2. `HangarManager` 改为示例 Manager

将：

- `Assets/GameScripts/HotFix/GameLogic/SceneGameManager/HangarManager.cs`

重命名为：

- `Assets/GameScripts/HotFix/GameLogic/SceneGameManager/ExampleSceneGameManager.cs`

新类名为 `ExampleSceneGameManager`，继承：

```csharp
SceneGameManagerBase<DynamicSceneSpawner>
```

示例内容展示：

- 如何指定 `TargetSceneType`
- 如何在 `OnSceneSpawnCompleted()` 中编写场景初始化逻辑
- 如何通过 `GetSpawnedObject("PlayerSpawnRoot")` 获取 `DynamicSpawnPoint.registerKey` 注册过的动态对象

该脚本不再承载机库业务逻辑，仅作为复制改名的实现参考。

对应 `.meta` 文件一并重命名并保留原 GUID。

### 3. 更新 DynamicSpawn 使用教程

更新：

- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/README.md`

主要调整：

- 默认使用 `SpawnPointSceneSpawner`，不再要求每个场景都写一个空派生类
- 说明只有需要额外收集规则或完成钩子时，才继承 `DynamicSceneSpawner`
- 新增“场景业务初始化怎么写”章节，指向 `ExampleSceneGameManager.cs`
- 文件清单中补充 `SpawnPointSceneSpawner.cs` 和 `ExampleSceneGameManager.cs`

## 验证

已执行：

```powershell
dotnet build GameLogic.csproj --no-restore
```

结果：

- 构建通过
- 0 个错误
- 1 个既有警告：`UIBase.cs` 中 nullable 注释上下文警告 `CS8632`

已扫描确认：

- `HangarSceneSpawner` 和 `HangarManager` 旧类名已无代码引用
- `GameLogic.csproj` 已指向 `ExampleSceneGameManager.cs` 和 `SpawnPointSceneSpawner.cs`

## 注意事项

- 本次没有在 Unity 编辑器中打开场景验证组件显示情况。
- 由于 `.meta` GUID 保留，已有场景脚本引用应迁移到新脚本资产；仍建议下次打开 Unity 后检查挂载节点 Inspector 是否正常显示 `SpawnPointSceneSpawner` / `ExampleSceneGameManager`。
- `ExampleSceneGameManager` 是示例脚本，真实场景应复制后改为自己的 `XxxManager` 并填写实际 `TargetSceneType` 与 `registerKey`。
