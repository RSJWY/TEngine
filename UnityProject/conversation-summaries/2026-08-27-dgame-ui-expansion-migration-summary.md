# DGame UI 组件扩展迁移到 TEngine（第一梯队）

> 日期：2026-08-27
> 关联研究：[DGame 可迁移功能评估与逐模块迁移指南](./code-research/2026-08-26-dgame-migration-evaluation-research.md)

## 背景

DGame 在 `Assets/Scripts/HotFix/GameLogic/Module/UIModule/Expansion/` 下沉淀了一套自研 UGUI 扩展组件，借鉴 UGUI Pro 类商业资产设计。TEngine 原生 UI 模块只有 `UIWindow`/`UIWidget` 生命周期骨架，缺少按钮缩放/长按/双击/音效、图片圆角/遮罩/镜像、文本描边/渐变/阴影/字间距、图文混排富文本等常用能力。

本次迁移第一梯队的四个自研组件 + 配套 `ListPool` + 2 个 Shader + Editor 脚本。

## 迁移决策过程

### 1. SuperScrollView 是外部插件

- 确认 `Assets/Plugins/SuperScrollView/` 含 `Document.pdf`（3.3MB 官方文档）+ Editor 脚本，是第三方付费插件。
- DGame 把运行时源码搬进了热更程序集，但本质是第三方代码。
- **决策：不迁移**，作为独立插件按需引入。

### 2. 四组件依赖分析（按纯度排序）

| 组件 | 纯度 | 具体依赖 |
| --- | --- | --- |
| UIImage | 零依赖 | 纯 UGUI（`Image` + `IMeshModifier` + 3 Extend） |
| UIText | 1 处 | `UITextOutlineExtend` 需 `ResourceModule.LoadAsset<Material>` + `DLogger` + 材质资源 |
| UIButton | 2 处 | `ClickScaleExtend` 依赖 DOTween；`ClickSoundExtend` 依赖 Luban（`GameProto.SysSoundID` + `SoundConfigMgr`） |
| RichTextItem | 1 处 | `using DGame`（`SetSprite` 扩展方法）+ `DLogger` |

### 3. TEngine 现状验证（关键发现）

- **TEngine 已有 `SetSpriteExtensions`**：`Assets/TEngine/Runtime/Module/ResourceModule/Extension/Implement/SetSpriteExtensions.cs`，全局静态类（无命名空间），签名与 DGame 完全一致 → RichTextItem 删 `using DGame` 天然兼容。
- **TEngine 已有 DOTween**：`Assets/Plugins/Demigiant/DOTween/DOTween.dll`，`autoReferenced` 默认引用 → ClickScale 无障碍。
- **TEngine `AudioModule.Play` 签名兼容**：`Play(AudioType type, string path, ..., bool bInPool = false)`，`bInPool` 参数名一致。
- **TEngine `GameModule` 属性名不同**：`ResourceModule`→`Resource`，`AudioModule`→`Audio`。`GameModule` 在 `GameLogic/GameModule.cs`，全局无命名空间类。
- **TEngine 无 `ListPool` 等价物**：`MemoryPool` API 不同。

### 4. ListPool 公共化决策

- DGame 的 `ListPool<T>` + `Pool<T>` 是纯泛型对象池，5 个 Extend（UIText 的 4 个 + UIImage 的 MirrorExtend）共用。
- 原设计 `internal` + `namespace GameLogic`，是模块内共享。
- **决策：抽到 `TEngine/Runtime/Core/ListPool/`，命名空间 `TEngine`，`public`**，与 `MemoryPool` 同级作为通用基础设施。调用方代码不变（类名/方法名不变），只需加 `using TEngine;`。

## 最终目录结构

### TEngine Core（公共化）

```
Assets/TEngine/Runtime/Core/ListPool/
├── Pool.cs       (public class Pool<T>，命名空间 TEngine)
└── ListPool.cs   (public static class ListPool<T>，命名空间 TEngine)
```

### GameLogic 热更程序集

