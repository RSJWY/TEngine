# TEngine FSM 状态机与 Procedure 流程开发模式

> **适用场景**：新增/修改启动流程（Procedure）、用 FsmModule 自建状态机（怪物 AI、角色行为、网络连接状态）、排查流程卡住问题 | **关联文档**：[modules.md](modules.md)（模块访问）、[resource-api.md](resource-api.md)（资源异步加载）、[hotfix-workflow.md](hotfix-workflow.md)（热更边界）

## 决策：三种"状态管理"选哪个

| 场景 | 方案 | 参考 |
|---|---|---|
| 游戏启动 / 热更资源流程 | Procedure 流程状态 | `Assets/GameScripts/Procedure/`（已有 11 个流程） |
| 怪物 AI、角色行为、网络状态等多实例对象 | `FsmModule.CreateFsm` 自建状态机 | 本文"自建状态机模板" |
| 单对象的阶段推进（加载进度动画等） | 手写 `int` 阶段 + `Update` switch | `GameSceneModule.cs` 三段式进度 |

## 核心 API 速查

状态机管理器通过 `GameModule.Fsm`（热更侧）访问：

```csharp
// 创建：owner 必须是 class；states 会立即全部 OnInit；同 owner+name 重复创建抛异常
var fsm = GameModule.Fsm.CreateFsm("MonsterAI", monster,
    new IdleState(), new PatrolState(), new ChaseState());

fsm.Start<IdleState>();                  // 启动，IsRunning 后再调抛异常，只能调一次
fsm.HasState<ChaseState>();              // 查询状态
fsm.SetData("Key", value);               // 跨状态共享数据（Dictionary<string,object>，懒初始化）
var v = fsm.GetData<string>("Key");
fsm.RemoveData("Key");

GameModule.Fsm.DestroyFsm<Monster>("MonsterAI");  // 销毁：触发 OnLeave(isShutdown:true) + 所有状态 OnDestroy
```

状态切换**只能在状态内部**发起（`FsmState<T>` 的 protected 方法）：

```csharp
public class IdleState : FsmState<Monster>
{
    protected override void OnUpdate(IFsm<Monster> fsm, float elapseSeconds, float realElapseSeconds)
    {
        ChangeState<PatrolState>(fsm);    // 旧状态 OnLeave → 重置计时 → 新状态 OnEnter
    }
}
```

生命周期时序：`OnInit`（CreateFsm 时一次性）→ `OnEnter` / `OnUpdate`（每帧）/ `OnLeave(fsm, isShutdown)` → `OnDestroy`（销毁时）。

**关键机制**：状态以 Type 为键，同一状态类在一台 FSM 里是**单实例**——状态类字段在离开再进入后**保留上次值**，需要重置的放 `OnEnter`；FSM 实例走 MemoryPool 复用，字段清理在 `Clear()` 中完成。

---

## 新增启动流程：三步

1. `Assets/GameScripts/Procedure/` 新建类，继承 `Procedure.ProcedureBase`（注意是游戏侧的，不是 `TEngine.ProcedureBase`）：

```csharp
using Cysharp.Threading.Tasks;
using TEngine;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;   // 项目固定别名

namespace Procedure
{
    public class ProcedureXxx : ProcedureBase
    {
        public override bool UseNativeDialog { get; }            // true = 允许原生弹窗（下载失败等）

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            DoSomethingAsync().Forget();
        }

        private async UniTaskVoid DoSomethingAsync()
        {
            await UniTask.Yield();
            ChangeState<ProcedureNext>(procedureOwner);          // 需把 owner 存字段供异步回调用
        }
    }
}
```

2. 注册进 `Assets/TEngine/Settings/ProcedureSetting.asset` 的 `availableProcedureTypeNames`（反射 `Activator.CreateInstance` 实例化，**漏注册则 ChangeState 抛 "not exist"**）。
3. 在上游流程里 `ChangeState<ProcedureXxx>(...)` 引用。

现有流程链（改动时对照，避免断链）：

```
Launch → Splash → InitPackage → InitResources → CreateDownloader
                                                   ├─ 无缺失 → DownloadOver → ClearCache → Preload → LoadAssembly → StartGame(终态)
                                                   └─ 有缺失 → DownloadFile ─┬─ 成功 → DownloadOver
                                                                              └─ 失败 → 回跳 CreateDownloader 重试
```

跨状态共享数据的 Key 常量集中在游戏侧 `ProcedureBase`（如 `DownloadPackageNamesKey`），新增 Key 加在那里，不要散落在各流程里。

## 四种业务模式（均有项目实例）

