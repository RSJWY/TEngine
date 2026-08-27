# DGame Utility 散件迁移到 TEngine（第二梯队）

> 日期：2026-08-27
> 关联研究：[DGame 可迁移功能评估与逐模块迁移指南](./code-research/2026-08-26-dgame-migration-evaluation-research.md)
> 关联总结：[DGame UI 组件扩展迁移（第一梯队）](./2026-08-27-dgame-ui-expansion-migration-summary.md)

## 背景

第一梯队迁移了 `UIButton`/`UIImage`/`UIText`/`RichTextItem` 四组件 + `ListPool` + 2 Shader，但 `Utility/` 下的 7 个 UGUI 散件辅助组件因"四组件无引用"未迁。本次补迁全部 7 件 + 配套 `EaseUtil` 缓动工具 + `UIMat` 材质资源。

## 迁移前依赖核对（重点纠正）

本次迁移前对每个散件做了精确的依赖核对，纠正了几个误判：

1. **`UnityUtil.AddMonoBehaviour` — TEngine 有**：第一梯队迁移时已合并 DGame `UnityUtil` 缺失方法到 `TEngine.Utility.Unity`（`Utility.Unity.cs`），`AddMonoBehaviour<T>(go)` 签名完全一致。`UIDragListener` 之前判断"缺"是错的。
2. **`EaseUtil` — TEngine 确实缺**：TEngine 有 `Utility.Tween` + `ITweenHelper` 接口，但 grep 全仓库**无任何实现类、无 `SetTweenHelper` 调用**，是空壳，调任何 Tween API 都会抛 `"ITweenHelper is invalid."`。
3. **`UIImageEffect` — 成本被高估**：之前判断"成本最高、要改异步"，实际 TEngine 有同步 `LoadAsset<T>(location)` API 签名一致；且 `UIMat.mat` 引用的 `Sprites Shader.shader` 在第一梯队已迁移且 **GUID 一致**（`d92937db9d207ab459cbcf9fcb5160a6`），材质直接复制即生效，无需重定向。

## 迁移清单与改造点

### 零改造直接搬（4 个 + 2 Editor）

| 文件 | 说明 |
| --- | --- |
| `EmptyGraph.cs` | 零顶点 `Graphic`，25 行 |
| `NestedScrollRect.cs` | 嵌套 `ScrollRect` 拖拽冲突解决，100 行 |
| `CircleLayoutGroup.cs` | 圆形/扇形 `LayoutGroup`，三角递推优化，234 行 |
| `UIEffectSortingOrder.cs` | 特效排序同步 Canvas `sortingOrder`，139 行 |
| `CircleLayoutGroupEditor.cs` | Editor（含 Scene 辅助线 Gizmo），342 行 |
| `UIEffectSortingOrderEditor.cs` | Editor（含影响预览面板），490 行 |

### 低改造（1 个）

| 文件 | 改造点 |
| --- | --- |
| `UIDragListener.cs` | `DGame.Utility.UnityUtil.AddMonoBehaviour<UIDragListener>(go)` → `TEngine.Utility.Unity.AddMonoBehaviour<UIDragListener>(go)`（**完全限定名**，不能简写为 `Utility.Unity`——`GameLogic` 命名空间下若有 `Utility` 类会遮蔽 `TEngine.Utility`，见「编译修复」），加 `using TEngine;` |

### 中改造（1 个 + 1 配套文件）

| 文件 | 改造点 |
| --- | --- |
| `UIExtension.cs` | ① `DGame.Utility.EaseUtil.*` → `EaseUtil.*`（独立类，见下）；② `DGame.Utility.EaseType` → `EaseType`；③ `DGame.Utility.MathUtil.TryGetMouseDownUIPos` 内联为 `UIExtension` 私有方法（只用了 Unity 原生 `Input`/`RectTransformUtility`，不搬整个 629 行 `MathUtil`）；④ `UIModule.UICanvas`（DGame 静态属性）→ `UIModule.UIRoot`（TEngine 静态 `Transform`）；⑤ `UIModule.UICamera`（DGame 静态属性）→ `UIModule.Instance.UICamera`（TEngine 实例属性） |
| `EaseUtil/EaseUtil.cs` | 命名空间 `DGame` → `GameLogic`；**不嵌套在 `partial class Utility` 里**，`EaseUtil` 和 `EaseType` 独立平级（避免 `GameLogic.Utility` 遮蔽 `TEngine.Utility`，见「编译修复」）；其余零改（自包含：UniTask + UGUI + `System.Threading`） |