```
Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/
├── UIButton/
│   ├── Core/   (BaseUIButton, UIButton)
│   └── Extend/ (ClickProtect, ClickScale, ClickSound[改造], DoubleClick, LongPress)
├── UIImage/
│   ├── Core/   (BaseUIImage, UIImage)
│   └── Extend/ (MaskExtend, MirrorExtend, RoundedCornersExtend)
├── UIText/
│   ├── Core/   (BaseUIText, UIText)
│   └── Extend/ (Circle, GradientColor, Outline, Shadow, Spacing, VertexColor)
├── RichTextItem/ (RichTextItem, RichTextParser, RichTextData, RichTextConfig)
└── Shader/       (GuideMask.shader, Sprites Shader.shader)
```

### Editor 隔离

```
Assets/Editor/UIModuleExpansion/
├── UnityEditorUtil.cs          (Editor 布局工具，补迁移)
├── UIButton/   (UIButtonEditor, UIButtonDrawEditor)
├── UIImage/    (UIImageEditor, UIImageDrawEditor)
├── UIText/     (UITextEditor, UITextDrawEditor, GradientColorInspector)
└── RichTextItem/ (RichTextItemEditor)
```

### 不迁移

- `SuperScrollView/` — 付费第三方插件
- `Utility/` — CircleLayoutGroup/EmptyGraph/NestedScrollRect/UIDragListener/UIEffectSortingOrder/UIExtension/UIImageEffect（四组件无引用，独立辅助件）

## 改造点详表

| 文件 | 改造 |
| --- | --- |
| `Pool.cs` / `ListPool.cs` | 命名空间 `GameLogic`→`TEngine`；可见性 `internal`→`public` |
| `UIImageMirrorExtend.cs` | 加 `using TEngine;`（用 `ListPool<UIVertex>`） |
| `UITextGradientColorExtend.cs` / `ShadowExtend` / `SpacingExtend` / `CircleExtend` / `VertexColorExtend` | 加 `using TEngine;` |
| `UITextOutlineExtend.cs` | 加 `using TEngine;`；`DGame.DLogger.Error`→`Log.Error`（3 处）；`GameModule.ResourceModule`→`GameModule.Resource`；`DGame.Utility.UnityUtil.FindObjectOfType<Camera>()`→`Object.FindObjectOfType<Camera>()` |
| `UIButtonClickSoundExtend.cs` | 重写：删 `using GameProto`；`int m_clickSoundID`→`string m_clickSoundLocation`；删 `SoundConfigMgr.TryGetValue` 查表；`GameModule.AudioModule.Play(DGame.AudioType.UISound, ...)`→`GameModule.Audio.Play(AudioType.UISound, ..., bInPool: true)`；加 `using AudioType = TEngine.AudioType;` 别名消歧；`SetClickSoundID(int)`→`SetClickSoundLocation(string)` |
| `BaseUIButton.cs` | `SetClickSoundID(int soundID)`→`SetClickSoundLocation(string location)` |
| `RichTextConfig.cs` | 删 `using DGame;`（TEngine `SetSprite` 全局静态类天然兼容） |
| `RichTextItem.cs` | 删 `using DGame;`；加 `using TEngine;`；`DLogger.Info`→`Log.Info` |
| `UnityEditorUtil.cs` | 补迁移到 `Assets/Editor/UIModuleExpansion/`；`DGame.Utility.UnityUtil.FindObjectOfType`→`Object.FindObjectOfType`（2 处） |

## 遇到的编译错误与修复

### 错误 1：`AudioType` 二义性

```
error CS0104: 'AudioType' is an ambiguous reference between 'TEngine.AudioType' and 'UnityEngine.AudioType'
```

**原因**：`using TEngine;` + `using UnityEngine;` 同时引入同名枚举。DGame 原代码用 `DGame.AudioType` 显式限定没二义性，TEngine 版因同时 using 两个命名空间触发。

**修复**：`UIButtonClickSoundExtend.cs` 头部加 `using AudioType = TEngine.AudioType;` 别名。

