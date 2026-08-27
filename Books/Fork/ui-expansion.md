# UI 组件扩展

迁移自 DGame 的自研 UGUI 扩展组件集，涵盖按钮、图片、文本、富文本四类常用 UI 组件，以及配套的对象池和 Shader。

## 背景

DGame 在 `Assets/Scripts/HotFix/GameLogic/Module/UIModule/Expansion/` 下沉淀了一套自研 UGUI 扩展组件，设计上借鉴 UGUI Pro 类商业资产，但代码为自研实现。TEngine 原生 UI 模块只提供 `UIWindow`/`UIWidget` 生命周期骨架，缺少按钮缩放/长按/双击/点击音效、图片圆角/遮罩/镜像、文本描边/渐变/阴影/字间距、图文混排富文本等常用 UI 组件能力。

本 fork 将 DGame 的四个自研组件（`UIButton`/`UIImage`/`UIText`/`RichTextItem`）迁移到 TEngine 的 `GameLogic` 热更程序集，并配套迁移了 `ListPool` 通用对象池和两个 Shader。第二梯队补迁了 `Utility/` 下的 7 个 UGUI 散件辅助组件（`CircleLayoutGroup`/`EmptyGraph`/`NestedScrollRect`/`UIDragListener`/`UIEffectSortingOrder`/`UIExtension`/`UIImageEffect`）+ `EaseUtil` 缓动工具 + `UIMat` 材质资源。`SuperScrollView` 为付费第三方插件，**未迁移**。

## 改动摘要

### 新增组件

- `UIButton`：继承 `UnityEngine.UI.Button`，组合式扩展设计，5 个 `Extend` 独立开关——点击保护（防连点）、缩放动画（DOTween）、长按、双击、点击音效。
- `UIImage`：继承 `UnityEngine.UI.Image`，实现 `IMeshModifier`，3 个 `Extend`——圆角、遮罩（含多边形/扇形填充）、镜像。
- `UIText`：继承 `UnityEngine.UI.Text`，实现 `IMeshModifier`，6 个 `Extend`——描边、渐变、阴影、字间距、顶点色、环形排布。
- `RichTextItem`：图文混排富文本组件，支持 `[icon:xxx]` 图标标签、`[emoji_001]` 动画表情标签、超链接，内部复用 `UIButton`/`UIText`/`UIImage`。

### 配套基础设施

- `ListPool<T>` + `Pool<T>`：纯泛型对象池，`UIText`/`UIImage` 的多个 `Extend` 用于顶点缓冲复用。**从 `GameLogic` 命名空间抽到 `TEngine` 命名空间**，可见性 `internal`→`public`，放在 `TEngine/Runtime/Core/ListPool/`，与 `MemoryPool` 同级，所有程序集可复用。
- `GuideMask.shader`、`Sprites Shader.shader`：`UIImage`/`UIText` 的配套材质 Shader。

### Editor 脚本

- 所有 `Editor` 脚本隔离到 `Assets/Editor/UIModuleExpansion/`，不进热更程序集（遵循 CLAUDE.md 红线第 6 条）。
- 配套迁移了 `UnityEditorUtil` 工具类（`ResetInCanvasFor`/`LayoutFrameBox`/`LayoutHorizontal`/`GetGUIRect` 等 Editor 布局辅助方法），命名空间保持 `GameLogic`。

### API 改造对齐

迁移过程中对 DGame 残留依赖做了如下改造：

