# 动画模块（AnimModule）

## 背景

DGame 基于 Unity PlayableGraph 实现了一套代码驱动的 3D 动画图封装。它把 Unity 底层 Playable API（`AnimationClipPlayable`/`AnimationMixerPlayable`/`AnimationLayerMixerPlayable`）封装为层级糖衣，支持多层级动画混合、权重过渡、动画片段动态增删、手动驱动。TEngine 自带无对应模块，迁移后填补 3D 动画图方向的空白。

## 改动摘要

- 迁移 DGame `AnimModule` 到 `TEngine/Runtime/Module/AnimModule/`（框架层，`TEngine` 命名空间）。
- 9 个 .cs 文件自成体系，无 PlayableGraph 之外的特殊依赖。
- 实现模块：`AnimModule : Module, IUpdateModule, IAnimModule`，靠 TEngine `ModuleSystem` 反射约定自动注册（接口 `IAnimModule`→实现类 `AnimModule`，同命名空间同程序集），**无需手动 `RegisterModule`**。
- 核心实体：`AnimPlayable : MemoryObject, IAnimPlayable`（一个 Animator 对应一个动画图）。
- 热更层 `GameModule.cs` 新增 `Anim` 访问器。

### 行为变化

- `Module` 基类方法名对齐 TEngine：`OnCreate()`→`OnInit()`，`OnDestroy()`→`Shutdown()`。
- `MemoryObject` API 对齐 TEngine：`Spawn<T>()`→`MemoryPool.Alloc<T>()`，`Release()`→`MemoryPool.Dealloc()`，`OnRelease()`→`InitFromPool()`+`RecycleToPool()`。
- `DGameException`→`System.Exception`，`DLogger`→`TEngine.Log`。
- 私有字段统一 `_小驼峰`。

### 保持不变

- PlayableGraph 构建逻辑（`AnimationLayerMixerPlayable` 根混合器 + 每层 `AnimationMixerPlayable` + `AnimationClipPlayable` 叶子）完全保留。
- 权重过渡（`StartWeightFade` + `Mathf.MoveTowards`）完全保留。
- 多种 `WrapMode`（Once/Loop/ClampForever）处理逻辑完全保留。
- 手动驱动（`DirectorUpdateMode.Manual` + `Evaluate`）完全保留。
- 动态增删动画片段（`AddAnimationClip`/`RemoveAnimationClip`）完全保留。
- `IUpdateModule.Update` 每帧驱动所有活跃动画图完全保留。

## 使用方式

```csharp
// 创建动画图（传入 Animator）
IAnimPlayable animPlayable = GameModule.Anim.CreateAnimPlayable(animator);

// 添加动画片段
animPlayable.AddAnimationClip("Idle", idleClip, WrapMode.Loop, layer: 0, fadeDuration: 0.25f);
animPlayable.AddAnimationClip("Run", runClip, WrapMode.Loop, layer: 0, fadeDuration: 0.25f);

// 播放动画（自动处理权重过渡）
animPlayable.Play("Idle", fadeDuration: 0.25f);
animPlayable.Play("Run", fadeDuration: 0.25f);

// 停止动画
animPlayable.Stop("Idle");

// 获取动画信息
AnimInfo info = animPlayable.GetAnimInfo("Run");
float progress = info.NormalizedTime;

// 销毁动画图
GameModule.Anim.DestroyAnimPlayable(animPlayable);
```

模块每帧自动驱动所有活跃动画图的 `PlayableGraph.Evaluate`。

## 注意事项

- **生命周期**：`AnimPlayable` 是 `MemoryObject`，`CreateAnimPlayable` 从内存池分配，`DestroyAnimPlayable` 归还内存池。创建后必须销毁，否则 `PlayableGraph` 泄漏。
- **手动驱动**：`PlayableGraph` 设为 `DirectorUpdateMode.Manual`，不随 Unity 自动播放，由 `AnimModule.Update` 每帧 `Evaluate`。若需自动播放可调 `PlayGraph()`。
- **多层级混合**：`AnimationLayerMixerPlayable` 为根，每个 Layer 对应一个 `AnimMixer`，同层动画在 `AnimMixer` 内做权重过渡。
- **WrapMode.Once**：播放完毕后 `AnimMixer` 检测所有子节点 `IsDone`，自动权重淡出至 0 并断开连接。
- **程序集归属**：`AnimModule/` 在 `TEngine.Runtime.asmdef` 范围内，`UnityEngine.Playables`/`UnityEngine.Animations` 是 Unity 内置模块，天然可访问无需额外引用。

## 关键文件

- `UnityProject/Assets/TEngine/Runtime/Module/AnimModule/IAnimModule.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AnimModule/AnimModule.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AnimModule/IAnimPlayable.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AnimModule/AnimPlayable.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AnimModule/AnimNode.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AnimModule/AnimClip.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AnimModule/AnimMixer.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AnimModule/AnimState.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/AnimModule/AnimationWrapper.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/GameModule.cs`（新增 `Anim` 访问器）

## 相关记录

- `UnityProject/conversation-summaries/2026-08-27-anim-module-migration-summary.md`
- `UnityProject/conversation-summaries/code-research/2026-08-27-AnimModule迁移到TEngine.md`
