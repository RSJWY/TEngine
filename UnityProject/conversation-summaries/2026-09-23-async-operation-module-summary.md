# 模块级自定义异步操作 AsyncOperationModule

## 任务

用户要求研究 YooAsset 3.0.6 的 `CustomAsyncOperation` 体系（`https://www.yooasset.com/docs/solution/CustomOperation`），抄出一套模块级的自定义异步操作，不依赖 YooAsset。两个明确设计决策：
1. 保留 Unity 协程支持（`yield return op`）——成本 10 行，项目 Procedure 层在用，去掉只亏不赚。
2. UniTask 完整支持——`ToUniTask`/`WithCancellation`/进度上报/池化零分配，对齐 YooAsset 官方 Sample。

额外要求：带 abort-on-cancel 语义（`StartOperation(op, token)` 取消即中止操作，注明独占要求）+ 编辑器可视化监控脚本（挂载到 GameEntry 下）。

## 研究过程

### YooAsset 3.0.6 CustomAsyncOperation 体系拆解

完整链路 5 个文件约 1300 行，位于 `Library/PackageCache/com.tuyoogame.yooasset@3.0.6/Runtime/AsyncOperation/`：

| 文件 | 行数 | 作用 |
|---|---|---|
| `AsyncOperationBase.cs` | 734 | 状态机 + 完成/回调/进度/子任务/优先级/await |
| `AsyncOperationScheduler.cs` | 187 | 双队列（running+pending）调度器，优先级排序 |
| `AsyncOperationSystem.cs` | 298 | 多调度器管理 + 全局时间切片 `IsBusy` |
| `CustomAsyncOperation.cs` | 32 | 把 `internal` Start/Update/Abort 转成 `public` |
| `OperationAwaiter.cs` | 48 | `GetAwaiter()`，支持 `await operation` |

关键发现：`AsyncOperationBase` 零资源逻辑，只依赖 `YooLogger`（日志）、`TimeUtility`（DEBUG 计时格式化）、`DiagnosticOperationInfo`（诊断）。移植成 TEngine 版本时换 `Log` 即可。

### TEngine 驱动方式对齐

- `RootModule`（`Assets/TEngine/Runtime/Module/RootModule.cs:144`）每帧调 `ModuleSystem.Update()`。
- `ModuleSystem`（`Assets/TEngine/Runtime/Core/ModuleSystem.cs:29`）按优先级轮询所有 `IUpdateModule`。
- 做成 `Module + IUpdateModule` 的 `AsyncOperationModule` 注册进 `ModuleSystem`，由 `RootModule.Update()` 驱动，天然"模块级"。

### 命名冲突风险分析

TEngine.Runtime 有 14 个文件 `using YooAsset;`。如果在 `namespace TEngine` 下放同名类型（如 `EOperationStatus`），会静默遮蔽 YooAsset 的类型导致现有代码编译错误（C# 命名空间内类型优先于 using 导入）。

确认冲突点：`EOperationStatus` 被 `ResourceModule.cs`/`SceneModule.cs` 使用（这些文件 `using YooAsset;`）。

解决方案：全部新类型放 `namespace TEngine`，但避开 YooAsset 同名：
- `OperationStatus`（vs YooAsset `EOperationStatus`）
- `GameAsyncOperation`（vs YooAsset `AsyncOperationBase`）
- `GameOperationAwaiter`（vs YooAsset `OperationAwaiter`）
- `OperationScheduler`（internal，vs YooAsset `AsyncOperationScheduler`）
- `AsyncOperationModule`（vs YooAsset `AsyncOperationSystem`）

## 实现细节

### 文件清单

`Assets/TEngine/Runtime/Module/AsyncOperationModule/`（8 个 .cs）：

