# TEngine TimerModule vs DGame GameTimer 对比研究

> 日期：2026-08-26
> 对比对象：
> - TEngine：`Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs` + `ITimerModule.cs`
> - DGame：`Assets/DGame/Runtime/Module/GameTimer/GameTimer.cs` + `GameTimerModule.cs` + `IGameTimerModule.cs`（本地 `E:\Unity\DGame\GameUnity`，GitHub AmaniDawn/DGame）

## 一、文件结构

### TEngine
- `TimerModule.cs`：`internal class TimerModule : Module, IUpdateModule, ITimerModule`，内嵌 `[Serializable] internal class Timer` 数据类。
- `ITimerModule.cs`：对外接口。
- 委托：`public delegate void TimerHandler(object[] args)`（命名空间 `TEngine`）。

### DGame
- `GameTimer.cs`：`public class GameTimer`（数据载体，`[Serializable]`），带两个构造函数（无限循环 / 限定次数循环）+ `Destroy()` + `IsNull()`。
- `GameTimerModule.cs`：`internal sealed class GameTimerModule : Module, IUpdateModule, IGameTimerModule`。
- `IGameTimerModule.cs`：对外接口。
- 委托：`public delegate void TimerHandler(object[] args)`（命名空间 `DGame`，与 TEngine 同名同签名但不同命名空间）。

## 二、核心 API 对比

| 能力 | TEngine | DGame |
|------|---------|-------|
| 创建一次性 | `AddTimer(cb, time, isLoop=false, isUnscaled=false, args)` | `CreateOnceGameTimer / CreateUnscaledOnceGameTimer` |
| 创建无限循环 | `AddTimer(cb, time, isLoop=true, ...)` | `CreateLoopGameTimer / CreateUnscaledLoopGameTimer` |
| **创建限定次数循环** | **不支持**（只有 isLoop bool） | **`CreateLoopCountGameTimer / CreateUnscaledLoopCountGameTimer(interval, loopCount, ...)`** |
| 暂停/恢复 | `Stop(id)` / `Resume(id)` | `Pause(timer)` / `Resume(timer)` |
| 查询运行 | `IsRunning(id)` | `IsRunning(timer)` |
| 剩余时间 | `GetLeftTime(id)` | `GetTimerLeft(timer)` |
| 重置 | `Restart(id)` + `Reset/ResetTimer` 两个重载（参数顺序不一致） | `Restart(timer)` + `Reset(timer, ...)` 两个重载 |
| 移除 | `RemoveTimer(id)`（标记） | `DestroyGameTimer(timer)`（标记） |
| 全清 | `RemoveAllTimer()` | `DestroyAllGameTimer()` |
| 系统定时器 | `AddSystemTimer(cb)` 1秒 AutoReset | `CreateSystemTimer(cb)` 1秒 AutoReset |

关键差异：
- **句柄**：TEngine 返回 `int timerId`（`_curTimerId` 自增、不回收）；DGame 返回 `GameTimer` 对象引用，更类型安全。
- **循环次数**：DGame 独有 `LoopCount` + `HasLoopCount` 字段，支持限定次数循环；TEngine 只有 isLoop bool。
- **API 语义**：TEngine 用单一 `AddTimer` 通配；DGame 用六个工厂方法（Once/Loop/LoopCount × scaled/unscaled）语义清晰。

## 三、数据结构与实现机制

### TEngine
- `List<Timer> _timerList`（scaled）+ `List<Timer> _unscaledTimerList`（unscaled）。
- `List<int> _cacheRemoveTimers / _cacheRemoveUnscaledTimers`：缓存待移除索引。
- 插入排序：`InsertTimer` 按 `curTime` 升序插入 List（O(n) 插入）。
- Update：遍历 List，`curTime -= elapseSeconds`，到期回调；非循环加入缓存移除列表，循环则 `curTime += time`。
- 移除：标记 `isNeedRemove`，Update 末尾倒序 `RemoveAt`（List RemoveAt 是 O(n)）。
- **坏帧处理**：`LoopCallInBadFrame` / `LoopCallUnscaledInBadFrame` **递归调用，无次数上限**（TimerModule.cs:287-339），若 interval 极小且大量积压，存在栈溢出风险。

### DGame
- `DGameLinkedList<GameTimer> m_gameTimers` + `m_unscaleGameTimers`（自定义双向链表，带节点池）。
- 插入排序：`InsertGameTimer` 按 `TriggerTime` 升序插入链表。
- Update：`while (curNode != null)` + `nextNode` 遍历链表，`TriggerTime -= elapseSeconds`，到期回调。
  - 有 `HasLoopCount` 分支：`LoopCount--`，>0 重排，<=0 则 `Destroy()` + `Remove`。
  - 无限循环：`TriggerTime += IntervalTime`。
  - 一次性：`Destroy()` + `Remove`。
- 移除：标记 `IsNeedRemove`，Update 时 `Destroy()` + 链表 Remove（O(1)）。
- **坏帧处理**：`HandleLoopBadFrame` / `HandleUnscaleLoopBadFrame` 用 **while 循环 + `m_maxBadFrameCheckCnt = 10` 上限**（GameTimerModule.cs:112-161），不会栈溢出。
- `DestroyAllGameTimer` 额外调用 `ClearNodePool()` 清理节点池。

## 四、对象生命周期

- TEngine `Timer`：internal class，外部无法持有引用，只能靠 int id 操作；无显式 Destroy，靠 GC 回收。
- DGame `GameTimer`：public class，外部持有引用；有 `IsDestroyed` 标志 + 静态 `IsNull(timer)`（null 或 IsDestroyed）防重复使用；`Destroy()` 显式清空所有字段。

## 五、共同点

1. 框架定位一致：都继承 `Module, IUpdateModule`，由框架 `Update(elapseSeconds, realElapseSeconds)` 驱动（时间基准由上层传入，非自己读 Time.time）。
2. 都分 scaled / unscaled 两条独立链。
3. 都按剩余时间升序插入（排序保证到期任务集中在前）。
4. 都用 `TimerHandler(object[] args)` 委托避免闭包（params 传参）。
5. 都是标记 `IsNeedRemove` 下一帧统一清理，非立即移除。
6. 都附带 `System.Timers.Timer` 系统定时器（1秒间隔、AutoReset=true）。

## 六、结论

DGame 的 `GameTimer` 是 TEngine `TimerModule` 的**改良版**，主要改进：

1. **句柄**：int id → 对象引用，类型更安全。
2. **循环次数**：新增 `LoopCount` 限定次数循环能力。
3. **坏帧安全**：递归无上限 → while + 10 次上限，消除栈溢出风险（TEngine 最大隐患）。
4. **数据结构**：List + RemoveAt O(n) → 双向链表 + 节点池 O(1) 删除，性能与内存更优。
5. **API 语义**：单一 AddTimer + 混乱重载 → 六个工厂方法，可读性更强。
6. **对象管理**：internal 靠 GC → public + IsDestroyed 标志 + 显式 Destroy() 清字段。

TEngine 待改进点：递归坏帧处理无上限（应改为 while + 上限）、List 的 O(n) 删除、缺乏限定循环次数能力。
