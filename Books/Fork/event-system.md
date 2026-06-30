# 事件系统

本页记录 fork 中对 `GameEvent` 事件系统的定制改动。

## 按事件类型批量取消监听

### 背景

原框架取消监听时通常需要传回注册时的委托。这在生命周期复杂、监听方和取消方不在同一处，或者事件 ID 来自生成接口时不够方便。

这个 fork 补充了按事件 ID 清空监听的能力，用于解决“无需持有原委托，也能从别处取消某类事件全部监听”的场景。

### 改动摘要

- 新增 `GameEvent.RemoveAllListeners`。
- 在 `GameEvent`、`EventDispatcher`、`EventDelegateData` 三层接入。
- 支持 int / string 两种事件 ID。
- 可使用手写 `const` 事件 ID，也可使用接口事件生成的 `IXxx_Event.OnXxx`。
- 只清空指定事件 ID 下的监听，不影响其他事件。
- 复用底层既有的延迟增删机制，回调过程中调用也安全。

### 使用方式

```csharp
// int 事件 ID
GameEvent.RemoveAllListeners(MyEventId);

// string 事件 ID
GameEvent.RemoveAllListeners("MyEvent");

// 接口事件生成 ID
GameEvent.RemoveAllListeners(IGameSceneEvent.OnSceneLoadOver);
```

### 适用场景

- 场景切换时清理某类事件的历史监听。
- 调试工具或运行时重置逻辑需要集中清空某个事件。
- 监听方不可控，无法安全保存原始委托。

### 注意事项

- 这是“按事件 ID 清空全部监听”，不是按对象或模块定向解绑。
- 不应替代正常生命周期内的精确解绑；组件自身能持有委托时仍优先使用原有移除方式。
- 对公共事件使用时要确认不会误删其他系统仍需要的监听。

### 关键文件

- `Assets/TEngine/Runtime/Core/Event/GameEvent.cs`
- `Assets/TEngine/Runtime/Core/Event/EventDispatcher.cs`
- `Assets/TEngine/Runtime/Core/Event/EventDelegateData.cs`