### 材质资源（1 个）

| 资源 | 来源 | 目标 | 说明 |
| --- | --- | --- | --- |
| `UIMat.mat` | DGame `Assets/BundleAssets/Materials/UIMat.mat` | TEngine `Assets/AssetRaw/Materials/UIMat.mat` | 45 行，零纹理依赖，只引用 `Sprites Shader.shader`（GUID 一致，第一梯队已迁移） |

### 高改造（1 个，实际成本极低）

| 文件 | 改造点 |
| --- | --- |
| `UIImageEffect.cs` | `GameModule.ResourceModule.LoadAsset<Material>("UIMat")` → `GameModule.Resource.LoadAsset<Material>("UIMat")`（同步 API 签名一致），加 `using TEngine;` |

## 最终目录结构

### 运行时 — GameLogic 热更程序集

```
Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/
├── EmptyGraph.cs
├── NestedScrollRect.cs
├── CircleLayoutGroup.cs
├── UIEffectSortingOrder.cs
├── UIDragListener.cs
├── UIExtension.cs
├── UIImageEffect.cs
└── EaseUtil/
    └── EaseUtil.cs          (含 EaseType 枚举)
```

### Editor

```
Assets/Editor/UIModuleExpansion/Utility/
├── CircleLayoutGroupEditor.cs
└── UIEffectSortingOrderEditor.cs
```

### 资源

```
Assets/AssetRaw/Materials/UIMat.mat   (+ .meta，GUID 与 DGame 源一致)
```

## 关键发现

### 1. `TEngine.Utility.Unity` ≈ `DGame.Utility.UnityUtil`

第一梯队迁移时已把 DGame `UnityUtil` 的全部方法（协程/帧回调/生命周期/EventTrigger/AddMonoBehaviour/FindChild/SetLayer/Random/Array/Instantiate/Raycast/Regex/Material/Touch/HashCode/Resolution）合并到 `TEngine.Utility.Unity`。两者几乎是同一份代码的两个副本，命名空间 `DGame` vs `TEngine`，`IMonoDriver` vs `IUpdateDriver`，`DLogger` vs `Log`。

### 2. TEngine `Utility.Tween` 是空壳

`Assets/TEngine/Runtime/Extension/Tween/` 有 `Utility.Tween`（845 行静态 API）+ `ITweenHelper` 接口，但 grep 全仓库**无任何实现类**，也无 `SetTweenHelper(...)` 调用。调任何 Tween API 都会抛 `"ITweenHelper is invalid."` 异常。`UIExtension` 的缓动部分直接依赖 `GameLogic.Utility.EaseUtil` 绕开空壳。

### 3. UIModule 访问形态差异

| DGame | TEngine | 差异 |
| --- | --- | --- |
| `UIModule.UICanvas`（静态属性） | `UIModule.UIRoot`（静态 `Transform`） | TEngine 无 `UICanvas` 属性，`UIRoot` 是 Canvas 的 transform |
| `UIModule.UICamera`（静态属性） | `UIModule.Instance.UICamera`（实例属性） | TEngine `UICamera` 是实例属性，需通过 `Singleton<UIModule>.Instance` 访问 |

### 4. TEngine ResourceModule 有同步 API

`GameModule.Resource.LoadAsset<T>(string location, string packageName = "")` 同步加载存在（`ResourceModule.cs:701`），`UIImageEffect` 的 `InitMatDict` 懒加载 + 同步加载模式不用改异步。

## 自检结果

- 零 `DGame` 命名空间残留（grep `DGame` 无匹配）
- 零 `ResourceModule`/`AudioModule`/`DLogger` 残留
- `UIDragListener.cs` / `UIImageEffect.cs` 已加 `using TEngine;`
- Editor 脚本依赖的 `UnityEditorUtil.LayoutFrameBox` 在第一梯队已迁移

## 编译修复（迁移后）

迁移后编译报 3 个错误，根因都是命名空间遮蔽：

### 1. `GameLogic.Utility` 遮蔽 `TEngine.Utility`