### 错误 2：`UnityEditorUtil` 不存在

```
error CS0103: The name 'UnityEditorUtil' does not exist in the current context
```

**原因**：DGame 的 Editor 工具类（`ResetInCanvasFor`/`LayoutFrameBox`/`LayoutHorizontal`/`GetGUIRect`）未随核心组件一起迁移，多个 Editor 脚本引用它。

**修复**：补迁移 `UnityEditorUtil.cs` 到 `Assets/Editor/UIModuleExpansion/`，命名空间保持 `GameLogic`（与使用方一致），内含 `DGame.Utility.UnityUtil.FindObjectOfType`→`Object.FindObjectOfType` 改造。

## API 对齐对照表（供后续迁移参考）

| DGame | TEngine | 说明 |
| --- | --- | --- |
| `GameModule.ResourceModule` | `GameModule.Resource` | 属性名不同 |
| `GameModule.AudioModule` | `GameModule.Audio` | 属性名不同 |
| `DGame.DLogger.Error/Info` | `TEngine.Log.Error/Info` | 日志类名 + 命名空间 |
| `DGame.AudioType.UISound` | `TEngine.AudioType.UISound` | 枚举命名空间，需别名消歧 |
| `DGame.SetSpriteExtensions.SetSprite` | `SetSpriteExtensions.SetSprite`（全局） | TEngine 无命名空间，删 using 即生效 |
| `DGame.Utility.UnityUtil.FindObjectOfType` | `UnityEngine.Object.FindObjectOfType` | 直接用 Unity 原生 |
| `GameProto.SysSoundID` + `SoundConfigMgr` | （去除）直接用资源地址 string | 去 Luban 依赖 |

## 依赖验证结果

- **GameLogic.asmdef** 已引用 `TEngine.Runtime`（GUID `24c092aee38482f4e80715eaa8148782`）✅
- **DOTween** 在 `Assets/Plugins/Demigiant/DOTween/DOTween.dll`（预编译 DLL，`autoReferenced`）✅
- **TEngine `SetSpriteExtensions`** 全局静态类（无命名空间）✅
- **TEngine `AudioModule.Play`** 签名 `Play(AudioType, string, bLoop, volume, bAsync, bInPool)` ✅，`bInPool` 参数名一致
- **TEngine `Log`** 有 `Error(string)`/`Info(string)` ✅

## 待验证项

1. Unity Editor 打开后 HybridCLR 编译 `GameLogic` 程序集是否通过
2. Editor 程序集（`Assembly-CSharp-Editor`）引用 `GameLogic` 是否通过
3. `UIText` 描边材质 `UGUIPro_UIText` 的 YooAsset 资源是否已配置（未配置则描边功能不可用，不阻塞编译）
4. 按钮 `ClickSound` 默认音效地址 `btn_click` 需在 YooAsset 有对应音频资源

## 关键文件清单

### TEngine Core

- `Assets/TEngine/Runtime/Core/ListPool/Pool.cs` — `public class Pool<T>`
- `Assets/TEngine/Runtime/Core/ListPool/ListPool.cs` — `public static class ListPool<T>`

### GameLogic 热更

- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIButton/` — 7 文件
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIImage/` — 5 文件
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIText/` — 8 文件
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/RichTextItem/` — 4 文件
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/Shader/` — 2 shader

### Editor

- `Assets/Editor/UIModuleExpansion/` — 9 文件（含 `UnityEditorUtil.cs`）

## Fork 文档同步

- 新增专题文档：`Books/Fork/ui-expansion.md`
- 更新 `Books/Fork/README.md` 改动索引 + 最近重点
- 更新 `Books/Fork-定制改动说明.md` 专题文档表格
- 更新 `Books/Fork/CHANGELOG.md` 追加 2026-08-27
- 更新根 `README.md` fork 概览（新增 UI 组件扩展方向）
- 更新研究文档 `conversation-summaries/code-research/2026-08-26-dgame-migration-evaluation-research.md` 追加第八节迁移结果
