# PurrNet DS 场景加载协调（阶段四 4.2）实施计划

> **状态**：计划已落盘，**未实施**
> **来源**：`.plans/purrnet-ds-integration.md` 阶段四 步骤 4.2
> **研究依据**：[PurrNet × TEngine 场景加载协调研究](../conversation-summaries/code-research/2026-10-18-purrnet-scene-coordination-research.md)
> **创建时间**：2026-10-18
> **最近更新**：2026-10-24（多轮：按当前源码重写 → 收敛为纯场景加载协调 → 补齐实施缺口；详见第十二节「本次更新说明」）
> **优先级**：中（有实际联机场景时为前置必需）
> **执行授权**：需用户明确要求后开始；Unity 场景/预制体写入、真实 DS 构建须单独确认

---

## 零、先看这段白话（不涉及代码）

切场景就像剧组换拍摄场地，现在**两拨人都在管换场地**：

- **TEngine 的加载页**（`SwitchSceneUI`）：先把场地搭好但不上人，进度条走到 80% 再喊“开拍”——进度条好看，卡顿被盖住。
- **PurrNet（联机库）**：规定**只有服务器/房主有资格喊换场地**，玩家收到通知后自己搭场地，而且**一搭好就直接开拍**，不等进度条。

两拨人同时动手就会出事：场地搭两次拆两次、进度条乱跳，最惨是场景里的东西没人生成，玩家进去**一片空**。

**结论**：让服务器一个人说了算，玩家端别自己搭场地，只把进度条摆出来给人看。

1. **服务器换场景**：房主/专用服务器用 PurrNet 的方式喊“换”，这是唯一真源。
2. **玩家端只看不干**：收到“要换场地了”的通知后打开加载页、跑**假进度**（按时间爬，够用），收到“搭好了”的通知再收进度条、通知场景生成东西。
3. **玩家想换场景只能申请**：点按钮不是自己换，而是告诉服务器“我想去 X”，服务器同意后再走第 1 步。

> **2026-10-24 补充（合并 main 后）**：TEngine 侧加载页现在比原来精细得多——进度条分**七个阶段**跑，而且**最后 10%（90%→100%）是留给“场景里的东西生成”的**（`RegisterInitTask` 门闩）。所以“玩家端只看不干”这句要说清楚：**被动端也要走完这七个阶段**，只是第 1 阶段发起真实加载的那一步不归它做，改由服务器的加载完成事件来“顶”进度。

---

## 一、目标与验收

### 1.1 目标

1. **场景加载只有一个驱动方**：服务器（DS 或 Host）权威发起，客户端不再自行加载。
2. **保留 TEngine 加载页体验**：客户端仍能看到 `SwitchSceneUI` 加载页，场景激活后 `OnSceneReady` 照常派发，`DynamicSceneSpawner` 等下游不受影响。
3. **不动框架、不动 PurrNet 包**：所有新增逻辑放业务层（`GameScripts/HotFix/GameLogic`）。

### 1.2 验收标准（可逐条勾验）

- [ ] 服务器调用协调器切场景后，客户端自动跟随，且**同一场景只被加载一次**（无重复 `LoadSceneAsync` 日志）。
- [ ] 客户端加载过程中能看到 `SwitchSceneUI`，进度条不跳变、不卡死，且**七阶段语义与主动模式一致**（能走到 100%，不卡在 80%/90%）。
- [ ] 场景激活后客户端 `OnSceneReady` 正常派发，场景内 `DynamicSceneSpawner` 生成对象成功，其 `RegisterInitTask` 上报的进度能驱动 90%→100% 段（**该 Spawner 必须为 `LoadingWait` 模式**）。
- [ ] DS 启动（无 UI）不报 `NullReferenceException`、不黑屏退出；日志可见 `[NetworkScene]` 关键节点。
- [ ] 服务器侧场景加载完成后 `GameModule.GameScene.CurrentSceneType` 与目标场景一致，场景内 `DynamicSceneSpawner` 在非编辑器构建下仍走 YooAsset 分支。
- [ ] 客户端中途加入：进服后自动恢复服务器当前场景，并走同一套被动加载页。
- [ ] 客户端断线重连：场景状态恢复，不出现双场景并存。
- [ ] 未联机（单机）流程完全不受影响：`GameApp` 启动仍走 `GameModule.GameScene.LoadScene(SceneType.MainScene)`。
- [ ] 客户端在本地直接调用场景切换 API 时被拒绝并给出明确日志（不允许绕过服务器）。
- [ ] **各客户端加载页独立关闭**（不互相等待、互不阻塞）。

---

## 二、现状与约束（已核实，带证据）

> 本节于 2026-10-24 按当前源码**重新核实**，行号与文件名为磁盘现状，与 2026-10-18 版本不同处已在第十二节列出。

### 2.1 三套体系一句话

| 体系 | 位置 | 职责 |
|---|---|---|
| `SceneModule` | `Assets/TEngine/Runtime/Module/SceneModule/` | YooAsset 场景句柄管理、挂起/激活/卸载 |
| `GameSceneModule` | `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs` | **七阶段进度状态机**、加载页展示、业务事件、初始化任务门闩 |
| PurrNet `ScenesModule` | 包缓存 `Runtime/CoreModules/Scenes/` | 服务器权威场景指令、SceneID、历史重放、YooAsset 场景桥接 |

**加载页实际类名**：`SwitchSceneUI`（`Assets/GameScripts/HotFix/GameLogic/UI/Core/SwitchSceneUI/SwitchSceneUI.cs`），`[Window(UILayer.UI, location: "SwitchSceneUI")]`，每帧只读 `GameModule.GameScene.DisplayProgress` 与 `DisplayPhaseText`。**注意：不存在 `SwitchUI` 类**，历史文档里出现的 `SwitchUI` 字样是旧命名，计划中统一用 `SwitchSceneUI`。

### 2.2 硬约束（决定方案的根因）