1. `OperationStatus.cs` — `OperationStatus` 枚举 + `AsyncOperationDebugInfo`/`OperationSchedulerDebugInfo` 快照类
2. `GameAsyncOperation.cs` — 抽象基类（状态机/Completed 事件/优先级/进度/子任务树/协程/awaiter/同步等待）
3. `GameOperationAwaiter.cs` — 裸 `await op` 的 awaiter
4. `OperationScheduler.cs` — internal 双队列调度器（pending→running），优先级排序，时间切片让出
5. `IAsyncOperationModule.cs` — 模块接口
6. `AsyncOperationModule.cs` — `Module + IUpdateModule`，多调度器管理，`MaxTimeSlice`，abort-on-cancel 重载
7. `GameAsyncOperationUniTaskExtensions.cs` — `ToUniTask`/`WithCancellation`/进度上报，池化零分配
8. `Editor/AsyncOperationMonitor.cs` — `#if UNITY_EDITOR` 可视化监控组件

改动：`Assets/GameScripts/HotFix/GameLogic/GameModule.cs` 加 `AsyncOperation` 属性。

### abort-on-cancel 实现

```csharp
public void StartOperation(string schedulerName, GameAsyncOperation operation, CancellationToken cancellationToken)
{
    if (cancellationToken.CanBeCanceled)
    {
        cancellationToken.Register(static op => ((GameAsyncOperation)op).AbortOperation(), operation);
    }
    StartOperation(schedulerName, operation);
}
```

XML 注释注明独占要求：传入的操作实例必须由调用方独占，不要同时提交给其它调度器，不要让多处代码共享同一个等待。否则任一处取消都会让所有等待方收到 `Failed("Operation was aborted.")`。

### 与 YooAsset 的差异

1. **实例化而非全局 static**：`AsyncOperationSystem` 是 static，本模块是 `Module` 实例，`IsBusy` 走 `_instance` 静态桥接。
2. **abort-on-cancel 语义**：YooAsset `WithCancellation(token)` 只取消等待，操作继续跑完；本模块 `StartOperation(op, token)` 取消即 `AbortOperation()`。
3. **无 Diagnostic 系统**：未移植 `DiagnosticOperationInfo`/`DiagnosticReport`，调试走自有 `AsyncOperationDebugInfo` 快照 + `AsyncOperationMonitor`。
4. **无 `CustomAsyncOperation` 二级包装**：驱动方法保持 internal，用户操作必须交模块调度（更安全）。

### UniTask 扩展

对齐 YooAsset 官方 Sample `AsyncOperationBaseExtensions.cs`（`Samples~/UniTask Sample/UniTask/Runtime/External/YooAsset/`）：
- 池化 `IUniTaskSource` + `IPlayerLoopItem` + `ITaskPoolNode` 三件套
- `ToUniTask(progress, timing, cancellationToken, cancelImmediately)`
- `WithCancellation(token)` 只取消等待（注释说明用 `StartOperation(op, token)` 获得 abort 语义）

TEngine.Runtime 已引用 UniTask（`UpdateDriver.cs` 在用），无需新增 asmdef 引用。

## 验证

### 编译

Unity 刷新后零编译错误。唯一一次错误是 `AsyncOperationMonitor.cs` 里 `Editor` 被解析为命名空间而非类型，改 `UnityEditor.Editor` 修复。

### 冒烟测试

临时脚本 `AsyncOperationSmokeTest.cs`（已删除）验证三项：

1. **3 帧推进**：progress 0→0.33→0.67→1.0，第 3 帧 `Succeeded` ✓
2. **abort-on-cancel**：`cts.Cancel()` 后立即 `status=Failed`、`error=Operation was aborted.` ✓
3. **快照**：`GetDebugInfo()` 返回 1 个调度器 ✓

## Fork 文档对齐

按 fork-docs skill 流程更新四层文档：

1. 新增专题文档 `Books/Fork/async-operation.md`（背景/改动摘要/命名隔离/与 YooAsset 差异/使用方式/注意事项/关键文件/相关记录）
2. `Books/Fork/CHANGELOG.md` 追加 2026-09-23 条目
3. `Books/Fork/README.md` 索引表 + 最近重点
4. 根 `README.md` 主题表 + fork 定位说明
5. `Books/Fork-定制改动说明.md` 索引表

## 提交

commit `b4a4cb22`：19 个文件（含 .meta），1842 行新增。工作区其余无关变动（字体、AOT 清单、packages、ProjectSettings、GameEntry.prefab 等）未动。