**报错**：
- `UIDragListener.cs(30): error CS0117: 'Utility' does not contain a definition for 'Unity'`
- `BaseClientSaveData.cs(197): error CS0117: 'Utility' does not contain a definition for 'PlayerPrefs'`（DataCenter 迁移遗留，非本次引入，但同根因）

**根因**：`EaseUtil.cs` 原写法是 `namespace GameLogic { public static partial class Utility { public static class EaseUtil {...} } }`，在 `GameLogic` 命名空间下创建了 `Utility` 类。`UIDragListener`/`BaseClientSaveData` 引用 `Utility.Unity`/`Utility.PlayerPrefs` 时，C# 命名空间解析优先匹配当前命名空间（`GameLogic`）下的 `Utility`，而非 `TEngine.Utility`，但 `GameLogic.Utility` 只有 `EaseUtil`，找不到 `Unity`/`PlayerPrefs`。

**修复**：
- `EaseUtil.cs` 拆出 `partial class Utility` 外壳，`EaseUtil` 静态类和 `EaseType` 枚举直接挂到 `GameLogic` 命名空间下平级独立（不再嵌套在 `Utility` 里）。`GameLogic` 命名空间下不再有 `Utility` 类，`Utility.Unity`/`Utility.PlayerPrefs` 正确解析到 `TEngine.Utility`。
- `UIExtension.cs` 的 `Utility.EaseUtil`/`Utility.EaseType` → `EaseUtil`/`EaseType`（同命名空间直接引用）。
- `UIDragListener.cs` 的 `Utility.Unity` → `TEngine.Utility.Unity`（完全限定名，双保险）。

**教训**：在 `GameLogic` 热更程序集里**不要建 `Utility` 这个类名**，会遮蔽 `TEngine.Utility`。DGame 原代码用 `DGame.Utility` partial class 是因为 DGame 命名空间和 TEngine 命名空间隔离，迁移时不能照搬这个结构。

### 2. `Editor` 命名空间遮蔽 `Editor` 类

**报错**：
- `CircleLayoutGroupEditor.cs(9): error CS0118: 'Editor' is a namespace but is used like a type`
- `UIEffectSortingOrderEditor.cs(14): error CS0118: 'Editor' is a namespace but is used like a type`

**根因**：`Assets/Editor/` 目录下编译时存在 `global::Editor` 命名空间（Unity 机制），`using UnityEditor;` 后 `Editor` 被解析为命名空间而非 `UnityEditor.Editor` 基类。

**修复**：两个 Editor 脚本的 `: Editor` → `: UnityEditor.Editor`（完全限定名）。与第一梯队迁移的 `RichTextItemEditor` 写法一致（当时已踩过此坑）。

## 待验证项

1. Unity Editor 打开后 HybridCLR 编译 `GameLogic` 程序集是否通过
2. Editor 程序集（`Assembly-CSharp-Editor`）引用 `GameLogic` 是否通过
3. `UIMat.mat` 材质的 YooAsset location 是否为 `UIMat`（需在 YooAsset 收集器配置 `Assets/AssetRaw/Materials/UIMat.mat`）
4. `CircleLayoutGroupEditor` 的 `[DrawGizmo]` 在 Scene 视图是否正常绘制辅助线
5. `UIEffectSortingOrderEditor` 的 `LayoutFrameBox` 依赖是否正确解析（`UnityEditorUtil` 在 `GameLogic` 命名空间，Editor 脚本同命名空间）

## Fork 文档同步

- 更新专题文档：`Books/Fork/ui-expansion.md`（追加 Utility 件套章节、EaseUtil 说明、UIMat 材质说明、关键文件清单）
- 更新 `Books/Fork/CHANGELOG.md`（追加 2026-08-27 第二梯队条目）

## 关键文件清单

### 运行时

- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/EmptyGraph.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/NestedScrollRect.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/CircleLayoutGroup.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/UIEffectSortingOrder.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/UIDragListener.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/UIExtension.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/UIImageEffect.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/EaseUtil/EaseUtil.cs`

### Editor

- `Assets/Editor/UIModuleExpansion/Utility/CircleLayoutGroupEditor.cs`
- `Assets/Editor/UIModuleExpansion/Utility/UIEffectSortingOrderEditor.cs`

### 资源

- `Assets/AssetRaw/Materials/UIMat.mat` + `.meta`