| 约束 | 事实 | 证据 |
|---|---|---|
| 只有服务器能加载 | `if (!_asServer) { LogError("Only server can load scenes"); return null; }` | `ScenesModule.YooAsset.cs:229-233` |
| 只有服务器能卸载 | `if (!_asServer) { LogError("Only server can unload scenes; for now at least ;)"); ... }` | `ScenesModule.YooAsset.cs:397-401`、`416-420` |
| 客户端加载**无挂起窗口** | YooAsset 场景加载第 4 参硬编码 `true`（`allowSceneActivation`），两条路径都是 | `ScenesModule.YooAsset.cs:148-153`（action 路径）、`:263`（`LoadYooAssetSceneAsync` 路径） |
| TEngine 主动模式依赖挂起 | `LoadSceneAsync(..., suspendLoad: true, ...)` → 阶段 2 检查点 `UnSuspend` → 阶段 3 轮询 `IsSceneLoadDone` | `GameSceneModule.cs:710-712`、`:759`、`:428` |
| NM 必须在 DontDestroyOnLoad 场景 | Single 加载前检查，不合格**只报 Error 不中断**（危险：场景仍会加载，等于把 NM 所在场景卸载掉） | `ScenesModule.YooAsset.cs:301-310` |
| Host 不重复加载 | 服务器加载时把同一 `handle` 塞给 client 模块（`ownsHandle=false`） | `ScenesModule.YooAsset.cs:274-284` |
| 中途加入自动恢复 | 服务器写 `_history`（`AddLoadAddressableAction`），新玩家重放 | `ScenesModule.YooAsset.cs:252-257`、`603-620` |
| 场景完整性由 PurrNet 记账 | 加载完成后 `AddScene` + `RegisterYooAssetScene` + 派发 `onYooAssetSceneLoaded` | `ScenesModule.YooAsset.cs:106-117` |
| `PrepareForSingleYooAssetSceneLoad` 会清理旧场景 | Single 加载前卸载所有非 DontDestroyOnLoad 的已注册场景与挂起操作 | `ScenesModule.YooAsset.cs:312-334` |
| DS 不能开加载页 | DS 分支跳过 `LauncherMgr.Initialize()`，`ShowUI` 会 NRE | 阶段三经验（`ProcedureInitResources` 4 处 DS 守卫） |

### 2.3 当前 `GameSceneModule` 七阶段状态机（**本次更新核心**）

`_currentPhase` 取值与语义（`GameSceneModule.cs:252` 注释 + `Update` 实现）：

| 阶段 | 名称 | 进度段 | 驱动/出口 |
|---|---|---|---|
| 0 | 预热 | 0→10% | `WarmupSpeed`；到 0.10 调 `StartRealLoading()` 进 1 |
| 1 | 加载 | 10%→80% | `progressCallBack`（`OnLoadProgress`）映射 YooAsset 0~0.9 → 10%~80%；`_sceneLoadComplete && ≥0.79` 或超时兜底 → 2 |
| 2 | 「80%」停帧检查点 | 固定 0.80 | 停留 `StageCheckpointFrames`(2) 帧后调 `EnterFinishPhase()`（派发 `OnSceneLoadOver` + `UnSuspend`）→ 3 |
| 3 | 激活 | 80%→90% | 轮询 `GameModule.Scene.IsSceneLoadDone(_sceneName)`；`Phase2AbsoluteTimeout`(30s) 兜底 → 4 |
| 4 | 「90%」停帧检查点 | 固定 0.90 | 停留 2 帧 → 5，重置 `_phase3Elapsed`/`_lastInitTarget` |
| 5 | 业务就绪 | 90%→100% | `ComputeInitProgress()` 按 `_initTasks` 平分；`Phase3AbsoluteTimeout`(30s) 兜底；到 1.0 → 6 |
| 6 | 100% 帧等待 | 固定 1.0 | 等「任务全完成 + 激活后 `PostActivateFrames`(2) 帧 + 100% 停留 `_holdAt100Duration`(0.5s)」→ `FinishAndClose()` |

**关键常量**（`GameSceneModule.cs:260-310`）：`DefaultWarmupDuration=0.7s`、`DefaultFinishDuration=2s`、`DefaultHoldAt100Duration=0.5s`、`LoadingSpeed=1`、`InitSegmentSpeed=1`、`StageCheckpointFrames=2`、`SkipSpeed=100`、`Phase1StallTimeout=60s`、`Phase1AbsoluteTimeout=180s`、`Phase2AbsoluteTimeout=30s`、`PostActivateFrames=2`、`Phase3AbsoluteTimeout=30s`。

**统一终结出口** `FinishAndClose()`（`:771-791`）：`_finishCallBack?.Invoke()` → `CloseUI<SwitchSceneUI>()` → `GameEvent.Get<IGameSceneEvent>().OnSceneReady(_sceneType)` → `_isActive=false`。`_isFinished` 防重。

**会话启动入口** `StartSceneLoad()`（`:641-703`）：`_isActive` 时**直接拒绝**并打 Error（不触发回调）；重置全部会话字段 → `RecordScene` → `OnSceneLoadStart` → 判定 `_skipMode` → `GameModule.UI.ShowUI<SwitchSceneUI>()`。

**陷阱 1**（`:47`）：`suspendLoad=true` + `progressCallBack` 时 `LoadSceneAsync` 内部 `while(!IsDone)` 死循环，故只 fire-and-forget。
**陷阱 2**（`:50`）：`_currentPhase >= 2` 后 `OnLoadProgress` 直接 return，保护 target=1.0 不被打回 0.90。

### 2.4 初始化任务门闩 `RegisterInitTask`（**新增能力，必须在方案中处理**）

签名（`IGameSceneModule.cs:47/55`，实现 `GameSceneModule.cs:508/519`）：

```csharp
void RegisterInitTask(UniTask task, string label = null);
void RegisterInitTask(UniTask task, Func<float> progressProvider, string label = null);
```

- 语义：注册后加载页**阶段 5/6 会等它完成**，进度条 90%→100% 按任务数平分驱动（`ComputeInitProgress()`，`GameSceneModule.cs:566-588`：各任务进度求和 ÷ 任务数，已完成按 1 计）。
- 已完成场景：`Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/DynamicSceneSpawner.cs:215` 已调用 `GameModule.GameScene.RegisterInitTask(SpawnAsync(), () => _spawnProgress, GetType().Name)`。
- **越界语义**：`RegisterInitTask` 在 `!_isActive` 时**直接 `Forget()` 后台执行、不参与等待**（`:523-527`）。
- **对被动模式的意义**：被动模式必须让 `_isActive` 在正确时刻为 true，否则场景内 `DynamicSceneSpawner` 的注册会**静默失效**（不报错、只是不等），加载页会“任务没等就关”。这是被动模式最容易踩的坑。

### 2.5 场景枚举改为自动生成（**本次更新核心**）

`SceneType` 等**已不再是手写枚举**，由 `Assets/Editor/SceneTools/SceneEnumGenerator/SceneEnumConfig.asset` 生成，勿手改：

| 文件 | 内容 |
|---|---|
| `Module/GameScene/Generated/SceneType.g.cs` | `enum SceneType`（值 = 哈希 ID） |
| `Module/GameScene/Generated/SceneConstName.g.cs` | `const string XxxName`（资源地址常量） |
| `Module/GameScene/Generated/SceneTypeMapping.g.cs` | 双向映射 `GetSceneName` / `GetSceneTypeFromName` |
| `Module/GameScene/Generated/SceneTypeDisplayName.g.cs` | 中文显示名 |

