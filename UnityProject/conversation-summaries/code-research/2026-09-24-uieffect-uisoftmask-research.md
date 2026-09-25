# UIEffect 与 UISoftMask 插件研究报告

> 研究日期：2026-09-24
> 研究对象：`com.coffee.ui-effect@5.11.7`、`com.coffee.softmask-for-ugui@3.6.5`
> 研究目的：深入理解两个插件的架构、能力、协作机制，为业务侧使用提供技术参考

---

## 一、插件概览

| 属性 | UIEffect | UISoftMask (SoftMaskForUGUI) |
|---|---|---|
| 包名 | `com.coffee.ui-effect` | `com.coffee.softmask-for-ugui` |
| 版本 | 5.11.7 | 3.6.5 |
| 作者 | Coffee | Coffee |
| 命名空间 | `Coffee.UIEffects` / `Coffee.UIEffectInternal` | `Coffee.UISoftMask` / `Coffee.UISoftMaskInternal` |
| 核心能力 | UI Graphic 材质级视觉效果（色调/颜色/模糊/过渡/阴影/渐变/边缘/细节） | 替代原生 Mask 的软边缘遮罩（RenderTexture 软遮罩 + 抗锯齿 + 精确点击） |
| 性能策略 | 材质池化 + shader keyword 变体 + 顶点扩展 | RenderTexture 降采样 + 脏标记 + 帧缓存 |
| 项目设置 | `Assets/ProjectSettings/UIEffectProjectSettings.asset` | `Assets/ProjectSettings/UISoftMaskProjectSettings.asset` |

两者是同一作者的前后配套插件，设计上可以协作：UIEffect 的 shader 内嵌 `SOFTMASKABLE` 代码块，在 SoftMask 环境下自动启用软遮罩采样。

---

## 二、UIEffect 详细分析

### 2.1 类继承体系

```
UIEffectBase (abstract, UIBehaviour, IMeshModifier, IMaterialModifier, ICanvasRaycastFilter, [ITimeControl])
├── UIEffect              -- 主组件，挂在 Graphic 上，全功能效果注入
└── UIEffectReplica       -- 副本组件，跟随 UIEffect 或 UIEffectPreset，批量复制效果
```

### 2.2 核心架构

#### 2.2.1 UIEffectBase — 基类（327 行）

**职责**：管理材质生命周期、顶点修改、Canvas 重建回调。

**关键接口实现**：
- `IMaterialModifier.GetModifiedMaterial(Material baseMaterial)`：从 `MaterialRepository` 按 Hash128 获取/创建实例化材质。Hash 由 `baseMaterial.GetHashCode() + effectId + samplingScaleId + rootId` 组成，相同效果配置共享同一材质实例。
- `IMeshModifier.ModifyMesh(VertexHelper vh)`：调用 `UIEffectContext.ModifyMesh` 修改顶点（扩展边界、UV Mask、翻转、阴影）。
- `ICanvasRaycastFilter.IsRaycastLocationValid`：由子类实现，用于过渡效果时阻止点击穿透。
- 注册 `UIExtraCallbacks.onBeforeCanvasRebuild / onAfterCanvasRebuild`，在 Canvas 重建前后做脏标记处理和 ViewMatrix 更新。

**材质查找逻辑**（`GetModifiedMaterial`）：
```csharp
shader = UIEffectProjectSettings.shaderRegistry.FindOptionalShader(
    baseMaterial.shader,
    "(UIEffect)",                    // 后缀匹配
    "Hidden/{0} (UIEffect)",         // 格式化匹配
    "Hidden/UI/Default (UIEffect)"   // 最终回退
);
```
即：如果原 shader 有对应的 `(UIEffect)` 变体 shader，就用它；否则用 `Hidden/UI/Default (UIEffect)` 作为回退。这保证了任何 UI shader（TMP、UI/Default、自定义）都能被 UIEffect 接管。

#### 2.2.2 UIEffect — 主组件（2196 行）

**序列化属性（Inspector 可见）**：

