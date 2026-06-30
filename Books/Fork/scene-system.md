# 场景系统

本页记录 fork 中围绕动态场景加载和场景切换进度的改动。

## DynamicSpawn 通用 Spawner 与场景 Manager 示例

### 背景

原 `HangarSceneSpawner` 只是继承 `DynamicSceneSpawner` 并返回 `CollectFromSpawnPoints()`，实际职责是“从子节点的 `DynamicSpawnPoint` 收集加载项”，并不属于机库专属逻辑。

继续让每个场景复制一个空派生类，会增加无意义脚本数量，也容易让使用者误以为必须为每个场景写加载器。

### 改动摘要

- `HangarSceneSpawner` 重命名并改造为 `SpawnPointSceneSpawner`。
- `SpawnPointSceneSpawner` 作为大多数场景可直接挂载的通用加载脚本。
- `SpawnPointSceneSpawner` 仍继承 `DynamicSceneSpawner`，只负责调用 `CollectFromSpawnPoints()`。
- 保留原有批量异步加载、完成事件、注册表和 Editor 预览能力。
- `HangarManager` 改为 `ExampleSceneGameManager`，仅作为场景业务管理器示例。
- `ExampleSceneGameManager` 继承 `SceneGameManagerBase<DynamicSceneSpawner>`。
- 示例演示如何指定 `TargetSceneType`，以及如何在 `OnSceneSpawnCompleted()` 中通过 `GetSpawnedObject("PlayerSpawnRoot")` 获取动态加载出的对象。
- `DynamicSpawn` 使用教程同步更新：默认挂 `SpawnPointSceneSpawner`，只有需要额外收集规则或完成钩子时才写 `XxxSceneSpawner`。

### 使用方式

大多数场景只需要：

1. 在场景中新建 `DynamicSpawnRoot`。
2. 给 `DynamicSpawnRoot` 挂 `SpawnPointSceneSpawner`。
3. 在其子节点挂 `DynamicSpawnPoint` 并填写 `location`。
4. 如需业务初始化，复制 `ExampleSceneGameManager` 为自己的 `XxxManager`。
5. 在 `DynamicSpawnPoint.registerKey` 填写 key 后，通过 `GetSpawnedObject("你的key")` 获取加载出的对象。

### 何时写专属 Spawner

只有以下情况才建议写专属 Spawner：

- 加载项不完全来自 `DynamicSpawnPoint`。
- 需要混合代码生成的 `SpawnItem`。
- 需要 override `OnAllSpawned()` 做加载器层面的完成钩子。

### 关键文件

- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/DynamicSceneSpawner.cs`
- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/DynamicSpawnPoint.cs`
- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/Load/SpawnPointSceneSpawner.cs`
- `Assets/GameScripts/HotFix/GameLogic/SceneGameManager/SceneGameManagerBase.cs`
- `Assets/GameScripts/HotFix/GameLogic/SceneGameManager/ExampleSceneGameManager.cs`
- `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/README.md`

### 验证记录

```powershell
dotnet build GameLogic.csproj --no-restore
```

结果：0 错误，0 警告。

### 相关记录

- `UnityProject/conversation-summaries/2026-06-27-dynamic-spawn-generalization-summary.md`

## 场景加载进度拆分到 GameSceneModule

### 背景

原 `LoadingUI` 是“胖窗口”：三段式进度状态机、`LoadSceneAsync(suspendLoad=true)` 资源加载、`UnSuspend` 激活、完成回调、Tips 文案、关闭时机都塞在 `UIWindow` 内。

主要问题：

- 进度与加载控制等基础设施逻辑混入表现层。
- UI 同时掌管数据和流程，职责越界。
- 文件引用了仓库中不存在的 `GameTipsData` 类型。
- 文件还引用了已迁移的旧全局事件 `Event_LoadOver` / `Event_SceneLoadStart`，实际已无法编译。
- 激活采用“UI 发 `Event_LoadOver` -> 模块自收再 `UnSuspend`”的自发自收事件回路，流程绕远。

### 改动摘要

- `GameSceneModule` 实现 `IUpdateModule`。
- 借 `ModuleSystem.Update` 驱动状态机，无需 `Timer` 或 UI 内 `OnUpdate` 控制加载流程。
- 空闲期 `_isActive=false` 早退，避免每帧空转。
- 三段式进度原样迁入 `Update(elapse, realElapse)`：
  - 预热 0 -> 10%
  - 加载 10 -> 90%
  - 收尾 90 -> 100% + 停留
- 使用 `realElapseSeconds` 驱动，暂停时加载页动画不冻结。
- phase 2 钳制 `delta <= 0.05`，防止激活帧跳过 100%。
- 新增 `float DisplayProgress`，暴露平滑后的展示进度，只读供 UI 渲染。
- 激活改为模块直连：`EnterFinishPhase` 在 90% 直接 `GameModule.Scene.UnSuspend(_sceneName)`。
- 激活后派发 `IGameSceneEvent.OnSceneLoadOver` 作对外通知。
- `SwitchUI` 降为纯展示，只读 `GameModule.GameScene.DisplayProgress` 写进度条和百分比文本。
- `SwitchUI` 不再持有加载状态，也不主动关闭自身，由模块 `CloseUI<SwitchUI>` 关闭。
- `SwitchUI` 层级从 `UILayer.UI` 调整到 `Top`，作为全屏遮罩。
- 删除 `LoadSceneDataBody`，模块自持 `_sceneName` / `_finishCallBack`。
- 移除 `_eventMgr` 与 `OnSceneLoadOver` 自监听。
- 去除 `GameTipsData`。

### 运行时流程

```text
GameSceneModule.LoadScene(sceneType, finishCallBack) / JumpToMainScene()
  └─ StartSceneLoad
       RecordScene()
       GameEvent.OnSceneLoadStart()
       重置状态机
       GameModule.UI.ShowUI<SwitchUI>()

ModuleSystem.Update 每帧 -> GameSceneModule.Update(elapse, realElapse)
  ├─ phase 0 预热 0 -> 10%
  │    到位后 StartRealLoading()
  ├─ phase 1 加载 10% -> 90%
  │    LoadSceneAsync(suspendLoad=true, cb=OnLoadProgress)
  │    加载完成且展示进度到 89% 后进入收尾
  └─ phase 2 收尾 90% -> 100% + 停留 0.5s
       EnterFinishPhase: UnSuspend(sceneName) 激活场景并派发 OnSceneLoadOver
       FinishAndClose: finishCallBack() -> CloseUI<SwitchUI> -> OnSceneReady(sceneType)

SwitchUI.OnUpdate
  └─ 读取 GameModule.GameScene.DisplayProgress 并渲染进度条和百分比
```

终结顺序刻意保持为：

```text
回调 -> 关加载页 -> OnSceneReady
```

这是为了对齐 `DynamicSceneSpawner` “SwitchUI 关闭后才收 OnSceneReady”的契约。

### 保留的关键陷阱

- `suspendLoad=true` + `progressCallBack` 时，`LoadSceneAsync` 内部 `while(!IsDone)` 会一直 yield，`await` 会死循环，因此只 fire-and-forget，进度全由 `progressCallBack` 驱动。
- suspendLoad 时 `IsDone` 永远 false，`progressCallBack` 每帧回调 `value=0.9` 会反复覆盖 target。因此 `OnLoadProgress` 在 `phase >= 2` 直接 return，保护收尾 `target=1.0` 不被打回 0.90。
- 选择 90% 激活而非 100% 激活，是为了用最后 10% 动画和 100% 停留遮盖激活帧卡顿。

### 关键文件

- `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/IGameSceneModule.cs`
- `Assets/GameScripts/HotFix/GameLogic/UI/SwitchUI/SwitchUI.cs`
- `Assets/AssetRaw/UI/SwitchUI.prefab`

### 相关记录

- `UnityProject/conversation-summaries/2026-06-30-switchui-scene-progress-refactor-summary.md`