**当前实际场景清单（仅 2 个）**：

```csharp
SceneType.MainScene        = 11975355;     // const string MainSceneName        = "MainScene"
SceneType.dedicated_server = 1552302036;   // const string dedicated_serverName = "dedicated_server"
```

`GetSceneTypeFromName` 同时匹配资源地址与枚举名（`GameSceneModule.cs:221-236`）。

> **影响**：新增联机场景必须走 `SceneEnumConfig` 配置工具生成，**不得手写枚举**（红线：不补丁生成产物）。S1/S5 中凡涉及“指定场景”的地方都要按生成后的 `SceneType` 取值书写。

### 2.6 现状代码位置（改造点）

- `GameApp.cs:42-59` `StartGameLogic()`：注册 `IUIJumpControl` / `IGameSceneModule` / `INetworkModule`；DS 分支进 `StartDedicatedServer()`，否则 `Screen.ApplyAll()` + `LoadScene(SceneType.MainScene)`。
- `GameApp.cs:68-100` `StartDedicatedServer()`：`--scene-type` 走 `GameSceneModule.LoadScene(SceneType)`（**会开加载页 → DS 无 UI 会 NRE**）；`--scene` 走 `GameModule.Scene.LoadSceneAsync(location)`（绕开 PurrNet）；未指定 fallback `MainScene`。
- `GameSceneModule.StartSceneLoad()`：固定主动加载流程，需增加被动入口。
- `GameSceneModule.CurrentSceneType` / `GameValueStatic.CurrentSceneType`：被 `DynamicSceneSpawner.IsNormalBoot`（`DynamicSceneSpawner.cs:234`）用于判定 YooAsset 模式；服务器路径必须写回（S2/S3），否则非编辑器构建下动态对象不生成。
- `NetworkModule.cs`：已封装 NM 生命周期（`NetworkManager` / `IsServer` / `IsHost` / `IsAutoStarted` / `BindNetworkManager` / `StartServer|Client|Host` / `StopNetwork`），可作为协调器宿主。
- `IGameSceneEvent`：现有 4 个事件（`OnSceneLoadStart` / `OnSceneLoadOver` / `OnDynamicSpawnComplete` / `OnSceneReady`），被动模式需复用并可能扩展。

---

## 三、方案决策

### 3.0 谁负责关加载页（**2026-10-24 澄清**）

**结论：各客户端自己关，服务器不参与。**

`SwitchSceneUI` 是**每个客户端各自的 UI**，`GameSceneModule` 也是每个端各自的实例；服务器端（DS）根本没有 UI。客户端自己就知道什么时候可以关——七阶段状态机跑到阶段 6 自然就关，不需要服务器远程命令。

```
客户端：被动加载场景 → 阶段 5 等自己的动态初始化（RegisterInitTask）
        → 阶段 6 关掉自己的加载页 → 派发 OnSceneReady
```

**责任边界**：本计划只负责场景加载与加载页；加载页的关闭是各端本地动作，互不等待。

### 3.1 选定方案 A：服务器权威 + 客户端被动加载页

| 备选 | 结论 |
|---|---|
| **A. 服务器权威 / 客户端被动**（选定） | 场景一致性由 PurrNet 保证，改业务层，保留加载页 |
| B. 自建 RPC 广播 + 各端自行加载 | 放弃 PurrNet 场景模块（SceneID/历史/重连全要自研），不推荐 |
| C. 改造 PurrNet 包支持挂起 | **违反「不主动改框架/包」红线，排除** |

### 3.2 关键决策记录（含本次更新）

| 决策点 | 选择 | 理由 |
|---|---|---|
| 场景加载发起方 | 服务器唯一 | PurrNet 硬约束（`:229-233`） |
| 客户端进度来源 | 伪进度 + 事件驱动 | PurrNet 客户端路径硬编码 `allowSceneActivation=true`，**拿不到挂起期进度**，也不为此改包 |
| 客户端加载完成判定 | `onYooAssetSceneLoaded` 事件 | 唯一可靠的完成信号（`:55`、`:112-117`） |
| 被动模式复用的状态机 | **完整七阶段**（0~6） | 与主动模式行为对齐，避免 80%/90% 语义漂移 |
| 被动模式阶段 2 是否 `UnSuspend` | **不调用** | 客户端场景已由 PurrNet 直接激活，无挂起可解；调用会误伤 |
| 被动模式阶段 3 如何判定激活 | 以 `onYooAssetSceneLoaded` 为完成信号 + N 帧确认，**不轮询 `IsSceneLoadDone`** | `IsSceneLoadDone` 走 TEngine `SceneModule` 的句柄表，被动路径下无对应句柄 |
| 被动模式阶段 5 门闩 | **正常启用**（`_isActive` 必须为 true） | 场景内 `DynamicSceneSpawner` 依赖它上报进度（`DynamicSceneSpawner.cs:215`） |
| 服务器侧场景状态 | **加载前写回，失败回滚** | `DynamicSceneSpawner.IsNormalBoot` 依赖 `CurrentSceneType`；晚写会让服务器动态对象不生成 |
| Host 加载页 | **第一版不开** | Host 的 client 模块只有 `loaded`、没有 `start`，开页会闪且无进度 |
| 协调器订阅 | `GetModule<ScenesModule>(asServer)` + `onNetworkStarted` | `nm.sceneModule` 在 Host 下只返回 server 模块；懒查找可能晚于网络启动 |
| `--scene` 未命中 | Error + fallback `MainScene` 走协调器 | 裸 `SceneModule.LoadSceneAsync` 不写状态，会把已知 bug 留在启动路径 |
| 客户端切场景 | 请求服务器 | 客户端无权限 |
| DS 是否走加载页 | 否 | 无 UIRoot，`SwitchSceneUI` 会 NRE |
| 单机流程 | 完全不变 | 未联机不引入网络依赖 |
| 桥接放哪 | 业务层 `NetworkModule` 目录 | 不改框架、不散落 |
| 场景枚举来源 | `SceneEnumConfig` 生成 | 生成产物不可手改 |

---

## 四、实施步骤

> 每一步独立可验证；顺序有依赖，S1 不做则 S2~S6 无法联调。

### S0 前置核实（**本次新增，实施第一步必做**）

