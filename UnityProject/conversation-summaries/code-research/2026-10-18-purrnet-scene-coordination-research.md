# PurrNet × TEngine 场景加载协调（阶段四 4.2）研究报告

- **日期**：2026-10-18
- **对应计划**：`.plans/purrnet-ds-integration.md` 阶段四 步骤 4.2「场景加载协调」
- **研究方式**：只读源码（TEngine 框架层、业务层、PurrNet 包缓存、YooAsset 本地包）+ 既有 PurrNet 官方文档研究报告
- **结论一句话**：**服务器权威、客户端被动**——DS/Host 用 PurrNet `ScenesModule.LoadYooAssetSceneAsync` 发起权威加载；客户端不自己加载，只监听 PurrNet 场景事件、复用 TEngine 加载页做「被动展示」，完成后照常派发 `OnSceneReady`。框架层与 PurrNet 包都不改，桥接放在业务层。

---

## 一、三套场景加载体系（源码事实）

### 1.1 TEngine `SceneModule`（框架层，`Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs`）

| 机制 | 事实 | 证据 |
|---|---|---|
| 句柄持有 | `_currentMainScene` 存主场景 handle；`_subScenes` 字典存子场景 handle | SceneModule.cs:18-20 |
| 加载入口 | `LoadSceneAsync(location, sceneMode, suspendLoad, priority, gcCollect, progressCallBack)` | ISceneModule.cs:23-24 |
| 挂起语义 | 底层传 `!suspendLoad` 给 YooAsset 第 4 参 | SceneModule.cs:109/140/194/219 |
| YooAsset 第 4 参 | **`bool allowSceneActivation = true`**（3.0.5 本地包，不是旧版 `suspendLoad` 命名） | `Packages/com.tuyoogame.yooasset/Runtime/ResourcePackage/ResourcePackage.cs:480` |
| 推论 | TEngine 的 `suspendLoad=true` ⇒ `allowSceneActivation=false` ⇒ 场景挂起 | 上述两条合成 |
| 激活/完成判定 | `UnSuspend(location)` 置激活；`IsSceneLoadDone(location)` 轮询 `IsDone` | SceneModule.cs:286-310、313 |
| 卸载 | `UnloadAsync/Unload` **只处理 `_subScenes` 里的子场景**；主场景 handle 没有对应卸载路径 | SceneModule.cs:365-446 |
| 进度 | `InvokeProgress(SceneHandle, Action<float>)` 把 YooAsset 进度回调出去 | SceneModule.cs:239 |

### 1.2 `GameSceneModule`（业务层，`Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs`）

- **三段式进度状态机**：阶段 0 预热（0→10%，纯动画）→ 阶段 1 真实加载（10%→90%，进度来自 `GameModule.Scene.LoadSceneAsync(..., suspendLoad: true, progressCallBack: OnLoadProgress)`）→ 阶段 2 收尾（90%→100%）。
- **加载调用**：`StartRealLoading()` 固定 `suspendLoad: true, priority: 100, gcCollect: true`（:503-507）。
- **激活权**：`EnterFinishPhase()` 在阶段 1→2 调 `GameModule.Scene.UnSuspend(_sceneName)`（:543-559）。
- **完成判定**：阶段 2 轮询 `GameModule.Scene.IsSceneLoadDone(_sceneName)` + 激活后 2 帧 + 100% 停留（:355-389）。
- **并发保护**：`_isActive` 为真时拒绝新请求并报错（:444-451）。
- **事件契约**（`IGameSceneEvent`，`Assets/GameScripts/HotFix/GameLogic/IEvent/IGameSceneEvent.cs`）：
  - `OnSceneLoadStart(sceneType)`（:454）
  - `OnSceneLoadOver()`（:552）
  - `OnSceneReady(sceneType)`（:585，加载页关闭后）
  - `OnDynamicSpawnComplete(sceneType)`（由 `DynamicSceneSpawner` 加载完动态对象后派发）
- **下游依赖**：`DynamicSceneSpawner` 监听 `OnSceneReady` 才开始加载场景内预制体（`Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/DynamicSceneSpawner.cs:156/189`，`SpawnPointSceneSpawner` 为其子类）。**任何新方案必须保证该事件在场景真正激活后派发，否则场景空。**
- **UI**：`SwitchSceneUI : UIWindow`（`UI/Core/SwitchSceneUI/`），加载页只读 `DisplayProgress/DisplayPhaseText` 渲染，不参与加载控制。

### 1.3 PurrNet `ScenesModule`（包缓存 `Library/PackageCache/dev.purrnet.purrnet@fb95db239295/Runtime/CoreModules/Scenes/`）