**模式 A：OnEnter 发起异步，回调里直接切状态**——适合单一异步任务（`ProcedureInitPackage`）：

```csharp
protected override void OnEnter(ProcedureOwner procedureOwner)
{
    _procedureOwner = procedureOwner;      // 存字段，异步回调里用
    InitPackage(procedureOwner).Forget();
}
```

**模式 B：OnEnter 置 bool，OnUpdate 轮询汇合后切换**——适合多个并行异步（`ProcedureLoadAssembly` 等待 dll + AOT 元数据两路加载；`ProcedureLaunch` 等待部署配置）：

```csharp
if (!_loadAssemblyComplete || !_loadMetadataAssemblyComplete) return;   // OnUpdate 里
ChangeState<ProcedureStartGame>(_procedureOwner);
```

**模式 C：数据字典做跨状态通信**——两条纪律（`ProcedureInitPackage`/`ProcedureDownloadFile`）：
- **进入状态先清理旧数据**：`OnEnter` 开头 `RemoveData` 上轮残留（重试回跳后防脏数据）；
- 可变对象存引用共享：`List<string>` 存入后边下边删，下游读到最新值。

**模式 D：失败回跳 + 重试计数放数据字典**——`ProcedureDownloadFile` 失败 `ChangeState<ProcedureCreateDownloader>` 回上游；重试计数存 `DownloadRetryCountKey` 而非状态字段（回跳后状态字段仍在，但计数语义上属于"会话"，且需在成功/换包时主动 Remove）。

**异步回调切状态的防重**：回跳可能和超时弹窗并发，参考 `_downloadFailedHandled` 标志——置位后其余回调路径直接 return（`ProcedureDownloadFile.RetryCurrentDownloadWithDelay`）。

---

## 自建状态机模板（热更侧）

```csharp
// 状态定义：放热更程序集内
public class MonsterIdleState : FsmState<Monster>
{
    private float _idleTime;                                      // 注意：单实例字段会跨进入保留

    protected override void OnEnter(IFsm<Monster> fsm)
    {
        _idleTime = 0f;                                           // 需要重置的字段在 OnEnter 归零
        fsm.Owner.PlayAnim("Idle");
    }

    protected override void OnUpdate(IFsm<Monster> fsm, float elapseSeconds, float realElapseSeconds)
    {
        _idleTime += elapseSeconds;
        if (fsm.GetData<bool>("Aggro") || _idleTime > 3f)
        {
            ChangeState<MonsterPatrolState>(fsm);
        }
    }

    protected override void OnLeave(IFsm<Monster> fsm, bool isShutdown) { }
}

// 挂载：Monster.OnInit 里创建，OnDestroy 里销毁（成对，否则 FSM 常驻泄漏）
_fsm = GameModule.Fsm.CreateFsm("MonsterAI", this, new MonsterIdleState(), new MonsterPatrolState());
_fsm.Start<MonsterIdleState>();
```

owner 类型自选（`where T : class`），多个怪物用不同 `name` 参数隔离：`CreateFsm($"MonsterAI_{id}", ...)`。

## 红线与坑

1. **`Start` 只能调一次**，`IsRunning` 后再调抛异常；重启用 `ProcedureModule.RestartProcedure`（流程）或 Destroy 后重建（自建 FSM）。
2. **`ChangeState` 只能在状态内部调用**，外部想切状态只能 Destroy 重建或通过事件通知状态自行切换。
3. **状态字段跨进入保留**（Type 为键单实例）——计时器、缓存列表等必须在 `OnEnter` 重置。
4. **数据字典必须清理**：上游 `OnEnter` 清残留；会话级数据（重试计数）用完 `RemoveData`。
5. **异步回调里切状态先判防重标志**，避免重试路径并发触发两次 ChangeState。
6. **主包侧流程代码用 `ModuleSystem.GetModule<T>()`**（此时热更 GameModule 包装层未加载），**热更侧一律 `GameModule.XXX`**——这是位置差异，不是矛盾。
7. **suspendLoad 场景加载的坑**不走本文方案：进度驱动参考 `GameSceneModule.cs` 头部注释（陷阱 1：suspendLoad+progressCallBack 时 await 死循环，只能 fire-and-forget；陷阱 2：阶段≥2 后 progressCallBack 必须忽略，否则 target 被打回 0.90 永远卡 90%）。
8. **流程内 IO 一律 UniTask**（禁止 Coroutine/同步加载），弹窗用 `LauncherMgr.ShowMessageBox`（带 autoConfirmDelay 可做自动确认）。