- **目的**：源码持续演进，实施前必须重新对齐。
- **动作**：
  1. 重新读 `GameSceneModule.cs`，确认阶段数、常量名、`RegisterInitTask` 签名未变；
  2. 重新读 `IGameSceneModule.cs`，确认接口方法集；
  3. 重新读 `ScenesModule.YooAsset.cs`，确认 `LoadYooAssetSceneAsync` 签名与事件签名；
  4. 确认 `SceneEnumConfig` 当前场景清单（生成文件 4 件套）；
  5. 确认 `DynamicSceneSpawner.IsNormalBoot` 仍以 `CurrentSceneType != null` 判定（服务器路径必须写回状态，否则会退化为测试分支）。
- **验收**：上述五项与本节描述一致；不一致则以源码为准并同步修订本文件。

### S1 前置：确认/改造场景挂载（Unity 对象操作，需单独授权）

- **目标**：DS 场景与客户端启动场景都挂上 `NetworkManager`（含 Transport），且 **NM 所在对象所在场景为 DontDestroyOnLoad**。
- **涉及**：`Assets/AssetRaw/Scenes/dedicated_server.unity`、客户端主场景，必要时新建“启动壳场景”。
- **风险提示（本次强化）**：`PrepareForSingleYooAssetSceneLoad`（`ScenesModule.YooAsset.cs:301-310`）在 NM 场景非 DontDestroyOnLoad 时**只打 Error 不 return**，随后会继续执行 `:312-334` 的清理逻辑——把已注册的非 DDOL 场景全部卸载。也就是说**这条检查失败不会中断，而是静默拆掉场景**，上线后表现为“切场景后世界空了”。必须作为联调必查项。
- **验收**：运行日志中无 “Network manager scene is not DontDestroyOnLoad” 报错；`GameModule.Network.NetworkManager` 非空。
- **注意**：场景/Prefab 写入属 Unity 操作，实施前先预览具体对象与范围并集中确认；不给 `.meta` 手工编辑。

### S1.5 联调测试场景（Unity 对象操作，需单独授权）

- **目的**：当前 `SceneType` 只有 `MainScene` / `dedicated_server`，且两个场景内都没有 `DynamicSceneSpawner`；S6 的“被动加载页 + 90%→100% 进度”验收没有载体。
- **新增**：一个联机测试场景（建议 `network_test`），通过 `SceneEnumConfig` 工具注册并重新生成 4 件套，**不手写枚举**。
- **场景内容**：
  - 一个 `DynamicSceneSpawner` 子类，`initMode = LoadingWait`，至少 2~3 个可加载项（用于验证 `RegisterInitTask` 与进度驱动）；
  - 场景资源加入与 `MainScene` 相同的 YooAsset 收集列表/package（`DefaultPackage`）。
- **已有基础**：`Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset:111/124` 已收集 `Assets/AssetRaw/Scenes`；`Assets/Editor/SceneTools/SceneEnumGenerator/SceneEnumConfig.asset:15` 的 `SceneFolder` 也是该目录，新增场景放进去即可被收集与枚举扫描。
- **NM 要求**：测试场景本身**不要**挂 `NetworkManager`；NM 必须留在 DontDestroyOnLoad 的启动场景（见 S1）。
- **验收**：`MainScene ↔ network_test` 双向切换，客户端被动进度到 100%，服务器侧对象生成成功。
- **注意**：新增场景与 Spawner 子类均属 Unity 资源写入，实施前先预览并集中确认。

### S2 新增协调器 `NetworkSceneCoordinator`

- **新增文件**：`Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkSceneCoordinator.cs`
- **职责**：
  - `LoadSceneServer(SceneType)`：仅服务器可调（`!nm.isServer` 直接 Error 返回 false）；`SceneType → location` 复用 `GameModule.GameScene.GetSceneName`；调用 `nm.GetModule<ScenesModule>(true).LoadYooAssetSceneAsync(packageName, location, settings)`（**按包名在前的重载**，`:224-228`）。不要用 `nm.sceneModule`（Host 下它只返回 server 模块）。
  - **服务器侧状态写回（本次补缺口，S5 的前置）**：在调用 `LoadYooAssetSceneAsync` **之前**调 `GameModule.GameScene.NotifyExternalSceneLoadStarting(sceneType)`。原因：场景 `Awake` 早于 `onYooAssetSceneLoaded`，晚写会导致 `DynamicSceneSpawner.IsNormalBoot == false`，非编辑器构建下动态对象全部不生成。加载失败/超时时调 `NotifyExternalSceneLoadFailed()` 回滚。
  - **服务器侧完成收口（本次补缺口）**：`onYooAssetSceneLoaded(asServer=true)` 时调 `GameModule.GameScene.NotifyExternalSceneLoaded()`；该方法只派发 `OnSceneReady(sceneType)`，不关加载页、不结束会话（服务器本来就没有会话）。
  - **服务器侧失败检测（本次补缺口）**：`LoadYooAssetSceneAsync` 返回的 `YooAssetSceneHandle` 订阅 `Completed`；`Status != EOperationStatus.Succeeded` 时调 `NotifyExternalSceneLoadFailed()` 并派发 `OnSceneLoadFailed`。PurrNet 只在成功时派发 `onYooAssetSceneLoaded`，失败只打日志（`:119-123`），不能只等事件。
  - **`packageName` 必须取 `"DefaultPackage"`**，不要传空串——`LoadYooAssetSceneAsync` 对空包名直接 LogError 返回 null（`:235-239`）。项目内 `AssetReference.cs:27` 也以 `"DefaultPackage"` 为默认值。
  - **`PurrSceneSettings` 精确写法**（结构体定义见 `ScenesModule.cs:41-46`，字段类型照抄否则编译不过）：
    ```csharp
    var settings = new PurrSceneSettings
    {
        mode        = LoadSceneMode.Single,        // UnityEngine.SceneManagement
        physicsMode = LocalPhysicsMode.None,       // UnityEngine.SceneManagement.LocalPhysicsMode
        isPublic    = true
    };
    ```
  - `RequestSceneChange(SceneType)`：客户端向服务器发请求，由服务器校验后转 `LoadSceneServer`。传输优先用 `nm.playerModule.SendToServer(new SceneChangeRequest { sceneType = ... })` + 服务器 `Subscribe<SceneChangeRequest>`；**不在协调器（普通类）里声明 `[ServerRpc]`**。若构建时该结构体未被 PurrNet 代码生成覆盖，再退回“在 NM 的 `NetworkIdentity` 上挂一个最小 `NetworkModule` 组件承载 RPC”（Unity 资源写入，需单独授权）。
  - **订阅对象与绑定时机（本次补缺口）**：
    - 服务器侧订阅 `nm.GetModule<ScenesModule>(true)`；客户端侧订阅 `nm.GetModule<ScenesModule>(false)`，且**仅当 `!nm.isHost`**。
    - `NetworkModule.OnInit` 只 `new`，不订阅；`Bind(nm)` 时若网络已启动（`isServer`/`isClient`）立即订阅，否则挂 `nm.onNetworkStarted` 等启动后再订阅。
    - `Unbind()` 退订全部事件并复位 `IsTransitioning`；同时订阅 `nm.onNetworkShutdown`，在断线时清理订阅，保证重连后能重新订阅。
    - `Bind` 时同步 `GameModule.GameScene.SetLocalLoadAllowed(!(nm.isClient && !nm.isHost))`；`onNetworkStarted` 回调里再同步一次（启动前后 role 可能不同）；`Unbind`/`Shutdown` 恢复为 `true`，避免退出联机后单机流程被锁死。
  - **客户端被动入口（本次补缺口，必须同步）**：`onYooAssetSceneStartLoading(asServer=false)` 回调里**同步**调 `BeginPassiveLoad(sceneType)` + `NotifyPassiveLoadStarted()`，不得延迟到下一帧（必须早于场景 `Awake`，否则阶段 5 门闩失效）；`onYooAssetSceneLoaded(asServer=false)` 时调 `NotifyPassiveLoadCompleted()`。若 loaded 到达时没有活跃被动会话（Host 或不按序），打 Warning 并忽略，不补开页面。
  - 去重与超时：同一 `location` 正在加载时忽略重复请求（可复用 `IsYooAssetSceneLoading(packageName, location)`，`:356-366`）；等待完成超时打 Warning 并回滚场景状态。
