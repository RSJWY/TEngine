# DGame FrameAnimModule 与 GameObjectPoolModule 迁移到 TEngine

> 时间：2026-08-27
> 来源：[DGame](https://github.com/AmaniDawn/DGame) `Assets/Scripts/HotFix/GameLogic/Module/FrameAnimModule/` + `Assets/DGame/Runtime/Module/GameObjectPoolModule/`

## 背景

用户询问 DGame 的 FrameAnimModule、AnimModule、GameObjectPoolModule 三个模块的职责。解释清楚后，用户要求将 FrameAnimModule 和 GameObjectPoolModule 迁移到 TEngine，并根据 `UIFrameAnimatorAgent` 新写一个基于 `RawImage` 的 `UIFrameRawAnimatorAgent`。AnimModule 因依赖 DGame 自定义 PlayableGraph 封装层（IAnimPlayable/AnimPlayable/AnimationWrapper），暂不迁移。

## 决策

经与用户确认三项关键决策：
1. **迁移范围**：只迁 FrameAnimModule + GameObjectPoolModule（AnimModule 不迁）。
2. **ModelConfig 依赖**：改用简单配置结构体 `FrameAnimConfig`（TEngine 未用 Luban）。
3. **代码风格**：私有字段 `_小驼峰`（遵循 TEngine naming-rules.md）。

## 依赖映射

迁移前彻底研究了两边基础设施，建立完整映射表（详见 code-research 文档）：

| DGame | TEngine | 关键变化 |
|-------|---------|---------|
| `MemoryObject.Spawn<T>()`/`Release()`/`OnRelease()` | `MemoryPool.Alloc<T>()`/`Dealloc()`/`InitFromPool()+RecycleToPool()` | 方法名+抽象方法结构变化 |
| `Singleton<T>.Destroy()`/`OnDestroy()`/`m_instance` | `Singleton<T>.Release()`/`OnRelease()`/`_instance` | 方法名变化 |
| `GameTime.Time`/`UnscaledTime` | `UnityEngine.Time.time`/`unscaledTime` | 直接用 Unity 原生 |
| `DLogger` | `Log` | 方法名兼容 |
| `GameModule.ResourceModule`/`GameTimerModule` | `GameModule.Resource`/`Timer` | 属性名变化 |
| `GameTimer` 对象 + `IGameTimerModule` | `int timerId` + `ITimerModule` | 对象句柄→int，`CreateLoopGameTimer`→`AddTimer`，`DestroyGameTimer`→`RemoveTimer` |
| `Module.OnCreate()`/`OnDestroy()` | `Module.OnInit()`/`Shutdown()` | 方法名变化 |
| `DGameException` | `System.Exception` | 无对应物 |
| `DGameLinkedList<T>` | `LinkedList<T>` | 简化，去节点池 |
| `Utility.UnityUtil.AddMonoBehaviour<T>` | 内联 `TryGetComponent`+`AddComponent` | 无对应物 |
| `ModelConfig`（Luban 生成）| 新建 `FrameAnimConfig` 结构体 | 去 Luban |
| `FrameSpritePoolGenerator`（Roslyn SourceGenerator）| 手写 `FrameSpritePool.Gen.cs` | TEngine 无 SourceGenerator |

## 产物清单

### FrameAnimModule（热更层 `Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/`）
- `FrameAnimConfig.cs` — 新建结构体，替代 ModelConfig
- `FrameAnimName.cs` — 枚举照搬
- `FrameClip.cs` — 纯逻辑，`m_`→`_`
- `FrameSpritePool.cs` + `FrameSpritePool.Gen.cs` — 手写补全原 Roslyn 输出
- `FrameSpriteMgr.cs` — 调度器，`GameTimer`→`int timerId`，`Singleton.OnRelease`
- `FrameAnimatorAgent.cs` — 场景版 SpriteRenderer
- `UIFrameAnimatorAgent.cs` — UI 版 Image
- `UIFrameRawAnimatorAgent.cs` — **新建** RawImage 版，`rawImage.texture = sprite.texture`

### GameObjectPoolModule（框架层 `Assets/TEngine/Runtime/Module/GameObjectPoolModule/`）
- `IGameObjectPoolModule.cs`
- `GameObjectPoolRoot.cs`
- `GameObjectPoolDebugInfo.cs`（含 `GameObjectPoolObjectDebugInfo`）
- `GameObjectPool.cs`（含 `GameObjectPoolIdentity`）
- `GameObjectPoolModule.cs`

### Editor（`Assets/TEngine/Editor/GameObjectPoolModule/`）
- `GameObjectPoolModuleDebuggerWindow.cs`

### GameModule 访问器
- `Assets/GameScripts/HotFix/GameLogic/GameModule.cs` 新增 `GameObjectPool` 访问器

## 关键发现

- **TEngine 自带 ObjectPoolModule 不可替代**：TEngine 的 `ObjectPoolModule` 是逻辑对象引用计数池（管 `ObjectBase` 句柄），不是 GameObject 实例化池。DGame 的 `GameObjectPoolModule` 有独特价值（异步预热/YooAsset location 加载/自动销毁/DontDestroy/并发锁），应迁移。
- **模块自动注册**：TEngine `ModuleSystem.GetModule<IXxxModule>()` 用反射约定——接口去 `I` 前缀找实现类（同命名空间同程序集），`IGameObjectPoolModule`/`GameObjectPoolModule` 均在 `TEngine` 命名空间+`TEngine.Runtime` 程序集，符合约定，无需手动 `RegisterModule`。
- **TEngine 有 GameTime**：`GameTime.time`/`unscaledTime`（小写字段），由 `RootModule.StartFrame()` 驱动。迁移代码用 `UnityEngine.Time` 直接，稳妥且不依赖驱动时序。

## 验证结果

用户在 Unity 中打开项目编译通过。

## 文档同步

- 新建 `Books/Fork/frame-anim.md`、`Books/Fork/game-object-pool.md` 专题文档
- 更新 `Books/Fork/README.md`（索引表+最近重点）、`Books/Fork-定制改动说明.md`（索引表）、根 `README.md`（主题表+概览）
- 更新 `Books/Fork/CHANGELOG.md`
- 新建 `conversation-summaries/code-research/2026-08-27-DGame模块迁移到TEngine.md` 研究文档

## 关键词

FrameAnimModule迁移、GameObjectPoolModule迁移、UIFrameRawAnimatorAgent、RawImage.sprite.texture、FrameAnimConfig替代ModelConfig、FrameSpritePoolGenerator手写Gen、MemoryObject Spawn→Alloc、Singleton OnDestroy→OnRelease、GameTimer→int timerId、ITimerModule、ModuleSystem反射约定注册、DGameLinkedList→LinkedList、DGameException→Exception、GameModule.GameObjectPool访问器、TEngine.Runtime程序集
