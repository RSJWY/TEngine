# DGame 模块迁移到 TEngine 研究记录

> 时间：2026-08-27
> 主题：将 DGame 的 FrameAnimModule、GameObjectPoolModule 迁移到 TEngine

## 一、依赖映射表（DGame → TEngine）

| DGame 依赖 | DGame 位置 | TEngine 对应物 | TEngine 位置 | 迁移说明 |
|-----------|-----------|---------------|-------------|---------|
| `MemoryObject` 抽象类 | `DGame/Runtime/Core/MemoryPool/MemoryObject.cs` | `TEngine.Runtime/Core/MemoryPool/MemoryPoolExtension.cs` | 同左 | API 改：`Spawn<T>()`→`Alloc<T>()`，`Release()`→`Dealloc()`，`OnRelease()`→实现 `InitFromPool()+RecycleToPool()` |
| `MemoryPool` 静态类 | `DGame/Runtime/Core/MemoryPool/MemoryPool.cs` | `TEngine.Runtime/Core/MemoryPool/` | 同左 | 同上 |
| `Singleton<T>` | `GameLogic/Module/SingletonSystem/Singleton.cs` | `GameLogic/SingletonSystem/Singleton.cs`（热更层自带）| 同左 | API 改：`Destroy()`→`Release()`，`OnDestroy()`→`OnRelease()`，`m_instance`→`_instance` |
| `GameTime` | `DGame/Runtime/Core/GameTime/GameTime.cs`（属性 Time/UnscaledTime）| `TEngine.Runtime/Core/GameTime/GameTime.cs`（字段 time/unscaledTime）| 同左 | 迁移代码用 `UnityEngine.Time.time/Time.unscaledTime` 直接，稳妥且不依赖 RootModule 驱动顺序 |
| `DLogger` | `DGame/Runtime/Core/DGameLog/DLogger.cs` | `TEngine.Runtime/Core/Log/Log.cs` | 同左 | 方法名兼容：`Log.Error/Warning/Info/Assert` |
| `GameModule`（热更）| `GameLogic/GameModule.cs`（ResourceModule/GameTimerModule）| `GameLogic/GameModule.cs`（Resource/Timer）| 同左 | 属性名改：`ResourceModule`→`Resource`，`GameTimerModule`→`Timer` |
| `IResourceModule` | `DGame.Runtime`（LoadAssetAsync/LoadGameObjectAsync/UnloadAsset）| `TEngine.Runtime/Module/ResourceModule/IResourceModule.cs` | 同左 | API 兼容，签名一致 |
| `GameTimer` 对象 + `IGameTimerModule` | `DGame/Runtime/Module/GameTimer/` | `ITimerModule`（返回 `int timerId`）| `TEngine.Runtime/Module/TimerModule/` | API 改：`GameTimer` 对象→`int timerId`（0=无效），`CreateLoopGameTimer`→`AddTimer(cb, time, isLoop=true, isUnscaled)`，`DestroyGameTimer`→`RemoveTimer(id)`，`GameTimer.IsNull`→`id==0` |
| `TimerHandler` 委托 | `DGame`（`void(object[])`）| `TEngine`（`void(object[])`）| 同左 | 签名一致 |
| `Module` 抽象类 | `DGame/Runtime/Core/ModuleSystem/Module.cs`（OnCreate/OnDestroy）| `TEngine.Runtime/Core/Module.cs`（OnInit/Shutdown）| 同左 | 方法名改：`OnCreate()`→`OnInit()`，`OnDestroy()`→`Shutdown()` |
| `IUpdateModule` | 同上（Update）| `TEngine.Runtime/Core/Module.cs` | 同左 | API 完全一致：`Update(float, float)` |
| `ModuleSystem` | `DGame/Runtime/Core/ModuleSystem/`（GetModule/RegisterModule）| `TEngine.Runtime/Core/ModuleSystem.cs` | 同左 | **关键**：TEngine 用反射约定自动创建——接口 `IXxxModule`→实现类 `XxxModule`（去 I 前缀），同命名空间同程序集。无需手动 RegisterModule |
| `DGameException` | `DGame/Runtime/Core/DGameLog/` | 无对应物 | — | 迁移代码用 `System.Exception` |
| `DGameLinkedList<T>` | `DGame/Runtime/Core/DGameStruct/`（带节点池的 LinkedList）| 无对应物 | — | 迁移代码简化用 `System.Collections.Generic.LinkedList<T>`（无节点池优化，但功能等价） |
| `Utility.UnityUtil.AddMonoBehaviour<T>` | `DGame/Runtime/Core/Utility/UnityUtil.cs`（无则加组件）| 无完全对应 | — | 迁移代码内联：`if (!go.TryGetComponent<T>(out var c)) c = go.AddComponent<T>();` |
| `ModelConfig`（Luban 生成）| `GameProto/LubanConfig/` | 无（TEngine 未用 Luban）| — | 新建 `FrameAnimConfig` 结构体（含 FrameCfgLocation/ModelScale/DeathFrameSpeed/UIScale 四字段），Agent.Init 改收此结构体 |

## 二、FrameSpritePoolGenerator 处理

原 DGame 用 Roslyn SourceGenerator（`Tools/Generata Tools/SourceGenerator/FrameSpritePoolGenerator/`）依据 `FrameAnimName` 枚举成员在编译期生成 `FrameSpritePool` 的 partial 补全：每个枚举成员生成 `public List<Sprite> Xxx` 字段（首字母大写）+ `GetSprites/AddSprite/SortAllSprites/SortSprite/ParseLastNumber` 方法。