| 分类 | 字段 | 说明 |
|---|---|---|
| **Tone 色调** | `m_ToneFilter` (ToneFilter), `m_ToneIntensity` | Grayscale/Sepia/Negative/Retro/Posterize |
| **Color 颜色** | `m_ColorFilter` (ColorFilter), `m_Color`, `m_ColorIntensity`, `m_ColorGlow` | Multiply/Additive/Subtractive/Replace/MultiplyLuminance/MultiplyAdditive/HsvModifier/Contrast |
| **Sampling 采样** | `m_SamplingFilter` (SamplingFilter), `m_SamplingIntensity`, `m_SamplingWidth`, `m_SamplingScale` | BlurFast/BlurMedium/BlurDetail/Pixelation/RgbShift/EdgeLuminance/EdgeAlpha |
| **Transition 过渡** | `m_TransitionFilter` (TransitionFilter), `m_TransitionRate`, `m_TransitionTex`, `m_TransitionTexScale/Offset/Speed`, `m_TransitionRotation`, `m_TransitionKeepAspectRatio`, `m_TransitionWidth`, `m_TransitionSoftness`, `m_TransitionRange` (MinMax01), `m_TransitionColorFilter`, `m_TransitionColor`, `m_TransitionColorGlow`, `m_TransitionPatternReverse`, `m_TransitionAutoPlaySpeed`, `m_TransitionGradient` (Gradient) | Fade/Cutoff/Dissolve/Shiny/Mask/Melt/Burn/Blaze/Pattern |
| **Target 目标** | `m_TargetMode` (TargetMode), `m_TargetColor`, `m_TargetRange`, `m_TargetSoftness` | None/Hue/Luminance — 仅影响特定颜色区域 |
| **Blend 混合** | `m_BlendType` (BlendType), `m_SrcBlendMode`, `m_DstBlendMode` | AlphaBlend/Multiply/Additive/SoftAdditive/MultiplyAdditive/Custom |
| **Shadow 阴影** | `m_ShadowMode` (ShadowMode), `m_ShadowDistance`, `m_ShadowIteration`, `m_ShadowFade`, `m_ShadowMirrorScale`, `m_ShadowBlurIntensity`, `m_ShadowColorFilter`, `m_ShadowColor`, `m_ShadowColorGlow` | None/Shadow/Shadow3/Outline/Outline8/Mirror |
| **Gradation 渐变** | `m_GradationMode` (GradationMode), `m_GradationIntensity`, `m_GradationColorFilter`, `m_GradationColor1~4`, `m_GradationGradient` (Gradient), `m_GradationOffset`, `m_GradationScale`, `m_GradationRotation`, `m_GradationWrapMode`, `m_GradationReverse` | None/Horizontal/HorizontalGradient/Vertical/VerticalGradient/Radial/RadialGradient/Diagonal/DiagonalToRightBottom/DiagonalToLeftBottom/Angle/AngleGradient |
| **Edge 边缘** | `m_EdgeMode` (EdgeMode), `m_EdgeWidth`, `m_EdgeColorFilter`, `m_EdgeColor`, `m_EdgeColorGlow`, `m_EdgeShinyRate`, `m_EdgeShinyWidth`, `m_EdgeShinyAutoPlaySpeed`, `m_PatternArea` (PatternArea) | None/Plain/Shiny + All/Inner/Edge |
| **Detail 细节** | `m_DetailFilter` (DetailFilter), `m_DetailIntensity`, `m_DetailThreshold` (MinMax01), `m_DetailColor`, `m_DetailTex`, `m_DetailTexScale/Offset/Speed` | None/Masking/Multiply/Additive/Subtractive/Replace/MultiplyAdditive |
| **Flip 翻转** | `m_Flip` (Flags) | Horizontal/Vertical/Effect/Shadow |
| **其他** | `m_AllowToModifyMeshShape`, `m_CustomRoot` (RectTransform) | 控制顶点形状扩展和过渡根节点 |

**公共 API**（100+ 属性/方法）：
- 所有上述序列化字段都有对应 `public` 属性 getter/setter，setter 内部会设置脏标记并刷新。
- HSV 便捷属性：`colorHueShift`、`colorSaturationShift`、`colorValueShift`、`colorContrastShift`、`colorBrightnessShift`、`colorAlpha`（当 `colorFilter == HsvModifier` 时生效）。
- `SetRate(float rate, CullingMask mask)`：由 Tweener 调用，按掩码批量设置各效果强度（0→1）。
- `LoadPreset(string name/UIEffect/UIEffectPreset, bool append)`：加载预设配置。
- `SavePreset(UIEffectPreset dst, bool append)`：保存当前配置为预设。
- `Clear()`：重置为默认。

**IsRaycastLocationValid**：
```csharp
switch (transitionFilter) {
    case None/Shiny/Mask/Pattern: return true;  // 这些模式不阻止点击
    default: return transitionRate < 1;          // 过渡中禁止点击穿透
}
```

#### 2.2.3 UIEffectContext — 效果上下文（855 行）

**职责**：纯数据容器 + 材质属性应用器 + 顶点修改器。不继承 MonoBehaviour，通过对象池管理。

**核心机制**：
1. **材质属性写入**（`ApplyToMaterial`）：将所有效果参数写入材质的 shader uniform（`_ToneIntensity`、`_ColorFilter`、`_TransitionRate`、`_GradationColor1~4` 等 70+ 属性 ID）。
2. **Shader Keyword 管理**：按效果分类管理 keyword 组：
   - `s_ToneKeywords`：`TONE_GRAYSCALE` ~ `TONE_POSTERIZE`
   - `s_ColorKeywords`：`COLOR_FILTER`
   - `s_SamplingKeywords`：`SAMPLING_BLUR_FAST` ~ `SAMPLING_EDGE_ALPHA`
   - `s_TransitionKeywords`：`TRANSITION_FADE` ~ `TRANSITION_BLAZE`
   - `s_TargetKeywords`：`TARGET_HUE`、`TARGET_LUMINANCE`
   - `s_EdgeKeywords`：`EDGE_PLAIN`、`EDGE_SHINY`
   - `s_DetailKeywords`：`DETAIL_MASKING` ~ `DETAIL_SUBTRACTIVE`
   - `s_GradationKeywords`：`GRADATION_GRADIENT`、`GRADATION_COLOR2`、`GRADATION_COLOR4`
   
   `SetKeyword(material, keywords, index)` 启用当前 index 的 keyword，禁用其余所有。

