# Fork 运行时模块

本文汇总当前 fork 新增或增强的运行时模块。通用模块生命周期和注册机制仍参考本目录其他文档；fork 变更历史见 [Fork 定制改动总览](../../../../../Books/Fork/README.md)。

## 访问入口

热更业务统一通过 `GameModule` 访问模块：

```csharp
GameModule.Config
GameModule.Screen
GameModule.GameScene
GameModule.GameObjectPool
GameModule.Anim
```

这些属性内部已经缓存 `ModuleSystem.GetModule<T>()`。业务代码不要重复查询模块。

## RuntimeConfigModule

`GameModule.Config` 用于部署配置、工具配置和小型业务配置，不替代大型强类型配置表。

```csharp
await GameModule.Config.LoadAllAsync(cancellationToken);

if (GameModule.Config.TryGet<DeployConfig>(out var config))
{
    // 使用配置
}

await GameModule.Config.ReloadAsync("sub/Foo", cancellationToken);
```

规则：

- 配置目录为 `Assets/StreamingAssets/Configs/`。
- 默认清单为 `config_manifest.toml`，兼容 `config_manifest.json`。
- TOML 和 JSON 可以混用，配置名支持 `sub/Foo` 形式的子目录。
- `IsLoaded` 表示完成过一次加载流程；单个配置失败时仍可能为 `true`。
- 清单失败会抛异常；单个配置缺失、重复或格式错误只记录并跳过。
- 消费方优先使用 `TryGet` 和 `TryGetText`，并准备默认值。

详细说明见 [runtime-config.md](../../../../../Books/Fork/runtime-config.md)。

## TimerModule

原有计时器 API 保持兼容，并新增限定次数循环：

```csharp
int timerId = GameModule.Timer.AddLoopCountTimer(
    OnTick,
    time: 0.5f,
    loopCount: 3,
    isUnscaled: false,
    args: payload);
```

- 执行 `loopCount` 次后自动移除。
- 坏帧补触发单帧最多执行 10 次，避免递归栈溢出。
- 场景切换不会自动清理业务计时器，对象销毁时仍需 `RemoveTimer(timerId)`。

## GameObjectPoolModule

这是基于 YooAsset location 的 GameObject 实例池，与 TEngine 原有的逻辑对象 `ObjectPoolModule` 不同。

```csharp
await GameModule.GameObjectPool.CreateGameObjectPoolAsync(
    "BattleEffect",
    initCapacity: 5,
    maxCapacity: 20,
    autoDestroyTime: 60f,
    dontDestroy: false,
    allowMultiSpawn: false,
    ct: cancellationToken);

GameObject effect = await GameModule.GameObjectPool.SpawnAsync(
    "BattleEffect", parent, cancellationToken);

GameModule.GameObjectPool.Recycle(effect);
```

直接销毁且不归池时使用：

```csharp
GameModule.GameObjectPool.Remove(effect);
```

约束：

- 对象自动挂载 `GameObjectPoolIdentity`，不要修改其 `PoolKey`。
- 同一 location 的并发建池由模块内部加锁。
- `autoDestroyTime <= 0` 表示不自动销毁。
- `DestroyAllPool(false)` 会保留 `dontDestroy` 常驻池。
- 调试窗口：`TEngine Tools/Debugger/GameObject Pool`。

详细说明见 [game-object-pool.md](../../../../../Books/Fork/game-object-pool.md)。

## AnimModule

`GameModule.Anim` 使用手动驱动的 PlayableGraph 管理 3D 动画：

```csharp
IAnimPlayable playable = GameModule.Anim.CreateAnimPlayable(animator);
playable.AddAnimationClip("Idle", idleClip, WrapMode.Loop, layer: 0, fadeDuration: 0.25f);
playable.Play("Idle", 0.25f);

GameModule.Anim.DestroyAnimPlayable(playable);
```

- 模块每帧驱动 PlayableGraph。
- 创建的 `IAnimPlayable` 必须通过 `DestroyAnimPlayable` 销毁。
- 不要直接 `new AnimPlayable`，内部对象使用 `MemoryPool.Alloc/Dealloc`。

详细说明见 [anim-module.md](../../../../../Books/Fork/anim-module.md)。

## GameSceneModule

业务场景切换使用 `GameModule.GameScene`，底层资源场景 API 仍由 `GameModule.Scene` 提供。

```csharp
GameModule.GameScene.LoadScene(SceneType.BattleScene, OnSceneReady);
float progress = GameModule.GameScene.DisplayProgress;
```

- `SwitchUI` 只展示 `DisplayProgress`，不拥有加载状态机。
- 加载终结顺序为：完成回调 -> 关闭加载页 -> `OnSceneReady`。
- 阶段 1 超时使用“停滞 60 秒 + 绝对 180 秒”双门槛。
- 通用动态加载场景优先使用 `SpawnPointSceneSpawner`。

详细说明见 [scene-system.md](../../../../../Books/Fork/scene-system.md)。

## ScreenModule

`GameModule.Screen` 封装 Windows Standalone 多显示器窗口布局：

```csharp
GameModule.Screen.ApplyAll();
GameModule.Screen.ApplyScreen(0);
GameModule.Screen.SetTopmost(1, true);
```

- 其他平台调用为安全空实现并记录警告。
- 配置由 `RuntimeConfigModule` 加载 `ScreenConfig.toml` 或 `.json` 后注入。
- 应用布局前会切换到窗口模式，全屏会覆盖位置和尺寸。
- 多屏句柄映射必须在 Windows 真机验证。

详细说明见 [window-management.md](../../../../../Books/Fork/window-management.md)。

## 生命周期约束

- `GameModule.Shutdown()` 只在游戏退出时调用。
- `MemoryObject` 派生类型使用 `MemoryPool.Alloc/Dealloc`。
- Win32、文件系统和原生互操作实现保留在 AOT 层，不迁入 HotFix。