TEngine 无 SourceGenerator 机制。迁移改为**手写等价文件** `FrameSpritePool.Gen.cs`，与生成器输出完全等价。新增 `FrameAnimName` 枚举成员时需同步在此文件补字段与 case。

## 三、TEngine 自带 ObjectPoolModule 评估

TEngine 已有 `TEngine.Runtime/Module/ObjectPoolModule/`（`IObjectPoolModule` + `ObjectBase`），但它管的是**逻辑对象引用计数池**（`ObjectBase` 持有句柄），不是 GameObject 实例化池。DGame 的 `GameObjectPoolModule` 有独特价值：基于 YooAsset location 异步加载/预热、容量上限、自动销毁、DontDestroy 常驻、并发建池锁、每帧空池回收。两者不可替代，故迁移 DGame 版。

## 四、模块注册机制确认

TEngine `ModuleSystem.GetModule<T>()` 采用**反射约定**：接口 `IGameObjectPoolModule`（命名空间 `TEngine`）→ 查找实现类 `GameObjectPoolModule`（同命名空间、同程序集 `TEngine.Runtime`）→ `Activator.CreateInstance` 创建 → 自动 `OnInit()` + 注册 Update。**无需手动注册**。

`IGameObjectPoolModule` 与 `GameObjectPoolModule` 均在 `TEngine` 命名空间、`TEngine.Runtime` 程序集，符合约定，`GameModule.GameObjectPool`（热更层访问器）和 Editor 调试窗口的 `GetModule()` 均可直接工作。

## 五、迁移产物清单

### FrameAnimModule（热更层，`Assets/GameScripts/HotFix/GameLogic/Module/FrameAnimModule/`）
- `FrameAnimConfig.cs`（新建，替代 ModelConfig）
- `FrameAnimName.cs`（枚举照搬）
- `FrameClip.cs`（纯逻辑，m_→_）
- `FrameSpritePool.cs`（partial 声明）
- `FrameSpritePool.Gen.cs`（手写生成器输出等价物）
- `FrameSpriteMgr.cs`（调度器，GameTimer→int timerId，Singleton.OnRelease）
- `FrameAnimatorAgent.cs`（场景版 SpriteRenderer，MemoryObject API 改写，收 FrameAnimConfig）
- `UIFrameAnimatorAgent.cs`（UI 版 Image）
- `UIFrameRawAnimatorAgent.cs`（新建，RawImage 版，`rawImage.texture = sprite.texture`）

### GameObjectPoolModule（框架层，`Assets/TEngine/Runtime/Module/GameObjectPoolModule/`）
- `IGameObjectPoolModule.cs`
- `GameObjectPoolRoot.cs`
- `GameObjectPoolDebugInfo.cs`（含 GameObjectPoolObjectDebugInfo）
- `GameObjectPool.cs`（含 GameObjectPoolIdentity，DGameLinkedList→LinkedList，MemoryObject API 改写）
- `GameObjectPoolModule.cs`（Module.OnInit/Shutdown，DLogger→Log，DGameException→Exception）

### Editor（`Assets/TEngine/Editor/GameObjectPoolModule/`）
- `GameObjectPoolModuleDebuggerWindow.cs`（UnityEditorUtil.LayoutFoldoutBox→内联 Foldout，菜单改 TEngine Tools）

### GameModule 访问器
- `Assets/GameScripts/HotFix/GameLogic/GameModule.cs` 新增 `GameObjectPool` 访问器

## 六、潜在风险点（需用户在 Unity 中验证）

1. **GameTime 风格**：迁移代码用 `UnityEngine.Time.time/unscaledTime` 而非 TEngine 的 `GameTime.time/unscaledTime`。功能等价，仅风格不一致。若要严格遵循 TEngine 风格可后续改为 `GameTime`（需确认 `RootModule.StartFrame()` 调用时序早于帧动画调度）。
2. **FrameSpritePool 资源 Prefab**：原 DGame 的帧动画 Prefab 挂有 `FrameSpritePool` 组件并填好各动画的 Sprite 列表。迁移后需在 TEngine 项目中重新制作或迁移这些 Prefab，Inspector 字段名与 `FrameSpritePool.Gen.cs` 一致（Idle/Run/Attack... 首字母大写）。
3. **MonoSingleton 命名空间**：DGame 原 `FrameSpriteMgr` 继承 `GameLogic.Singleton<T>`（纯 C# 单例）。TEngine 热更层 `Singleton<T>` 也在 `GameLogic` 命名空间，API 已对齐，应无问题。
4. **GameObjectPool 的 MemoryObject 复用**：TEngine `MemoryObject` 的 `InitFromPool` 在 `Alloc` 时自动调用。`GameObjectPool.Create` 静态工厂在 `Alloc` 后继续赋值，`InitFromPool` 仅设 `IsDestroyed=false`，其余字段在 `RecycleToPool` 已清。首次创建字段为默认值，逻辑正确。
5. **编译验证**：Unity 项目无法在 CLI 直接编译。需在 Unity Editor 中打开项目触发编译验证。GameLogic.asmdef 已引用 TEngine.Runtime（GUID:e34a5702dd353724aa315fb8011f08c3）与 UniTask，引用链完整。
