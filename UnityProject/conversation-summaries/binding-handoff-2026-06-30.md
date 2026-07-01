# 临时会话交接：纯数据 DataBinding 方案

## 2026-07-01 命名更新

用户反馈 `Binding` 容易和项目里的 `UIBinding` 混在一起，因此已统一改名为 `DataBinding`。

当前准确命名如下：

- 运行时目录：`Assets/GameScripts/HotFix/GameLogic/DataBinding/`
- 生成输出目录：`Assets/GameScripts/HotFix/GameLogic/Generated/DataBinding/`
- 编辑器工具目录：`Assets/Editor/Tools/DataBindingGenerator/`
- 菜单：`Tools/数据绑定/生成`
- Odin 面板：`Tools/数据绑定/生成器面板`
- 数据类标记：`[DataBindingModel]`
- 忽略字段：`[DataBindIgnore]`
- 容差字段：`[DataBindTolerance]`
- 信号字段：`[DataBindSignal]`
- 格式化策略：绑定层不提供格式化特性，订阅方/UI 层自行处理展示文本。
- 值类型：`DataBindingProperty<T>`
- 信号类型：`DataBindingSignal`
- 订阅作用域：`DataBindingScope`
- 容差比较：`DataBindingComparison`
- 生成类：`XxxBinder`
- 生成文件：`XxxBinder.g.cs`

下方早期记录里若出现 `Bindable*`、`Tools/Binding`、`Tools/Data Binding`、`Generated/Binding`、`XxxBinding` 或旧格式化 Attribute 设想，应按本节为准。

时间：2026-06-30
项目路径：`E:\WorkSpace\TEngine\UnityProject`
性质：临时交接总结，供后续会话继续优化或接手实现。

## 用户目标

用户希望在 TEngine/Unity 项目里做一个低成本的数据更新传递方案。

原始痛点：

- 直接拿 UI 实例不方便，尤其跨功能区时耦合重。
- 使用 `GameEvent` 广播又担心太频繁，尤其无人机飞行数据这类高频字段。
- 希望利用 C# 引用类型特性，让多个使用方持有同一个数据状态引用。
- 重点要求：降低使用成本和学习难度。
- 后续明确要求：不要管任何 UI 的事，核心能力必须是纯数据层。

最终确认的设计方向：

- 不依赖 UI。
- 不依赖 `GameEvent`。
- 不强制继承框架类。
- 普通数据类加特性。
- Editor 生成 Binding 代码。
- 订阅返回 `IDisposable`，生命周期由使用方自己管理。
- 高频属性支持 `SyncFrom()` 标脏 + `Flush()` 合批通知。
- 一次性触发类字段使用 `BindableSignal`，不要用 `BindableProperty<bool>` 直接表达按钮 down。

## 已实现内容

### 1. 运行时核心

新增目录：

`Assets/GameScripts/HotFix/GameLogic/Binding/`

新增文件：

- `BindableAttributes.cs`
- `BindableProperty.cs`
- `BindableSignal.cs`
- `BindableComparison.cs`
- `BindingScope.cs`

这些文件位于 `GameLogic` 命名空间下，属于热更业务程序集区域。

#### BindableAttributes.cs

提供生成器用的标记特性：

```csharp
[BindableModel]
[BindIgnore]
[BindTolerance(float)]
[BindSignal]
```

用途：

- `[BindableModel]`：标记一个普通 class/struct，需要生成对应 `XxxBinding`。
- `[BindIgnore]`：跳过某个 public field/property。
- `[BindTolerance(0.01f)]`：给 float、double、Vector2、Vector3、Vector4、Quaternion 等高频数值加容差比较。
- `[BindSignal]`：把 bool 字段/属性生成为 `BindableSignal`。

#### BindableProperty.cs

核心状态值：

```csharp
public sealed class BindableProperty<T> : IBindableProperty
```

主要 API：

```csharp
T Value { get; }
bool HasValue { get; }
bool IsDirty { get; }

IDisposable Subscribe(Action<T> listener, bool notifyNow = true);

bool Set(T value);
bool Set(T value, Func<T, T, bool> equals);

bool SetDirty(T value);
bool SetDirty(T value, Func<T, T, bool> equals);

void SetSilently(T value);
bool Flush();
```

行为：

- `Set()`：立刻设置并通知。
- `SetDirty()`：只标记脏，不立刻通知。
- `Flush()`：如果脏，则通知订阅者。
- `Subscribe()` 返回 `IDisposable`，调用 `Dispose()` 取消订阅。
- 订阅时如果已有值且 `notifyNow = true`，会立即回调当前值。
- 通知过程中取消订阅是安全的。
- 通知过程中新增订阅不会吃到当前这一轮通知。