- **Host 模式关键坑**：Host 下服务器与客户端是同机两个 `ScenesModule` 实例，`LoadYooAssetSceneAsync` 会把**同一 handle** 塞给 client 模块（`:274-284`）。协调器必须**严格按回调里的 `asServer` 分流**，只处理自己那一侧，否则 Host 会收到两次、触发两轮被动加载。
- **Host 第一版行为（本次明确）**：Host **不打开**被动加载页。Host 的 client 模块只收到 `loaded`、收不到 `start`，开页会闪一下且没有真实进度；统一按服务器分支处理（写回状态 + `NotifyExternalSceneLoaded`）。若后续要给 Host 开页，需单独设计，不在本计划范围。
- **宿主方式**：**不要**做成 `Module`（会多一个需要 `ModuleSystem.RegisterModule` 注册的类型）。做成 `NetworkModule` 内部持有的普通类：`NetworkModule.OnInit` 里 `new` + `Bind`，`Shutdown` 里 `Unbind()`。
- **验收**：编辑器 Host 调用一次，客户端收到事件；日志可见完整链路（含 `Bind`/`Subscribe` 记录）。

### S3 `GameSceneModule` 增加被动模式（**本节按七阶段状态机重写**）

- **修改**：`IGameSceneModule.cs`、`GameSceneModule.cs`
- **新增接口**：

```csharp
/// <summary>被动模式：不发起加载，只展示进度并等待外部完成通知。</summary>
void BeginPassiveLoad(SceneType sceneType);

/// <summary>被动模式：外部通知「服务器已开始加载」。</summary>
void NotifyPassiveLoadStarted();

/// <summary>被动模式：外部通知「服务器场景已加载完成并激活」（由协调器在 onYooAssetSceneLoaded 时调用）。</summary>
void NotifyPassiveLoadCompleted();

/// <summary>外部加载方专用（服务器）：加载前写回场景状态，不启动本模块加载会话、不打开加载页。</summary>
void NotifyExternalSceneLoadStarting(SceneType sceneType);

/// <summary>外部加载方专用（服务器）：加载失败时回滚到进入前的场景状态。</summary>
void NotifyExternalSceneLoadFailed();

/// <summary>外部加载方专用（服务器）：加载完成，派发 OnSceneReady（不关页、不结束会话）。</summary>
void NotifyExternalSceneLoaded();

/// <summary>是否允许本端主动发起场景加载（联机客户端由 NetworkModule 关闭；单机/服务器保持开启）。</summary>
void SetLocalLoadAllowed(bool allowed);
```

- **改动要点（逐阶段对齐）**：
  - **入口 `BeginPassiveLoad`**：复用 `StartSceneLoad` 的会话初始化与 `RecordScene` / `OnSceneLoadStart` / `ShowUI<SwitchSceneUI>`，但把 `StartRealLoading()` 替换为**只登记被动标志**（新增 `_isPassive`）。`_isActive = true` 必须在打开加载页**之前**置位，否则阶段 5 门闩失效。由协调器在 `onYooAssetSceneStartLoading` 回调里**同步**调用，保证早于场景 `Awake`。
  - **阶段 0（预热 0→10%）**：与主动一致（纯动画，不依赖加载）。
  - **阶段 1（10%→80%）**：**不调** `StartRealLoading()`；改用**伪进度**——从收到 `NotifyPassiveLoadStarted` 起，按时间以受 `LoadingSpeed` 约束的速率爬向 0.79；真实完成信号来自 `NotifyPassiveLoadCompleted`。
    - 设 `_sceneLoadComplete = true` 后仍按原判定 `_sceneLoadComplete && ≥0.79 → 阶段 2`。
    - **超时兜底必须保留**：被动模式下 `_lastLoadProgress` 恒为 0，`Phase1StallTimeout` 的“progress > 0 才累计”分支永不触发，因此**只有 `Phase1AbsoluteTimeout`（180s）在兜底**。建议为被动模式单独加更短的绝对超时（如 30s）并在超时后走兜底收尾，避免玩家长时间卡在 80% 以下。
  - **阶段 2（80% 检查点）**：保留停帧，但 **`EnterFinishPhase()` 内部按 `_isPassive` 分流**：被动模式**派发 `OnSceneLoadOver()` 但不调 `GameModule.Scene.UnSuspend(_sceneName)`**（无挂起可解，且 PurrNet 已完成激活）。
  - **阶段 3（80%→90%）**：`_sceneActivated` 的判定改为**被动模式直接以 `_sceneLoadComplete`（即 `NotifyPassiveLoadCompleted` 已到达）为准 + 等待 N 帧**，**不轮询** `GameModule.Scene.IsSceneLoadDone(_sceneName)`；`Phase2AbsoluteTimeout` 保留兜底。
  - **阶段 4（90% 检查点）**：与主动一致。
  - **阶段 5（业务就绪 90%→100%）**：**与主动完全一致**，`_initTasks` 门闩正常工作——这是被动模式的关键价值点（`DynamicSceneSpawner` 借此上报生成进度）。**不调** `GameModule.Scene.UnSuspend`，其余不动。`Phase3AbsoluteTimeout`(30s) 保留。
  - **阶段 6（100% 帧等待）与 `FinishAndClose`**：**完全复用**，`OnSceneReady(_sceneType)` 照常派发。`LoadingWait` 模式的 `DynamicSceneSpawner` 已在阶段 5 被等待；`SceneReady` 模式的 Spawner 会在此时才开始生成。
  - **并发保护**：保留 `_isActive` 硬保护；被动期间拒绝主动 `LoadScene` 请求并报 Error（`:644-651` 已有逻辑，无需改动）。
  - **新增字段**：`_isPassive`（会话级，在 `StartSceneLoad` 重置，`Shutdown` 清理）。
  - **外部状态写回（本次补缺口）**：三个 `NotifyExternalScene*` 方法只操作 `RecordScene` / `CurrentSceneType` / `GameValueStatic` 与 `OnSceneReady` 事件，**不**改 `_isActive`、**不**开 UI、**不**启动七阶段。`NotifyExternalSceneLoadStarting` 保存进入前快照（供失败回滚），重复调用只更新一次；`NotifyExternalSceneLoadFailed` 恢复快照并记 Error，不派发 `OnSceneReady`；`NotifyExternalSceneLoaded` 在 `CurrentSceneType` 非空时派发 `OnSceneReady(CurrentSceneType)`，为空则记 Error 并跳过，且**不调** `FinishAndClose`。
  - **本地加载拦截（本次补缺口）**：新增 `SetLocalLoadAllowed(bool)`，在**主动入口** `LoadScene` / `JumpToMainScene` 检查（**不要**放在 `StartSceneLoad` 内部，避免误伤 `BeginPassiveLoad`）；不允许时打 Error（提示走 `RequestSceneChange`）并直接 return，不触发回调。默认允许，保证单机流程不变；由 `NetworkModule` 在绑定/断线时切换（联机客户端 `!isHost` 时关闭）。
  - **服务器侧 `OnSceneReady` 语义（本次明确）**：只代表场景已激活，**不保证** `LoadingWait` 动态生成完成。服务器业务若需等待生成，监听 `OnDynamicSpawnComplete`（多个 Spawner 时自行聚合）。
