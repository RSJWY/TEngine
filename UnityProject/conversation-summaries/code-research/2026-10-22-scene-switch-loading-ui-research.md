# 场景切换加载页与"等待场景准备完毕"机制研究

> 日期：2026-10-22
> 触发问题：当前场景切换 UI，是不是只有场景切换广播事件，并没有切换 UI 等待场景准备完毕功能？

## 结论

**不是。** 当前实现已经具备完整的"切换 UI 等待场景准备完毕"功能，且等待逻辑不依赖广播事件，而是 `GameSceneModule` 内置的三段式进度状态机在关闭加载页前显式等待场景真实激活完成。广播事件（`IGameSceneEvent`）只是流程中的通知手段，不是唯一机制。

## 架构分工

| 角色 | 文件 | 职责 |
| --- | --- | --- |
| `GameSceneModule` | `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs` | 独占加载控制：打开/关闭加载页、三段式进度状态机、挂起加载、激活场景、等待激活、完成回调 |
| `SwitchSceneUI` | `Assets/GameScripts/HotFix/GameLogic/UI/Core/SwitchSceneUI/SwitchSceneUI.cs` | 纯展示：每帧 `OnUpdate` 读 `GameModule.GameScene.DisplayProgress` 刷进度条与文案，不参与控制 |
| 框架 `SceneModule` | `Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs` | 底层能力：`LoadSceneAsync(suspendLoad)`、`UnSuspend(location)`（:286）、`IsSceneLoadDone(location)`（:313，句柄 IsDone 才 true） |
| `IGameSceneEvent` | `Assets/GameScripts/HotFix/GameLogic/IEvent/IGameSceneEvent.cs` | 四个广播：`OnSceneLoadStart` / `OnSceneLoadOver` / `OnDynamicSpawnComplete` / `OnSceneReady` |

## 加载会话全流程（三段式状态机）

1. **入口** `LoadScene(SceneType, finishCallBack, warmup?, finish?, hold100?)` → `StartSceneLoad`：
   - 硬保护：上一会话未结束直接拒绝新请求；
   - 记录 Previous/CurrentSceneType → 派发 `OnSceneLoadStart` → 重置状态机 → `GameModule.UI.ShowUI<SwitchSceneUI>()`。
2. **阶段 0 预热（0→10%）**：默认 0.7s 纯动画，走完才 `StartRealLoading()` 发起真实加载。
3. **阶段 1 加载（10→90%）**：`GameModule.Scene.LoadSceneAsync(..., suspendLoad: true, progressCallBack: OnLoadProgress)`，把 YooAsset 0~0.9 映射到 10%~90%；`value>=0.9` 标记 `_sceneLoadComplete`。fire-and-forget（suspendLoad + progressCallBack 时 await 会死循环，见模块注释陷阱 1/2）。停滞 60s / 绝对 180s 超时兜底。
4. **阶段 2 收尾（90→100% + 等待激活 + 停留）** `EnterFinishPhase`：
   - 派发 `OnSceneLoadOver`，随后**直接 `GameModule.Scene.UnSuspend(_sceneName)` 激活场景**（激活权归本模块，不走 UI 自发自收事件）；
   - 每帧轮询 `GameModule.Scene.IsSceneLoadDone(_sceneName)`（= 句柄 IsDone）直到场景真实激活完成，30s 绝对超时兜底；
   - 激活后再渲染 `PostActivateFrames=2` 帧遮盖首帧 shader 编译/资源上传卡顿；
   - 90→100% 收尾动画（默认 2s）遮盖激活卡顿，走满后 100% 停留（默认 0.5s）。
5. **统一终结出口** `FinishAndClose`（`_isFinished` 防重）：finishCallBack（如 `JumpToMainScene` 的 CloseAll+JumpToMain）→ `CloseUI<SwitchSceneUI>()` 兜底关页 → 派发 `OnSceneReady(_sceneType)` → `_isActive=false` 结束会话。