3. **顶点修改**（`ModifyMesh`）：
   - `ApplyFlipWithoutEffect` / `ApplyFlipWithEffect`：翻转顶点。
   - `GetExpandSize`：按 SamplingFilter 和 TransitionFilter 扩展顶点边界（Blur 需要更大的采样区域）。
   - 每个 quad 计算边界和 UV Mask，写入 `uv1`（TEXCOORD1）。
   - `ApplyShadow`：按 ShadowMode 复制顶点做阴影/描边/镜像。

4. **Gradient → Texture 烘焙**：`gradationRampTex` / `transitionRampTex` 将 Gradient 烘焙为 256x1 RGBAFloat Texture2D，通过对象池管理。

5. **ViewMatrix 更新**（`UpdateViewMatrix`）：Transition/Gradation/Detail/EdgeShiny 效果需要将顶点从世界空间转换到 transitionRoot 局部空间，计算 `_RootViewMatrix`、`_GradViewMatrix`、`_MirrorRootViewMatrix` 等，支持旋转和保持宽高比。

#### 2.2.4 UIEffectTweener — 效果动画器（583 行）

**职责**：驱动 UIEffect 的各项效果强度做 0→1 的动画过渡。

**关键配置**：
- `CullingMask`（Flags）：Tone/Color/Sampling/Transition/GradiationOffset/GradiationRotation/EdgeShiny/Event — 控制哪些效果参与动画。
- `WrapMode`：Once/Loop/PingPongOnce/PingPongLoop。
- `Direction`：Forward/Reverse。
- `UpdateMode`：Normal(`Time.deltaTime`)/Unscaled(`Time.unscaledDeltaTime`)/Manual（手动调 `UpdateTime`/`SetTime`）。
- `PlayOnEnable`：None/Forward/Reverse/KeepDirection。
- `AnimationCurve` + `separateReverseCurve`：支持正反向不同曲线。
- `m_Delay`、`m_Duration`、`m_Interval`（循环间隔）。

**工作原理**：
- `Update()` 按 deltaTime 推进 `_time`，按 WrapMode 计算当前 `rate`（0~1）。
- `rate` setter 调用 `target.SetRate(evaluatedRate, cullingMask)`，将 rate 分发给各效果（Tone→toneIntensity、Color→colorIntensity、Sampling→samplingIntensity、Transition→transitionRate、Gradation→gradationOffset/Rotation、Edge→edgeShinyRate）。
- 完成/变化时触发 `m_OnComplete` / `m_OnChangedRate` UnityEvent。

#### 2.2.5 UIEffectPreset — 效果预设（210 行）

**职责**：ScriptableObject，存储一组效果参数，可复用。

- `[CreateAssetMenu]` 可在 Project 窗口创建。
- `UpdateContext(UIEffectContext dst)`：将预设值写入 Context。
- 存储在 `Assets/ProjectSettings/UIEffectPresets/` 目录。
- `UIEffectProjectSettings.LoadPreset(name)` 按名加载预设。
- 支持 V1（UIEffect 组件 prefab）和 V2（UIEffectPreset ScriptableObject）两种格式。

#### 2.2.6 UIEffectReplica — 效果副本（305 行）

**职责**：让一个 Graphic 的效果跟随另一个 UIEffect 或 UIEffectPreset。

**两种模式**：
1. **跟随场景中的 UIEffect**（`target` 字段）：共享 `effectId` 和 `transitionRoot`，实时同步效果。适合多个元素共享同一过渡动画。
2. **跟随 UIEffectPreset**（`preset` 字段）：从预设读取静态效果配置。适合批量应用统一效果。

**额外控制**：
- `useTargetTransform`：是否使用目标元素的 RectTransform 作为过渡根。
- `customRoot`：自定义过渡根。
- `samplingScale`：独立采样缩放。
- `allowToModifyMeshShape`：是否允许修改顶点形状。

### 2.3 MaterialRepository — 材质池（94 行）

**职责**：全局材质实例池，按 Hash128 共享。

- `ObjectRepository<Material>` 内部使用字典 + 弱引用管理。
- `Get(hash, ref material, onCreate, source)`：按 hash 查找，不存在则用 `onCreate` 创建。
- `Release(ref material)`：归还到池（引用计数归零时销毁）。
- `Clear()`：Domain Reload 时清空（`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`）。

### 2.4 GraphicProxy — 图形代理

**职责**：为不同 Graphic 类型（Image/Text/TMP）提供统一接口。

- `GraphicProxy`（基类）：处理标准 UGUI Image/Text。
- `TmpProxy`：处理 TextMeshProUGUI / TMP_SubMeshUI，需要 `TMP_ENABLE` 宏。
- `ImageProxy`：处理 Image。
- `Find(Graphic)`：按注册顺序逆序查找匹配的 Proxy。
- 关键方法：`IsText`、`OnPreModifyMesh`（启用 additionalShaderChannels）、`ModifyExpandSize`、`GetMainTexture`、`GetAlpha`。

### 2.5 Shader 机制