#### BindableSignal.cs

一次性信号：

```csharp
public sealed class BindableSignal
```

主要 API：

```csharp
IDisposable Subscribe(Action listener);
void Emit();
```

用途：

- 按钮 down。
- 一次性触发。
- 状态边沿。
- 逻辑脉冲。

注意：生成器当前对 `[BindSignal] bool xxx` 的处理是：

- `false -> true` 时 `Emit()`。
- 持续 `true` 不重复触发。
- 回到 `false` 后，下次再变 `true` 可以再次触发。

#### BindableComparison.cs

提供容差比较：

```csharp
public static bool AreEqual<T>(T oldValue, T newValue, float tolerance)
```

已支持：

- `float`
- `double`
- `Vector2`
- `Vector3`
- `Vector4`
- `Quaternion`

其他类型回退到 `EqualityComparer<T>.Default.Equals`。

Quaternion 使用 `Quaternion.Angle(old, new) <= tolerance`，容差单位是角度。

#### BindingScope.cs

批量管理订阅：

```csharp
public sealed class BindingScope : IDisposable
```

主要 API：

```csharp
T Add<T>(T subscription) where T : IDisposable;
void Clear();
void Dispose();
```

用途：

```csharp
private readonly BindingScope _scope = new();

_scope.Add(binding.power.Subscribe(OnPowerChanged));
_scope.Add(binding.resetButtonDown.Subscribe(OnReset));

_scope.Dispose();
```

## 2. Editor 生成器

新增目录：

`Assets/Editor/Tools/BindableGenerator/`

新增文件：

`BindableBindingGenerator.cs`

菜单入口：

`Tools/数据绑定/生成`

生成输出目录：

`Assets/GameScripts/HotFix/GameLogic/Generated/Binding/`

生成器实现方式：

- 使用 UnityEditor 的 `TypeCache.GetTypesWithAttribute<BindableModelAttribute>()` 扫描模型类型。
- 不使用 Roslyn Source Generator。
- 不修改 UI。
- 不运行时代码反射。
- 生成普通 `.g.cs` 文件。

生成规则：

- 标记 `[BindableModel]` 的 class/struct 会生成 `XxxBinding`。
- public field 默认生成 `BindableProperty<T>`。
- public property 默认生成 `BindableProperty<T>`，要求有 public getter，忽略 indexer。
- `[BindIgnore]` 跳过。
- `[BindTolerance]` 生成 `SetDirty(value, comparer)`。
- `[BindSignal]` 只支持 bool，生成 `BindableSignal`。

生成类 API：

```csharp
public void SyncFrom(ModelType data);
public void SyncAndFlush(ModelType data);
public void Flush();
```

行为：

- `SyncFrom(data)`：
  - 属性字段只调用 `SetDirty()`。
  - Signal 字段按 false->true 边沿即时 `Emit()`。
- `Flush()`：
  - 对所有非 Signal 的 `BindableProperty<T>` 调用 `Flush()`。
- `SyncAndFlush(data)`：
  - 等于 `SyncFrom(data); Flush();`

## 使用示例

用户可写普通数据类：

```csharp
namespace GameLogic
{
    [BindableModel]
    public class DroneNormalData
    {
        public float speed;

        public float distance;

        [BindTolerance(0.01f)]
        public float power;

        [BindTolerance(0.01f)]
        public Vector3 position;

        [BindSignal]
        public bool resetButtonDown;

        [BindIgnore]
        public object runtimeOnly;
    }
}
```

运行菜单：

`Tools/数据绑定/生成`

预计生成：

```csharp
public sealed class DroneNormalDataBinding
{
    public BindableProperty<float> speed { get; } = new BindableProperty<float>();
    public BindableProperty<float> distance { get; } = new BindableProperty<float>();
    public BindableProperty<float> power { get; } = new BindableProperty<float>();
    public BindableProperty<Vector3> position { get; } = new BindableProperty<Vector3>();
    public BindableSignal resetButtonDown { get; } = new BindableSignal();

    private bool _resetButtonDownSignalActive;

    public void SyncFrom(DroneNormalData data)
    {
        if (data == null)
        {
            return;
        }

        speed.SetDirty(data.speed);
        distance.SetDirty(data.distance);
        power.SetDirty(data.power, (oldValue, newValue) => BindableComparison.AreEqual(oldValue, newValue, 0.01f));
        position.SetDirty(data.position, (oldValue, newValue) => BindableComparison.AreEqual(oldValue, newValue, 0.01f));

        if (data.resetButtonDown)
        {
            if (!_resetButtonDownSignalActive)
            {
                resetButtonDown.Emit();
            }

            _resetButtonDownSignalActive = true;
        }
        else
        {
            _resetButtonDownSignalActive = false;
        }
    }

    public void SyncAndFlush(DroneNormalData data)
    {
        SyncFrom(data);
        Flush();
    }

    public void Flush()
    {
        speed.Flush();
        distance.Flush();
        power.Flush();
        position.Flush();
    }
}
```