**加载页关闭条件 = 动画走满 + 场景真实激活完成 + 激活后 2 帧 + 100% 停留结束**（`SkipLoadingAnimation` 或预热传 0 仅跳过动画与停留，仍等待真实激活）。为何 90% 激活而非 100%：激活有一帧卡顿，留最后 10% 动画做视觉遮盖。

## 事件与等待的关系

- `OnSceneLoadStart`（加载开始）、`OnSceneLoadOver`（资源就绪可激活）、`OnSceneReady`（加载页已关、场景可开始业务初始化，`DynamicSceneSpawner` 监听它生成动态对象）是给观察方的通知；
- "等待场景准备完毕"由模块内部轮询 `IsSceneLoadDone` 实现，与事件互不替代；
- 业务侧没有绕过 `GameSceneModule` 直接调 `GameModule.Scene.LoadScene*` 的调用点（已 grep 核实）。

## 关键代码位置

- 状态机驱动：`GameSceneModule.Update`（GameSceneModule.cs:308）
- 激活与等待：`EnterFinishPhase`（:543）+ 阶段 2 轮询 `IsSceneLoadDone`（:367）
- 终结出口：`FinishAndClose`（:568）
- UI 纯展示：`SwitchSceneUI.OnUpdate`（SwitchSceneUI.cs:20）

## 补充：加载页是否等待"场景后初始化资源"（动态生成/业务预加载）——现状不支持

时序（2026-10-22 追加核实）：

1. `FinishAndClose` 顺序：finishCallBack → `CloseUI<SwitchSceneUI>()` → **之后**才派发 `OnSceneReady`（模块注释明确"须在 SwitchUI 关闭后派发"）。
2. `SpawnInitMode.SceneReady` 模式的 `DynamicSceneSpawner` 监听 `OnSceneReady`，即**加载页关闭后才开始**动态加载（DynamicSceneSpawner.cs:64 枚举注释"SceneReady（加载页关闭后）"）。
3. `SceneGameManagerBase<TSpawner>` 监听 `OnDynamicSpawnComplete`（双保险：被动事件 + 0.1s 轮询 `IsSpawnCompleted`）才执行场景业务初始化（打开场景 UI、相机就位等）——同样在加载页关闭之后。
4. `Awake/Start/OnEnable` 模式的 Spawner 在场景激活（阶段 2，加载页还开着）就开始加载，与加载页收尾天然并行，但模块**不 await** 它们，完成与否不影响关页。
5. 加载页关闭条件里只有"动画 + 场景句柄 IsDone + 2 帧 + 100% 停留"，**不含任何业务初始化门闩**。

**但已预留扩展点**：`DynamicSceneSpawner.cs:84` 注释"方案乙预留：`SpawnAsync` 为 public，将来可由 GameSceneModule 在 finishCallBack 前 await 调用"；且 `IsSpawnCompleted`、`OnDynamicSpawnComplete` 均为现成完成信号。

可行改造方向（未实施）：

- GameSceneModule 增加「初始化门闩」：注册 API（如 `RegisterInitTask(UniTask, label)` 或计数门闩），状态机加阶段 3（门闩未满时进度保持 100%、可配"初始化中"文案），带绝对超时兜底防业务异常卡死玩家。
- 若要等待 SceneReady 模式 Spawner，需将其触发提前（改 initMode 或新增 `OnSceneInit` 类事件在等待前派发），否则与"等它完成再关页"形成死锁。
- UI 零改动（仍每帧读 `DisplayProgress`）；文案按需扩展 `SceneLoadTipsConfig`。

## 实施记录（2026-10-22，已按上述方向落地）

改动 4 文件，未提交：