#### 2.5.1 UIEffect.shader（198 行）

`Hidden/UI/Default (UIEffect)` — 核心回退 shader。

**结构**：
- Properties：标准 UGUI + `Blend [_SrcBlend] [_DstBlend]`（UIEFFECT 注入）。
- Pass 内：
  - `#pragma shader_feature_local_fragment` 管理所有效果 keyword（Tone/Color/Sampling/Transition/Edge/Detail/Target/Gradation）。
  - `#pragma shader_feature_fragment UIEFFECT_EDITOR`：编辑器 SceneView 预览控制。
  - **SOFTMASKABLE 代码块**：`#pragma shader_feature_local_fragment _ SOFTMASKABLE`，启用时 `#include SoftMask.cginc`，在 frag 中调用 `SoftMask()` 乘以 alpha。

#### 2.5.2 TMP Shader 变体

`UIEffectProjectSettings.InitializeOnLoadMethod` 注册了 TMP 全系列 shader 的 `(UIEffect)` 变体：
- `Hidden/TextMeshPro/Distance Field (UIEffect)`
- `Hidden/TextMeshPro/Mobile/Distance Field (UIEffect)`
- `Hidden/TextMeshPro/Distance Field SSD (UIEffect)`
- `Hidden/TextMeshPro/Mobile/Distance Field SSD (UIEffect)`
- `Hidden/TextMeshPro/Distance Field Overlay (UIEffect)`
- `Hidden/TextMeshPro/Mobile/Distance Field Overlay (UIEffect)`
- `Hidden/TextMeshPro/Bitmap (UIEffect)`
- `Hidden/TextMeshPro/Mobile/Bitmap (UIEffect)`

这些 shader 通过 `ShaderSampleImporter` 从 Samples~ 目录导入，`CgincPathSync` 确保 TMP cginc 路径正确。

#### 2.5.3 ShaderSampleImporter

负责按需导入 shader 样本（Samples~ 目录下的 shader 文件），避免将所有 shader 都打入包内。支持：
- `RegisterShaderSamples`：注册 shader 样本（shaderName, groupName, version）。
- `RegisterShaderAliases`：注册 shader 别名映射（如 `Hidden/Hidden/.../SoftMaskable) (UIEffect)` → `Hidden/.../(UIEffect)`）。
- `RegisterDeprecatedShaders`：标记旧 shader 为废弃。
- `ImportShaderIfSelected`：当选中含 UIEffect 的对象时按需导入。

---

## 三、UISoftMask 详细分析

### 3.1 类继承体系

```
Mask (UnityEngine.UI)
└── SoftMask (Mask, ISoftMasking, IMaskable, IMaskingShapeContainerOwner, ISerializationCallbackReceiver)
```

```
MonoBehaviour
├── SoftMaskable (IMaterialModifier, IMaskable)     -- 挂在子 Graphic 上，参与软遮罩
├── MaskingShape (UIBehaviour, IMeshModifier, IMaterialModifier, IComparable<MaskingShape>, IMaskable, ISoftMasking)  -- 子遮罩形状
├── AlphaHitTestTarget (ICanvasRaycastFilter)        -- 独立的 alpha 精确点击
└── TerminalMaskingShape                              -- 终端遮罩形状（不参与正常渲染）
```

### 3.2 SoftMask — 软遮罩主组件（905 行）

#### 3.2.1 三种遮罩模式

```csharp
public enum MaskingMode {
    SoftMasking,    // 软遮罩：用 RenderTexture 做软遮罩，alpha 控制透明度
    AntiAliasing,   // 抗锯齿：抑制遮罩边缘锯齿，不显示遮罩图形
    Normal          // 普通：退化为原生 Mask 的 stencil 遮罩
}
```

**实际模式判定**（`GetActualMaskingMode`）：
- 如果全局 `UISoftMaskProjectSettings.softMaskEnabled == false`，SoftMasking 模式降级为 AntiAliasing。
- 如果 `maskingMode == Normal`，保持 Normal。

#### 3.2.2 核心属性

| 属性 | 类型 | 说明 |
|---|---|---|
| `maskingMode` | MaskingMode | 遮罩模式 |
| `downSamplingRate` | DownSamplingRate | 降采样率 x1/x2/x4/x8，值越大性能越好质量越低 |
| `softnessRange` | MinMax01 | 软度范围（alpha min/max，差值越大羽化越强） |
| `alphaHitTest` | bool | 透明部分不可点击（需贴图 Read/Write Enabled） |
| `antiAliasingThreshold` | float | 抗锯齿阈值 |
| `clearColor` | Color | 软遮罩缓冲区清除色 |
| `softMaskDepth` | int | 软遮罩深度（嵌套层级，最大 4） |
| `softMaskBuffer` | RenderTexture | 软遮罩缓冲区（只读） |

#### 3.2.3 软遮罩缓冲区渲染流程（`RenderSoftMaskBuffer`）