| 机制 | 事实 | 证据 |
|---|---|---|
| 模块获取 | `NetworkManager.sceneModule => _serverSceneModule ?? _clientSceneModule`；服务器/客户端各一个实例 | NetworkManager.cs:982、1051-1052、1309 |
| **只有服务器能加载** | 普通 `LoadSceneAsync(int/string, ...)` 开头 `if (!_asServer) Error("Only server can load scenes")` | ScenesModule.cs:1205-1209、1229-1233 |
| **只有服务器能卸载** | `UnloadSceneAsync(...)` 同样 `_asServer` 守卫；且禁止卸载 NM 所在场景 | ScenesModule.cs:1330-1342、1373-1389 |
| YooAsset 权威加载 | `LoadYooAssetSceneAsync(packageName, location, PurrSceneSettings settings)`，非服务器直接报错返回 null | ScenesModule.YooAsset.cs:220-240 |
| 场景 Action 类型 | `Load / LoadAddressable / Unload / SetActive` 四种，打包成 `SceneActionsBatch` 下发 | SceneHistory.cs:8-14、61-69 |
| 场景历史 | 服务器 `_history.AddLoadAction(...)`；`OptimizeHistory()` 按 Single 语义裁剪历史；`GetFullHistory()` 给新玩家/重连重放，`GetDelta()` 增量 | ScenesModule.cs:1260-1265；SceneHistory.cs:134-252 |
| **客户端本地加载** | 收到 `LoadAddressableSceneAction` 后 `package.LoadSceneAsync(location, mode, physicsMode, true, 100)` —— **第 4 参 allowSceneActivation=true，直接激活，无挂起窗口** | ScenesModule.YooAsset.cs:129-163（148-153） |
| YooAsset 场景 key | `purrnet-yooasset-v1:{packageName长度}:{packageName}{location}` 编码后随 action 下发，各端自行解码加载 | ScenesModule.YooAsset.cs:13、57-91 |
| 加载事件 | `onYooAssetSceneStartLoading`（开始加载，服务器与客户端都会触发）、`onYooAssetSceneLoaded`（注册完成） | ScenesModule.YooAsset.cs:45-55、112-117、163 |
| 通用场景事件 | `onPreSceneLoaded/onSceneLoaded/onPostSceneLoaded/onPreSceneUnloaded/onSceneUnloaded/onPostSceneUnloaded/onSceneVisibilityChanged` 等 | ScenesModule.cs:75-105 |
| 状态查询 | `IsYooAssetSceneLoading(packageName, location)`、`IsYooAssetSceneLoaded(...)`、`TryGetSceneIdByYooAssetLocation(...)` | ScenesModule.YooAsset.cs:342-368 |
| Host 模式 | 服务器加载时把同一 `PendingSceneOperation` 塞给 client 模块的 `_pendingOperations`，不重复加载 | ScenesModule.cs:1280-1284 |
| Single 前置处理 | 服务器 Single 加载前把所有非 DontDestroyOnLoad 的已注册场景 `RemoveScene`；并警告「NM 所在场景不是 DontDestroyOnLoad 却要 Single 加载」 | ScenesModule.cs:1250-1256、1238-1247 |
| SceneID | 服务器 `GetNextID()` 递增分配；`lastSceneId` 可查 | ScenesModule.cs:113、1219 |
| 配套模块 | `ScenePlayersModule`（场景↔玩家）、`SceneObjectsModule`（场景内对象）、`SceneEqualityComparer` | 同目录文件清单 |

### 1.4 既有官方文档研究结论（本仓库 2026-09-04 报告）

- 「**Scene Module（仅服务端调用）**：`sceneModule.LoadSceneAsync("name", settings)`；`PurrSceneSettings`：mode（Single/Additive）、physicsMode、isPublic（是否拉所有玩家进场景）」。
- DS 通用建议：「需自行处理从主菜单场景跳转到游戏场景」。
- 出处：`conversation-summaries/code-research/2026-09-04-purrnet-02-官方文档详细报告.md:106、204`

---

## 二、冲突点分析（为什么不能「两边各加载一次」）

1. **双加载 / 双激活**
   客户端若同时走 `GameSceneModule.LoadScene`（自己 `LoadSceneAsync`）又收到 PurrNet 的 `LoadAddressableSceneAction`，同一场景会被加载两次（TEngine 的 handle + PurrNet 的 handle），Single 模式互相卸载，SceneID 与 Unity Scene 对应关系错乱。

