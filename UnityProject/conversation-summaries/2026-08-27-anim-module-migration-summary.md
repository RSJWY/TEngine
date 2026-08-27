# DGame AnimModule 迁移到 TEngine

> 时间：2026-08-27
> 来源：[DGame](https://github.com/AmaniDawn/DGame) `Assets/DGame/Runtime/Module/AnimModule/`（9 个 .cs 文件）

## 背景

前两批迁移（FrameAnimModule + GameObjectPoolModule、UI 组件扩展 + Utility 散件）已完成后，用户要求把 DGame 的 AnimModule 迁移到 TEngine。该模块用 PlayableGraph 做代码驱动的 3D 动画图，本质是把 Unity 底层 Playable API 封一层糖，9 个文件自成体系，无 PlayableGraph 之外的特殊依赖，迁移成本低于 GameObjectPoolModule。

前序会话总结（`2026-08-27-frame-anim-gameobject-pool-migration-summary.md`）中曾标注"AnimModule 因依赖 DGame 自定义 PlayableGraph 封装层，暂不迁移"——本次即补迁该模块。

## 决策

经用户确认，按与前两批一致的依赖映射规则迁移：

| DGame | TEngine |
|-------|---------|
| `MemoryObject.Spawn<T>()`/`Release()`/`OnRelease()` | `MemoryPool.Alloc<T>()`/`Dealloc()`/`InitFromPool()+RecycleToPool()` |
| `Module.OnCreate()`/`OnDestroy()` | `Module.OnInit()`/`Shutdown()` |
| `DGameException` | `System.Exception` |
| `DLogger` | `Log` |
| `ModuleSystem.GetModule<T>()` | 不变（TEngine 同名同签名） |
| 私有字段 `m_xxx` | `_小驼峰` |
| 命名空间 `DGame` | `TEngine` |

## 产物清单

### 框架层（`Assets/TEngine/Runtime/Module/AnimModule/`）

9 个 .cs 文件全部迁移：

| 文件 | 职责 | 关键改动 |
|------|------|---------|
| `AnimationWrapper.cs` | `[Serializable]` 动画片段包装数据类 | 命名空间 `DGame`→`TEngine` |
| `AnimState.cs` | `AnimInfo` 动画信息只读视图 | `m_animClip`→`_animClip` |
| `IAnimModule.cs` | 动画模块接口 | 命名空间改 `TEngine` |
| `IAnimPlayable.cs` | 动画图接口 | 命名空间改 `TEngine` |
| `AnimNode.cs` | Playable 节点抽象基类 | `DGameException`→`Exception`，`m_`→`_`，清理残留注释 |
| `AnimClip.cs` | 动画片段节点（继承 AnimNode） | `m_`→`_` |
| `AnimMixer.cs` | 动画混合器（继承 AnimNode） | `DLogger`→`Log`，`m_`→`_`，清理残留注释 |
| `AnimPlayable.cs` | 动画图（`MemoryObject`） | `Spawn→Alloc`/`Release→Dealloc`/`OnRelease→InitFromPool+RecycleToPool`，`DLogger`→`Log`，`DGameException`→`Exception` |
| `AnimModule.cs` | 动画模块实现 | `OnCreate→OnInit`/`OnDestroy→Shutdown`，`DGameException`→`Exception`，`m_`→`_` |

### GameModule 访问器

- `Assets/GameScripts/HotFix/GameLogic/GameModule.cs` 新增 `Anim` 访问器（`IAnimModule`），`Shutdown()` 补 `_anim = null`（顺便补了前序遗漏的 `_gameObjectPool = null`）。

## 关键发现

- **模块自动注册**：`ModuleSystem.GetModule<IAnimModule>()` 用反射约定——接口 `IAnimModule` 去 `I` 前缀找 `AnimModule`（同命名空间 `TEngine` 同程序集 `TEngine.Runtime`）。已验证 `ModuleSystem.cs:81` 的拼接逻辑：`{interfaceType.Namespace}.{interfaceType.Name.Substring(1)}, {interfaceType.Assembly.GetName().Name}`，迁移后的接口和实现类完全符合，无需手动 `RegisterModule`。

- **MemoryObject API 验证**：通过读取 `TEngine.Runtime/Core/MemoryPool/MemoryPoolExtension.cs` 确认真实 API：`MemoryObject` 是抽象类，含 `InitFromPool()`/`RecycleToPool()` 两个抽象方法；`MemoryPool.Alloc<T>()` 内部调用 `Acquire<T>()` 后调 `InitFromPool()`；`MemoryPool.Dealloc(MemoryObject)` 内部调 `RecycleToPool()` 后调 `Release()`。原 DGame 的 `MemoryObject.Spawn<T>()`/`Release()`/`OnRelease()` 是 DGame 自定义的，TEngine 用 `Alloc/Dealloc/InitFromPool+RecycleToPool`。

- **Module 基类 API 验证**：`TEngine.Runtime/Core/Module.cs` 确认：`public abstract void OnInit()` + `public abstract void Shutdown()` + `public virtual int Priority => 0`。`IUpdateModule` 接口含 `Update(float elapseSeconds, float realElapseSeconds)`。

- **AnimPlayable 的 OnRelease 拆分**：原 DGame 的 `AnimPlayable.OnRelease()` 既有销毁逻辑又有状态重置。迁移时拆成 `InitFromPool()`（空实现，因为 Create 方法会全量初始化）和 `RecycleToPool()`（销毁 clips/mixers/graph + 重置 `_isDestroyed`）。这与 GameObjectPool 的做法一致。

- **注释清理**：AnimNode.cs 原有两处被注释掉的旧代码（`// parent.AddInput(m_curPlayable, ...)`、`// m_graph.Disconnect(m_parent, ...)`），AnimMixer.cs 有一处（`// var animHashCode = ...`）。这些注释中的 `m_` 会被 grep 误判为 DGame 残留，已全部清理。

## 验证结果

由于 Unity 无法在 CLI 编译，做了完整静态检查：

1. **grep 零残留**：`grep -r "DGame|DGameException|DLogger|MemoryObject\.|m_[a-z]" AnimModule/` 无匹配。
2. **API 存在性**：所有引用的类型（`Module`、`IUpdateModule`、`MemoryObject`、`MemoryPool`、`Log`、`ModuleSystem`、`UnityEngine.Playables`、`UnityEngine.Animations`）均在 `TEngine.Runtime` 程序集或 Unity 内置模块中验证存在。
3. **程序集归属**：目标目录 `Assets/TEngine/Runtime/Module/AnimModule/` 属于 `TEngine.Runtime.asmdef` 范围，`noEngineReferences: false` 天然可访问 Unity Playables/Animations。
4. **反射约定**：`IAnimModule`（`TEngine` 命名空间）与 `AnimModule`（`TEngine` 命名空间，`internal sealed`）同程序集，符合反射约定。

待用户在 Unity 中打开项目编译验证。

## 文档同步

- 新建 `Books/Fork/anim-module.md` 专题文档
- 更新 `Books/Fork/README.md`（索引表+最近重点）、`Books/Fork-定制改动说明.md`（索引表）、根 `README.md`（主题表）、`Books/Fork/CHANGELOG.md`
- 新建 `conversation-summaries/code-research/2026-08-27-AnimModule迁移到TEngine.md` 研究文档

## 关键词

AnimModule迁移、PlayableGraph、AnimPlayable、AnimClip、AnimMixer、AnimNode、AnimationWrapper、MemoryObject Alloc/Dealloc、InitFromPool/RecycleToPool、Module OnInit/Shutdown、IUpdateModule、ModuleSystem反射约定注册、DGameException→Exception、DLogger→Log、GameModule.Anim访问器、TEngine.Runtime程序集、3D动画图