1. **脏标记检查**：`isDirty == false` 则跳过（只在 Transform/View 变化时标记脏）。
2. **帧缓存**：`FrameCache.TryGet` 确保同一帧内不重复渲染。
3. **父级优先**：如果有父 SoftMask，先渲染父级的缓冲区。
4. **深度检查**：`softMaskDepth` 越界（<0 或 >=4）则跳过。
5. **CommandBuffer 构建**：
   - `SetRenderTarget(softMaskBuffer)`
   - 深度 0 或分辨率变化时 `ClearRenderTarget`
   - 设置 VP 矩阵（支持 Stereo 双目）
   - 从父级 Blit 缓冲区（非右眼时）
   - 绘制自身 Mesh（`SoftMaskUtils.GetSoftMaskingMaterial(Additive)`）
   - 绘制 MaskingShapeContainer 中的所有形状
6. **执行**：`Graphics.ExecuteCommandBuffer(_cb)`

#### 3.2.4 脏标记与传播

- `SetSoftMaskDirty()`：标记自身脏，递归标记所有子 SoftMask 脏。
- `OnBeforeCanvasRebuild`：每帧检查 Transform 是否变化（`HasChanged` + `transformSensitivity`），变化则标记脏。
- `CanvasViewChangeTrigger`：监听 Canvas 视图变化（分辨率/相机变化），触发 `OnCanvasViewChanged`。

#### 3.2.5 RenderTexture 管理

- `softMaskBuffer` getter：按 Hash128（GetHashCode + 屏幕尺寸）从 `RenderTextureRepository` 获取/创建。
- `DownSamplingRate` 控制缓冲区分辨率：`RenderTextureRepository.GetScreenSize((int)downSamplingRate)`。
- 支持 URP `allowRenderScale` 和平台 `allowDynamicResolution`。
- 禁用时自动 Release。

#### 3.2.6 父子关系

- `UpdateParentSoftMask()`：在 Transform Parent 变化时，向上查找最近的 SoftMaskingEnabled 的 SoftMask 作为父级。
- 父子的 `children` 列表维护层级关系。
- `softMaskDepth`：从根 SoftMask 遍历到当前，每经过一个 SoftMaskingEnabled 的 SoftMask 深度+1。

#### 3.2.7 Raycast

`IsRaycastLocationValid`：
1. 父级先检查。
2. 基类 `base.IsRaycastLocationValid`（原生 Mask 的矩形检查）。
3. 非 SoftMasking 模式直接返回。
4. `alphaHitTest` 启用时做 alpha 精确检查。
5. `_shapeContainer` 不为空时检查 MaskingShape 区域。

### 3.3 SoftMaskable — 软遮罩参与者（444 行）

**职责**：挂在 SoftMask 子树中的 Graphic 上，让该 Graphic 的材质采样软遮罩缓冲区。

**自动添加**：`SoftMaskUtils.AddSoftMaskableOnChildren` 在 SoftMask 启用时自动给子树 Graphic 添加 `SoftMaskable` 组件（可通过 `m_HideGeneratedComponents` 隐藏）。

**材质修改**（`GetModifiedMaterial`）：
1. 检查全局 `softMaskEnabled`、自身启用、Graphic maskable、非终端。
2. 计算 `softMaskDepth`，越界则返回原材质。
3. 按 Hash128（baseMaterial + softMaskBuffer + stencilBits + stereo + power + threshold）从 MaterialRepository 获取/创建材质。
4. **Shader 查找**：`FindOptionalShader(baseShader, "(SoftMaskable)", "Hidden/{0} (SoftMaskable)", "Hidden/UI/Default (SoftMaskable)")`。
5. **属性写入**：
   - `_SoftMaskTex`：软遮罩缓冲区 RenderTexture。
   - `_SoftMaskColor`：深度对应的 RGBA（0~3 层），控制多层遮罩的叠加。
   - `_SoftMaskingPower`：软遮罩强度指数（0.5~5）。
   - `_SoftMaskableStereo`：是否双目。
   - `_AllowRenderScale` / `_AllowDynamicResolution`。
6. **Keyword**：`EnableKeyword("SOFTMASKABLE")`。

**ignoreSelf / ignoreChildren**：
- `ignoreSelf`：自身不参与软遮罩。
- `ignoreChildren`：子树不参与软遮罩。
- 设置后 `hideFlags = HideFlags.None`（否则 `HideFlags.DontSaveInEditor`）。

**power**：软遮罩幂指数，值越大透明越快。解决重叠物体透视问题。

**编辑器特殊处理**：
- `UpdateSceneViewMatrix`：SceneView 相机需要不同的 VP 矩阵，通过 `_GameVP` / `_GameVP_2` 传入。
- `SOFTMASK_EDITOR` keyword：编辑器 SceneView 使用 `WorldToUv`（世界坐标→UV），运行时使用 `ClipToUv`（裁剪坐标→UV），性能更好。

### 3.4 MaskingShape — 遮罩形状（497 行）

**职责**：在 SoftMask 子树中添加额外的遮罩形状，支持加法/减法。

**两种方法**：
```csharp
public enum MaskingMethod {
    Additive,   // 加法：增加遮罩区域
    Subtract     // 减法：扣除遮罩区域（打洞）
}
```

**Raycast 方法**：
```csharp
public enum RaycastMethod {
    Auto,       // 自动：Additive→Additive, Subtract→Subtract
    Additive,   // 加法：在区域内可点击
    Subtract,    // 减法：在区域内不可点击
    Ignore       // 忽略：不影响点击
}
```