2. **激活控制权互斥**
   TEngine 三段式的全部价值在「挂起 → 90% 时 UnSuspend → 等待真实激活」。而 PurrNet 客户端路径写死 `allowSceneActivation=true`（ScenesModule.YooAsset.cs:152），**没有挂起窗口**。同一场景只能有一个驱动方：要么 TEngine 驱动（PurrNet 不参与加载），要么 PurrNet 驱动（TEngine 只能围观）。

3. **客户端无权发起**
   客户端调 `sceneModule.LoadSceneAsync(...)` 会直接 `Error("Only server can load scenes")`（ScenesModule.cs:1205-1209）。因此「客户端主动切场景」必须改为「请求服务器切场景」。

4. **主场景 handle 脏引用**
   PurrNet 客户端 Single 加载会卸载当前主场景，但 `SceneModule._currentMainScene` 仍指着已销毁场景；后续 `IsSceneLoadDone/Unload` 路径可能落在无效 handle 上（`UnloadAsync` 只查 `_subScenes`，主场景不会走卸载，风险主要在主场景引用失效后再次被读取）。**具体表现未运行时验证**。

5. **事件契约必须保住**
   `DynamicSceneSpawner` 依赖 `OnSceneReady`。如果客户端场景由 PurrNet 加载而没人派发 `OnSceneReady`，场景里的动态对象（含 DS 的 `SpawnPointSceneSpawner`）永远不会生成。

6. **DS 不能用加载页**
   DS 分支已跳过 `LauncherMgr.Initialize()`（`ProcedureLaunch` DS 分支），`GameSceneModule.StartSceneLoad` 会 `GameModule.UI.ShowUI<SwitchSceneUI>()`——DS 下没有 UIRoot，正是阶段三踩过的 NRE 坑。**DS 侧只应调用权威加载，不应进入三段式 UI 流程。**

7. **场景来源不同**
   本项目场景是 `Assets/AssetRaw/Scenes/` 的热更场景，`SceneType → location` 由 `SceneTypeMapping` 提供（`GameSceneModule.GetSceneName`），必须走 YooAsset 重载（packageName + location），不能用 buildIndex 版（要求场景在 build settings，`ScenesModule.cs:1147-1159`）。

---

## 三、方案对比

| 维度 | A. 服务器权威 + 客户端被动加载页（推荐） | B. 业务自建 RPC 广播 + 各端 GameSceneModule 自行加载 | C. 改造 PurrNet 包支持挂起 |
|---|---|---|---|
| 场景一致性 | 强（服务器唯一真源，SceneID/历史/重连由 PurrNet 保证） | 弱（业务自己保证，重连需自己做） | 强 |
| 加载页体验 | 保留 SwitchSceneUI，但进度只能是「伪进度 + 事件驱动」 | 完整三段式真实进度 | 完整三段式真实进度 |
| 客户端切场景 | 请求服务器 | 需自己设计协议 | 同 A |
| 改动面 | 业务层新增协调器 + GameSceneModule 被动入口 | 业务层自建协议 + 客户端直接加载（但客户端无权 LoadScene，等于绕开 PurrNet） | 改第三方包（**违反「不主动改框架/包」红线**） |
| 断线重连/中途加入 | PurrNet `SceneHistory` 自动重放，零额外代码 | 需自己实现状态同步 | 同 A |
| 维护成本 | 低（跟库升级） | 中高 | 高（每次升级 PurrNet 都要维护补丁） |

**结论：选 A。C 直接排除（红线）。B 仅在「完全不用 PurrNet 场景模块、只用它传 RPC」时才考虑，不推荐。**

---

## 四、推荐方案 A 详细设计

### 4.1 职责分层（不改框架、不改包）

```
DS / Host（服务器权威）                     客户端（被动）
NetworkSceneCoordinator (业务新增)          NetworkSceneCoordinator（同一份代码，分支行为）
  └─ sceneModule.LoadYooAssetSceneAsync()    └─ 订阅 onYooAssetSceneStartLoading(asServer=false)
     packageName + location                       └─ GameSceneModule.BeginPassiveLoad(sceneType)
     PurrSceneSettings{mode,physicsMode,isPublic}  └─ SwitchSceneUI 伪进度展示
                                                   └─ onYooAssetSceneLoaded → EndPassiveLoad()
                                                         └─ OnSceneLoadStart/Over/Ready 照常派发
```

### 4.2 服务器/DS 侧