| DGame 原代码 | TEngine 新代码 | 说明 |
| --- | --- | --- |
| `using DGame;`（RichTextConfig） | 删除 | TEngine 的 `SetSpriteExtensions` 是全局静态类（无命名空间），删 `using DGame` 后 `image.SetSprite(...)` 天然生效 |
| `DGame.DLogger.Error/Info` | `TEngine.Log.Error/Info` | 日志类名 + 命名空间 |
| `GameModule.ResourceModule` | `GameModule.Resource` | TEngine `GameModule` 属性名不同 |
| `GameModule.AudioModule` | `GameModule.Audio` | TEngine `GameModule` 属性名不同 |
| `DGame.Utility.UnityUtil.FindObjectOfType<T>()` | `UnityEngine.Object.FindObjectOfType<T>()` | 直接用 Unity 原生 |
| `DGame.AudioType.UISound` | `TEngine.AudioType.UISound` | 枚举命名空间不同 |
| `GameProto.SysSoundID` + `SoundConfigMgr.TryGetValue` | （去除）直接用 `string` 资源地址 | 去 Luban 依赖（红线） |
| `int m_clickSoundID` | `string m_clickSoundLocation` | ClickSound 去配置表，改为序列化资源地址字符串 |
| `SetClickSoundID(int)` | `SetClickSoundLocation(string)` | 方法名同步改 |
| `namespace GameLogic` `internal class Pool<T>` | `namespace TEngine` `public class Pool<T>` | ListPool 公共化 |
| `DGame.Utility.UnityUtil.AddMonoBehaviour` | `TEngine.Utility.Unity.AddMonoBehaviour`（完全限定名） | `GameLogic` 命名空间下不能简写为 `Utility.Unity`，会匹配到 `GameLogic.Utility`（若存在）而非 `TEngine.Utility`，详见「命名空间遮蔽」注意事项 |
| `: Editor`（Editor 脚本基类） | `: UnityEditor.Editor`（完全限定名） | `Assets/Editor/` 目录下存在 `global::Editor` 命名空间，`Editor` 被解析为命名空间而非 `UnityEditor.Editor` 基类 |

### Utility 散件辅助组件（第二梯队）

第一梯队迁移时，`Utility/` 下的 7 个散件因"四组件无引用"未迁。第二梯队补迁，均为零外部依赖或低改造成本：

| 组件 | 说明 | 改造点 |
| --- | --- | --- |
| `EmptyGraph` | 零顶点 `Graphic`，做透明可点击区/分隔条，省 mesh | 无，直接搬 |
| `NestedScrollRect` | 解决嵌套 `ScrollRect` 拖拽抢事件，按 `delta` 与 `Vector2.up` 夹角判定手势归内外层 | 无 |
| `CircleLayoutGroup` | `LayoutGroup` 派生，圆形均匀分布 + 扇形分布，三角递推避免逐元素 `Sin/Cos`，脏标记防重复 rebuild | 无 |
| `UIEffectSortingOrder` | 让 UI 子树里的 `Renderer`/`SortingGroup` 跟随 Canvas `sortingOrder` + 偏移，`Auto/SortingGroup/Renderer/Both` 四模式 | 无 |
| `UIDragListener` | 聚合 `IBeginDrag/IDrag/IEndDrag/IPointerDown/Up` 五接口到事件包装器 | `DGame.Utility.UnityUtil.AddMonoBehaviour` → `TEngine.Utility.Unity.AddMonoBehaviour`（已存在等价 API） |
| `UIExtension` | 19 个 `SetActive` 重载（带 cache 防抖 + 纯 null 保护）+ UniTask 缓动扩展（`FadeToAlpha`/`SmoothValue`）+ `TryGetMouseDownUIPos` | 搬 `EaseUtil.cs`（自包含）+ 内联 `TryGetMouseDownUIPos`（原 DGame `MathUtil` 单方法）+ `UIModule.UICanvas`→`UIModule.UIRoot`、`UIModule.UICamera`→`UIModule.Instance.UICamera` |
| `UIImageEffect` | `BaseMeshEffect` 派生，灰度 + 圆形遮罩二合一，材质按 4 组合静态缓存，`LateUpdate` 集中刷新 | `GameModule.ResourceModule.LoadAsset<Material>` → `GameModule.Resource.LoadAsset<Material>`（同步 API 签名一致） |

### EaseUtil 缓动工具