**材质修改**（`GetModifiedMaterial`）：
- 使用 `StencilMaterial.Add` 创建 stencil 材质。
- Additive：`StencilOp.Replace, CompareFunction.Equal` — 写入 stencil，区域内显示。
- Subtract：`SoftMaskEnabled ? StencilOp.Keep : StencilOp.Zero, CompareFunction.Equal` — 扣除 stencil，区域内打洞。
- `showMaskGraphic`：是否显示遮罩图形本身（false 时 `colorMask = 0`，只写 stencil 不渲染颜色）。

**软遮罩缓冲区绘制**（`DrawSoftMaskBuffer`）：
- 获取 Graphic 的 mesh（通过 IMeshModifier 缓存）。
- 用 `SoftMaskUtils.GetSoftMaskingMaterial(maskingMethod)` 材质绘制到缓冲区。
- 支持 `softnessRange` 和 `alpha`。

**容器管理**：
- 自动注册到最近的 Mask 的 `MaskingShapeContainer`。
- `CompareTo`：按 Graphic depth 排序，确保正确绘制顺序。

### 3.5 AlphaHitTestTarget — 独立精确点击（39 行）

**职责**：让任意 Graphic 支持 alpha 精确点击测试，不依赖 SoftMask。

- `ICanvasRaycastFilter.IsRaycastLocationValid`：调用 `Utils.AlphaHitTestValid(graphic, sp, eventCamera, 0.01f)`。
- 需要贴图 Read/Write Enabled。

### 3.6 UISoftMaskProjectSettings

**全局设置**（`Assets/ProjectSettings/UISoftMaskProjectSettings.asset`）：
- `m_SoftMaskEnabled`：全局开关（false 时 SoftMasking 降级为 AntiAliasing）。
- `m_StereoEnabled`：双目支持。
- `m_TransformSensitivity`：Transform 变化检测灵敏度。
- `m_SoftMaskable`：是否自动生成 SoftMaskable 组件。
- `m_HideGeneratedComponents`：隐藏自动生成的组件。
- `m_ShaderVariantRegistry`：shader 变体注册表，`OptionalShaders` 中注册了 `Hidden/UI/Default (UIEffect)` → `Hidden/UI/Default (UIEffect)` 的映射。

---

## 四、两者协作机制

### 4.1 Shader 双向注册

**UIEffect 侧**（`UIEffectProjectSettings.asset`）：
```yaml
m_OptionalShaders:
- key: Hidden/UI/Default (SoftMaskable)    # SoftMask 插件的 shader
  value: Hidden/UI/Default (UIEffect)       # 映射到 UIEffect 的 shader
```
即：当一个 SoftMaskable 需要给 `Hidden/UI/Default (SoftMaskable)` shader 的材质做 UIEffect 效果时，回退到 `Hidden/UI/Default (UIEffect)` shader（它同时包含 UIEFFECT 和 SOFTMASKABLE 代码块）。

**SoftMask 侧**（`UISoftMaskProjectSettings.asset`）：
```yaml
m_OptionalShaders:
- key: Hidden/UI/Default (UIEffect)         # UIEffect 插件的 shader
  value: Hidden/UI/Default (UIEffect)       # 映射到自身
```
即：UIEffect 的 shader 被注册为 SoftMask 的可选 shader。

### 4.2 Shader 代码层面的协作

`UIEffect.shader` 中同时包含两段代码块：

```glsl
// ==== UIEFFECT START ====
#pragma shader_feature_local_fragment _ TONE_GRAYSCALE ... GRADATION_COLOR4
// ==== UIEFFECT END ====

// ==== SOFTMASKABLE START ====
#pragma shader_feature_local_fragment _ SOFTMASKABLE
#if SOFTMASKABLE
#include "Packages/com.coffee.softmask-for-ugui/Shaders/SoftMask.cginc"
#endif
// ==== SOFTMASKABLE END ====
```

frag 函数中：
```glsl
// UIEffect 的效果处理...
// SoftMaskable 的软遮罩采样
#if SOFTMASKABLE
color.a *= SoftMask(IN.vertex, IN.worldPosition, color.a);
#endif
```

**执行链**：
1. `SoftMaskable.GetModifiedMaterial` 查找 `(SoftMaskable)` 变体 shader → 映射到 `(UIEffect)` shader。
2. `UIEffectBase.GetModifiedMaterial` 查找 `(UIEffect)` 变体 shader → 得到同时含两段代码的 shader。
3. 最终材质同时启用 `SOFTMASKABLE` keyword（由 SoftMaskable 设置）和各 UIEffect keyword（由 UIEffectContext 设置）。
4. 渲染时：先做 UIEffect 效果处理，再做 SoftMask 软遮罩 alpha 乘法。

### 4.3 SoftMask.cginc 核心逻辑

```glsl
// 运行时：裁剪坐标 → UV
#define SoftMask(clipPos, _, __) SoftMaskSample(ClipToUv(clipPos), 1)

// 编辑器：世界坐标 → UV（SceneView 需要）
#define SoftMask(_, worldPos, alpha) SoftMaskSample(WorldToUv(worldPos), alpha)
```