- 入口：`NetworkSceneCoordinator.LoadSceneServer(SceneType sceneType)`（业务调用，规划里是 `GameApp.StartDedicatedServer()` 与后续业务切场景）。
- 实现要点：
  - `networkManager.isServer` 断言；非服务器打 Error 返回（与 PurrNet 语义一致）。
  - `SceneType → (packageName, location)`：复用 `GameModule.GameScene.GetSceneName(sceneType)` 拿 location；packageName 用本项目默认包（`DefaultPackage`，需确认与 `ProcedureInitPackage` 初始化的一致）。
  - `settings = new PurrSceneSettings { mode = LoadSceneMode.Single, physicsMode = LocalPhysicsMode.None, isPublic = true }`。
  - `isPublic=true` 表示把所有玩家拉进场景（官方文档 09-04 报告:106）；后续做「分房间/分组可见」再改。
  - **DS 下不打开 SwitchSceneUI**：UI 相关全部由 `IsDedicatedServerBuild` 守卫。
  - 完成信号：`onYooAssetSceneLoaded(sceneId, packageName, location, asServer=true)`；服务器没有「加载页」，但业务可在此派发 `OnSceneReady` 给本机逻辑（DS 上 `SpawnPointSceneSpawner` 需要）。

### 4.3 客户端侧（核心）

- **不再调用** `GameModule.GameScene.LoadScene(sceneType)` 走真实加载（那会双加载）。
- 新增 `GameSceneModule.BeginPassiveLoad(SceneType sceneType)`（被动模式，最小改动）：
  - 记录 `RecordScene(sceneType)`、派发 `OnSceneLoadStart`；
  - 置 `_isActive = true` 但 **不** 调 `StartRealLoading`；
  - 阶段推进改为「伪进度」：阶段 0 预热照旧；阶段 1 用一条受控的目标进度（例如按 `onYooAssetSceneStartLoading` 之后每帧向 0.9 缓动，或直接到 0.9），**true 完成点由 `onYooAssetSceneLoaded` 事件置 `_sceneLoadComplete = true`**；
  - 阶段 2 **不得调 `UnSuspend`**（场景不是 TEngine 加载的，没有挂起），激活完成判定改为「收到 `onYooAssetSceneLoaded` 后等待 N 帧」；
  - 收尾走现有 `FinishAndClose()`：关 `SwitchSceneUI`、派发 `OnSceneReady`、`_isActive=false`。
  - 若 3 秒内没收到加载完成事件，走超时兜底（沿用 `Phase2AbsoluteTimeout` 风格），派发 `OnSceneReady` 让场景至少可用，并打 Warning。
- **被动模式与现有主动模式互斥**：`_isActive` 保护不变；被动期间任何 `LoadScene` 调用仍被拒绝，避免竞态。
- 客户端「想切场景」的业务调用改为 `NetworkSceneCoordinator.RequestSceneChange(sceneType)`——即发 RPC 给服务器，由服务器执行 `LoadSceneServer`。

### 4.4 事件与下游兼容

- `OnSceneLoadStart`：被动模式开始时派发（与主动模式一致）。
- `OnSceneLoadOver`：收到 `onYooAssetSceneLoaded` 时派发（语义为「场景资源已就绪」，与主动模式的 90% 出口一致）。
- `OnSceneReady`：`SwitchSceneUI` 关闭后派发（保持 `DynamicSceneSpawner` 契约）。
- `OnDynamicSpawnComplete`：不变，由 Spawner 自己派发。

### 4.5 主场景/启动阶段保持现状

- 未联机（无 NetworkManager 或未 Start）时，`GameApp.StartGameLogic` 仍走 `GameModule.GameScene.LoadScene(SceneType.MainScene)`——不引入任何网络依赖。
- 联机建立后（`OnServerStarted/OnClientConnected` 之后）才启用协调器；DS 分支直接走 `LoadSceneServer`（替换现在的 `GameModule.GameScene.LoadScene` / `GameModule.Scene.LoadSceneAsync`，避免 DS 走 UI 流程）。
- 中途加入玩家：`SceneHistory` 会重放 `GetFullHistory()`，客户端收到 `LoadAddressableSceneAction` 自己加载 → 正好命中被动模式的 `onYooAssetSceneStartLoading`。

### 4.6 DS 分支改造点（对应阶段四 4.2 + 阶段三遗留项 3）

现状（`GameApp.cs:68-100`）：
- `--scene-type` → `GameModule.GameScene.LoadScene(sceneType)`（会打开 SwitchSceneUI，DS 无 UI，风险）；
- `--scene` → `GameModule.Scene.LoadSceneAsync(sceneName)`（TEngine 加载，不经过 PurrNet，服务器侧场景与客户端不同步）。

