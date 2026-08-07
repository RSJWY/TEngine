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

## 阶段 1 超时双门槛化（修复固定 5s 误杀大场景冷启动）

### 背景

`GameSceneModule` 阶段 1（真实加载 10%->90%）原用固定 5s 绝对超时兜底：

```csharp
else if (_phase1ElapsedTime >= 5.0f) EnterFinishPhase();
```

`suspendLoad=true` 时 YooAsset `progress` 正常最高到 0.9，大场景（约 90MB+）打包后首次冷启动（StreamingAssets 读盘/解压/依赖加载）经常超过 5s。5s 到期时 `_sceneLoadComplete` 仍为 false，超时兜底把"慢加载"误判成"卡死"，强制 `EnterFinishPhase` -> `UnSuspend`，可能在场景资源未加载到 0.9 时就激活场景。Editor 与二次进入（热缓存）往往正常，问题集中在打包后冷启动第一次。

### 改动摘要

- 删除固定 `5.0f` 绝对超时。
- 新增停滞超时 `Phase1StallTimeout = 60f`：仅当 YooAsset 真实进度已 >0 且连续无提升才判定异常。
- 新增绝对超时 `Phase1AbsoluteTimeout = 180f`：总时长兜底防彻底卡死。
- 新增 `_lastLoadProgress` / `_phase1StallElapsed` 字段，`StartSceneLoad` 重置块一并置 0。
- `OnLoadProgress` 记录原始进度，进度严格大于上次值时重置停滞计时（progress 基于字节数单调递增，慢速爬升算健康）。
- 冷启动解压期 `progress` 长期为 0 时不累计停滞（`_lastLoadProgress > 0f` 守卫），避免误杀。
- 超时日志补充 `scene/elapsed/stall/rawProgress/display/complete` 便于排查。
- 修正 issue 方案伪代码 `if` 累加 / `else if` 判断互斥导致停滞超时永不触发的逻辑 bug：实际实现将停滞累计改为独立 `if`，超时判断放在 `if/else if` 链尾。

保持不变：

- `suspendLoad=true`、0.9 激活、阶段 2 收尾动画与 100% 停留。
- 陷阱 2 规避：`OnLoadProgress` 在 `phase >= 2` 直接 return，保护收尾 `target=1.0` 不被打回 0.90。
- 阶段 1 正常收尾条件 `_sceneLoadComplete && _displayProgress >= 0.89f`（显示进度平滑追赶等待）。
- `_skipMode` 快速跳过分支。

### 注意事项

- `Phase1StallTimeout` / `Phase1AbsoluteTimeout` 为 `const`，如需按场景体积调参可改为可配置属性（参考 `SkipLoadingAnimation` 先例）。
- "进度提升"判定用严格大于 `value > _lastLoadProgress`，不要用 `+0.001` 阈值，否则会漏掉合法小幅推进、加剧误杀。
- 停滞累计是独立 `if`，必须在 `if/else if` 链之前，不能与超时判断放成 `if/else if` 互斥，否则停滞超时永不触发。

### 关键文件

- `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-08-07-scene-phase1-timeout-fix-summary.md`
- GitHub Issue #1

## 场景枚举自动生成（SceneEnumConfig）

### 背景

原方案中每新增一个场景，需手动同步 4 处（`GameSceneModule.cs` 内）：

1. `SceneType` 枚举追加值（且要求"往后追加不插入"以保证顺序稳定）；
2. `SceneConstName` 常量追加资源地址字符串；
3. `GetSceneName` 的 `switch` 加 `case`；
4. `GetSceneTypeFromName` 的 `if` 加反向匹配。

四处必须一致，容易漏改/错改（issue #2）。资源地址来自 YooAsset `Scenes` Group（`AddressByFileName`，地址 = 场景文件名），与代码枚举的对应关系纯靠人工维护。

### 改动摘要