`SoftMaskSample(uv, a)`：
1. 双目支持：Stereo 时 UV 偏移。
2. RenderScale / DynamicResolution 补偿。
3. 采样 `_SoftMaskTex`（SoftMask 的 RenderTexture）。
4. 按 `_SoftMaskColor`（RGBA 四层深度标记）选择对应层 alpha。
5. 幂运算 `pow(alpha, _SoftMaskingPower)`。
6. 编辑器模式：屏幕外区域用 `_SoftMaskOutsideColor`，Subtract 模式做 alpha clip。

---

## 五、性能分析

### 5.1 UIEffect 性能策略

| 策略 | 说明 |
|---|---|
| 材质池化 | `MaterialRepository` 按 Hash128 共享材质实例，相同效果配置不重复创建 |
| Shader Keyword | 使用 `shader_feature_local_fragment`，只编译使用到的变体 |
| 顶点扩展按需 | 只有 Blur/Melt/Burn 等需要扩展边界的效果才扩展 |
| 脏标记 | `_isContextDirty` + `SetVerticesDirty`/`SetMaterialDirty`，避免每帧全量刷新 |
| Context 对象池 | `UIEffectContext` 用 `InternalObjectPool` 管理 |
| Gradient 烘焙缓存 | `_isGradientDirty` 标记，只在变化时重新烘焙 256x1 纹理 |
| 帧缓存 | `FrameCache` 防止同帧重复操作 |

### 5.2 UISoftMask 性能策略

| 策略 | 说明 |
|---|---|
| RenderTexture 降采样 | `DownSamplingRate` x1~x8，大幅降低缓冲区分辨率 |
| RenderTexture 池化 | `RenderTextureRepository` 按 Hash128 共享 |
| 脏标记传播 | `SetSoftMaskDirty` 递归子树，但只在 Transform/View 变化时触发 |
| 帧缓存 | `FrameCache` 防止同帧重复渲染缓冲区 |
| 屏幕外跳过 | `IsInScreen()` 检查，不在屏幕内不渲染 |
| CommandBuffer 池化 | `_cb` 和 `_mpb` 复用 |
| Mesh 池化 | `MeshExtensions.Rent/Return` |
| 父级缓冲区 Blit | 非根 SoftMask 直接从父级 Blit 缓冲区，减少重复绘制 |

### 5.3 性能注意点

- **UIEffect 模糊（Blur）**：需要扩展顶点边界 10~20 像素，增加顶点数和采样次数，移动端慎用 `BlurDetail`。
- **UIEffect Transition + Gradation**：需要每帧更新 ViewMatrix（`useViewMatrix == true`），有矩阵计算开销。
- **SoftMask 嵌套**：`softMaskDepth` 最大 4 层，每层一个 RenderTexture，嵌套越深内存和 Draw Call 越多。
- **SoftMask 降采样**：`x8` 时缓冲区分辨率为屏幕 1/8，适合静态 UI；动态 UI 用 `x1` 或 `x2`。
- **AlphaHitTest**：需要贴图 Read/Write Enabled，增加内存和 CPU 开销，仅在必要时启用。

---

## 六、业务使用场景

### 6.1 UIEffect 典型场景

| 场景 | 推荐配置 |
|---|---|
| 按钮禁用灰化 | ToneFilter=Grayscale, ToneIntensity=1 |
| 按钮高亮发光 | ColorFilter=MultiplyAdditive, ColorGlow=true |
| 入场溶解动画 | TransitionFilter=Dissolve, TransitionTex=噪声图, 配合 Tweener |
| 切换过渡 | TransitionFilter=Fade/Cutoff, 配合 Tweener Loop |
| 闪卡光泽 | TransitionFilter=Shiny, TransitionAutoPlaySpeed=1 |
| 文字描边 | ShadowMode=Outline/Outline8 |
| 渐变文字/背景 | GradationMode=Horizontal/Vertical/Radial, GradationGradient |
| 边缘流光 | EdgeMode=Shiny, EdgeShinyAutoPlaySpeed=1 |
| 像素化特效 | SamplingFilter=Pixelation |
| RGB 错位故障 | SamplingFilter=RgbShift |

### 6.2 UISoftMask 典型场景

| 场景 | 推荐配置 |
|---|---|
| 圆角头像/卡片 | SoftMask + 圆形/圆角 Graphic 作为遮罩 |
| 不规则形状 UI | SoftMask + 自定义形状 Graphic |
| 渐隐遮罩边缘 | SoftMask + softnessRange 调整 |
| 镂空/打洞 | SoftMask + MaskingShape(Subtract) |
| 精确点击 | AlphaHitTestTarget 或 SoftMask.alphaHitTest |
| 多层嵌套遮罩 | 多个 SoftMask 嵌套（最多 4 层） |

### 6.3 两者组合场景

| 场景 | 配置 |
|---|---|
| 软遮罩内做溶解过渡 | SoftMask + UIEffect(TransitionFilter=Dissolve) |
| 圆角卡片内做模糊 | SoftMask + UIEffect(SamplingFilter=BlurMedium) |
| 不规则形状内做渐变 | SoftMask + UIEffect(GradationMode=Radial) |

