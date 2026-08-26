# TimerModule 计时器模块

本页记录 fork 对 TEngine `TimerModule` 的整合改进，改动参考 DGame 项目的 `GameTimerModule` 实现，在保持 TEngine 命名空间、`int` 句柄和 `params` 传参的前提下，吸收 DGame 的坏帧安全、链表存储和限定循环次数能力。

## 改动摘要

- 底层存储由 `List<Timer>` 改为 `GameFrameworkLinkedList<T>`（TEngine 已有带节点池的双向链表），删除节点为 O(1)，移除时不再整体搬移。
- 坏帧处理由递归 `LoopCallInBadFrame` 改为 `while` 循环 + `MaxBadFrameCheckCount = 10` 上限（`HandleLoopBadFrame` / `HandleUnscaledLoopBadFrame`），消除极端场景下的栈溢出风险。
- 新增 `AddLoopCountTimer(callback, time, loopCount, isUnscaled, args)` API，支持限定次数循环；`Timer` 内嵌类新增 `hasLoopCount` / `loopCount` 字段。
- 保留全部旧 API（`AddTimer` / `Stop` / `Resume` / `IsRunning` / `GetLeftTime` / `Restart` / `ResetTimer` / `Reset` / `RemoveTimer` / `RemoveAllTimer`），`int` 句柄和 `params object[] args` 不变，业务代码零改动。
- `Reset` / `ResetTimer` 重置时同步清空 `hasLoopCount` / `loopCount`，避免残留状态。
- `Shutdown` 增加 `ClearCachedNodes()` 调用，清理链表节点池。
- `DestroySystemTimer` 增加 `Dispose` 和列表 `Clear`，修复原版只 `Stop` 不释放的问题。

## 背景

对比 DGame `GameTimerModule` 发现 TEngine 原 `TimerModule` 存在两处实质问题：

1. `LoopCallInBadFrame` 递归调用无次数上限，循环定时器间隔极小且大量积压时可能栈溢出。
2. `List<Timer>` + `RemoveAt` 是 O(n) 删除，大量定时器时每次清理都要搬移数组。

DGame 的对应方案（`while` + 上限、双向链表 + 节点池、`LoopCount`）更优，且 TEngine 已有等价的 `GameFrameworkLinkedList<T>` 可直接复用，无需移植 DGame 的 `DGameLinkedList`。

## 使用方式

旧 API 完全不变：

```csharp
// 一次性 / 无限循环（与原版一致）
int id = GameModule.Timer.AddTimer(OnTick, 1f);
int id = GameModule.Timer.AddTimer(OnTick, 0.5f, isLoop: true);
GameModule.Timer.RemoveTimer(id);
```

新增限定次数循环：

```csharp
// 每0.5秒触发一次，跑3次后自动移除
int id = GameModule.Timer.AddLoopCountTimer(OnTick, 0.5f, 3);

// 不受 Time.timeScale 影响
int id = GameModule.Timer.AddLoopCountTimer(OnTick, 1f, 5, isUnscaled: true);

// 同样支持 params 传参，避免闭包
int id = GameModule.Timer.AddLoopCountTimer(OnTick, 1f, 3, args: a, b);
```

## 注意事项

- **兼容性**：旧 `AddTimer` 签名和 `int` 句柄保持不变，`CommonToastUI` / `SceneGameManagerBase` / `UIModule` 等已有调用方无需改动。
- **句柄回收**：`_curTimerId` 单调自增不回收，与原版一致；定时器移除后 id 不复用。
- **坏帧上限**：`MaxBadFrameCheckCount = 10` 是单帧内补触发循环定时器的最大次数，超过则等下一帧继续，避免一帧内无限回调。
- **必须手动移除**：模块 `Shutdown` 时才全清，场景切换不会自动清理，业务方仍需在 `OnDestroy` 调 `RemoveTimer`。

## 关键文件

- `Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs`
- `Assets/TEngine/Runtime/Module/TimerModule/ITimerModule.cs`
- `Assets/TEngine/Runtime/Core/DataStruct/GameFrameworkLinkedList.cs`（复用，未改动）

## 相关记录

- 整合自 [DGame](https://github.com/AmaniDawn/DGame) `Assets/DGame/Runtime/Module/GameTimer/`。
- 对比研究：`UnityProject/conversation-summaries/code-research/2026-08-26-tengine-timer-vs-dgame-gametimer-research.md`
