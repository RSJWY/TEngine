# 第三方 UI 效果插件（UIEffect / UISoftMask）

> 本文档记录项目中集成的两个 Coffee 系列 UGUI 视觉效果插件的能力与用法。
> 这两个插件是第三方开源插件，不属于 TEngine 框架或 fork 定制内容，但已在项目中正确配置，可直接在业务侧使用。

## 背景

UGUI 原生能力有限：Mask 只有硬边 stencil 遮罩，Image/Text 缺乏色调、模糊、溶解、描边等常见视觉效果。本 fork 集成了 Coffee 的两个插件来补齐这一层：

- **UIEffect**（`com.coffee.ui-effect@5.11.7`）：材质级 shader 注入，给任意 UI Graphic 加 8 大类视觉效果。
- **UISoftMask（`com.coffee.softmask-for-ugui@3.6.5`）**：RenderTexture 软遮罩，替代原生 Mask 的硬边。

两者设计为可协作：UIEffect 的 shader 内嵌 `SOFTMASKABLE` 代码块，在 SoftMask 子树中自动启用软遮罩采样。

## 改动摘要

- 通过 UPM 引入 `com.coffee.ui-effect@5.11.7` 和 `com.coffee.softmask-for-ugui@3.6.5`。
- `Assets/ProjectSettings/UIEffectProjectSettings.asset`：启用 HDR 颜色选择器；shader 变体注册表映射 `Hidden/UI/Default (SoftMaskable) → Hidden/UI/Default (UIEffect)`。
- `Assets/ProjectSettings/UISoftMaskProjectSettings.asset`：软遮罩全局启用、Stereo 启用；shader 变体注册表映射 `Hidden/UI/Default (UIEffect) → Hidden/UI/Default (UIEffect)`。
- 两个 ProjectSettings 互相注册 shader 映射，保证任意 UI shader 都能找到同时含 UIEffect 和 SoftMaskable 两段代码的变体。

## UIEffect 能力总览

### 8 大类效果

| 分类 | 枚举 | 能力 |
| --- | --- | --- |
| 色调 Tone | `ToneFilter` | Grayscale / Sepia / Negative / Retro / Posterize |
| 颜色 Color | `ColorFilter` | Multiply / Additive / Subtractive / Replace / MultiplyLuminance / MultiplyAdditive / HsvModifier / Contrast |
| 采样 Sampling | `SamplingFilter` | BlurFast / BlurMedium / BlurDetail / Pixelation / RgbShift / EdgeLuminance / EdgeAlpha |
| 过渡 Transition | `TransitionFilter` | Fade / Cutoff / Dissolve / Shiny / Mask / Melt / Burn / Blaze / Pattern |
| 阴影 Shadow | `ShadowMode` | None / Shadow / Shadow3 / Outline / Outline8 / Mirror |
| 渐变 Gradation | `GradationMode` | Horizontal / Vertical / Radial / Diagonal / Angle 等 12 种模式 |
| 边缘 Edge | `EdgeMode` | None / Plain / Shiny（流光边缘） |
| 细节 Detail | `DetailFilter` | None / Masking / Multiply / Additive / Subtractive / Replace / MultiplyAdditive |

### 配套组件

| 组件 | 用途 |
| --- | --- |
| `UIEffect` | 主组件，挂在 Graphic 上，全功能效果注入 |
| `UIEffectTweener` | 效果动画驱动，支持循环/乒乓/曲线，按 `CullingMask` 控制哪些效果参与动画 |
| `UIEffectPreset` | ScriptableObject 预设，存复用配置，放在 `Assets/ProjectSettings/UIEffectPresets/` |
| `UIEffectReplica` | 效果副本，让多个元素跟随同一个 UIEffect 或 Preset |

### 核心机制

- **材质池化**：`MaterialRepository` 按 Hash128 共享材质实例，相同效果配置不重复创建。
- **Shader Keyword 变体**：`shader_feature_local_fragment` 按需编译，只编译使用到的变体。
- **GraphicProxy**：统一处理 Image/Text/TMP，TMP 全系列 shader 都有 `(UIEffect)` 变体。
- **顶点扩展**：Blur/Melt/Burn 等效果自动扩展顶点边界 10~20 像素。

