# PurrNet DS 场景加载协调（阶段四 4.2）实施计划

> **状态**：计划已落盘，**未实施**
> **来源**：`.plans/purrnet-ds-integration.md` 阶段四 步骤 4.2
> **研究依据**：[PurrNet × TEngine 场景加载协调研究](../conversation-summaries/code-research/2026-10-18-purrnet-scene-coordination-research.md)
> **创建时间**：2026-10-18
> **优先级**：中（有实际联机场景时为前置必需）
> **执行授权**：需用户明确要求后开始；Unity 场景/预制体写入、真实 DS 构建须单独确认

---

## 零、先看这段白话（不涉及代码）

切场景就像剧组换拍摄场地，现在**两拨人都在管换场地**：

- **TEngine 的加载页**：先把场地搭好但不上人，进度条走到 90% 再喊“开拍”——进度条好看，卡顿被盖住。
- **PurrNet（联机库）**：规定**只有服务器/房主有资格喊换场地**，玩家收到通知后自己搭场地，而且**一搭好就直接开拍**，不等进度条。

两拨人同时动手就会出事：场地搭两次拆两次、进度条乱跳，最惨是场景里的东西没人生成，玩家进去**一片空**。

**结论**：让服务器一个人说了算，玩家端别自己搭场地，只把进度条摆出来给人看。

1. **服务器换场景**：房主/专用服务器用 PurrNet 的方式喊“换”，这是唯一真源。
2. **玩家端只看不干**：收到“要换场地了”的通知后打开加载页、跑**假进度**（按时间爬，够用），收到“搭好了”的通知再收进度条、通知场景生成东西。
3. **玩家想换场景只能申请**：点按钮不是自己换，而是告诉服务器“我想去 X”，服务器同意后再走第 1 步。

---

## 一、目标与验收

### 1.1 目标

1. **场景加载只有一个驱动方**：服务器（DS 或 Host）权威发起，客户端不再自行加载。
2. **保留 TEngine 加载页体验**：客户端仍能看到 `SwitchSceneUI` 加载页，场景激活后 `OnSceneReady` 照常派发，`DynamicSceneSpawner` 等下游不受影响。
3. **不动框架、不动 PurrNet 包**：所有新增逻辑放业务层（`GameScripts/HotFix/GameLogic`）。

### 1.2 验收标准（可逐条勾验）

- [ ] 服务器调用协调器切场景后，客户端自动跟随，且**同一场景只被加载一次**（无重复 `LoadSceneAsync` 日志）。
- [ ] 客户端加载过程中能看到 `SwitchSceneUI`，进度条不跳变、不卡死。
- [ ] 场景激活后客户端 `OnSceneReady` 正常派发，场景内 `DynamicSceneSpawner` 生成对象成功。
- [ ] DS 启动（无 UI）不报 `NullReferenceException`、不黑屏退出；日志可见 `[NetworkScene]` 关键节点。
- [ ] 客户端中途加入：进服后自动恢复服务器当前场景，并走同一套被动加载页。
- [ ] 客户端断线重连：场景状态恢复，不出现双场景并存。
- [ ] 未联机（单机）流程完全不受影响：`GameApp` 启动仍走 `GameModule.GameScene.LoadScene(SceneType.MainScene)`。
- [ ] 客户端在本地直接调用场景切换 API 时被拒绝并给出明确日志（不允许绕过服务器）。

---

## 二、现状与约束（已核实，带证据）

### 2.1 三套体系一句话

| 体系 | 位置 | 职责 |
|---|---|---|
| `SceneModule` | `Assets/TEngine/Runtime/Module/SceneModule/` | YooAsset 场景句柄管理、挂起/激活/卸载 |
| `GameSceneModule` | `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/` | 三段式进度、加载页、业务事件 |
| PurrNet `ScenesModule` | 包缓存 `Runtime/CoreModules/Scenes/` | 服务器权威场景指令、SceneID、历史重放 |

### 2.2 硬约束（决定方案的根因）

