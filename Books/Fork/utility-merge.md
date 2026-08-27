# 运行时工具合并（Utility 扩展）

从 DGame（AmaniDawn/DGame）迁移合并 `Utility` 工具类到 TEngine Runtime Core，补齐 TEngine 精简版 `Utility.Unity` 缺失的方法，并为 JSON 体系补充 `FromJsonOverwrite`。

## 动机

TEngine 的 `Utility.Unity` 是从 DGame `UnityUtil` 派生的精简版，只保留了协程驱动和 Update 注入，删除了组件增删、子节点查找、Layer 批量设置、EventTrigger 封装、物理/随机/正则/材质/触摸/分辨率等一大批实用方法。这些方法无外部依赖、纯 UnityEngine，迁移后可显著减少业务代码中的样板逻辑。

## 改动文件

### `Utility.Unity.cs`（合并新增方法）

路径：`Assets/TEngine/Runtime/Core/Utility/Utility.Unity.cs`

在原有协程/Update注入/FindObjectOfType 基础上补回以下 region：

| Region | 方法 | 说明 |
| --- | --- | --- |
| 自定义组件事件管理 | `AddCustomEventListener` / `RemoveCustomEventListener` | EventTrigger 事件封装，支持任意 EventTriggerType |
| AddComponent | `AddMonoBehaviour<T>` / `AddMonoBehaviour(Type,` ) | TryGetComponent 去重增删，避免重复挂组件 |
| | `RmvMonoBehaviour<T>` / `RmvMonoBehaviour(Type,` ) | Editor 下检测 Asset 防误销毁 |
| 查找子节点 | `FindChild` / `FindChildByName` / `FindChildComponent<T>` / `FindChildComponent(Type,` ) | 递归查找子节点 |
| Layer | `SetLayer(GameObject,` ) / `SetLayer(Transform,` ) | 递归设置子物体 Layer |
| 随机数 | `RandomRangeInt` / `RandomRangeFloat` / `RandomInsideCircle` | Random 封装 |
| 数组创建 | `CreateUnityArray<T>` / `CreateUnityArray(Type,` ) | 泛型/反射数组创建 |
| 实例化 | `Instantiate(GameObject` ) / `Instantiate<T>` | 空值安全实例化 |
| 物理 | `Raycast` | 射线检测封装 |
| 正则 | `GetRegexMatchGroups` | 正则分组提取 |
| 材质 | `SetMaterialVector3` | 材质 Vector3 属性设置 |
| 触摸 | `TryGetTouchByFingerId` | 按 fingerId 查找 Touch |
| HashCode | `GetHashCodeByString` | 字符串 HashCode |
| ResolutionHelper | `GetResolutions` / `SetScreenResolution` / `SetScreenResolutionWithMode` | 屏幕分辨率读写（带缓存） |

#### Obfuz TypeInferenceRule 特性

4 个接受 `Type` 参数的泛型方法标注了 `[TypeInferenceRule(TypeInferenceRules.TypeReferencedByFirstArgument)]`，用于 Obfuz 混淆时的类型推断提示：

- `AddMonoBehaviour(Type, GameObject)`
- `RmvMonoBehaviour(Type, GameObject)`
- `FindChildComponent(Type, Transform, string)`
- `CreateUnityArray(Type, int)`

需要 `using UnityEngineInternal;`（Unity 内部命名空间，Runtime 可用，DGame 同版本 Unity 已验证）。文件顶部加 `#pragma warning disable CS0618` 抑制过时警告，与 DGame 原文件一致。

### `UnityExtension.cs`（新建扩展方法糖衣）

路径：`Assets/TEngine/Runtime/Extension/Unity/UnityExtension.cs`

将 `Utility.Unity.AddCustomEventListener` 封装为 `UIBehaviour` 扩展方法，调用更简洁：

```csharp
// 原始调用
Utility.Unity.AddCustomEventListener(button, EventTriggerType.PointerDown, OnDown);

// 扩展方法
button.AddCustomEventListener(EventTriggerType.PointerDown, OnDown);
button.RemoveCustomEventListener(EventTriggerType.PointerDown, OnDown);
```

### JSON 体系 `FromJsonOverwrite` 补充

TEngine 已有完整 JSON 体系（`IJsonHelper` + `Utility.Json` + `NewtonsoftJsonHelper` + `DefaultJsonHelper`），无需迁移 DGame 版本。仅补充 DGame 有而 TEngine 缺的 `FromJsonOverwrite`（覆盖写入已有对象）：

| 文件 | 改动 |
| --- | --- |
| `Utility.Json.IJsonHelper.cs` | 接口新增 `FromJsonOverwrite(string json, object obj, object settings = null)` |
| `NewtonsoftJsonHelper.cs` | 用 `JsonConvert.PopulateObject` 实现 |
| `DefaultJsonHelper.cs` | 用 `JsonUtility.FromJsonOverwrite` 兜底 |
| `Utility.Json.cs` | 对外 API `FromJsonOverwrite`，带异常包装 |

## 依赖

- **Newtonsoft.Json**：已在 manifest（`com.unity.nuget.newtonsoft-json`），无需新增
- **Obfuz**：已集成（`com.code-philosophy.obfuz`），`TypeInferenceRule` 随 Unity 引擎提供（`UnityEngineInternal` 命名空间）
- **UniTask**：已集成，协程驱动部分无变化
- 无其他外部依赖

## 未迁移项

以下 DGame 工具类本次未迁移，后续按需评估：

| 类 | 原因 |
| --- | --- |
| `PhysicsUtil` | 泛型化物理范围检测，价值高但需确认业务需求 |
| `EaseUtil` | UGUI 缓动（CanvasGroup/Slider/Image/Scrollbar），**已迁移**（见 `ui-expansion.md`），TEngine 原 `Utility.Tween` 空壳已删除 |
| `BitMask32/64` | 轻量位运算工具，按需迁移 |