## UISoftMask 能力总览

### 三种遮罩模式

| 模式 | 说明 |
| --- | --- |
| `SoftMasking` | 用 RenderTexture 做软遮罩，alpha 控制透明度，可羽化/渐隐边缘 |
| `AntiAliasing` | 抑制遮罩边缘锯齿，不显示遮罩图形 |
| `Normal` | 退化为原生 Mask 的 stencil 遮罩 |

### 配套组件

| 组件 | 用途 |
| --- | --- |
| `SoftMask` | 主组件，继承 Mask，替代原生 Mask |
| `SoftMaskable` | 自动挂到子 Graphic 上，让材质采样软遮罩缓冲区 |
| `MaskingShape` | 在遮罩区域内加/减形状（打洞、镂空），支持 Additive/Subtract |
| `AlphaHitTestTarget` | 独立的 alpha 精确点击测试，不依赖 SoftMask |

### 核心机制

- **RenderTexture 降采样**：`DownSamplingRate` x1~x8，大幅降低缓冲区分辨率。
- **脏标记传播**：只在 Transform/View 变化时标记脏并重绘，`SetSoftMaskDirty` 递归子树。
- **帧缓存**：`FrameCache` 防止同帧重复渲染缓冲区。
- **嵌套**：`softMaskDepth` 最大 4 层，每层一个 RenderTexture。

## 两者协作

UIEffect 的 shader（`Hidden/UI/Default (UIEffect)`）内嵌了 `SOFTMASKABLE` 代码块：

```glsl
// ==== SOFTMASKABLE START ====
#pragma shader_feature_local_fragment _ SOFTMASKABLE
#if SOFTMASKABLE
#include "Packages/com.coffee.softmask-for-ugui/Shaders/SoftMask.cginc"
#endif
// ==== SOFTMASKABLE END ====
```

frag 函数中：
```glsl
#if SOFTMASKABLE
color.a *= SoftMask(IN.vertex, IN.worldPosition, color.a);
#endif
```

**执行链**：
1. `SoftMaskable.GetModifiedMaterial` 查找 `(SoftMaskable)` 变体 shader → 映射到 `(UIEffect)` shader。
2. `UIEffectBase.GetModifiedMaterial` 查找 `(UIEffect)` 变体 shader → 得到同时含两段代码的 shader。
3. 最终材质同时启用 `SOFTMASKABLE` keyword（SoftMaskable 设置）和各 UIEffect keyword（UIEffectContext 设置）。
4. 渲染时：先做 UIEffect 效果处理，再做 SoftMask 软遮罩 alpha 乘法。

## 使用方式

### 典型场景

| 场景 | 推荐配置 |
| --- | --- |
| 按钮禁用灰化 | UIEffect → ToneFilter=Grayscale, ToneIntensity=1 |
| 按钮高亮发光 | UIEffect → ColorFilter=MultiplyAdditive, ColorGlow=true |
| 入场溶解动画 | UIEffect → TransitionFilter=Dissolve + TransitionTex(噪声图) + UIEffectTweener |
| 切换过渡 | UIEffect → TransitionFilter=Fade/Cutoff + UIEffectTweener(Loop) |
| 闪卡光泽 | UIEffect → TransitionFilter=Shiny, TransitionAutoPlaySpeed=1 |
| 文字描边 | UIEffect → ShadowMode=Outline/Outline8 |
| 渐变文字/背景 | UIEffect → GradationMode=Horizontal/Vertical/Radial + GradationGradient |
| 边缘流光 | UIEffect → EdgeMode=Shiny, EdgeShinyAutoPlaySpeed=1 |
| 圆角头像/卡片 | SoftMask + 圆形/圆角 Graphic 作为遮罩 |
| 不规则形状 UI | SoftMask + 自定义形状 Graphic |
| 渐隐遮罩边缘 | SoftMask + softnessRange 调整 |
| 镂空/打洞 | SoftMask + MaskingShape(Subtract) |
| 精确点击 | AlphaHitTestTarget 或 SoftMask.alphaHitTest |
| 软遮罩内做溶解 | SoftMask + UIEffect(TransitionFilter=Dissolve) |
| 圆角卡片内做模糊 | SoftMask + UIEffect(SamplingFilter=BlurMedium) |