实际生成代码会使用 `global::` 限定，避免命名空间冲突。

订阅示例：

```csharp
private readonly BindingScope _scope = new BindingScope();
private readonly DroneNormalDataBinding _binding = new DroneNormalDataBinding();

public void Init()
{
    _scope.Add(_binding.speed.Subscribe(OnSpeedChanged));
    _scope.Add(_binding.power.Subscribe(OnPowerChanged));
    _scope.Add(_binding.resetButtonDown.Subscribe(OnResetButtonDown));
}

public void UpdateData(DroneNormalData data)
{
    _binding.SyncFrom(data);
    _binding.Flush();
}

public void Dispose()
{
    _scope.Dispose();
}
```

如果不需要合批，可以直接：

```csharp
_binding.SyncAndFlush(data);
```

## 重要设计取舍

### 为什么不接 UIBase

用户明确要求“不要管任何 UI 的事”。因此：

- 没有改 `UIBase`。
- 没有提供 UI 自动解绑方法。
- 没有依赖 `UIWindow` / `UIWidget`。
- 没有依赖 `GameEvent`。

后续如果某个使用方想在 UI 里用，可以自己用 `BindingScope` 管订阅生命周期，但核心层不会知道 UI 存在。

### 为什么先用 Editor 生成器

没有使用 Roslyn Source Generator，原因：

- Unity + HybridCLR + asmdef 下接入 Roslyn SourceGenerator 成本高。
- Editor 生成 `.g.cs` 更稳定，团队学习成本低。
- 生成结果可见，可调试，可提交。
- 与项目现有 UI 生成器风格更接近。

### 为什么 Signal 是边沿触发

按钮 down 这类字段如果用 `BindableProperty<bool>`：

- 连续两帧都是 true，第二帧不会通知。
- true 状态本身不代表“一次触发”。

所以 `[BindSignal] bool` 当前设计为：

- false -> true：触发。
- true -> true：不重复触发。
- true -> false：复位。

这适合 `resetButtonDown`、`dropButtonDown`、`markButtonDown`、`destructButtonDown` 等字段。

## 已验证内容

已执行：

```powershell
git -c safe.directory=E:/WorkSpace/TEngine -C E:\WorkSpace\TEngine diff --check -- UnityProject/Assets/GameScripts/HotFix/GameLogic/Binding UnityProject/Assets/GameScripts/HotFix/GameLogic/Generated UnityProject/Assets/Editor/Tools/BindableGenerator
```

结果：通过。

已用 Roslyn 临时编译运行时 Binding 文件：

- `BindableAttributes.cs`
- `BindableProperty.cs`
- `BindableSignal.cs`
- `BindableComparison.cs`
- `BindingScope.cs`

结果：通过。

已用 Roslyn 临时编译 Editor 生成器：

- `BindableBindingGenerator.cs`

引用临时运行时 DLL、UnityEngine.CoreModule、UnityEditor.CoreModule。

结果：通过。

临时编译产物：

- `Temp\BindableRuntimeCheck.dll`
- `Temp\BindableEditorCheck.dll`

已清理。

已补 `.meta`：

- `Assets/GameScripts/HotFix/GameLogic/Binding.meta`
- `Assets/GameScripts/HotFix/GameLogic/Generated.meta`
- `Assets/GameScripts/HotFix/GameLogic/Generated/Binding.meta`
- `Assets/Editor/Tools/BindableGenerator.meta`
- 各新增 `.cs` 文件对应 `.meta`

## 未完成/未验证

未实际打开 Unity Editor。

因此未验证：

- Unity 菜单 `Tools/数据绑定/生成` 是否在 Editor UI 中正常出现。
- 首次 Unity 编译后，`TypeCache` 是否能扫描到 `[BindableModel]`。
- 真实模型类生成的 `.g.cs` 是否符合业务预期。
- 生成后的 `.g.cs` 是否进入 `GameLogic.asmdef` 编译。

