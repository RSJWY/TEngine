# 模块级自定义异步操作（AsyncOperationModule）

## 背景

YooAsset 3.0.6 提供了一套 `CustomAsyncOperation` 体系：业务继承基类、在调度器里分帧推进，自带状态机、优先级排序、时间切片预算、完成回调、子任务树、协程/awaiter/同步等待。但它是 `YooAsset` 命名空间下的内部设施，不依赖资源系统的业务流程想用就得拖进整个 YooAsset 运行时。

本 fork 把这套设计抽出来做成 TEngine 自有的框架级模块 `AsyncOperationModule`，业务自定义异步流程（批量加载、分帧解析、多阶段初始化等）不再绑定 YooAsset，由 `ModuleSystem` 统一驱动。

## 改动摘要

- 新增 `TEngine/Runtime/Module/AsyncOperationModule/`（框架层，`TEngine` 命名空间），8 个 .cs 文件自成体系，仅依赖 `TEngine.Runtime` 已有的 `Log`/`GameFrameworkException`/`Module`/`ModuleSystem` 和 UniTask。
- 实现模块：`AsyncOperationModule : Module, IUpdateModule, IAsyncOperationModule`，靠 TEngine `ModuleSystem` 反射约定自动注册（接口 `IAsyncOperationModule`→实现类 `AsyncOperationModule`，同命名空间同程序集），**无需手动 `RegisterModule`**。
- 抽象基类 `GameAsyncOperation`：状态机（`None/Processing/Succeeded/Failed`）、`Completed` 事件（注册时已完成立即执行）、`Priority`/`Progress`/`IsDone`/`Error`、子任务树（`AddChildOperation`/`RemoveChildOperation`，`Abort` 递归中止）、协程（`IEnumerator`）、awaiter（`GetAwaiter`）、同步等待（`WaitForCompletion`）。
- 调度器 `OperationScheduler`：双队列（pending→running）、优先级排序、时间切片让出（`IsBusy`）。
- 多调度器管理：全局调度器 + 命名调度器（`CreateScheduler`/`DestroyScheduler`/`AbortSchedulerOperations`），绑定业务域生命周期。
- abort-on-cancel 重载：`StartOperation(op, CancellationToken)` 把 token 取消转为 `AbortOperation()`，做到"取消等待 = 中止操作"。
- UniTask 完整支持：`ToUniTask`/`WithCancellation`/`IProgress<float>` 进度上报/`PlayerLoopTiming` 选择/`TaskTracker` 可见性/池化零分配。
- 编辑器可视化监控组件 `AsyncOperationMonitor`：`#if UNITY_EDITOR` 包裹，挂载到任意物体（推荐 GameEntry 下），Inspector 实时显示调度器、操作状态/进度/耗时/优先级/错误，支持历史记录。
- 热更层 `GameModule.cs` 新增 `AsyncOperation` 访问器。

### 命名隔离

类型全部放 `namespace TEngine`，但避开 YooAsset 同名以防遮蔽：

| 本模块 | YooAsset 对应 | 说明 |
| --- | --- | --- |
| `OperationStatus` | `EOperationStatus` | 枚举名不同，避免 14 个 `using YooAsset;` 的 TEngine.Runtime 文件静默改绑定 |
| `GameAsyncOperation` | `AsyncOperationBase` | 基类 |
| `GameOperationAwaiter` | `OperationAwaiter` | awaiter |
| `OperationScheduler` | `AsyncOperationScheduler` | internal 调度器 |
| `AsyncOperationModule` | `AsyncOperationSystem` | 模块（实例化，非 static） |

### 保持不变

- 状态机流转语义（`None→Processing→Succeeded/Failed`，`SetResult`/`SetError` 对已 Done 抛异常）完全保留。
- `Completed` 事件注册时已完成立即同步执行的语义完全保留。
- `WaitForCompletion` 走 `InternalWaitForCompletion` 默认循环（`Thread.Sleep(1)`）的语义完全保留。
- 子任务循环依赖检测（DEBUG 下 `WouldCreateCycle`）完全保留。
- 调度器双队列 + 优先级 `IComparable` 排序 + 时间切片 `IsBusy` 让出完全保留。
- UniTask 扩展的池化 `IUniTaskSource` + `IPlayerLoopItem` + `ITaskPoolNode` 三件套实现完全保留（对齐 YooAsset 官方 Sample）。

### 与 YooAsset 的差异