- 新增 Editor 工具 `SceneEnumConfig`（ScriptableObject，Odin 表格）：作为数据源 + 顺序注册表，统一管理场景列表。
- 每条目以 GUID 追踪场景身份：场景改名不丢失引用，资源地址自动跟随新文件名；枚举名/枚举值保持稳定（代码契约）。
- Inspector「同步场景资源」扫描 `Assets/AssetRaw/Scenes`：新增场景追加（枚举名默认 = 文件名清洗，枚举值 = max+1），已删除场景标记 `Active=false`（枚举值保留占位），改名场景按 GUID 识别并刷新引用。
- Inspector「生成枚举代码」落盘 3 个自动生成文件（带 `<auto-generated>` 注释 + `[GeneratedCode]` 特性，IDE 提示勿手改）：
  - `SceneType.g.cs`：枚举（显式 `EnumValue`，含中文备注 XML 注释）；
  - `SceneConstName.g.cs`：资源地址常量（值 = 场景文件名，改名自动跟随）；
  - `SceneTypeMapping.g.cs`：双向 `Dictionary` 映射（替代手写 `switch`/`if`）。
- `GameSceneModule.cs` 删除内嵌的 `SceneType`/`SceneConstName`，`GetSceneName`/`GetSceneTypeFromName` 转发到 `SceneTypeMapping`；三段式进度等业务逻辑零改动。
- 菜单 `TEngine > 场景枚举配置` 自动创建/打开配置资产（存于 `Assets/Resources/SceneEnumConfig.asset`）。

保持不变：

- `namespace GameLogic`、`SceneType`/`SceneConstName` 类型名，所有引用（`GameApp`、事件、`SceneGameManagerBase`、回放文件名解析等）零改动。
- 回放文件名解析用枚举名字符串（`nameof(SceneType.Xxx)`），行为与原 `Enum.TryParse` 一致。
- `MainScene` 枚举值 0、资源地址 "MainScene"。

### 使用方式

1. 菜单 `TEngine > 场景枚举配置` 打开配置资产（首次自动创建到 `Assets/Resources/`）。
2. Inspector 点「同步场景资源」扫描目录填入列表。
3. 表格内调整枚举名、填中文备注（Odin 多行内联编辑）。
4. 点「生成枚举代码」落盘 3 个 `.g.cs`。

场景改名/删除/新增：

| 操作 | EnumName/EnumValue | 资源地址 |
| --- | --- | --- |
| 新增 | 新分配，值 = max+1 | 取文件名 |
| 删除 | 保留，`Active=false` | 不生成 |
| 改名 | 不变（GUID 识别） | 自动跟随新文件名 |

### 注意事项

- **Editor 程序集需重新编译**：修改生成器代码后，必须等 Unity 编译完成再点「生成枚举代码」，否则执行旧生成器逻辑会覆盖修复后的 `.g.cs`。验证：生成后确认 `SceneTypeMapping.g.cs` 是 `static SceneTypeMapping()` + 索引器赋值，而非集合初始化器。
- **重复 key 防护**：`_fromName` 字典可能因"资源地址 == 枚举名"产生重复 key，生成器有三层防护--生成前校验 `EnumValue` 唯一、生成时资源地址等于枚举名则跳过 `nameof` 条目、`_fromName` 用静态构造函数 + 索引器赋值（覆盖不报错）。
- 配置资产放 `Assets/Resources/`：该 ScriptableObject 引用 `SceneAsset`（Editor-only 类型），Player 构建时会入包但运行时类型不可用（产生 Missing Script 警告），仅 Editor 使用的配置不影响功能。如需规避可改放 `Assets/Editor/` 下。
- 枚举名清洗规则：非法字符转 `_`，数字开头加 `_`，重名加 `_2` 后缀。建议场景文件名用英文合法标识符。

### 关键文件

- `Assets/Editor/SceneTools/SceneEnumGenerator/SceneEnumConfig.cs`
- `Assets/Editor/SceneTools/SceneEnumGenerator/SceneEnumSyncUtil.cs`
- `Assets/Editor/SceneTools/SceneEnumGenerator/SceneEnumCodeGenerator.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/SceneType.g.cs`（自动生成）
- `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/SceneConstName.g.cs`（自动生成）
- `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/SceneTypeMapping.g.cs`（自动生成）
- `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs`（改造：删除内嵌枚举/常量，映射转发）

### 相关记录

- GitHub Issue #2
- `UnityProject/conversation-summaries/code-researc/2026-08-07-scene-enum-auto-generate-research.md`
