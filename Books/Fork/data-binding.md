# 数据绑定

## 纯数据 DataBinding 生成器

### 背景

项目里原有 `UIBinding` 容易让“数据变化通知”和“UI 绑定”混在一起。这个 fork 新增一套纯数据层 DataBinding，用来在模块、UI 或其他使用方之间传递数据状态变化，但不依赖 UI 框架，也不通过 `GameEvent` 做高频广播。

### 改动摘要

- 新增 `DataBindingProperty<T>`、`DataBindingSignal`、`DataBindingScope` 和 `DataBindingComparison`，提供值订阅、一次性信号、订阅生命周期管理和容差比较。
- 新增 `[DataBindingModel]`、`[DataBindIgnore]`、`[DataBindFormat]`、`[DataBindTolerance]`、`[DataBindSignal]`，用普通数据类型声明生成规则。
- 新增 Editor 生成器，扫描带 `[DataBindingModel]` 的类型并生成 `XxxBinder.g.cs` 到 `Assets/GameScripts/HotFix/GameLogic/Generated/DataBinding/`。
- 新增 Unity 菜单 `Tools/数据绑定/生成` 和 Odin 面板 `Tools/数据绑定/生成器面板`。
- 生成类提供 `SyncFrom(data)`、`SyncAndFlush(data)` 和 `Flush()`；高频字段可先 `SetDirty()` 再统一 `Flush()`。
- 明确保持独立：不接管任何 UI 生命周期，不依赖 `UIWindow`、`UIWidget` 或 `GameEvent`。

### 使用方式

给普通数据类型打标记：

```csharp
namespace GameLogic
{
    [DataBindingModel]
    public class DroneNormalData
    {
        [DataBindFormat("{0:F0} km/h")]
        public float speed;

        [DataBindTolerance(0.01f)]
        public float power;

        [DataBindSignal]
        public bool resetButtonDown;
    }
}
```

在 Unity 中执行 `Tools/数据绑定/生成`，生成对应的 `DroneNormalDataBinder.g.cs`。使用方自行持有 binder 和订阅作用域：

```csharp
private readonly DataBindingScope _scope = new DataBindingScope();
private readonly DroneNormalDataBinder _binder = new DroneNormalDataBinder();

public void Init()
{
    _scope.Add(_binder.speed.Subscribe(OnSpeedChanged));
    _scope.Add(_binder.power.Subscribe(OnPowerChanged));
    _scope.Add(_binder.resetButtonDown.Subscribe(OnResetButtonDown));
}

public void UpdateData(DroneNormalData data)
{
    _binder.SyncFrom(data);
    _binder.Flush();
}

public void Dispose()
{
    _scope.Dispose();
}
```

### 注意事项

- 当前生成器只扫描 public instance fields/properties，跳过 indexer 和 `[DataBindIgnore]` 成员。
- `[DataBindSignal]` 只支持 bool，行为是 `false -> true` 时触发一次，持续 true 不重复触发，回到 false 后可再次触发。
- `[DataBindFormat]` 目前是单字段格式化；跨字段组合显示建议先手写 partial/适配层，后续再评估表达式特性。
- 生成器不自动清理已不存在模型对应的旧文件；清理动作只删除带本生成器固定 header 的 `*Binder.g.cs`。
- 这套机制是数据层工具，不应该反向知道具体 UI，也不应该替代已有 UI 生成器。

### 关键文件

- `UnityProject/Assets/GameScripts/HotFix/GameLogic/DataBinding/DataBindingAttributes.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/DataBinding/DataBindingProperty.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/DataBinding/DataBindingSignal.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/DataBinding/DataBindingComparison.cs`
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/DataBinding/DataBindingScope.cs`
- `UnityProject/Assets/Editor/Tools/DataBindingGenerator/DataBindingGenerator.cs`
- `UnityProject/Assets/Editor/Tools/DataBindingGenerator/DataBindingGeneratorWindow.cs`

### 相关记录

- `UnityProject/conversation-summaries/binding-handoff-2026-06-30.md`