| 约束 | 事实 | 证据 |
|---|---|---|
| 只有服务器能加载 | `if (!_asServer) Error("Only server can load scenes")` | ScenesModule.cs:1205-1209、1229-1233 |
| 只有服务器能卸载 | 同上；且禁止卸载 NM 所在场景 | ScenesModule.cs:1330-1342、1373-1389 |
| 客户端加载无挂起窗口 | 客户端 action 路径 `LoadSceneAsync(..., true, 100)`，`allowSceneActivation=true` | ScenesModule.YooAsset.cs:148-153 |
| TEngine 三段式依赖挂起 | `suspendLoad:true` → `UnSuspend` → `IsSceneLoadDone` | GameSceneModule.cs:503/543/367 |
| 激活语义对得上 | YooAsset 3.0.5 第 4 参是 `allowSceneActivation` | ResourcePackage.cs:480 |
| NM 必须在 DontDestroyOnLoad 场景 | Single 加载前会检查并报错 | ScenesModule.cs:1238-1247 |
| 中途加入自动恢复 | 服务器写 `_history`，新玩家 `GetFullHistory()` 重放 | ScenesModule.cs:1260；SceneHistory.cs:141-147 |
| Host 不重复加载 | 服务器加载时把同一 operation 塞给 client 模块 | ScenesModule.cs:1280-1284 |
| DS 不能开加载页 | DS 分支跳过 `LauncherMgr.Initialize()`，`ShowUI` 会 NRE | 阶段三经验（ProcedureInitResources 补漏） |

### 2.3 现状代码位置（改造点）

- `GameApp.cs:68-100` `StartDedicatedServer()`：`--scene-type` 走 `GameSceneModule.LoadScene`（会开加载页）；`--scene` 走 `GameModule.Scene.LoadSceneAsync`（绕开 PurrNet）。
- `GameSceneModule.StartSceneLoad()`：固定主动加载流程，需增加被动入口。
- `NetworkModule.cs`：已封装 NM 生命周期，可作为协调器宿主。
- `IGameSceneEvent`：现有 4 个事件，被动模式需复用并可能扩展。

---

## 三、方案决策

### 3.1 选定方案 A：服务器权威 + 客户端被动加载页

| 备选 | 结论 |
|---|---|
| **A. 服务器权威 / 客户端被动**（选定） | 场景一致性由 PurrNet 保证，改业务层，保留加载页 |
| B. 自建 RPC 广播 + 各端自行加载 | 放弃 PurrNet 场景模块（SceneID/历史/重连全要自研），不推荐 |
| C. 改造 PurrNet 包支持挂起 | **违反「不主动改框架/包」红线，排除** |

### 3.2 关键决策记录

| 决策点 | 选择 | 理由 |
|---|---|---|
| 场景加载发起方 | 服务器唯一 | PurrNet 硬约束 |
| 客户端进度来源 | 伪进度 + 事件驱动 | PurrNet 不暴露内部 handle 进度；不为此改包 |
| 客户端加载完成判定 | `onYooAssetSceneLoaded` 事件 | 唯一可靠的完成信号 |
| 被动模式是否等激活 | 等事件 + N 帧 | 与主动模式行为对齐 |
| 客户端切场景 | 请求服务器 | 客户端无权限 |
| DS 是否走加载页 | 否 | 无 UIRoot，会 NRE |
| 单机流程 | 完全不变 | 未联机不引入网络依赖 |
| 桥接放哪 | 业务层 `NetworkModule` 目录 | 不改框架、不散落 |

---

## 四、实施步骤

> 每一步独立可验证；顺序有依赖，S1 不做则 S2~S6 无法联调。

### S1 前置：确认/改造场景挂载（Unity 对象操作，需单独授权）

- **目标**：DS 场景与客户端启动场景都挂上 `NetworkManager`（含 Transport），且 NM 所在对象所在场景为 **DontDestroyOnLoad**。
- **涉及**：`Assets/AssetRaw/Scenes/dedicated_server.unity`、客户端主场景、必要时新建“启动壳场景”。
- **验收**：运行日志中无 “Network manager scene is not DontDestroyOnLoad” 报错；`GameModule.Network.NetworkManager` 非空。
- **注意**：场景/Prefab 写入属 Unity 操作，实施前先预览具体对象与范围并集中确认；不给 `.meta` 手工编辑。

### S2 新增协调器 `NetworkSceneCoordinator`

