# GameObject 对象池模块（GameObjectPoolModule）

## 背景

上游 TEngine 自带的 `ObjectPoolModule` 是**逻辑对象引用计数池**（管 `ObjectBase` 句柄），不是 GameObject 实例化池。DGame 自研的 `GameObjectPoolModule` 提供基于 YooAsset location 的异步实例化、预热、自动销毁等能力，填补了 TEngine 在「GameObject 实体池」方向的空白。

## 改动摘要

- 迁移 DGame `GameObjectPoolModule` 到 `TEngine/Runtime/Module/GameObjectPoolModule/`（框架层，`TEngine` 命名空间）。
- 实现模块：`GameObjectPoolModule : Module, IGameObjectPoolModule, IUpdateModule`，靠 TEngine `ModuleSystem` 反射约定自动注册（接口 `IGameObjectPoolModule`→实现类 `GameObjectPoolModule`，同命名空间同程序集），**无需手动 `RegisterModule`**。
- 核心实体：`GameObjectPool : MemoryObject`（按 location 维护一池预制体实例，支持预热/回收/自动销毁）。
- 对象身份标记：`GameObjectPoolIdentity`（挂在每个被池管理的 GameObject 上，回收时反查所属池）。
- 调试窗口：`GameObjectPoolModuleDebuggerWindow`（Editor，菜单 `TEngine Tools/Debugger/GameObject Pool`）。
- 热更层 `GameModule.cs` 新增 `GameObjectPool` 访问器。

### 行为变化

- `Module` 基类方法名对齐 TEngine：`OnCreate()`→`OnInit()`，`OnDestroy()`→`Shutdown()`。
- `MemoryObject` API 对齐 TEngine：`Spawn<T>()`→`Alloc<T>()`，`Release()`→`Dealloc()`，`OnRelease()`→实现 `InitFromPool()`+`RecycleToPool()`。
- `DGameLinkedList<T>`（带节点池的 LinkedList）简化为 `System.Collections.Generic.LinkedList<T>`。
- `AddMonoBehaviour<T>` 内联为 `TryGetComponent`+`AddComponent`。
- `DGameException`→`System.Exception`，`DLogger`→`Log`。
- 私有字段统一 `_小驼峰`。

### 保持不变

- 异步预热（`initCapacity`）、容量上限（`maxCapacity`）、自动销毁时间（`autoDestroyTime`）、DontDestroy 常驻、超容复用（`allowMultiSpawn`）等核心能力完全保留。
- 并发建池锁（`SemaphoreSlim` + `PoolCreateLock` RefCount）保留。
- 每帧空池自动回收（`IUpdateModule.Update` 扫表）保留。

## 使用方式

```csharp
// 异步实例化（自动建池/取池）
GameObject go = await GameModule.GameObjectPool.SpawnAsync("Assets/Prefabs/Enemy.prefab", parent, ct);

// 回收
GameModule.GameObjectPool.Recycle(go);

// 预创建带配置的对象池
await GameModule.GameObjectPool.CreateGameObjectPoolAsync(
    location: "Assets/Prefabs/Enemy.prefab",
    initCapacity: 5,
    maxCapacity: 20,
    autoDestroyTime: 60f,      // 空池 60s 后自动销毁
    dontDestroy: false,
    allowMultiSpawn: false);

// 丢弃（不归还池，直接销毁）
GameModule.GameObjectPool.Remove(go);

// 销毁指定池
GameModule.GameObjectPool.DestroyPool("Assets/Prefabs/Enemy.prefab");

// 销毁所有非常驻池（切场景用）
GameModule.GameObjectPool.DestroyAllPool(includeAll: false);
```

Editor 调试：菜单 `TEngine Tools/Debugger/GameObject Pool` 打开窗口，运行时查看所有池状态、对象明细、使用率进度条，支持销毁池操作。

## 注意事项

- **资源生命周期**：`LoadGameObjectAsync` 会实例化资源到场景，Destroy 时自动 `UnloadAsset`（TEngine `IResourceModule` 约定）。
- **身份标记**：每个被池管理的 GameObject 会自动挂 `GameObjectPoolIdentity` 组件（记录 `PoolKey`），回收时据此反查所属池。
- **并发安全**：同一 location 并发建池由 `SemaphoreSlim` 串行化，双重判定避免重复创建。
- **自动销毁**：`autoDestroyTime <= 0` 不自动销毁；`MarkedForDestroy` 标记后等所有对象归还才销毁；`DontDestroy` 池在 `DestroyAllPool(false)` 时保留。
- **与 TEngine `ObjectPoolModule` 区别**：本模块管 GameObject 实体实例化池；`ObjectPoolModule` 管 `ObjectBase` 逻辑对象引用计数池，两者互补不冲突。

## 关键文件

- `UnityProject/Assets/TEngine/Runtime/Module/GameObjectPoolModule/IGameObjectPoolModule.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/GameObjectPoolModule/GameObjectPoolRoot.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/GameObjectPoolModule/GameObjectPoolDebugInfo.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/GameObjectPoolModule/GameObjectPool.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/GameObjectPoolModule/GameObjectPoolModule.cs`
- `UnityProject/Assets/TEngine/Editor/GameObjectPoolModule/GameObjectPoolModuleDebuggerWindow.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/GameModule.cs`（新增 `GameObjectPool` 访问器）

## 相关记录

- `UnityProject/conversation-summaries/code-research/2026-08-27-DGame模块迁移到TEngine.md`
- `UnityProject/conversation-summaries/2026-08-27-frame-anim-gameobject-pool-migration-summary.md`
