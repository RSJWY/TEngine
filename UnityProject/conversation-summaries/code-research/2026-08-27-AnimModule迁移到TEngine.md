# AnimModule 迁移到 TEngine 研究记录

> 时间：2026-08-27
> 来源：[DGame](https://github.com/AmaniDawn/DGame) `Assets/DGame/Runtime/Module/AnimModule/`

## 模块概述

AnimModule 是 DGame 基于 Unity PlayableGraph 的代码驱动 3D 动画图模块。它把 Unity 底层的 Playable API（`AnimationClipPlayable`/`AnimationMixerPlayable`/`AnimationLayerMixerPlayable`）封装一层糖，支持：

- 多层级动画混合（LayerMixer）
- 权重过渡（StartWeightFade）
- 动画片段动态增删（AddAnimationClip/RemoveAnimationClip）
- 手动驱动（DirectorUpdateMode.Manual + Evaluate）
- 多种 WrapMode（Once/Loop/ClampForever）

## 9 个文件结构与依赖关系

```
AnimationWrapper.cs   — [Serializable] 数据类（Layer/WrapMode/Clip/FadeDuration）
AnimState.cs          — AnimInfo（AnimClip 的只读视图）
IAnimModule.cs        — 模块接口
IAnimPlayable.cs      — 动画图接口
AnimNode.cs           — 抽象基类（Playable 节点：连接/断开/权重过渡）
AnimClip.cs           — 动画片段节点 : AnimNode
AnimMixer.cs          — 动画混合器 : AnimNode
AnimPlayable.cs       — 动画图 : MemoryObject, IAnimPlayable
AnimModule.cs         — 模块 : Module, IUpdateModule, IAnimModule
```

依赖关系：`AnimModule` → 管理多个 `AnimPlayable` → 管理多个 `AnimMixer` + `AnimClip` → 都继承 `AnimNode`。`AnimInfo` 是 `AnimClip` 的只读视图，`AnimationWrapper` 是序列化数据。

## 关键 API 映射

### MemoryObject（AnimPlayable 继承）

| DGame | TEngine | 位置 |
|-------|---------|------|
| `MemoryObject.Spawn<T>()` | `MemoryPool.Alloc<T>()` | `MemoryPoolExtension.cs:35` |
| `MemoryObject.Release(this)` | `MemoryPool.Dealloc(this)` | `MemoryPoolExtension.cs:46` |
| `override void OnRelease()` | `override void InitFromPool()` + `override void RecycleToPool()` | `MemoryObject` 抽象类 |

**验证**：`Alloc<T>()` 内部 `Acquire<T>()` 后调 `InitFromPool()`；`Dealloc(MemoryObject)` 内部调 `RecycleToPool()` 后调 `Release()`。

原 DGame `AnimPlayable.OnRelease()` 逻辑：
1. 销毁所有 clips/mixers
2. 清空列表
3. `m_graph.Destroy()`
4. `m_isDestroyed = true`

迁移后拆分：
- `InitFromPool()`：空实现（`Create()` 方法全量初始化）
- `RecycleToPool()`：原 `OnRelease()` 的全部销毁+重置逻辑

### Module（AnimModule 继承）

| DGame | TEngine | 位置 |
|-------|---------|------|
| `override void OnCreate()` | `override void OnInit()` | `Module.cs:33` |
| `override void OnDestroy()` | `override void Shutdown()` | `Module.cs:38` |
| `interface IUpdateModule` | `interface IUpdateModule` | `Module.cs:8`（同签名） |

**验证**：`Module` 抽象类在 `TEngine.Runtime/Core/Module.cs`，含 `OnInit()`/`Shutdown()` 两个抽象方法 + `Priority` 虚属性。`IUpdateModule` 接口含 `Update(float, float)`，签名与 DGame 完全一致。

### 反射约定自动注册

`ModuleSystem.GetModule<IAnimModule>()` 的查找逻辑（`ModuleSystem.cs:81`）：

```csharp
string moduleName = $"{interfaceType.Namespace}.{interfaceType.Name.Substring(1)}, {interfaceType.Assembly.GetName().Name}";
Type moduleType = Type.GetType(moduleName);
```

即：`IAnimModule`（命名空间 `TEngine`，程序集 `TEngine.Runtime`）→ 查找 `TEngine.AnimModule, TEngine.Runtime`。

迁移后：
- 接口 `IAnimModule`：`namespace TEngine`，程序集 `TEngine.Runtime`
- 实现类 `AnimModule`：`namespace TEngine`，`internal sealed class AnimModule : Module, IUpdateModule, IAnimModule`，程序集 `TEngine.Runtime`

完全符合反射约定，**无需手动 `RegisterModule`**。

## 迁移要点

### 1. AnimPlayable 的 Create 方法

原 DGame：
```csharp
AnimPlayable animPlayable = MemoryObject.Spawn<AnimPlayable>();
animPlayable.m_animator = animator;
// ... 初始化 graph/mixerRoot/output
animPlayable.m_isDestroyed = false;
```

迁移后：
```csharp
AnimPlayable animPlayable = MemoryPool.Alloc<AnimPlayable>();
animPlayable._animator = animator;
// ... 初始化 graph/mixerRoot/output
animPlayable._isDestroyed = false;
```

注意：`Alloc<T>()` 会先调 `InitFromPool()`，所以 `InitFromPool()` 实现为空，全部初始化在 `Create()` 中完成。`Create()` 是静态工厂方法，不经过 `new`。

### 2. AnimPlayable 的 DestroyGraph

原 DGame：`public void DestroyGraph() => MemoryObject.Release(this);`
迁移后：`public void DestroyGraph() => MemoryPool.Dealloc(this);`

`Dealloc` 内部会调 `RecycleToPool()`（销毁逻辑）然后 `Release()`（归还池）。

### 3. AnimModule 的 Update 临时列表

`AnimModule.Update` 使用 `_tempAnimPlayableList` 临时列表避免遍历时修改字典。原 DGame 用 `m_tempAnimPlayableList`，迁移后改 `_tempAnimPlayableList`，逻辑不变。

### 4. 注释清理

AnimNode.cs 原有两处被注释掉的旧代码（含 `m_` 前缀变量名），AnimMixer.cs 有一处。为保持 grep 零残留，全部清理。这些注释不影响逻辑。

### 5. GameModule 访问器

```csharp
public static IAnimModule Anim => _anim ??= Get<IAnimModule>();
private static IAnimModule _anim;
```

`Shutdown()` 补 `_anim = null`。同时补了前序遗漏的 `_gameObjectPool = null`。

## 与 GameObjectPoolModule 迁移的对比

| 维度 | GameObjectPoolModule | AnimModule |
|------|---------------------|------------|
| 文件数 | 5 个 .cs + 1 Editor | 9 个 .cs，无 Editor |
| MemoryObject 子类 | GameObjectPool（复杂生命周期） | AnimPlayable（简单生命周期） |
| 模块特性 | 异步 + IUpdate + 并发锁 + YooAsset | 同步 + IUpdate + PlayableGraph |
| 特殊依赖 | YooAsset location、CancellationToken | 无（纯 Playable API） |
| 迁移难点 | 并发建池锁、自动销毁时机 | OnRelease 拆分为 InitFromPool+RecycleToPool |
| 迁移成本 | 高 | 低 |

## 结论

AnimModule 迁移完成，9 个文件全部对齐 TEngine 依赖映射。模块靠反射约定自动注册，无需手动 `RegisterModule`。静态检查零 DGame 残留，所有引用 API 均已验证存在。待 Unity 编译验证。
