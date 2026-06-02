# 事件系统新增按事件类型批量取消监听功能

**日期**: 2026-06-02  
**类型**: 框架核心增强 - 事件系统  
**影响范围**: `Assets/TEngine/Runtime/Core/GameEvent/` 三个文件

---

## 背景

用户反馈 TEngine 事件系统取消订阅时存在设计短板：`RemoveEventListener` 强制要求传入注册时的委托实例，导致以下场景无法实现：

1. Lambda 注册后无法在别处取消（每次写的 lambda 是新实例，`Delegate.Equals` 不成立）
2. 方法组注册但监听对象已销毁 / 在别的模块，拿不到原委托引用
3. 想凭事件 ID 一次性清空该事件的所有监听，但框架只有 `Shutdown()`（清空所有事件）和 `GameEventMgr.Clear()`（清空局部管理器记录的那批），粒度不对

用户已有大量预定义的 `const int` 事件 ID，期望能凭这些 const 直接清空对应事件，不需要持有原 handler。

---

## 诊断

阅读整条事件链路源码：

- `GameEvent`（静态门面）→ `EventMgr.Dispatcher` → `EventDispatcher`（`Dictionary<int, EventDelegateData>`）→ `EventDelegateData`（`List<Delegate> _listExist`）
- 移除操作最终走 `EventDelegateData.RmvHandler(Delegate handler)` → `_listExist.Remove(handler)`，依赖 `Delegate.Equals`（target + method 都相同才算同一个）
- 确认框架确实没有"按事件 ID 批量清"的入口，`RemoveEventListener` 所有重载都强制要 `handler` 参数

根因：这是 C# 原生 `event -=` 的限制延续，不算 bug，但作为全局事件总线缺少灵活性。

---

## 方案

**选型**：轻量增强方案（向后兼容，零破坏现有 API）

新增 `RemoveAllListeners(int/string eventType)`，凭事件 ID 清空该事件下的全部监听，不需要原委托，也不影响其他事件。复用框架已有的延迟增删机制（`_isExecute`/`_dirty`/`_deleteList`），保证回调遍历过程中调用也安全。

不采用订阅句柄 Token 模式（改动大、引入"管理句柄"的新负担），也不加"按对象取消"（超出当前需求）。

---

## 实现

### 1. `EventDelegateData.cs` — 新增 `RemoveAll()`

清空 `_listExist`（该事件挂的所有委托）。分支：

- 不在回调中（`_isExecute == false`）：直接 `_listExist.Clear()`
- 正在回调遍历中（`_isExecute == true`）：走延迟删除，`_deleteList.AddRange(_listExist)` + `_addList.Clear()`，等本轮 `Callback` 结束后 `CheckModify()` 落地

```csharp
/// <summary>
/// 移除本事件下的所有监听。
/// <remarks>无需传入注册时的委托，凭事件类型即可清空。</remarks>
/// </summary>
internal void RemoveAll()
{
    if (_isExecute)
    {
        // 正在回调遍历中：走延迟删除，避免破坏 _listExist 的遍历。
        _dirty = true;
        _deleteList.AddRange(_listExist);
        // 本帧尚未生效的待新增也一并取消，符合"全部清空"语义。
        _addList.Clear();
    }
    else
    {
        _listExist.Clear();
    }
}
```

### 2. `EventDispatcher.cs` — 新增 `RemoveAllListeners(int eventType)`

在 `_eventTable` 字典中定位到对应 `EventDelegateData`，调其 `RemoveAll()`。事件不存在时安全跳过。

```csharp
/// <summary>
/// 移除指定事件类型的所有监听。
/// <remarks>无需传入注册时的委托，凭事件类型即可清空，且不影响其他事件。</remarks>
/// </summary>
/// <param name="eventType">事件类型。</param>
public void RemoveAllListeners(int eventType)
{
    if (_eventTable.TryGetValue(eventType, out var data))
    {
        data.RemoveAll();
    }
}
```

### 3. `GameEvent.cs` — 新增静态门面

对外入口，委托给 `Dispatcher`。int / string 两版（string 版通过 `RuntimeId.ToRuntimeId` 转为 int）。

```csharp
/// <summary>
/// 移除指定事件类型的所有监听。
/// <remarks>无需传入注册时的委托，凭事件类型即可清空该事件下的全部监听，且不影响其他事件。</remarks>
/// </summary>
/// <param name="eventType">事件类型。</param>
public static void RemoveAllListeners(int eventType)
{
    _eventMgr.Dispatcher.RemoveAllListeners(eventType);
}

/// <summary>
/// 移除指定事件类型的所有监听。
/// <remarks>无需传入注册时的委托，凭事件类型即可清空该事件下的全部监听，且不影响其他事件。</remarks>
/// </summary>
/// <param name="eventType">事件类型。</param>
public static void RemoveAllListeners(string eventType)
{
    _eventMgr.Dispatcher.RemoveAllListeners(RuntimeId.ToRuntimeId(eventType));
}
```

---

## 文档更新