`EaseUtil` + `EaseType` 枚举从 DGame 整文件迁移，命名空间 `DGame` → `GameLogic`，放 `Expansion/Utility/EaseUtil/`。自包含（只依赖 UniTask + UGUI + `System.Threading`，TEngine 全有），提供 `FadeToAlphaAsync`/`SmoothValue`（CanvasGroup/Slider/Image/Scrollbar/float）+ 4 种缓动曲线（Linear/EaseInQuad/EaseOutQuad/EaseInOutQuad）。TEngine 原生 `Utility.Tween`（`ITweenHelper` 空壳，845 行全 `throw`，无实现类、无 `SetTweenHelper` 调用）**已删除**，故 `UIExtension` 的缓动部分直接依赖 `GameLogic.EaseUtil`。后续若上游补充 `ITweenHelper` 实现，可再合并回来。

### UIMat 材质资源

`UIImageEffect` 依赖的 `UIMat.mat` 材质从 DGame `Assets/BundleAssets/Materials/UIMat.mat` 复制到 TEngine `Assets/AssetRaw/Materials/UIMat.mat`。该材质零纹理依赖，只引用 `Sprites Shader.shader`（GUID `d92937db9d207ab459cbcf9fcb5160a6`，第一梯队已迁移且 GUID 一致，材质引用直接生效，无需重定向）。

### 明确保持不变

- `UIButton`/`UIImage`/`UIText`/`RichTextItem` 的命名空间保持 `GameLogic`（与 DGame 一致，TEngine 的 `GameLogic.asmdef` `rootNamespace` 也是 `GameLogic`）。
- 组件的类名、序列化字段、Inspector 面板结构、运行时行为全部保持不变，已在 Prefab 中引用这些组件的场景无需改动。
- `UIButtonClickScaleExtend` 继续依赖 `DG.Tweening`（DOTween），TEngine 项目已有 `Assets/Plugins/Demigiant/DOTween/DOTween.dll`，`autoReferenced` 默认引用，无需改 `GameLogic.asmdef`。
- `SuperScrollView` 未迁移，DGame 中对它的调用（`LoopListView2`/`LoopGridView`）不在本迁移范围。

## 使用方式

### UIButton

在 Prefab 上替换 `Button` 为 `UIButton`，Inspector 面板出现 5 个 Extend 开关：

- **ClickProtect**：防连点，设置 `m_protectTime` 秒数。
- **ClickScale**：按下缩放，依赖 DOTween（`m_isUseDoTween` 可关掉改用直接赋值），可挂子物体列表同步缩放，`m_reboundEffect` 控制回弹缓动。
- **ClickSound**：点击音效，`m_clickSoundLocation` 填 YooAsset 音频资源地址（如 `btn_click`），按下时走 `GameModule.Audio.Play(AudioType.UISound, location, bInPool: true)`。
- **DoubleClick**：双击监听，`m_clickInterval` 控制判定窗口。
- **LongPress**：长按监听，`m_longPressTime` 控制触发时长。

代码侧调用入口在 `BaseUIButton`，如 `AddLongPressListener`/`AddDoubleClickListener`/`SetClickSoundLocation`。

### UIImage

替换 `Image` 为 `UIImage`，Inspector 出现 3 个 Extend：

- **RoundedCorners**：圆角，`m_radiusX`/`m_radiusY` 控制半径。
- **MaskImage**：遮罩，支持 `DrawPolygon` 多边形/扇形填充，`SetFillPercent` 控制填充比例。
- **Mirror**：镜像，`m_mirrorType` 控制水平/垂直/四象限。

### UIText

替换 `Text` 为 `UIText`（或 `BaseUIText`），Inspector 出现 6 个 Extend：

- **Outline**：描边，依赖 YooAsset 中的 `UGUIPro_UIText` 材质（见注意事项）。
- **GradientColor**：渐变，支持上下/左右/四角分色。
- **Shadow**：阴影。
- **Spacing**：字间距。
- **VertexColor**：顶点色。
- **Circle**：环形排布。

代码侧 API：`SetOutLineColor`/`SetGradientColor`/`SetGradientTop2BottomColor` 等。