- **验收**：被动模式下进度条平滑到 100%，`SwitchSceneUI` 正常关闭，`OnSceneReady` 派发，`DynamicSceneSpawner` 的 `RegisterInitTask` 被等到。

### S4 事件扩展

- **修改**：新增 `INetworkSceneEvent`（优先新增，避免污染 `IGameSceneEvent` 契约）。
- **事件**：`OnServerSceneLoadRequested(SceneType)`、`OnServerSceneLoadCompleted(SceneType)`、`OnSceneLoadFailed(SceneType, string reason)`。
- **注意**：`OnSceneReady` 仍走 `IGameSceneEvent`，不改语义，避免 `DynamicSceneSpawner` 改动。`OnSceneLoadOver` 在被动模式下也照常派发（S3 已说明），语义保持“资源已到可激活状态”。

### S5 `GameApp.StartDedicatedServer()` 改造

- **修改**：`Assets/GameScripts/HotFix/GameLogic/GameApp.cs`
- **改动**：
  - `--scene-type`：改为 `coordinator.LoadSceneServer(sceneType)`（不再打开加载页，内部先写回场景状态再发起加载）。
  - `--scene`：先 `GetSceneTypeFromName(sceneName)` 反查；命中走协调器。**未命中不再退回裸 `GameModule.Scene.LoadSceneAsync`**（该路径不写 `CurrentSceneType`，会让服务器 `DynamicSceneSpawner` 退化为测试分支），改为记 Error 并 fallback `MainScene` 走协调器。
  - 未指定场景仍 fallback `MainScene`，但同样走协调器（保证服务器与客户端一致）。
- **验收**：DS 启动日志顺序正确、无 NRE、场景加载完成、无 “Only server can load scenes”。

### S6 联调与观测

- 增加 `[NetworkScene]` 前缀日志：请求、开始加载、加载完成、激活、收尾、超时、绑定/订阅、状态写回/回滚。
- **联调矩阵（本次补缺口）**：
  1. 编辑器 Host：`MainScene ↔ network_test` 各一次；
  2. DS + 1 客户端：DS 启动加载 `network_test`，客户端跟随并走完被动七阶段；
  3. 中途加入：客户端在切换完成后加入，走历史重放；
  4. 断线重连：客户端断开重连，不出现双场景。
- **服务器侧专项**：确认 `GameModule.GameScene.CurrentSceneType` 与目标场景一致、`DynamicSceneSpawner` 在非编辑器构建下走 YooAsset 分支、`OnDynamicSpawnComplete` 携带正确 `SceneType`。
- **验收**：对照 1.2 清单逐条勾验。

---

## 五、接口草案（仅签名，不写实现）

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkSceneCoordinator.cs
namespace GameLogic
{
    /// <summary>场景加载协调器：服务器权威发起，客户端被动展示。</summary>
    public sealed class NetworkSceneCoordinator
    {
        /// <summary>服务器专用：权威加载场景（DS 与 Host 都走这里）。</summary>
        public bool LoadSceneServer(SceneType sceneType);

        /// <summary>客户端专用：请求服务器切场景。</summary>
        public void RequestSceneChange(SceneType sceneType);

        /// <summary>是否正在协调一次场景切换。</summary>
        public bool IsTransitioning { get; }

        /// <summary>绑定/解绑 NetworkManager（业务注入或懒查找）。</summary>
        public void Bind(PurrNet.NetworkManager networkManager);
        public void Unbind();
    }
}

// IGameSceneModule 新增（被动模式）
/// <summary>被动模式：不发起加载，只展示进度并等待外部完成通知。</summary>
void BeginPassiveLoad(SceneType sceneType);

/// <summary>被动模式：外部通知「服务器已开始加载」，启动伪进度。</summary>
void NotifyPassiveLoadStarted();

/// <summary>被动模式：外部通知「服务器场景已加载完成并激活」，由协调器在 onYooAssetSceneLoaded 时调用。</summary>
void NotifyPassiveLoadCompleted();

// IGameSceneModule 新增（服务器侧外部加载状态）
/// <summary>外部加载方（服务器）在发起加载前写回场景状态，不启动加载会话、不打开加载页。</summary>
void NotifyExternalSceneLoadStarting(SceneType sceneType);

/// <summary>外部加载失败时回滚到进入前的场景状态。</summary>
void NotifyExternalSceneLoadFailed();

/// <summary>外部加载完成：派发 OnSceneReady，不关页、不结束会话。</summary>
void NotifyExternalSceneLoaded();