- **实例化而非全局 static**：YooAsset 的 `AsyncOperationSystem` 是 static，多调度器共享全局时间切片预算。本模块是 `Module` 实例，`IsBusy` 走 `AsyncOperationModule._instance` 静态桥接，生命周期跟随 `ModuleSystem`（`OnInit` 创建全局调度器，`Shutdown` 中止所有并清空）。
- **abort-on-cancel 语义**：YooAsset 的 `WithCancellation(token)` 只取消"等待"，操作继续跑完。本模块新增 `StartOperation(op, token)` 重载，token 触发即 `AbortOperation()`，操作立即从调度器移除并释放资源。
- **无 Diagnostic 系统**：YooAsset 的 `DiagnosticOperationInfo`/`DiagnosticReport` 体系未移植，调试走自有 `AsyncOperationDebugInfo` 快照 + `AsyncOperationMonitor` 组件。
- **无 `CustomAsyncOperation` 二级包装**：YooAsset 因 `AsyncOperationBase` 的 `StartOperation/UpdateOperation` 是 internal，需要 `CustomAsyncOperation` 把它们转 public 供用户手动驱动。本模块驱动方法保持 internal，用户操作必须交模块调度（设计上更安全，防误用）。

## 使用方式

### 继承写自定义操作

```csharp
public class LoadTextOp : GameAsyncOperation
{
    private readonly string _path;
    public string Text { get; private set; }

    public LoadTextOp(string path) => _path = path;

    protected override void InternalStart()
    {
        // 发起异步请求，把句柄存字段
    }

    protected override void InternalUpdate()
    {
        // 分帧轮询句柄是否完成，上报 Progress，完成后 SetResult/SetError
    }

    protected override void InternalDispose()
    {
        // 统一清理入口，释放句柄
    }

    protected override string InternalGetDescription() => $"load {_path}";
}
```

### 提交操作

```csharp
// 协程：yield return op
GameModule.AsyncOperation.StartOperation(op);

// async/await（裸 awaiter，无取消/进度）
await op;

// UniTask 完整支持（进度上报 + 取消等待）
await op.ToUniTask(progress: Progress.Create(p => Debug.Log(p)), cancellationToken: token);

// abort-on-cancel（取消即中止操作本体，独占语义）
GameModule.AsyncOperation.StartOperation(op, token);
```

### 命名调度器（业务域隔离）

```csharp
// 为下载域创建独立调度器
GameModule.AsyncOperation.CreateScheduler("Download", priority: 10);
GameModule.AsyncOperation.StartOperation("Download", op);

// 域结束时一次性中止所有
GameModule.AsyncOperation.DestroyScheduler("Download");
```

### 编辑器可视化

在 GameEntry 下新建 GameObject，挂载 `AsyncOperationMonitor` 组件（Add Component 搜 "Async Operation Monitor"）。运行时 Inspector 显示：
- 每个调度器：名称、优先级、running/pending 计数
- 每个操作：类型名、状态色块、进度条、耗时（ms/帧）、优先级、错误信息
- 已完成操作历史（可开关、带上限）

## 注意事项

- **独占要求**：`StartOperation(op, CancellationToken)` 重载会把 token 取消转为 `AbortOperation()`，传入的操作实例必须由调用方独占——不要同时提交给其它调度器，不要让多处代码共享同一个等待。否则任一处取消都会让所有等待方收到 `Failed("Operation was aborted.")`。
- **abort-on-cancel vs WithCancellation**：`op.WithCancellation(token)`（UniTask 扩展）只取消"等待"，操作继续跑完占调度器；`StartOperation(op, token)`（模块重载）取消即中止操作。按需求选择。
- **打包剥离**：`AsyncOperationMonitor` 整个文件 `#if UNITY_EDITOR` 包裹，打包时不编译。场景里挂载的物体会变成 missing script（无害 warning），不影响运行。
- **时间切片**：`MaxTimeSlice` 默认 `long.MaxValue`（不限制），设为 30ms 等值后每帧执行超过预算即让出。同步等待（`IsWaitForCompletion`）时 `IsBusy` 始终返回 false，确保操作能跑完。
- **程序集归属**：`AsyncOperationModule/` 在 `TEngine.Runtime.asmdef` 范围内，UniTask（`f51ebe6a...`）已被 TEngine.Runtime 引用（`UpdateDriver` 在用），无需新增 asmdef 引用。
- **模块优先级**：`AsyncOperationModule.Priority = 70`，早于 `ResourceModule`(4)/`ObjectPoolModule`(6) 等业务模块轮询，让操作推进早于业务查询。

## 关键文件

- `UnityProject/Assets/TEngine/Runtime/Module/AsyncOperationModule/IAsyncOperationModule.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AsyncOperationModule/AsyncOperationModule.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AsyncOperationModule/GameAsyncOperation.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AsyncOperationModule/GameOperationAwaiter.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AsyncOperationModule/OperationScheduler.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AsyncOperationModule/OperationStatus.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AsyncOperationModule/GameAsyncOperationUniTaskExtensions.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AsyncOperationModule/Editor/AsyncOperationMonitor.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/GameModule.cs`（新增 `AsyncOperation` 访问器）

## 相关记录

- `UnityProject/conversation-summaries/2026-09-23-async-operation-module-summary.md`