### RichTextItem

`RichTextItem` 组件直接挂载，文本内嵌标签解析：

- `[icon:item_icon_001]` — 图标，走 `RichTextConfig.SetSprite` → `image.SetSprite(spriteName)`（TEngine 全局扩展方法）。
- `[emoji_001]` — 动画表情，需先 `RichTextConfig.RegisterEmoji(tag, spriteName, frameIndex)` 注册。
- `<a href=...>text</a>` — 超链接，`OnLinkClicked` 事件回调。

### ClickSound 资源地址配置

迁移后 ClickSound 不再查 Luban 表，直接序列化资源地址。默认值为 `"btn_click"`，需在 YooAsset 配置对应音频资源：

- `Assets/AssetRaw/Audios/UISound/btn_click.wav`（或 `.mp3`）→ YooAsset location `btn_click`
- 无此资源时 `ClickSound` 开启会播放失败（`AudioModule.Play` 内部容错），不影响其余 Extend 功能。

## 注意事项

### AudioType 二义性

`TEngine.AudioType` 与 `UnityEngine.AudioType` 同名。`UIButtonClickSoundExtend` 已加 `using AudioType = TEngine.AudioType;` 别名消歧。**后续若其他迁移文件也用到 `AudioType`，需同样处理**。

### UIText 描边材质依赖

`UITextOutlineExtend` 通过 `GameModule.Resource.LoadAsset<Material>("UGUIPro_UIText")` 加载描边材质。该材质**未随代码迁移**，需单独配置：

- DGame 原路径：`Assets/BundleAssets/Materials/UGUIPro_UIText.mat`
- 迁移到 TEngine 后需在 `Assets/AssetRaw/` 下放置对应材质，YooAsset location 为 `UGUIPro_UIText`。
- 材质未配置时描边功能不可用（运行时 `Log.Error` 提示），不阻塞编译，其余 Extend 正常。

### GameModule 属性名差异

DGame 用 `GameModule.ResourceModule`/`GameModule.AudioModule`，TEngine 是 `GameModule.Resource`/`GameModule.Audio`。**后续迁移其他 DGame 模块时需统一替换**，否则编译报错。`GameModule` 类在 `Assets/GameScripts/HotFix/GameLogic/GameModule.cs`，无命名空间（全局类）。

### SetSprite 全局扩展方法

TEngine 的 `SetSpriteExtensions` 在 `Assets/TEngine/Runtime/Module/ResourceModule/Extension/Implement/SetSpriteExtensions.cs`，是**全局静态类无命名空间**。DGame 版在 `DGame` 命名空间。迁移 DGame 代码时**删 `using DGame;` 即自动生效**，无需改调用方签名（`image.SetSprite(location, setNativeSize, callback, cancellationToken)` 完全一致）。

### DOTween 依赖

`UIButtonClickScaleExtend` 用 `DG.Tweening` 的 `DOKill`/`DOScale`/`SetEase`/`SetUpdate`，都在 `DOTween.dll`（核心 DLL，非 DOTweenPro）。TEngine 已有 `Assets/Plugins/Demigiant/DOTween/DOTween.dll`，`autoReferenced` 默认引用，`GameLogic.asmdef` 无需补引用。

### Editor 脚本程序集

`Assets/Editor/UIModuleExpansion/` 下无独立 `asmdef`，使用 Unity 默认 `Assembly-CSharp-Editor` 程序集。`GameLogic.asmdef` `autoReferenced: true`，Editor 脚本可正常引用 `GameLogic` 命名空间下的 `UIButton`/`UIImage` 等类。若后续为 Editor 目录单独建 `asmdef`，需显式 reference `GameLogic`。

### SuperScrollView 未迁移

`LoopListView2`/`LoopGridView` 是付费第三方插件，DGame 把运行时源码搬进了热更程序集。本 fork **未迁移**。若项目需要循环列表，可独立引入 SuperScrollView 插件包到 `Assets/Plugins/SuperScrollView/`，是否进热更程序集按需决定。