1. `IGameSceneModule`：新增 `RegisterInitTask(UniTask task, string label = null)`。
2. `GameSceneModule`：新增阶段 3 初始化等待——注册 API（会话未激活时包装后 Forget 退化后台执行）、`WrapInitTask`（异常记一次转正常完成，轮询只判 `UniTaskStatus.Pending`）、阶段 2 关闭条件命中时若有 Pending 任务进入阶段 3（进度保持 100%，`DisplayPhaseText` 切初始化文案，100% 停留与等待并行计时），`Phase3AbsoluteTimeout=30s` 绝对超时兜底；`StartSceneLoad`/`Shutdown` 清空任务表。
3. `DynamicSceneSpawner`：枚举**追加在 Manual 之后**新增 `LoadingWait`（保持既有 int 值不位移，防已序列化场景错位）；该模式在 Awake 即 SpawnAsync 并注册给模块，编辑器直启退化为普通 Awake 行为；类注释"方案乙预留"改为已实现。
4. `SceneLoadTipsConfig`：新增可选 `initPhaseText` 字段（留空用模块默认"正在初始化场景资源…"，旧 .asset 无需改动）。

业务接入方式：场景中 Spawner 的 initMode 改为 LoadingWait 即被加载页等待；任意初始化逻辑也可在场景激活后直接 `GameModule.GameScene.RegisterInitTask(task, label)`。SceneReady 模式语义不变（加载页关闭后才开始、不被等待）。

验证：`workflow.py verify --profile code` → **solution-build passed**（全解决方案编译通过）；workflow-unit-tests（`.codex/tests` 目录缺失 ImportError）与 skill-and-document-structure（缺 yaml/markdown_it 依赖）为环境预置问题与本次无关；Unity Editor 验证被阻塞（`Tools/unity.exe` 缺失、Editor 未连接），Unity 域内编译待 Editor 打开确认。

## 实施记录二（2026-10-22，按用户指定分配重排进度阶段机）

用户指定：`0→80%（加载）→ 80→90%（激活）→ 90→100%（业务就绪进度驱动）→ 正常帧等待`，且 80%/90% 各停一两帧看阶段、分摊性能峰值。已落地，阶段机由 4 段扩为 7 段：

| 阶段 | 区间 | 内容 |
| --- | --- | --- |
| 0 | 0→10% | 预热动画（不变，skip 可跳） |
| 1 | 10→80% | YooAsset 0~0.9 映射到 10~80%（原映射到 90） |
| 2 | 80% 停帧 | `StageCheckpointFrames=2` 帧后 `EnterFinishPhase`（OnSceneLoadOver + UnSuspend），峰值分帧 |
| 3 | 80→90% | 激活段：`FinishSpeed`（0.10/`_finishDuration`）动画遮盖激活卡顿，轮询 `IsSceneLoadDone`，`Phase2AbsoluteTimeout=30s` 兜底 |
| 4 | 90% 停帧 | 2 帧后进入业务就绪段 |
| 5 | 90→100% | 业务就绪：`ComputeInitProgress()`（各任务进度源取最小值；无源任务完成前按 0）驱动 target，`InitSegmentSpeed=1.0/s` 平滑追赶，`_lastInitTarget` 单调钳制；无任务 target=1 快速爬过；`Phase3AbsoluteTimeout=30s` 超时置 `_initTimeoutHit` 强制收尾 |
| 6 | 100% | 正常帧等待：激活后渲染帧数（`PostActivateFrames=2`，激活完成后全局累计）+ 100% 停留 + 任务全部完成 → `FinishAndClose` |

配套改动：`RegisterInitTask` 新增带进度源重载 `(UniTask, Func<float>, string)`（`InitTaskEntry` 结构存 Task+Progress）；Spawner 新增 `_spawnProgress`（已处理项/总项，成功/失败/跳过都计），TrySpawn 以 `() => _spawnProgress` 注册；skip 模式跳过停帧与动画（`SkipSpeed=100`、检查点 0 帧）但仍等激活与任务；文案切换条件改为阶段 5 或（阶段 6 且有未完成任务）。验收仍以 solution-build passed 为准（同上环境限制）。

### 进度聚合方式（同日用户定版）

阶段 5 综合进度采用**按任务数平分**（非最初实现的取最小值）：`ComputeInitProgress = Σ(各任务进度) / 任务数`，每完成一项任务进度条实进一格。已完成任务一律按满进度计（防进度源少报把 90→100% 卡段直到超时）；无进度源任务完成前计 0、完成计 1；无任务返回 1。单调性仍由 `_lastInitTarget` 钳制兜底。