### README.md

在"本 Fork 的定制改动"章节新增"📡 事件系统"分类，记录本次能力：

> **按事件类型批量取消监听（`GameEvent.RemoveAllListeners`）** — 在 `GameEvent`/`EventDispatcher`/`EventDelegateData` 三层新增，无需持有注册时的委托，凭事件 ID（手写 `const` 或接口事件生成的 `IXxx_Event.OnXxx`）即可清空该事件下的全部监听，且不影响其他事件；支持 int / string 两种事件 ID。复用底层既有的延迟增删机制，回调过程中调用也安全。补足原框架"取消必须传回注册委托、无法从别处取消"的短板。

### event-system.md

1. 在"核心 API"章节补上 `RemoveAllListeners` 的说明，明确调用链路和延迟机制
2. 更新"清除事件的三种粒度"对照（原文档说"没有批量清单事件的方法"，现在有了）
3. 修正"误用不存在的 UnRegisterAll"的正确用法示例，加入 `RemoveAllListeners`

---

## 验证

### 编译
Unity 编译通过 ✓

### 功能测试

创建测试脚本 `EventRemoveAllTest.cs` 覆盖 5 个场景：

1. **正常清空** — 注册 → 发送(收到) → `RemoveAllListeners` → 发送(不收到)  ✓
2. **回调中清空** — 两个监听，第一个回调里调 `RemoveAllListeners`，验证延迟删除机制（第二个还应执行完，下次发送全部失效）  ✓
3. **多监听者** — 同一事件注册 3 个监听，一次清空全部失效  ✓
4. **不存在的事件** — 清空从未注册的事件 ID，验证不抛异常  ✓
5. **string 事件** — 验证 `RemoveAllListeners(string)` 版 API  ✓

测试全部通过，测试代码已删除（不纳入版本库）。

---

## 使用示例

```csharp
// 手写 const 事件
public const int OnHpChanged = 10001;
GameEvent.AddEventListener<int>(OnHpChanged, OnHp);
// ... 别的模块，凭 const 清空，不需要 OnHp 委托
GameEvent.RemoveAllListeners(OnHpChanged);

// 接口事件（Source Generator 自动生成的 IXxx_Event.OnXxx 也是 int）
GameEvent.AddEventListener<int>(IBattleEvent_Event.OnHpChanged, OnHp);
GameEvent.RemoveAllListeners(IBattleEvent_Event.OnHpChanged);

// string 事件
GameEvent.AddEventListener("OnGoldChanged", OnGold);
GameEvent.RemoveAllListeners("OnGoldChanged");
```

---

## 关键决策记录

### 为什么不做"按对象取消"（`RemoveEventListener(eventType, object target)`）

凭 `Delegate.Target` 匹配可以实现，但：
- 对静态 lambda（未捕获实例）无效（`Target` 是 null）
- 用户当前需求明确是"凭 const 清空单个事件"，不需要按对象筛选
- 可以后续增量补充，不影响本次 API

### 为什么不做订阅句柄 Token 模式

更彻底，但：
- 要给每个委托分配 id，改写 `EventDelegateData` 的存储结构（从 `List<Delegate>` 升级成 `List<(long id, Delegate)>`）
- 延迟增删机制整套跟着改
- 引入"持有并管理句柄"的新负担，反而可能引入"忘记 Dispose"的泄漏
- 对于"随手清掉"的场景（本次需求）比轻量方案更啰嗦

### 延迟删除机制的必要性

框架原本就有（`_isExecute`/`_dirty`/`_addList`/`_deleteList`），`RemoveAll()` 复用它：
- `Callback` 遍历 `_listExist` 时若调用 `RemoveAll`，直接 `Clear()` 会破坏正在进行的 `for` 循环
- 标记 `_dirty = true` 并登记到 `_deleteList`，等 `CheckModify()` 在遍历结束后统一处理
- `_addList.Clear()` 保证"全部清空"语义（本帧待新增的也不生效）

---

## 提交记录

**Commit**: `93a60e60`  
**分支**: `main`  
**远程**: 已推送到 `origin/main`

改动文件：
- `README.md`
- `UnityProject/.claude/skills/tengine-dev/references/event-system.md`
- `UnityProject/Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs`
- `UnityProject/Assets/TEngine/Runtime/Core/GameEvent/EventDispatcher.cs`
- `UnityProject/Assets/TEngine/Runtime/Core/GameEvent/GameEvent.cs`

---

## 后续可能的扩展方向

若未来需要更精细的取消能力，可考虑：

1. **按对象批量取消** — `RemoveEventListener(int eventType, object target)` 或 `RemoveAllListeners(object target)`（遍历所有事件，移除 `Delegate.Target == target` 的）
2. **订阅句柄模式** — 新增 `Subscribe` 返回 `EventHandle`，凭句柄精确取消单个 lambda
3. 两者叠加 — 粗粒度批量 + 细粒度精确，覆盖全场景

当前方案已完整覆盖用户需求，性价比最高，零破坏现有 API。
