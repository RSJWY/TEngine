# 调试器（Debugger）

本页记录 fork 中围绕 TEngine `Debugger` 模块的定制改动。

## 快捷键切换 Debug UI

### 背景

TEngine 原版 `Debugger` 在运行时只能通过点击左上角 FPS 漂浮图标来展开/收起完整调试窗口。在真机或全屏 Standalone 下，鼠标点击小图标不便，且无法在不动鼠标的情况下快速呼出调试面板。本 fork 为 `Debugger` 增加可配置的组合快捷键，一键切换完整 Debug UI 的显示与隐藏。

### 改动摘要

- `Debugger` 新增 `[SerializeField] KeyCode _toggleHotkey`（默认 `BackQuote`，即 `` ` `` 键）作为切换主键。
- 新增 `[SerializeField] KeyCode[] _toggleModifierKeys`（默认 `{ LeftShift }`）作为修饰键集合；数组中所有键处于按住状态时，本帧按下主键才触发切换。设为空数组或 `null` 表示无修饰键，单按主键即可。
- 切换行为复用既有 `ShowFullWindow` 属性：`true` 显示完整 Debug UI 面板，`false` 收回为 FPS 漂浮图标；切换时连带隐藏/恢复 `UIRoot/EventSystem`，避免 IMGUI 与 UGUI 输入冲突。
- 快捷键仅在 `_debuggerModule.ActiveWindow` 为 `true` 且 `_toggleHotkey != KeyCode.None` 时生效；`ActiveWindow` 关闭时快捷键不响应。
- 暴露公共属性 `ToggleHotkey` / `ToggleModifierKeys`，支持运行时通过代码修改。
- 原有点击图标切换行为保持不变。

### 使用方式

**Inspector 配置**（挂载 `Debugger` 组件的 GameObject）：

- `Toggle Hotkey`：切换主键，默认 `BackQuote`。
- `Toggle Modifier Keys`：修饰键数组，默认 `LeftShift`。清空表示无修饰键。

**默认组合**：`LeftShift + BackQuote`（`` Shift + ` ``）。

**代码修改**：

```csharp
Debugger.Instance.ToggleHotkey = KeyCode.F1;
Debugger.Instance.ToggleModifierKeys = new[] { KeyCode.LeftControl, KeyCode.LeftShift };
```

设为 `KeyCode.None` 可禁用快捷键。

### 注意事项

- 快捷键检测在 `Update` 中进行，使用 `Input.GetKeyDown`，对帧率敏感的连按场景无影响。
- 修饰键检测使用 `Input.GetKey`（持续按住判定），不要求严格的"先按修饰键再按主键"顺序，只要触发瞬间修饰键全按住即可。
- 真机平台若 `BackQuote` 无实体按键，请在 Inspector 改为其它可用 `KeyCode`。
- `ActiveWindow` 由 `DebuggerActiveWindowType` 策略控制（`AlwaysOpen` / `OnlyOpenWhenDevelopment` / `OnlyOpenInEditor`）；快捷键仅在窗口激活时有效，不改变激活策略本身。

### 关键文件

- `Assets/TEngine/Runtime/Module/DebugerModule/Debugger.cs`