### ListPool 公共化影响

`ListPool<T>` 从 DGame 的 `GameLogic` 命名空间 `internal` 改为 `TEngine` 命名空间 `public`。调用方代码 `ListPool<UIVertex>.Get()`/`Recycle(list)` **类名和方法名不变**，只需加 `using TEngine;`。TEngine 原无等价物（`MemoryPool` 是对象池但 API 不同），无冲突。

### 命名空间遮蔽（迁移坑）

**`GameLogic.Utility` 遮蔽 `TEngine.Utility`**：DGame 原代码用 `namespace DGame { public static partial class Utility { ... } }` 组织工具类。迁移时若照搬到 `namespace GameLogic { public static partial class Utility { ... } }`，则 `GameLogic` 命名空间下引用 `Utility.Unity`/`Utility.PlayerPrefs` 时，C# 解析优先匹配当前命名空间的 `GameLogic.Utility`（只有 `EaseUtil`），而非 `TEngine.Utility`，报 `CS0117: 'Utility' does not contain a definition for 'Unity'`。

**解法**：`EaseUtil`/`EaseType` 不嵌套在 `Utility` partial class 里，直接作为 `GameLogic` 命名空间下的独立平级类型。`GameLogic` 命名空间下不保留 `Utility` 类名。`UIDragListener` 引用 `TEngine.Utility.Unity` 时用**完全限定名**（双保险）。**后续迁移 DGame 代码若见 `DGame.Utility.XXX` partial class 组织方式，不要照搬到 `GameLogic` 命名空间下，改用独立类名。**

**`Editor` 命名空间遮蔽 `UnityEditor.Editor` 类**：`Assets/Editor/` 目录编译时存在 `global::Editor` 命名空间（Unity 机制），`using UnityEditor;` 后写 `: Editor` 会被解析为命名空间，报 `CS0118: 'Editor' is a namespace but is used like a type`。所有 Editor 脚本基类必须写完全限定名 `: UnityEditor.Editor`。第一梯队迁移时已踩此坑（`RichTextItemEditor` 等均用完全限定名），第二梯队迁移 `CircleLayoutGroupEditor`/`UIEffectSortingOrderEditor` 时漏改，后修复。

## 关键文件

### 运行时 — TEngine Core（公共化）

- `Assets/TEngine/Runtime/Core/ListPool/Pool.cs` — `public class Pool<T>`
- `Assets/TEngine/Runtime/Core/ListPool/ListPool.cs` — `public static class ListPool<T>`

### 运行时 — GameLogic 热更程序集

- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIButton/Core/BaseUIButton.cs` — 按钮基类，组合 5 个 Extend
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIButton/Extend/UIButtonClickSoundExtend.cs` — 去 Luban 改造重点
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIImage/Core/BaseUIImage.cs` — 图片基类
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIText/Core/BaseUIText.cs` — 文本基类，`IMeshModifier` 实现
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIText/Extend/UITextOutlineExtend.cs` — 描边，依赖材质加载
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/RichTextItem/RichTextItem.cs` — 富文本主组件
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/RichTextItem/RichTextConfig.cs` — 富文本配置，`SetSprite` 桥接
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Shader/GuideMask.shader`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Shader/Sprites Shader.shader`

### Editor

- `Assets/Editor/UIModuleExpansion/UnityEditorUtil.cs` — Editor 布局工具
- `Assets/Editor/UIModuleExpansion/UIButton/UIButtonEditor.cs` / `UIButtonDrawEditor.cs`
- `Assets/Editor/UIModuleExpansion/UIImage/UIImageEditor.cs` / `UIImageDrawEditor.cs`
- `Assets/Editor/UIModuleExpansion/UIText/UITextEditor.cs` / `UITextDrawEditor.cs` / `GradientColorInspector.cs`
- `Assets/Editor/UIModuleExpansion/RichTextItem/RichTextItemEditor.cs`
- `Assets/Editor/UIModuleExpansion/Utility/CircleLayoutGroupEditor.cs` — 圆形布局 Editor（含 Scene 辅助线）
- `Assets/Editor/UIModuleExpansion/Utility/UIEffectSortingOrderEditor.cs` — 特效排序 Editor（含影响预览）

### 运行时 — Utility 散件（第二梯队）

- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/EmptyGraph.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/NestedScrollRect.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/CircleLayoutGroup.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/UIEffectSortingOrder.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/UIDragListener.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/UIExtension.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/UIImageEffect.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/EaseUtil/EaseUtil.cs` — 缓动工具 + `EaseType` 枚举