/// <summary>是否允许本端主动发起场景加载（联机客户端由 NetworkModule 关闭；单机/服务器保持开启）。</summary>
void SetLocalLoadAllowed(bool allowed);
```

---

## 六、文件变更清单

### 新增

| 文件 | 说明 |
|---|---|
| `Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkSceneCoordinator.cs` | 协调器 |
| `Assets/GameScripts/HotFix/GameLogic/IEvent/INetworkSceneEvent.cs` | 网络场景事件（若选择新建） |
| `Assets/AssetRaw/Scenes/network_test.unity` | 联调测试场景（S1.5，需授权） |
| `Assets/GameScripts/HotFix/GameLogic/Scenes/DynamicSpawn/NetworkTestSceneSpawner.cs` | 测试用 `LoadingWait` Spawner 子类（S1.5） |

### 修改

| 文件 | 改动 |
|---|---|
| `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/IGameSceneModule.cs` | 被动模式三个入口 + 服务器外部状态三个方法（`NotifyExternalSceneLoadStarting/Failed/Loaded`）+ 本地加载开关 `SetLocalLoadAllowed` |
| `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs` | `_isPassive` 字段 + 七阶段被动分支（阶段 1 伪进度、阶段 2 不 UnSuspend、阶段 3 改用完成信号）+ 外部状态写回/回滚/完成派发 + `StartSceneLoad` 入口的本地加载拦截 |
| `Assets/GameScripts/HotFix/GameLogic/GameApp.cs` | DS 启动入口改协调器 |
| `Assets/GameScripts/HotFix/GameLogic/GameModule.cs` | 如新增 `GameModule.NetworkScene` 访问器 |
| `Assets/AssetRaw/Scenes/dedicated_server.unity`（及客户端主场景） | 挂 NM（S1，需授权） |

### 明确不动

- `Assets/TEngine/**`（框架层，含 `SceneModule`）
- `Library/PackageCache/**`、YooAsset 本地包、PurrNet 包
- `Procedure*` 启动链（DS 分支已够用，除非 S5 暴露新问题）
- `Module/GameScene/Generated/**`（生成产物，改场景请走 `SceneEnumConfig` 工具）
- `SwitchSceneUI`（纯展示，只读模块数据；本轮不改 UI）

---

## 七、约束与红线

1. 不改框架、不改第三方包；不引入兼容层。
2. 业务异步用 UniTask，取消/失败/超时语义必须显式处理。
3. 资源加载与释放配对；场景切换前后不得残留未归零引用。
4. 事件监听必须在销毁/解绑时注销（协调器 `Unbind`）。
5. UI 只读数据，加载控制归模块，不改 `SwitchSceneUI` 的运行时美化。
6. 日志统一走项目日志桥接，不用 `Debug.Log` 做业务日志（注意 `GameSceneModule.RecordScene` 现有 `Debug.Log` 属既有代码，不在本次范围）。
7. 不真实构建/发布 DS 除非用户单独授权。
8. 新增联机场景走 `SceneEnumConfig` 生成，不手写 `SceneType`。

---

## 八、验证方式

| 阶段 | 命令/方式 | 说明 |
|---|---|---|
| 编译 | `python .codex/scripts/workflow.py verify --profile code` | C# 全解决方案构建 |
| Editor | `python .codex/scripts/workflow.py verify --profile unity` | Editor 编译 + 指定资源/测试集 |
| 联调 | 编辑器 Host + 1 客户端、DS + 1 客户端、中途加入、断线重连（场景用 `MainScene ↔ network_test`） | 人工按 1.2 清单勾验 |
| DS 产物 | `python -m tengine_build run --preset dedicated_server_linux --json` | **需用户授权**；结果确认 `isServerBuild`、场景加载、无 NRE |
| 全量 | `python .codex/scripts/workflow.py verify --profile full` | 交付前 |

---

## 九、风险与未确认项

| # | 项 | 影响 | 处置 |
|---|---|---|---|
| 1 | 被动模式阶段 1 只能伪进度 | 观感与真实加载不完全匹配；且 `_lastLoadProgress` 恒为 0 导致停滞超时分支失效 | 接受伪进度；**被动模式单独设更短绝对超时**（建议 30s）兜底 |
| 2 | 客户端强制激活绕过 TEngine 主场景 handle | `IsSceneLoadDone` 在被动路径不可用 | 阶段 3 已改为以 `onYooAssetSceneLoaded` 为信号（S3）；联调观察 |
| 3 | 阶段 5 门闩在被动模式下若 `_isActive` 未及时置位会静默失效 | `DynamicSceneSpawner` 生成进度不上报，加载页提前关 | 强制在 `ShowUI` 前 `_isActive=true`；联调打印 `[NetworkScene]` 任务注册日志核验 |
| 4 | NM 场景非 DDOL 时 `PrepareForSingleYooAssetSceneLoad` **只报错不中断** | 静默卸载已注册场景，表现为“切场景后世界空了” | S1 必须落实；联调把该 Error 当失败处理 |
| 5 | `isPublic` / `ScenePlayersModule` 可见性语义未深挖 | 分房间场景可能不符合预期 | 首版 `isPublic=true`（全服同一场景），后续再做分组 |
| 6 | DS 未挂 NM 时协调器为空 | 功能不可用 | S1 前置；`LoadSceneServer` 明确报错并提示 |
| 7 | 客户端 action 队列 Tick 时机未知 | 伪进度起始点可能延迟 | 以 `onYooAssetSceneStartLoading` 为准，不假设时间 |
| 8 | 断线重连重放完整性 | 极端场景状态不一致 | 联调专项验证；必要时复查 `_history` 重建路径（`:603-620`） |
| 9 | 场景枚举为生成产物 | 手工加枚举会被下次生成覆盖 | 统一走 `SceneEnumConfig` |
| 10 | 服务器路径未写回 `CurrentSceneType` | `DynamicSceneSpawner.IsNormalBoot` 为 false，非编辑器构建下动态对象不生成 | S2 在加载前写回、失败回滚；S5 禁止裸回退；联调专项检查 |
| 11 | Host 第一版无加载页 | 切场景时 Host 本地无进度反馈，可能短暂黑屏/卡顿 | 已明确为设计选择；后续可单独给 Host 开页 |
| 12 | 协调器订阅晚于网络启动 | 收不到场景事件，切场景静默失效 | S2 规定 `onNetworkStarted` + 启动后补订阅；联调看 `[NetworkScene]` 绑定日志 |
| 13 | `--scene` 未命中 | 传错场景名会回落到 `MainScene` | 记 Error 并明确日志，不静默 |
| 14 | 测试场景 Spawner 未用 `LoadingWait` | 90%→100% 无人驱动，验收误判为失败 | S1.5 明确模式；联调打印 `RegisterInitTask` 日志 |
| 15 | 服务器加载失败时 PurrNet 不派发完成事件 | 状态停在目标场景、`IsTransitioning` 不收敛 | S2 订阅 handle `Completed` 检查 `Status`；配合超时回滚 |

**未确认项统一标注为「未验证」，不得在交付中写成已验证。**

---

## 十、执行顺序

```
S0 前置核实（对齐当前源码，必做）
  ↓
S1 场景挂 NM（需授权，Unity 操作；确认 DDOL）
  ↓
S1.5 联调测试场景 + LoadingWait Spawner（需授权，可与 S2/S3 并行）
  ↓
S2 协调器（无 UI 依赖，可先写）
  ↓
S3 GameSceneModule 被动模式（七阶段）
  ↓
S4 事件扩展
  ↓
S5 GameApp DS 入口改造
  ↓
S6 联调（Host → DS → 中途加入 → 重连）
  ↓
验证：code → unity → （授权后）DS 产物 → full
```

S2 与 S3 可并行开发；S1.5 可与 S2/S3 并行，但联调必须 S1 与 S1.5 完成。

---

## 十一、授权记录

- 2026-10-18：用户要求“落盘为详细的规划文件”，仅授权创建本计划文档与更新计划索引，**未授权**实施代码修改、Unity 场景写入或真实构建。
- 2026-10-24：用户合并 main 新提交后要求“更新 plan，尤其是实现细节”。仅授权**更新本计划文档**，仍未授权实施代码修改、Unity 场景写入或真实构建。
- 2026-10-24（二次）：用户要求本计划严格限定为场景加载协调本身，剔除与场景加载无关的职责。仅授权**更新本计划文档**。
- 2026-10-24（三次）：用户要求补齐实施缺口（服务器状态写回、联调测试场景、Host 行为、被动入口调用点、`--scene` 回退、绑定时机）。仅授权**更新本计划文档**。
- 后续执行时：先读本文件 + `.plans/purrnet-ds-integration.md` + 当时 `git status`；按当前源码状态复核签名与路径；Unity 写入、构建等按仓库授权规则单独确认。

---

## 十二、本次更新说明（2026-10-24）

### 12.1 更新原因

用户合并了 main 的新提交，**主要改动场景加载**，导致原计划与当前源码产生出入。

### 12.2 核实到的关键差异（对照 2026-10-18 版计划）

| # | 项 | 原计划描述 | 当前源码事实 | 证据 |
|---|---|---|---|---|
| 1 | 进度状态机 | 「三段式进度」（预热/加载/收尾） | **七阶段**（0 预热 / 1 加载 / 2 80%检查点 / 3 激活 / 4 90%检查点 / 5 业务就绪 / 6 100%帧等待） | `GameSceneModule.cs:252`、`Update` 全段 |
| 2 | 初始化任务门闩 | 未提及 | 新增 `RegisterInitTask`（含进度源重载），已由 `DynamicSceneSpawner` 使用；`!_isActive` 时静默 `Forget` | `IGameSceneModule.cs:47/55`、`GameSceneModule.cs:508-588`、`DynamicSceneSpawner.cs:215` |
| 3 | 加载页类名 | 文中混用 `SwitchUI` / `SwitchSceneUI` | 实际类为 **`SwitchSceneUI`**，`SwitchUI` 不存在 | `SwitchSceneUI.cs:8-9` |
| 4 | 场景枚举 | 视为手写枚举 | 由 `SceneEnumConfig` **自动生成 4 件套**，当前仅 `MainScene` / `dedicated_server` | `Generated/SceneType.g.cs` 等 |
| 5 | 客户端加载路径 | 「客户端 action 路径…有挂起窗口」表述含混 | 两条路径**均硬编码 `allowSceneActivation=true`**，客户端**根本没有挂起窗口**，`IsSceneLoadDone` 被动路径不可用 | `ScenesModule.YooAsset.cs:148-153`、`:263` |
| 6 | NM 非 DDOL 检查 | 记为「会检查并报错」 | 实为**只报 Error 不中断**，随后继续执行清理逻辑卸载已注册场景 | `ScenesModule.YooAsset.cs:301-310`、`:312-334` |
| 7 | 被动模式阶段划分 | 按三段式描述（伪进度到 0.9 → 等事件 → 收尾） | 需按七阶段逐段对齐，且阶段 5 门闩**必须保留可用** | 见 S3 |
| 8 | 卸载约束文案 | 引用 `ScenesModule.cs:1330-1342` 等 | 现位于 `ScenesModule.YooAsset.cs`，行号已变 | `:397-401`、`:416-420` |

### 12.3 本次改动的内容

1. 第二节「现状与约束」按当前源码**重新核实并更新全部行号与结论**，新增 2.3（七阶段状态机）、2.4（`RegisterInitTask` 门闩）、2.5（生成式场景枚举）。
2. S3 从「三段式被动模式」**重写为七阶段逐段对齐的被动模式**，明确：不调 `StartRealLoading`、不调 `UnSuspend`、不轮询 `IsSceneLoadDone`、**保留阶段 5 门闩**、新增 `_isPassive` 字段与 `NotifyPassiveLoadStarted/Completed` 两个入口。
3. 新增 **S0 前置核实步骤**，把“实施前必须重新对齐源码”写成流程要求（本次差异即为教训）。
4. 风险表新增：被动模式伪进度导致停滞超时失效（#1）、门闩静默失效（#3）、NM 非 DDOL 静默卸载（#4）、枚举生成产物（#9）。
5. 接口草案、文件清单、验收清单同步更新（含 `SwitchSceneUI` 正名与“七阶段语义一致”验收项）。
6. 补齐实施缺口：服务器侧场景状态写回/回滚/完成派发（S2/S3）、服务器失败检测、联调测试场景与 `LoadingWait` Spawner（S1.5）、Host 第一版行为、被动入口同步调用点、`--scene` 禁止裸回退、协调器绑定/订阅时机、联机客户端本地加载拦截；风险表扩充到 15 项。

### 12.4 仍未验证 / 需要注意

- 本计划**未实施任何代码**；上述差异均来自静态源码阅读，**未经过 Unity 编译或运行时验证**。
- 被动模式的伪进度速率、30s 兜底超时等具体数值**为建议值，未实测**。
- 上一轮更新未执行 `git diff/log`；本轮已按磁盘源码复核关键行号，但仍未做完整 diff 对照，**无法列出 main 合并引入的具体提交与文件清单**。以上差异是“当前源码现状”与“旧计划文本”的对照结果，不能等同于合并 diff 的精确范围。实施前如需要精确 diff，请在可用终端中执行 `git log --oneline -20` / `git diff <old>..HEAD -- UnityProject/Assets/GameScripts`。