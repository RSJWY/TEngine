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
- 生成类的公开同步函数会带 XML 注释，便于 IDE 中查看行为说明。
- Odin 面板的模型列表支持对单个模型执行“重新生成”，不必每次全量生成。
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

如果只想重新生成某一个模型，打开 `Tools/数据绑定/生成器面板`，在模型列表中点击该行的“重新生成”按钮即可。

### 特性说明

这些标记都定义在 `UnityProject/Assets/GameScripts/HotFix/GameLogic/DataBinding/DataBindingAttributes.cs`，命名空间是 `GameLogic`。C# 使用 Attribute 时可以省略类名末尾的 `Attribute`，所以 `[DataBindingModel]` 实际对应 `DataBindingModelAttribute`。

| 特性 | 标记位置 | 作用 | 生成行为 |
| --- | --- | --- | --- |
| `[DataBindingModel]` | class、struct | 声明这个普通数据类型需要生成 Binder。没有这个标记的类型不会被生成器扫描。 | 生成 `XxxBinder` 类，并为可绑定成员生成属性、`SyncFrom(data)`、`SyncAndFlush(data)` 和 `Flush()`。 |
| `[DataBindIgnore]` | field、property | 排除不应该参与数据同步的成员，例如运行时缓存、临时对象、不可展示的内部状态。 | 生成器跳过该成员，不生成对应的 `DataBindingProperty<T>` 或 `DataBindingSignal`。 |
| `[DataBindFormat("...")]` | field、property | 把原始字段按 `string.Format` 风格格式化成字符串，适合速度、距离、百分比等展示值。 | 生成 `DataBindingProperty<string>`，同步时写入 `string.Format(CultureInfo.InvariantCulture, format, value)` 的结果。 |
| `[DataBindTolerance(value)]` | field、property | 给高频数值设置容差，避免 float、Vector、Quaternion 等微小抖动反复触发刷新。 | 生成的同步代码调用 `SetDirty(value, comparer)`，通过 `DataBindingComparison.AreEqual(oldValue, newValue, tolerance)` 判断是否真的变化。 |
| `[DataBindSignal]` | bool field、bool property | 把 bool 边沿变化表达成一次性信号，适合按钮 down、重置、确认、触发类事件。 | 生成 `DataBindingSignal`。当数据从 `false` 变成 `true` 时 `Emit()` 一次，持续 `true` 不重复触发，回到 `false` 后允许下次再次触发。 |

示例：

```csharp
[DataBindingModel]
public class DroneNormalData
{
    [DataBindFormat("{0:F0} km/h")]
    public float speed;

    [DataBindTolerance(0.01f)]
    public float power;

    [DataBindSignal]
    public bool resetButtonDown;

    [DataBindIgnore]
    public object runtimeOnly;
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