### 资源

- `Assets/AssetRaw/Materials/UIMat.mat` — `UIImageEffect` 灰度/圆形遮罩材质

## 常见问题排查

### Q: 编译报 `AudioType` 二义性

`error CS0104: 'AudioType' is an ambiguous reference between 'TEngine.AudioType' and 'UnityEngine.AudioType'`

**原因**：`using TEngine;` + `using UnityEngine;` 同时引入同名枚举。

**修复**：在文件头加 `using AudioType = TEngine.AudioType;` 别名。已对 `UIButtonClickSoundExtend.cs` 处理，后续新迁移文件若遇同样报错照此处理。

### Q: 编译报 `UnityEditorUtil` 不存在

`error CS0103: The name 'UnityEditorUtil' does not exist in the current context`

**原因**：DGame 的 Editor 工具类未随核心组件一起迁移。

**修复**：已补迁移 `UnityEditorUtil.cs` 到 `Assets/Editor/UIModuleExpansion/`，命名空间 `GameLogic`。内含 `ResetInCanvasFor`/`LayoutFrameBox`/`LayoutHorizontal`/`LayoutVertical`/`GetGUIRect`/`GetOrCreateCanvas`/`ParentHasCanvas`/`DrawAutoSizeButton`。

### Q: 编译报 `DGame`/`DLogger`/`GameProto` 不存在

**原因**：DGame 残留依赖未清理干净。

**修复**：全局搜索 `\bDGame\b|\bDLogger\b|\bGameProto\b|\bSoundConfigMgr\b|\bSysSoundID\b|\bUnityUtil\b` 并按「API 改造对齐」表替换。

### Q: 运行时 `Log.Error: [OutlineMaterialCache] Material not found from YooAsset address: UGUIPro_UIText`

**原因**：`UITextOutlineExtend` 描边材质未配置到 YooAsset。

**修复**：在 `Assets/AssetRaw/` 下放置 `UGUIPro_UIText.mat` 材质资源，YooAsset 收集器配置 location 为 `UGUIPro_UIText`。材质本身可从 DGame 的 `Assets/BundleAssets/Materials/UGUIPro_UIText.mat` 复制。

### Q: ClickSound 点击无声

**原因**：默认 `m_clickSoundLocation = "btn_click"`，但 YooAsset 无对应音频资源。

**修复**：在 `Assets/AssetRaw/Audios/UISound/` 下放置 `btn_click` 音频，或在 Inspector 改 `m_clickSoundLocation` 为实际资源地址。

### Q: Prefab 上组件丢失引用

**原因**：`UIButton`/`UIImage`/`UIText` 的 `GUID` 变化（跨项目复制 `.meta` 后 GUID 会重新生成）。

**修复**：迁移时连同 `.meta` 一起复制可尽量保留 GUID，但跨项目 GUID 实际会变。已使用这些组件的 Prefab 需重新拖拽引用，或用 Unity 的 `Consolidate` 工具批量重定向。

## 相关记录

- [DGame 可迁移功能评估与逐模块迁移指南](../../UnityProject/conversation-summaries/code-research/2026-08-26-dgame-migration-evaluation-research.md) — 第一梯队迁移评估、依赖分析、迁移结果
- [2026-08-27 DGame UI 组件扩展迁移会话总结](../../UnityProject/conversation-summaries/2026-08-27-dgame-ui-expansion-migration-summary.md)
