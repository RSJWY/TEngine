# UIRawImage 扩展可行性研究（参考 UIImage 体系）

日期：2026-08-29
研究对象：
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIImage/`（UIImage / BaseUIImage + 3 个 Extend）
- `Assets/Editor/UIModuleExpansion/UIImage/`（UIImageEditor / UIImageDrawEditor）

## 结论

**可以实现。** 架构可完全照搬：`BaseUIRawImage : RawImage, IMeshModifier` 与 `BaseUIImage : Image, IMeshModifier` 同构。核心依据：`IMeshModifier.ModifyMesh(VertexHelper)` 由 `Graphic.UpdateGeometry()` 对所有 Graphic 子类统一调用，RawImage 也不例外。

## 机制分析

### 为什么通用
- `Graphic.UpdateGeometry()` → `GetComponentComponentsListComponent<RectTransform, IMeshModifier>` 遍历调用 `ModifyMesh`，与具体 Graphic 类型无关。
- UIImage 体系的数据流：`OnPopulateMesh` 判断是否需要 override（不 override 则走 base 生成默认网格）→ `ModifyMesh(VertexHelper)` 中由各 Extend 重写顶点。RawImage 同样适用。
- 三个 Extend 均为 `[Serializable]` 普通类，只持有 `Image` 引用，改为持有 `Graphic`/`RawImage` 即可复用结构。

### 三个扩展的移植差异点

| 扩展 | 可行性 | 适配点 |
|------|--------|--------|
| RoundedCorners 圆角 | ✅ 完全可行 | ① UV 计算：`DataUtility.GetOuterUV(sprite)` → 直接用 `RawImage.uvRect`（RawImage 无 Sprite/九宫格/图集 padding 概念，顶点 UV = lerp(uvRect)）；② `GetDrawingDimensions` 简化为直接用 `GetPixelAdjustedRect()`（无 sprite padding 修正） |
| Mask 不规则图形（圆/环/多边形） | ✅ 完全可行 | 同上，UV 映射改 `uvRect` + `texture`；RayCrossing 射线检测与 Graphic 类型无关，可原样保留 |
| Mirror 镜像 | ⚠️ 部分可行 | RawImage 仅 Simple 四边形绘制（无 Sliced/Tiled/Filled），只保留 `DrawSimple` 分支；`SetNativeSize` 改用 `texture.width/height` |

### RawImage 的 API 注意点
- `overrideSprite` / `sprite` → `texture` + `uvRect`（RawImage 用 Texture 直接绘制）。
- `RawImage.OnPopulateMesh` 本身就是画一个 quad，4 顶点 2 三角，非常适合被 ModifyMesh 重写。
- `type`/`hasBorder`/`pixelsPerUnitMultiplier` 等 Image 特有 API 在 RawImage 上不存在，Mirror 移植时相关分支直接删除。

### Inspector 复用
- `UIImageDrawEditor` 的 `DrawImageMaskGUI` / `DrawImageRoundedCornersGUI` / `DrawImageMirrorGUI` 只依赖 `SerializedProperty`，与组件类型解耦，可 100% 复用。
- 新建 `UIRawImageEditor : RawImageEditor`（UnityEditor.UI 命名空间，对应 `Assets/Editor/UIModuleExpansion/UIRawImage/`），`FindProperty` 路径改为 `m_uiRawImageXxxExtend.m_yyy`。
- `GameObject/UI/UIRawImage` 菜单项仿照 `UIImageDrawEditor.CreateUIImage`，用 `ObjectFactory.CreateGameObject` + `UnityEditorUtil.ResetInCanvasFor`。

### 目录结构建议（实施时）
```
Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIRawImage/
├── Core/
│   ├── UIRawImage.cs          // [Serializable] class UIRawImage : BaseUIRawImage {}
│   └── BaseUIRawImage.cs      // RawImage + IMeshModifier + 3 个 Extend 组合
└── Extend/
    ├── UIRawImageRoundedCornersExtend.cs
    ├── UIRawImageMaskExtend.cs
    └── UIRawImageMirrorExtend.cs
Assets/Editor/UIModuleExpansion/UIRawImage/
├── UIRawImageEditor.cs        // : RawImageEditor
└── UIRawImageDrawEditor.cs    // 菜单 + 复用/新写绘制方法
```

### 热更边界确认
- `GameScripts/HotFix/GameLogic/...` 属热更程序集，Extend 只用 UnityEngine.UI / TEngine ListPool，无 Editor 依赖，符合热更红线。
- Editor 脚本全部放 `Assets/Editor/UIModuleExpansion/UIRawImage/`，符合项目 Editor 目录规范。

## 典型使用场景
- 大图（网络头像、截图、RenderTexture、序列帧合并大图）需要圆角/圆形裁剪或镜像时，RawImage 版本避免为 Image 生成额外 Sprite 开销。
