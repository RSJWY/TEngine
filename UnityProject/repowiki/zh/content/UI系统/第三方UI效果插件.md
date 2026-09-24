# 第三方 UI 效果插件（UIEffect / UISoftMask）

本文介绍项目中集成的两个 Coffee 系列 UGUI 视觉效果插件。它们是第三方开源插件，不属于 TEngine 框架，但已在项目中正确配置，可直接在业务侧使用。

> 深度研究报告见 [conversation-summaries/code-research/2026-09-24-uieffect-uisoftmask-research.md](../../../../../../conversation-summaries/code-research/2026-09-24-uieffect-uisoftmask-research.md)。
> Fork 专题文档见 [Books/Fork/third-party-plugins.md](../../../../../../Books/Fork/third-party-plugins.md)。

## 插件概览

| 属性 | UIEffect | UISoftMask |
| --- | --- | --- |
| 包名 | `com.coffee.ui-effect` | `com.coffee.softmask-for-ugui` |
| 版本 | 5.11.7 | 3.6.5 |
| 核心能力 | 材质级 shader 注入，8 大类视觉效果 | RenderTexture 软遮罩，替代原生 Mask 硬边 |
| 项目设置 | `Assets/ProjectSettings/UIEffectProjectSettings.asset` | `Assets/ProjectSettings/UISoftMaskProjectSettings.asset` |

## UIEffect — UI 视觉效果

挂在任意 `Graphic`（Image/Text/TMP）上，通过修改材质和顶点添加视觉效果，无需额外 RenderTexture。

### 效果分类

| 分类 | 枚举 | 常用选项 |
| --- | --- | --- |
| 色调 Tone | `ToneFilter` | Grayscale（灰度）、Sepia（复古）、Negative（负片） |
| 颜色 Color | `ColorFilter` | Multiply、Additive、HsvModifier、Contrast + Glow 发光 |
| 采样 Sampling | `SamplingFilter` | BlurFast/Medium/Detail（模糊）、Pixelation、RgbShift |
| 过渡 Transition | `TransitionFilter` | Fade、Dissolve（溶解）、Shiny（闪光）、Burn（燃烧） |
| 阴影 Shadow | `ShadowMode` | Shadow、Outline、Outline8、Mirror |
| 渐变 Gradation | `GradationMode` | Horizontal、Vertical、Radial、Angle + Gradient |
| 边缘 Edge | `EdgeMode` | Plain（描边）、Shiny（流光边缘） |
| 细节 Detail | `DetailFilter` | Multiply、Additive、Masking 等 |

### 配套组件

- `UIEffectTweener`：给效果做 0→1 动画，支持循环/乒乓/曲线，按 `CullingMask` 选择哪些效果参与。
- `UIEffectPreset`：ScriptableObject 预设，存复用配置。
- `UIEffectReplica`：效果副本，让多个元素跟随同一个效果或预设。

### 代码示例

```csharp
// 灰度禁用
var effect = gameObject.AddComponent<UIEffect>();
effect.toneFilter = ToneFilter.Grayscale;
effect.toneIntensity = 1f;

// 溶解动画
effect.transitionFilter = TransitionFilter.Dissolve;
effect.transitionTexture = dissolveTexture;
effect.transitionRate = 0f;

var tweener = gameObject.AddComponent<UIEffectTweener>();
tweener.cullingMask = UIEffectTweener.CullingMask.Transition;
tweener.duration = 1f;
tweener.Play();
```

## UISoftMask — 软边缘遮罩

替代 Unity 原生 `Mask` 组件，提供软边缘遮罩（原生 Mask 只有硬边 stencil）。

### 三种模式

| 模式 | 说明 |
| --- | --- |
| `SoftMasking` | RenderTexture 软遮罩，alpha 控制透明度，可羽化/渐隐边缘 |
| `AntiAliasing` | 抑制遮罩边缘锯齿 |
| `Normal` | 退化为原生 Mask |

### 配套组件

- `SoftMaskable`：自动挂到子 Graphic 上，让材质采样软遮罩缓冲区。
- `MaskingShape`：在遮罩区域内加/减形状（打洞、镂空），`Additive`/`Subtract`。
- `AlphaHitTestTarget`：独立的 alpha 精确点击测试。

### 代码示例

```csharp
// 软遮罩
var softMask = gameObject.AddComponent<SoftMask>();
softMask.maskingMode = MaskingMode.SoftMasking;
softMask.downSamplingRate = DownSamplingRate.x2;
softMask.softnessRange = new MinMax01(0.2f, 0.8f);

// 打洞
var shape = childObject.AddComponent<MaskingShape>();
shape.maskingMethod = MaskingShape.MaskingMethod.Subtract;
```

## 两者协作

UIEffect 的 shader 内嵌 `SOFTMASKABLE` 代码块，在 SoftMask 子树中自动启用软遮罩采样。两个 ProjectSettings 互相注册 shader 映射，保证任意 UI shader 都能找到同时含两段代码的变体。

**最终效果**：在软遮罩区域内可以正常做模糊、溶解、渐变等特效，两者不冲突。

## 性能注意

- UIEffect 模糊需要扩展顶点边界 10~20px，移动端慎用 `BlurDetail`。
- SoftMask 降采样 `x8` 适合静态 UI，动态 UI 用 `x1` 或 `x2`。
- SoftMask 嵌套最多 4 层，每层一个 RenderTexture。
- `AlphaHitTest` 需要贴图 Read/Write Enabled，仅在必要时启用。
- `UISoftMaskProjectSettings.m_SoftMaskable = 0`：未启用自动生成 SoftMaskable 组件，需手动添加。

## 典型场景速查

| 场景 | 推荐配置 |
| --- | --- |
| 按钮禁用灰化 | UIEffect → ToneFilter=Grayscale |
| 入场溶解动画 | UIEffect → TransitionFilter=Dissolve + UIEffectTweener |
| 圆角头像 | SoftMask + 圆形遮罩图 |
| 镂空打洞 | SoftMask + MaskingShape(Subtract) |
| 圆角卡片内做模糊 | SoftMask + UIEffect(BlurMedium) |
| 闪卡光泽 | UIEffect → TransitionFilter=Shiny |
| 文字描边 | UIEffect → ShadowMode=Outline |