- **新增文件**：`Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkSceneCoordinator.cs`
- **职责**：
  - `LoadSceneServer(SceneType)`：仅服务器可调；`SceneType → location` 复用 `GameModule.GameScene.GetSceneName`；`packageName` 取本项目默认包；`settings = new PurrSceneSettings { mode = Single, physicsMode = None, isPublic = true }`；调用 `nm.sceneModule.LoadYooAssetSceneAsync(...)`。
  - `RequestSceneChange(SceneType)`：客户端向服务器发请求（RPC 或业务消息），由服务器转 `LoadSceneServer`。
  - 订阅 `sceneModule.onYooAssetSceneStartLoading` / `onYooAssetSceneLoaded`，按 `asServer` 分流：服务器侧直接派发业务事件；客户端侧转给 `GameSceneModule` 被动模式。
  - 去重与超时：同一 `location` 正在加载时忽略重复请求；等待完成超时打 Warning。
- **验收**：编辑器 Host 调用一次，客户端收到事件；日志可见完整链路。

### S3 `GameSceneModule` 增加被动模式

- **修改**：`IGameSceneModule.cs`、`GameSceneModule.cs`
- **新增入口**：`void BeginPassiveLoad(SceneType sceneType)`（被动展示，不发起加载）。
- **改动要点**：
  - 复用现有段状态机：`RecordScene` → `OnSceneLoadStart` → 预热 → 伪进度到 0.9 → 等完成事件 → 收尾 → `OnSceneReady`。
  - **不调** `StartRealLoading()`，**不调** `GameModule.Scene.UnSuspend()`。
  - 新增 `NotifyPassiveLoadCompleted()`：由协调器在 `onYooAssetSceneLoaded` 时调用，置 `_sceneLoadComplete = true` 并进入阶段 2。
  - 保留 `_isActive` 并发保护；被动期间拒绝主动 `LoadScene` 请求并报错。
  - 超时兜底（沿用 `Phase2AbsoluteTimeout` 风格）：超时后强制收尾并打 Warning，`OnSceneReady` 仍派发。
- **验收**：被动模式下进度条平滑到 100%，`SwitchSceneUI` 正常关闭。

### S4 事件扩展

- **修改**：新增 `INetworkSceneEvent`（或扩展 `IGameSceneEvent`，二选一，优先新增避免污染现有契约）。
- **事件**：`OnServerSceneLoadRequested(SceneType)`、`OnServerSceneLoadCompleted(SceneType)`、`OnSceneLoadFailed(SceneType, string reason)`。
- **注意**：`OnSceneReady` 仍走 `IGameSceneEvent`，不改语义，避免 `DynamicSceneSpawner` 改动。

### S5 `GameApp.StartDedicatedServer()` 改造

- **修改**：`Assets/GameScripts/HotFix/GameLogic/GameApp.cs`
- **改动**：
  - `--scene-type`：改为 `coordinator.LoadSceneServer(sceneType)`（不再打开加载页）。
  - `--scene`：先 `GetSceneTypeFromName` 反查；命中走协调器，未命中退回 `GameModule.Scene.LoadSceneAsync` 并打 Warning。
  - 未指定场景仍 fallback `MainScene`，但同样走协调器（保证服务器与客户端一致）。
- **验收**：DS 启动日志顺序正确、无 NRE、场景加载完成。

### S6 联调与观测

- 增加 `[NetworkScene]` 前缀日志：请求、开始加载、加载完成、激活、收尾、超时。
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

// IGameSceneModule 新增
/// <summary>被动模式：不发起加载，只展示进度并等待外部完成通知。</summary>
void BeginPassiveLoad(SceneType sceneType);

