# Fork 运行时模块

## GameModule 新增入口

热更业务统一通过以下属性访问 fork 模块：

```csharp
GameModule.Config          // IRuntimeConfigModule
GameModule.Screen          // IScreenModule
GameModule.GameScene       // IGameSceneModule
GameModule.GameObjectPool  // IGameObjectPoolModule
GameModule.Anim            // IAnimModule
```

这些属性内部已缓存 `ModuleSystem.GetModule<T>()`。业务代码不要重复直接查模块。实现新框架模块时遵循反射约定：接口 `IXxxModule` 与实现 `XxxModule` 位于同命名空间、同程序集，通常无需手工 `RegisterModule`。

## RuntimeConfigModule

用于部署配置、工具配置和小型业务配置，不替代 Luban 配置表。

- 配置目录：`Assets/StreamingAssets/Configs/`。
- 默认清单：`config_manifest.toml`，兼容 `config_manifest.json`。
- 支持 TOML/JSON 混用、子目录配置名、原始文本缓存和强类型对象缓存。
- `IsLoaded` 表示一次加载流程完成；单个配置失败仍可能为 `true`。
- 清单缺失或解析失败会抛异常；单个配置缺失、重复或格式不支持只记录并跳过。
- 消费方优先使用 `TryGet` / `TryGetText` 并提供默认值。

```csharp
await GameModule.Config.LoadAllAsync(ct);

if (GameModule.Config.TryGet<DeployConfig>(out var config))
{
    // 使用配置
}

await GameModule.Config.ReloadAsync("sub/Foo", ct);
```

`Get<T>()` 默认配置名为 `typeof(T).Name`。子目录配置必须显式传入如 `sub/Foo`。`Utility.Toml` 只负责序列化，文件加载与缓存由 `RuntimeConfigModule` 负责。

## TimerModule

旧 API 保持不变，并新增限定次数循环：

```csharp
int id = GameModule.Timer.AddLoopCountTimer(
    OnTick,
    time: 0.5f,
    loopCount: 3,
    isUnscaled: false,
    args: payload);
```

- `loopCount` 次执行后自动移除。
- 坏帧补触发单帧最多 10 次，避免递归栈溢出。
- 场景切换不会自动清理业务计时器；对象销毁时仍需 `RemoveTimer(id)`。

## GameObjectPoolModule

这是 GameObject 实例池，不是 TEngine 原有的 `ObjectPoolModule` 逻辑对象引用计数池。

```csharp
await GameModule.GameObjectPool.CreateGameObjectPoolAsync(
    location,
    initCapacity: 5,
    maxCapacity: 20,
    autoDestroyTime: 60f,
    dontDestroy: false,
    allowMultiSpawn: false,
    ct);

GameObject go = await GameModule.GameObjectPool.SpawnAsync(location, parent, ct);
GameModule.GameObjectPool.Recycle(go);
GameModule.GameObjectPool.Remove(go); // 直接销毁，不归池
```

- 每个对象自动挂 `GameObjectPoolIdentity`，不要手工改 `PoolKey`。
- 同一 location 并发建池由内部锁保护。
- `autoDestroyTime <= 0` 表示不自动销毁。
- 切场景可调用 `DestroyAllPool(includeAll: false)`，常驻池会保留。

## AnimModule

基于 PlayableGraph 的代码驱动 3D 动画图：

```csharp
IAnimPlayable playable = GameModule.Anim.CreateAnimPlayable(animator);
playable.AddAnimationClip("Idle", idle, WrapMode.Loop, layer: 0, fadeDuration: 0.25f);
playable.Play("Idle", fadeDuration: 0.25f);

GameModule.Anim.DestroyAnimPlayable(playable);
```

- `PlayableGraph` 使用 `DirectorUpdateMode.Manual`，由模块每帧 `Evaluate`。
- 创建后必须调用 `DestroyAnimPlayable`，否则 PlayableGraph 和池对象泄漏。
- `AnimPlayable` 使用 `MemoryPool.Alloc/Dealloc` 生命周期，不要直接 `new`。

## GameSceneModule

业务场景切换通过 `GameModule.GameScene`，底层场景资源 API 仍由 `GameModule.Scene` 提供。

```csharp
GameModule.GameScene.LoadScene(SceneType.BattleScene, OnReady);
float progress = GameModule.GameScene.DisplayProgress;
```

- `SwitchUI` 只展示 `DisplayProgress`，不控制加载状态机。
- 加载流程终结顺序固定为：回调 -> 关闭加载页 -> `OnSceneReady`。
- `suspendLoad=true` 时不要 `await LoadSceneAsync` 等待 `IsDone`；激活前 `IsDone` 不会完成。使用 progress callback 驱动并在合适阶段 `UnSuspend`。
- 阶段 1 超时采用进度停滞 60 秒 + 绝对 180 秒双门槛，不要恢复固定 5 秒超时。
- 通用动态加载场景优先挂 `SpawnPointSceneSpawner`；只有额外收集规则或完成钩子时才派生专属 Spawner。

## ScreenModule

`ScreenModule` 位于 AOT 的 `TEngine.Runtime`，封装 Win32 多显示器窗口布局；热更层只通过 `GameModule.Screen` 调用。

```csharp
GameModule.Screen.ApplyAll();
GameModule.Screen.ApplyScreen(0);
GameModule.Screen.SetTopmost(1, true);
```

- 仅 Windows Standalone 真正生效，其他平台安全空实现并记录警告。
- 配置由 `RuntimeConfigModule` 读取 `ScreenConfig.toml` 或 `.json`。
- 应用布局前模块会切到窗口化；全屏模式会覆盖位置和尺寸。
- 副屏必须在运行早期激活，且 Unity 激活后不可关闭。
- 多屏句柄映射需要 Windows 真机验证，Editor 不能完整模拟。

## 生命周期边界

- `GameModule.Shutdown()` 只在游戏退出时调用。
- `MemoryObject` 派生类型使用 `MemoryPool.Alloc/Dealloc`；普通 `IMemory` 仍使用 `Acquire/Release`。
- 框架模块的 Win32、文件系统和原生互操作实现留在 AOT 层，不要迁入 HotFix。
