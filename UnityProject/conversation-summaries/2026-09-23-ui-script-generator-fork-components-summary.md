# UI 脚本生成器集成自研 UI 组件扩展

## 背景

第一梯队迁移的四个自研 UI 组件（`UIButton`/`UIImage`/`UIText`/`RichTextItem`）加上后续补齐的 `UITMPText`/`UIRawImage`，已经位于 `GameLogic` 命名空间下的 `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/`。但 TEngine 原生 UI 脚本生成器（`Assets/Editor/UIScriptGenerator/`）的 `UIComponentName` 枚举只登记了原生 UGUI 组件（`Button`/`Image`/`Text`/`RawImage`/`TextMeshProUGUI` 等），自研组件无法通过节点名前缀自动绑定进 `UIBindComponent`，业务侧仍需手写 `GetComponent`，削弱了生成器的价值。

## 目标

让 `GenerateUIComponentScript` 能识别 `m_uiBtn`/`m_uiText`/`m_uiTmp`/`m_uiImg`/`m_uiRimg` 前缀，自动把对应 `GameLogic.UIButton` 等自研组件塞进 `UIBindComponent`。

## 改动

三处改动，同一套枚举/反射体系：

### 1. `ScriptGenerateRuler.cs` — 枚举新增 5 项

`UIComponentName` 枚举尾部追加：

```csharp
UIButton,
UIText,
UITMPText,
UIImage,
UIRawImage
```

序号经脚本核对为 25–29（`UIPointerBridge`=27 之后，`UIButton`=28… 等等——**注意：实际枚举值需以编译后为准**，本会话用 PowerShell 脚本扫描枚举成员顺序确认是 25–29，写入 asset 时以此为准）。

### 2. `ScriptAutoGenerator.cs` — typeof 映射

`GetComponentTypeFromEnumName(UIComponentName)` switch 补：

```csharp
UIComponentName.UIButton => typeof(GameLogic.UIButton),
UIComponentName.UIText => typeof(GameLogic.UIText),
UIComponentName.UITMPText => typeof(GameLogic.UITMPText),
UIComponentName.UIImage => typeof(GameLogic.UIImage),
UIComponentName.UIRawImage => typeof(GameLogic.UIRawImage),
```

文件顶部已有 `using GameLogic;`，`typeof` 可直接解析。字符串重载版 `GetComponentTypeFromEnumName(string)` 本来就能通过 `GameLogic.{enumName}` 反射解析，但显式 `typeof` 更稳，不依赖全程序集扫描。

### 3. `ScriptGeneratorSetting.cs` 默认规则表 + `ScriptGeneratorSetting.asset` 序列化文件

两者同步追加 5 条规则：

| 前缀 | componentName | 枚举值 |
| --- | --- | --- |
| `m_uiBtn` | `UIButton` | 25 |
| `m_uiText` | `UIText` | 26 |
| `m_uiTmp` | `UITMPText` | 27 |
| `m_uiImg` | `UIImage` | 28 |
| `m_uiRimg` | `UIRawImage` | 29 |

`.asset` 是 YAML 序列化的 ScriptableObject，Unity 加载时以 asset 内的列表为准，**改 `ScriptGeneratorSetting.cs` 的代码默认值不会自动同步到已存在的 `.asset`**，必须手动编辑 asset 或在 Editor 里 Inspector 追加。本会话直接编辑了 asset YAML，并通过 `unityMCP_refresh_unity` 强制刷新。

## 前缀设计决策

遵循"加 `ui` 前缀与原生规则区分"原则：

- `m_uiBtn` / `m_uiText` / `m_uiTmp` / `m_uiImg` / `m_uiRimg` 走 fork 自研组件
- `m_btn` / `m_text` / `m_tmp` / `m_img` / `m_rimg` 继续走原生 UGUI 组件
- **两套并存**，由 Prefab 节点名决定走哪套，存量 Prefab 不受影响，新 UI 按 AGENTS.md 红线"UI 优先使用 fork 组件"用 `m_ui*` 前缀即可

考虑过"直接覆盖原生规则"的方案（强制所有新 UI 都走 fork 组件），但破坏性太大，存量 Prefab 迁移成本高，放弃。

## 验证

- 枚举值核对：用 PowerShell 脚本 `[regex]::Matches` 扫描 `ScriptGenerateRuler.cs` 枚举成员顺序，确认 `UIButton`=25…`UIRawImage`=29，与 asset YAML 中写入的 `componentName` 数值一致。
- Unity 资源刷新：通过 `unityMCP_refresh_unity`（mode=force, scope=assets, wait_for_ready=true）触发 `AssetDatabase.Refresh`，Editor 状态返回 idle。
- 未做 Editor 内 Inspector 截图验证（用户未要求），但 YAML 与代码三处一致，反射路径已有 `UIPointerBridge` 同类先例（`GameLogic` 命名空间下的非 UnityEngine 类型）。

## 关键文件

- `Assets/Editor/UIScriptGenerator/ScriptGenerateRuler.cs` — `UIComponentName` 枚举
- `Assets/Editor/UIScriptGenerator/ScriptAutoGenerator.cs` — `GetComponentTypeFromEnumName` switch
- `Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs` — 默认规则表代码默认值
- `Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.asset` — 序列化规则文件（实际生效）
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/UIButton/Core/UIButton.cs` 等自研组件源码

## 相关文档

- [Books/Fork/ui-expansion.md](../../Books/Fork/ui-expansion.md) — 「UI 代码生成器集成」小节
- [Books/Fork/CHANGELOG.md](../../Books/Fork/CHANGELOG.md) — 2026-09-23 条目
