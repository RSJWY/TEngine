# UIText 的 TextMeshPro 版本可行性研究

日期：2026-08-29
研究对象：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIText/`（UIText + 6 个 Extend）
目标：评估做一个 TextMeshPro 版本（下称 UITMPText）的可行性与实现方案。

## 1. 现状架构（UGUI Text 版）

| 文件 | 职责 |
|------|------|
| `Core/UIText.cs` | 空壳，`UIText : BaseUIText`，菜单 `UIPro/UIText` |
| `Core/BaseUIText.cs` | `BaseUIText : Text, IMeshModifier`；核心入口 `ModifyMesh(VertexHelper)` 串联 6 扩展；自带 BestFit 逐字号递减算法（`OverrideForBestFit`） |
| `Extend/UITextSpacingExtend.cs` | 字符间距：顶点流按 `i/6` 平移（UGUI 每字符 6 顶点：4 唯一 + 2 重复） |
| `Extend/UITextVertexColorExtend.cs` | 四角顶点色双线性插值（Additive/Overlap 两种混合） |
| `Extend/UITextShadowExtend.cs` | 四角色阴影：复制顶点流（数量翻倍）+ 按位置重映射颜色 |
| `Extend/UITextOutlineExtend.cs` | Shader 描边：扩展顶点位置 ±width，把描边参数塞进 uv1/uv2/uv3/tangent/normal，配自定义 shader `UGUIPro_UIText.mat`（YooAsset 地址加载 + 材质缓存 `OutlineMaterialCache`），并自动开 Canvas 的 AdditionalShaderChannels |
| `Extend/UITextGradientColorExtend.cs` | 整体/逐字符（split）渐变，垂直+水平双轴，支持偏移 |
| `Extend/UITextCircleExtend.cs` | 环形排字：按字符中心 X 计算角度，旋转+贴圆 |

关键耦合点：
- 所有 Extend 的 `Initialize(Text text)` / `m_text.font.material.mainTexture` / `m_text.material == m_text.defaultMaterial` 均直接依赖 `UnityEngine.UI.Text`。
- Editor 侧 `Assets/Editor/UIModuleExpansion/UIText/UITextEditor.cs`（239 行）按 SerializedProperty 画面板，只认 `UIText`。
- `UIButtonDrawEditor` 创建子文本也用 `UIText`；`RichTextItem` 内部对象池直接 `AddComponent<UIText>()`。

## 2. TMP 渲染管线关键结论（源码验证）

项目已装 `com.unity.textmeshpro 3.0.9`（manifest.json），GameLogic 已有代码 using TMPro（CommonToast 等），asmdef 经由 TEngine 的 UGUI 引用链可用。

**TextMeshProUGUI 不走 UGUI 的 OnPopulateMesh / IMeshModifier 管线**：
- `TextMeshProUGUI.Rebuild(CanvasUpdate.PreRender)` → `OnPreRenderCanvas()` → `GenerateTextMesh()` → 直接 `m_canvasRenderer.SetMesh(m_mesh)`（TextMeshProUGUI.cs:203-223、TMPro_UGUI_Private.cs:4432-4444）。
- 因此**把 BaseUIText 的 IMeshModifier 那套直接搬到 TMP 子类上不会生效**。
- 官方扩展点：`OnPreRenderText` 事件（TMPro_UGUI_Private.cs:4421），在顶点数据上传 mesh 前触发，参数 `TMP_TextInfo`。
- 顶点数据形态：`textInfo.meshInfo[i].vertices(Vector3[]) / colors32 / uvs0 / uvs2`，**每字符 4 顶点**（与 UGUI 的 6 顶点流不同，排序也不同）。
- TMP 自己会强制 `canvas.additionalShaderChannels |= 25`（TexCoord1/2/3+Tangent+Normal），无需 Extend 里手动开。
- 多材质（fallback 字体/图混排）时 meshInfo 有多份，SubMesh 单独渲染——顶点修改要遍历所有 meshInfo。

## 3. 功能对照：哪些扩展在 TMP 上还需要做

| 现有扩展 | TMP 原生替代 | 结论 |
|---------|-------------|------|
| Spacing 字距 | `characterSpacing` / `wordSpacing`（布局期生效，含富文本） | 不需要重写 |
| BestFit | `enableAutoSizing` + `fontSizeMin/Max` | 不需要重写 |
| Outline 描边 | SDF shader 原生：`fontSharedMaterial` 设 `_OutlineWidth`/`OutlineColor`（用 material 实例） | 不需要顶点 hack，一行材质参数；含 fallback 字体时需给每个材质设 |
| GradientColor 渐变 | `enableVertexGradient` + `colorGradient`（4 角色） | 基础覆盖；**splitTextGradient（逐字符渐变）与 offset 无原生对应，需 OnPreRenderText 实现** |
| VertexColor 四角色 | 同上 VertexGradient 基本等价 | 基本不需要 |
| Shadow 四角色阴影 | Underlay（shader 属性）有单色阴影；四角色阴影无对应 | 需要 OnPreRenderText 复制顶点实现（注意 TMP 顶点数组的 capacity/vertexCount 管理与 meshInfo.ResizeMeshInfo） |
| Circle 环形字 | 无 | 需要 OnPreRenderText 实现（每字符 4 顶点版变换，比 UGUI 版更简单） |

## 4. 推荐实现方案

新建目录 `Expansion/UITMPText/`（与 UIText 并列，不动现有代码）：

```
UITMPText/
├── Core/
│   ├── UITMPText.cs          // : TextMeshProUGUI, [AddComponentMenu("UIPro/UITMPText")]
│   └── BaseUITMPText.cs      // 订阅 OnPreRenderText；Awake 里 += OnPreRenderText（注意 TMP 的 OnPreRenderCanvas 每次重绘都会触发）
└── Extend/
    ├── TMPShadowExtend.cs    // 顶点复制法：需要 meshInfo.vertices 扩容（ResizeMeshInfo）或预先算好
    ├── TMPGradientExtend.cs  // 整体/逐字符渐变，操作 colors32；逐字符=每 4 顶点一组
    └── TMPCircleExtend.cs    // 每 4 顶点取中心，绕 Z 旋转贴圆