---

## 七、项目集成现状

### 7.1 包引用

两个插件通过 Unity Package Manager 以 UPM 包形式引入，缓存在 `Library/PackageCache/`：
- `com.coffee.ui-effect@5.11.7`
- `com.coffee.softmask-for-ugui@3.6.5`

### 7.2 项目设置文件

- `Assets/ProjectSettings/UIEffectProjectSettings.asset`：UIEffect 全局设置（HDR 颜色选择器启用、空预设列表、shader 变体注册表映射 SoftMaskable→UIEffect）。
- `Assets/ProjectSettings/UISoftMaskProjectSettings.asset`：SoftMask 全局设置（软遮罩启用、Stereo 启用、自动生成 SoftMaskable 关闭、隐藏生成组件启用、shader 变体注册表映射 UIEffect→UIEffect）。

### 7.3 编译产物

- `Coffee.UIEffect.csproj` / `Coffee.UIEffect.Editor.csproj`
- `Coffee.SoftMaskForUGUI.csproj` / `Coffee.SoftMaskForUGUI.Editor.csproj`

### 7.4 注意事项

- 这两个插件是**第三方插件**，不属于 TEngine 框架或 fork 定制内容。
- 项目中另有自研的 `UIEffectSortingOrder`（`GameLogic/Module/UIModule/Expansion/Utility/`），名字相似但功能完全不同（用于特效排序同步 Canvas sortingOrder），不要混淆。
- `UISoftMaskProjectSettings.m_SoftMaskable = 0` 表示**未启用自动生成 SoftMaskable 组件**，需要手动添加或通过代码触发。

---

## 八、关键代码路径索引

### UIEffect
| 文件 | 行数 | 路径 |
|---|---|---|
| UIEffect.cs | 2196 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffect.cs` |
| UIEffectBase.cs | 327 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffectBase.cs` |
| UIEffectContext.cs | 855 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffectContext.cs` |
| UIEffectTweener.cs | 583 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffectTweener.cs` |
| UIEffectPreset.cs | 210 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffectPreset.cs` |
| UIEffectReplica.cs | 305 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffectReplica.cs` |
| UIEffectProjectSettings.cs | 345 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/UIEffectProjectSettings.cs` |
| Enums.cs | 177 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/Enums.cs` |
| MaterialRepository.cs | 94 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/Internal/Utilities/MaterialRepository.cs` |
| GraphicProxy.cs | 99 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/Utilities/GraphicProxy.cs` |
| TmpProxy.cs | 333 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Runtime/Utilities/TmpProxy.cs` |
| UIEffect.shader | 198 | `Library/PackageCache/com.coffee.ui-effect@5.11.7/Shaders/UIEffect.shader` |

### UISoftMask
| 文件 | 行数 | 路径 |
|---|---|---|
| SoftMask.cs | 905 | `Library/PackageCache/com.coffee.softmask-for-ugui@3.6.5/Runtime/SoftMask.cs` |
| SoftMaskable.cs | 444 | `Library/PackageCache/com.coffee.softmask-for-ugui@3.6.5/Runtime/SoftMaskable.cs` |
| MaskingShape.cs | 497 | `Library/PackageCache/com.coffee.softmask-for-ugui@3.6.5/Runtime/MaskingShape/MaskingShape.cs` |
| AlphaHitTestTarget.cs | 39 | `Library/PackageCache/com.coffee.softmask-for-ugui@3.6.5/Runtime/AlphaHitTestTarget.cs` |
| Hidden-UI-Default-SoftMaskable.shader | 167 | `Library/PackageCache/com.coffee.softmask-for-ugui@3.6.5/Shaders/Hidden-UI-Default-SoftMaskable.shader` |
| SoftMask.cginc | 115 | `Library/PackageCache/com.coffee.softmask-for-ugui@3.6.5/Shaders/SoftMask.cginc` |

---

## 九、总结

UIEffect 和 UISoftMask 是 Coffee 开发的 UGUI 视觉效果增强套件，两者设计为可协作的互补插件：

- **UIEffect** 通过材质级 shader 注入实现 8 大类视觉效果（色调/颜色/采样/过渡/目标/阴影/渐变/边缘/细节），配合 Tweener 做动画、Preset 做复用、Replica 做批量。核心机制是 `MaterialRepository` 池化 + `shader_feature` 变体 + `UIEffectContext` 属性写入。

- **UISoftMask** 通过 RenderTexture 软遮罩缓冲区替代原生 Mask 的硬边 stencil，支持软边缘/抗锯齿/多层嵌套/加减法形状/精确点击。核心机制是 `RenderTextureRepository` 池化 + 降采样 + 脏标记传播 + `SoftMaskable` 材质修改。

- **协作**：UIEffect 的 shader 内嵌 `SOFTMASKABLE` 代码块，在 SoftMask 子树中自动启用软遮罩采样；两个 ProjectSettings 互相注册 shader 映射，确保任意 shader 都能找到同时含两段代码的变体。

两者均为成熟的第三方插件，项目中已正确集成配置，可直接在业务侧使用。