### 代码控制效果

```csharp
// 获取或添加 UIEffect 组件
var effect = gameObject.GetComponent<UIEffect>() ?? gameObject.AddComponent<UIEffect>();

// 设置灰度效果
effect.toneFilter = ToneFilter.Grayscale;
effect.toneIntensity = 1f;

// 设置溶解过渡
effect.transitionFilter = TransitionFilter.Dissolve;
effect.transitionTexture = dissolveTexture;
effect.transitionRate = 0f; // 0=原图, 1=完全溶解

// 加载预设
effect.LoadPreset("MyPreset");

// 用 Tweener 做动画
var tweener = gameObject.GetComponent<UIEffectTweener>() ?? gameObject.AddComponent<UIEffectTweener>();
tweener.cullingMask = UIEffectTweener.CullingMask.Transition;
tweener.duration = 1f;
tweener.Play();
```

### SoftMask 使用

```csharp
// SoftMask 替代原生 Mask
var softMask = gameObject.GetComponent<SoftMask>() ?? gameObject.AddComponent<SoftMask>();
softMask.maskingMode = MaskingMode.SoftMasking;
softMask.downSamplingRate = DownSamplingRate.x2;
softMask.softnessRange = new MinMax01(0.2f, 0.8f);

// 打洞：在遮罩区域内的子物体上加 MaskingShape
var shape = childObject.AddComponent<MaskingShape>();
shape.maskingMethod = MaskingShape.MaskingMethod.Subtract;

// 精确点击
var hitTest = gameObject.AddComponent<AlphaHitTestTarget>();
```

## 注意事项

- **UIEffect 模糊（Blur）**：需要扩展顶点边界 10~20 像素，增加顶点数和采样次数，移动端慎用 `BlurDetail`。
- **UIEffect Transition + Gradation**：需要每帧更新 ViewMatrix（`useViewMatrix == true`），有矩阵计算开销。
- **SoftMask 嵌套**：`softMaskDepth` 最大 4 层，每层一个 RenderTexture，嵌套越深内存和 Draw Call 越多。
- **SoftMask 降采样**：`x8` 时缓冲区分辨率为屏幕 1/8，适合静态 UI；动态 UI 用 `x1` 或 `x2`。
- **AlphaHitTest**：需要贴图 Read/Write Enabled，增加内存和 CPU 开销，仅在必要时启用。
- **UISoftMaskProjectSettings.m_SoftMaskable = 0**：未启用自动生成 SoftMaskable 组件，需手动添加或通过代码触发。
- 项目中另有自研的 `UIEffectSortingOrder`（`GameLogic/Module/UIModule/Expansion/Utility/`），名字相似但功能完全不同（用于特效排序同步 Canvas sortingOrder），不要混淆。

## 关键文件

- `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffect.cs`（2196 行，主组件）
- `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffectContext.cs`（855 行，效果上下文）
- `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffectTweener.cs`（583 行，动画驱动）
- `Library/PackageCache/com.coffee.ui-effect@5.11.7/Shaders/UIEffect.shader`（198 行，核心 shader）
- `Library/PackageCache/com.coffee.softmask-for-ugui@3.6.5/Runtime/SoftMask.cs`（905 行，软遮罩主组件）
- `Library/PackageCache/com.coffee.softmask-for-ugui@3.6.5/Runtime/SoftMaskable.cs`（444 行，软遮罩参与者）
- `Library/PackageCache/com.coffee.softmask-for-ugui@3.6.5/Shaders/SoftMask.cginc`（115 行，软遮罩核心 cginc）
- `Assets/ProjectSettings/UIEffectProjectSettings.asset`（UIEffect 全局设置）
- `Assets/ProjectSettings/UISoftMaskProjectSettings.asset`（SoftMask 全局设置）

## 相关记录

- `UnityProject/conversation-summaries/code-research/2026-09-24-uieffect-uisoftmask-research.md`