注意：

当前项目的 `.csproj` 是 Unity 显式生成的 Compile Include 列表。新增文件不会马上出现在 `.csproj` 中，需要 Unity 刷新/重新生成工程文件后才会进入 IDE 工程。

## 后续建议

### 第一优先级：真实用例试跑

建议下次会话先找一个真实的无人机数据类，比如用户贴过的 `DroneNormalData`，加上：

```csharp
[BindableModel]
```

再按字段加：

```csharp
[BindTolerance]
[BindSignal]
[BindIgnore]
```

然后在 Unity 里执行：

`Tools/数据绑定/生成`

检查生成文件是否符合预期。

### 第二优先级：确认展示格式策略

用户贴过的 `DroneDataBinding` 里有这些字段：

- `speed` 原始是 float，UI 可展示为 `"{speed:F0} km/h"`。
- `distance` 原始是 float，UI 可展示为 `"{distance:F1} m"`。
- `horizontalSpeed` 原始是 float，UI 可展示为 `"{horizontalSpeed:F1} km/h"`。
- `verticalSpeed` 原始是 float，UI 可展示为 `"{verticalSpeed:F1} km/h"`。
- `coordinate` 原始是 Vector2，UI 可展示为 `"{x:F5} N ，{y:F5} E"`。

当前结论：生成器不处理展示格式，普通字段保留源类型；单位、精度和跨字段组合显示放到订阅方、UI 适配层或手写 partial 中处理。

### 第三优先级：生成器是否支持私有字段

当前只扫描 public instance fields/properties。

这是为了降低生成器复杂度，也避免强行破坏封装。

如果用户希望 private field 也能绑定，后续可考虑：

```csharp
[Bind]
private float _speed;
```

但这会涉及命名、访问权限、生成类同程序集访问等问题，暂未做。

### 第四优先级：输出文件清理

当前生成器只写入已有模型对应的 `.g.cs`。

暂未实现：

- 删除已经不存在的旧模型生成文件。
- 生成前清理输出目录。

为了避免误删，第一版没做自动清理。

后续可加一个单独菜单：

`Tools/数据绑定/清理生成文件`

或者只删除带固定 header 的 `.g.cs`。

### 第五优先级：性能细节

当前 `BindableProperty<T>` 使用 `List<Action<T>>`。

适合轻量订阅。

如果后续订阅数量很多或高频增删，可以考虑：

- 对 Subscription 做池化。
- 避免 `RemoveAll` 分配/遍历。
- 增加订阅数量调试统计。

当前不建议过早优化。

## 可能需要注意的代码点

### BindingScope.Dispose 后 Add 行为

当前：

```csharp
if (_disposed)
{
    subscription.Dispose();
    return subscription;
}
```

也就是说 Scope 已释放后再 Add，会立即释放传入订阅。

这通常是合理的，可以避免生命周期已结束后漏订阅。

### Subscribe notifyNow

`BindableProperty<T>.Subscribe(listener, notifyNow: true)` 默认会在已有值时立即通知当前值。

如果使用方只想监听后续变化：

```csharp
binding.power.Subscribe(OnPowerChanged, notifyNow: false);
```

### SetDirty + Flush

推荐高频数据：

```csharp
binding.SyncFrom(data);
binding.Flush();
```

或者模块每帧末尾统一：

```csharp
binding.Flush();
```

不要每个字段变化就立刻 `Set()`，否则会回到高频通知问题。

## 文件清单

新增运行时：

```text
Assets/GameScripts/HotFix/GameLogic/Binding/BindableAttributes.cs
Assets/GameScripts/HotFix/GameLogic/Binding/BindableProperty.cs
Assets/GameScripts/HotFix/GameLogic/Binding/BindableSignal.cs
Assets/GameScripts/HotFix/GameLogic/Binding/BindableComparison.cs
Assets/GameScripts/HotFix/GameLogic/Binding/BindingScope.cs
```

新增生成器：

```text
Assets/Editor/Tools/BindableGenerator/BindableBindingGenerator.cs
```

新增生成输出目录：

```text
Assets/GameScripts/HotFix/GameLogic/Generated/Binding/
```

新增对应 `.meta` 文件已一并添加。

## 当前状态一句话

纯数据 Binding 基础设施已经实现并通过静态编译验证；下一步应该在 Unity 中给真实数据类打 `[BindableModel]`，跑 `Tools/数据绑定/生成`，看生成代码和实际业务使用是否还需要补格式表达、清理策略或更多特性。