```

实现要点：
1. 顶点遍历单位从「6 顶点流」改为「每字符 4 顶点」：`for (i = 0; i + 4 <= meshInfo.vertexCount; i += 4)`。
2. 修改 colors32/vertices 后无需手动上传——OnPreRenderText 回调返回后 TMP 自动 `m_mesh.colors32 = ...` 上传。
3. 描边 API 封装：`fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, w)` + `SetColor(ID_OutlineColor, c)`，并调 `UpdateFontAsset` 相关 padding 无需关心（TMP 自动按材质属性扩展 padding）。
4. 阴影复制顶点方案要处理 `meshInfo.vertexCount` 超出预分配的问题——TMP 在 GenerateTextMesh 时按字符数分配 capacity，复制顶点必须 `meshInfo.ResizeMeshInfo(所需容量)`（TMP_MeshInfo 有现成方法）后再追加，且只在 meshInfo[0]（主材质）做即可。
5. Editor：新建 `Assets/Editor/UIModuleExpansion/UITMPText/UITMPTextEditor.cs`，可大量参考现有 UITextEditor 的折叠面板结构，但 Spacing/BestFit/Outline 面板换成 TMP 原生属性（fontSizeAutoSizing 等）。
6. `RichTextItem` 若要支持 TMP，后续单独加 `GetOrCreateText` 的 TMP 分支；本次不做。
7. GameObject 菜单注册：`[MenuItem("GameObject/UI/UITMPText", priority = 31)]`。

## 5. 风险与注意

- **不能直接继承 BaseUIText 换基类**：UGUI Text 与 TMP_Text 的网格管线完全不同，6 个 Extend 的 VertexHelper 版代码不可复用，需要按 TMP 数据结构重写（好消息是重写后大多更简单）。
- `OnPreRenderText` 每次文本重建都会触发（包括 anim、材质变更），扩展里避免分配（复用数组/缓存）。
- 多 meshInfo（fallback 字体）时，逐字符逻辑按 characterInfo 的 materialIndex 分桶处理，不能只改 meshInfo[0]。阴影顶点复制法在多 meshInfo 下复杂度高，建议首版只支持单材质主字库，文档注明。
- Unity 6 中 TMP 与 UGUI 包合并（com.unity.ugui 2.0 内置 TMP），本项目 3.0.9 独立包在当前 Unity 版本下正常，升级 Unity 时注意 API 兼容。
- HybridCLR 热更：TMP 在包内（非热更），GameLogic 引用 TMPro 与现有 CommonToast 做法一致，无边界问题。

## 6. 工作量预估

- Runtime：BaseUITMPText + 3 个 Extend（Gradient/Circle/Shadow）≈ 500~700 行，参考现有实现移植。
- Editor：UITMPTextEditor ≈ 300 行（砍掉 Spacing/BestFit/ShaderOutline 面板）。
- 描边/间距/BestFit 直接暴露 TMP 原生属性封装 API（SetOutLineColor 等保持同名 API 便于迁移）。