/// <summary>被动模式完成通知：由协调器在收到加载完成事件后调用。</summary>
void NotifyPassiveLoadCompleted();
```

---

## 六、文件变更清单

### 新增

| 文件 | 说明 |
|---|---|
| `Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkSceneCoordinator.cs` | 协调器 |
| `Assets/GameScripts/HotFix/GameLogic/IEvent/INetworkSceneEvent.cs` | 网络场景事件（若选择新建） |

### 修改

| 文件 | 改动 |
|---|---|
| `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/IGameSceneModule.cs` | 增加被动入口签名 |
| `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs` | 被动模式状态机分支 |
| `Assets/GameScripts/HotFix/GameLogic/GameApp.cs` | DS 启动入口改协调器 |
| `Assets/GameScripts/HotFix/GameLogic/GameModule.cs` | 如新增 `GameModule.NetworkScene` 访问器 |
| `Assets/AssetRaw/Scenes/dedicated_server.unity`（及客户端主场景） | 挂 NM（S1，需授权） |

### 明确不动

- `Assets/TEngine/**`（框架层，含 `SceneModule`）
- `Library/PackageCache/**`、YooAsset 本地包
- `Procedure*` 启动链（DS 分支已够用，除非 S5 暴露新问题）

---

## 七、约束与红线

1. 不改框架、不改第三方包；不引入兼容层。
2. 业务异步用 UniTask，取消/失败/超时语义必须显式处理。
3. 资源加载与释放配对；场景切换前后不得残留未归零引用。
4. 事件监听必须在销毁/解绑时注销（协调器 `Unbind`）。
5. UI 只读数据，加载控制归模块，不改 `SwitchSceneUI` 的运行时美化。
6. 日志统一走项目日志桥接，不用 `Debug.Log` 做业务日志。
7. 不真实构建/发布 DS 除非用户单独授权。

---

## 八、验证方式

| 阶段 | 命令/方式 | 说明 |
|---|---|---|
| 编译 | `python .codex/scripts/workflow.py verify --profile code` | C# 全解决方案构建 |
| Editor | `python .codex/scripts/workflow.py verify --profile unity` | Editor 编译 + 指定资源/测试集 |
| 联调 | 编辑器 Host 双端切场景、DS + 1 客户端、中途加入、断线重连 | 人工按 1.2 清单勾验 |
| DS 产物 | `python -m tengine_build run --preset dedicated_server_linux --json` | **需用户授权**；结果确认 `isServerBuild`、场景加载、无 NRE |
| 全量 | `python .codex/scripts/workflow.py verify --profile full` | 交付前 |

---

## 九、风险与未确认项

| # | 项 | 影响 | 处置 |
|---|---|---|---|
| 1 | 客户端 Single 加载对 TEngine 主场景 handle 的影响未验证 | 可能出现脏引用告警 | 被动模式不依赖 TEngine 主场景 handle；联调时观察 |
| 2 | 客户端进度只能是伪进度 | 观感与真实加载不完全匹配 | 接受；如需真实进度须改包（红线）或走方案 B |
| 3 | `isPublic` / `ScenePlayersModule` 可见性语义未深挖 | 分房间场景可能不符合预期 | 首版 `isPublic=true`（全服同一场景），后续再做分组 |
| 4 | DS 未挂 NM 时协调器为空 | 功能不可用 | S1 前置；`LoadSceneServer` 明确报错并提示 |
| 5 | 客户端 action 队列 Tick 时机未知 | 伪进度起始点可能延迟 | 以 `onYooAssetSceneStartLoading` 为准，不假设时间 |
| 6 | 断线重连重放完整性 | 极端场景状态不一致 | 联调专项验证；必要时用 `GetFullHistory` 兜底复查 |

**未确认项统一标注为「未验证」，不得在交付中写成已验证。**

---

## 十、执行顺序

```
S1 场景挂 NM（需授权，Unity 操作）
  ↓
S2 协调器（无 UI 依赖，可先写）
  ↓
S3 GameSceneModule 被动模式
  ↓
S4 事件扩展
  ↓
S5 GameApp DS 入口改造
  ↓
S6 联调（Host → DS → 中途加入 → 重连）
  ↓
验证：code → unity → （授权后）DS 产物 → full
```

S2 与 S3 可并行开发，但联调必须 S1 完成。

---

## 十一、授权记录

- 2026-10-18：用户要求“落盘为详细的规划文件”，仅授权创建本计划文档与更新计划索引，**未授权**实施代码修改、Unity 场景写入或真实构建。
- 后续执行时：先读本文件 + `.plans/purrnet-ds-integration.md` + 当时 `git status`；按当前源码状态复核签名与路径；Unity 写入、构建等按仓库授权规则单独确认。