改造后：
- 两条路径统一为 `NetworkSceneCoordinator.LoadSceneServer(sceneType)`；
- `sceneName` 路径先经 `GetSceneTypeFromName` 反查 `SceneType`，查不到再退回 TEngine 直加载并打 Warning（兼容自定义场景名）；
- 前提：DS 场景里必须预挂 NetworkManager（阶段三遗留项 3），否则协调器没有 `sceneModule`。

### 4.7 失败与超时

| 场景 | 处理 |
|---|---|
| 服务器加载失败（YooAsset 返回无效 handle / Status != Succeeded） | 派发失败事件；DS 打 Fatal 并保持进程（不退出，便于排查） |
| 客户端等待 `onYooAssetSceneLoaded` 超时 | 强制收尾 + Warning，`OnSceneReady` 仍派发（避免黑屏卡死） |
| 客户端连接中断/重连 | PurrNet 重放历史；被动模式下若已 `_isActive`，重新进入被动流程前先 `FinishAndClose` 收尾 |
| 重复切场景请求 | `_isActive` + 服务器侧去重（同一 location 且已在加载则忽略） |

---

## 五、落地步骤（建议顺序，未实施）

1. **确认前提**：DS 场景与客户端启动场景都预挂 NetworkManager，且 NM 所在场景为 **DontDestroyOnLoad**（PurrNet Single 加载的硬要求，ScenesModule.cs:1238-1247）。
2. 新增 `Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkSceneCoordinator.cs`（业务层，跟随 NetworkModule 目录），提供 `LoadSceneServer` / `RequestSceneChange` / 事件桥接。
3. `IGameSceneModule` + `GameSceneModule` 增加 `BeginPassiveLoad(SceneType)` 与被动进度分支（最小侵入，复用状态机）。
4. `GameApp.StartDedicatedServer()` 改为协调器入口（保留命令行参数语义）。
5. 新增/扩展 `IGameSceneEvent`（或新接口 `INetworkSceneEvent`）：`OnServerSceneLoadComplete(SceneType)` / `OnSceneLoadFailed(SceneType, string)`，供 UI 与业务监听。
6. 验收：编辑器 Host 双端切场景、DS + 1 客户端、客户端中途加入、断线重连、DS 单独启动（无 UI 报错）。

**验证方式**：C# 构建（`workflow.py verify --profile code`）+ Unity Editor 运行（`unity` profile）；DS 产物需用户授权后真实构建运行（沿用阶段三的授权边界）。

---

## 六、风险与未确认项（必须标注）

1. **未确认**：客户端 `ProcessLoadYooAssetAction` 在 Single 模式下 `PrepareForSingleYooAssetSceneLoad()` 对 TEngine 已持有主场景 handle 的具体影响（是否只做 Unity 侧卸载、是否清理自己的注册表）。
2. **未确认**：`ScenePlayersModule` 的可见性（`isVisible`/`onSceneVisibilityChanged`）与 `isPublic` 的精确语义，以及分房间场景如何工作。
3. **未确认**：客户端 action 队列的 Tick 时机（`IFixedUpdate`）与 `onYooAssetSceneStartLoading` 到真正开始加载之间的延迟（决定伪进度的观感）。
4. **已知约束**：`purrnet-yooasset-v1:` key 由服务器发、各端解析，`packageName` 在客户端必须存在同名已初始化包（本项目 `DefaultPackage`），否则客户端解码后 `YooAssets.GetPackage` 会抛异常（ScenesModule.YooAsset.cs:144-160 有 try/catch，只会 Error 不崩）。
5. **已知约束**：普通 `LoadSceneAsync`（buildIndex 版）要求场景在 build settings；本项目热更场景必须走 YooAsset 重载。
6. **风险**：被动模式下客户端进度条是伪进度，用户能感知「进度走得比真实加载快/慢」；后续若要真实进度，需要 PurrNet 暴露内部 handle（改包，触碰红线）或改用方案 B。
7. **风险**：DS 无 NetworkManager 时协调器为 null，必须先补阶段三遗留项 3。
8. **风险**：`_currentMainScene` 脏引用问题若在联机切场景后触发，表现为 `IsSceneLoadDone/Unload` 异常；建议被动模式下不依赖 TEngine 主场景 handle。

---

## 七、对计划文档的建议

- `.plans/purrnet-ds-integration.md` 步骤 4.2 可据此更新为「方案 A + 被动加载页」，并把「服务器权威 / 客户端被动 / 事件桥接」写成验收项。
- 与 4.1（对象池桥接）、4.3（NetworkRules）、4.4（网络 Prefab 注册）无冲突，可并行。
- 与阶段三遗留项 3（DS 场景预挂 NM）强相关：**没有 NM 就没有 `sceneModule`，4.2 无法落地**。
