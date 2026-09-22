# 窗口管理

本页记录 fork 中围绕 Windows Standalone 多显示器窗口布局控制的改动。

## 窗口布局控制模块 ScreenModule

### 背景

Unity 应用本身无法创建多个独立 OS 窗口。这里的“多窗口”指多显示器（multi-display）：激活副屏后，每块屏幕对应一个 Unity 窗口，窗口类名为 `UnityWndClass`，同进程同线程。

本模块参考 [RSJWYFamework 的 Screen 模块](https://github.com/RSJWY/RSJWYFamework/tree/main/Assets/RSJWYFamework/Runtime/Screen)，并按 TEngine `Module` 规范重写。

### 功能

Windows Standalone 下控制 Unity 多屏窗口的：

- 位置
- 大小
- 强制置顶（`HWND_TOPMOST`）
- 无边框模式（去除 `WS_CAPTION | WS_THICKFRAME`）

### 设计要点

- 模块放在 AOT 层 `TEngine.Runtime`，不放进热更层。
- `DllImport` 原生互操作在 HybridCLR 解释域中调用不稳定，因此 Win32 封装由 IL2CPP 直接编译。
- 热更层仅通过 `GameModule.Screen` / `TEngine.IScreenModule` 调用。
- 在 `GameApp.StartGameLogic()` 首次访问 `GameModule.Screen` 时，`ModuleSystem.GetModule` 自动创建模块并执行 `OnInit`。
- 模块在 AOT 层，`Type.GetType` 可正常解析，无需 `RegisterModule`。
- 基于 TEngine `Module` 生命周期：`OnInit` 保持空实现，布局仅由显式 API 调用触发，`Shutdown` 清缓存。
- Win32 句柄发现使用 `FindWindowEx` 循环枚举顶层 `UnityWndClass` 窗口，再用 `GetWindowThreadProcessId` 按进程过滤。
- 不使用 native -> managed 回调委托。
- 样式读写使用 `GetWindowLongPtr` / `SetWindowLongPtr`，兼容 64 位。
- 应用布局前自动切窗口化：全屏模式下 `SetWindowPos` 会被 Unity 或 OS 覆盖。
- 底层 `WindowsScreenNative` 整文件使用 `#if UNITY_STANDALONE_WIN && !UNITY_EDITOR` 包裹，Editor 与非 Windows 平台一律走安全空实现。
- 配置顶层 `Enabled=false` 可整体禁用模块（见下方配置字段）。
- 副屏句柄配对优先按显示器几何匹配（`MonitorFromWindow` + `GetMonitorInfo`），几何信息不可用时回退枚举顺序配对。
- 配置读取、显示器激活、窗口发现与映射、布局应用都会输出诊断日志，失败时带 `Win32Error`。

### 位置

模块目录：

```text
Assets/TEngine/Runtime/Module/ScreenModule/
├── IScreenModule.cs
├── ScreenModule.cs
├── ScreenConfig.cs
└── WindowsScreenNative.cs
```

配置：

```text
Assets/StreamingAssets/Configs/ScreenConfig.toml
```

`ScreenConfig.toml` 已登记进 `config_manifest.toml`。

### 使用方式

```csharp
// 已在 GameApp.StartGameLogic() 显式调用。
GameModule.Screen.ApplyAll();
GameModule.Screen.ApplyScreen(0);
GameModule.Screen.SetTopmost(1, true);
bool ok = GameModule.Screen.IsSupported;
```

### 配置示例

```toml
# 总开关，false 时模块所有布局 API 均为空操作。
Enabled = true

[[Screens]]
DisplayIndex = 0
Activate = true
X = 0
Y = 0
Width = 1920
Height = 1080
Topmost = false
Borderless = false

[[Screens]]
DisplayIndex = 1
Activate = true
X = 1920
Y = 0
Width = 1920
Height = 1080
Topmost = false
Borderless = false
```

注意 TOML 语法：`Enabled` 是顶层标量，必须写在第一个 `[[Screens]]` 之前。

### 配置字段

| 字段 | 含义 |
| --- | --- |
| `Enabled` | 总开关，`false` 时所有布局 API 不执行，用于单屏或不需要窗口控制的场景整体禁用 |
| `DisplayIndex` | Unity Display 索引，0 为主屏，1/2/... 为副屏 |
| `Activate` | 是否激活该 Display，副屏必须激活才会创建窗口 |
| `X` / `Y` | 窗口位置，屏幕坐标系 |
| `Width` / `Height` | 窗口宽高，单位像素 |
| `Topmost` | 是否强制置顶 |
| `Borderless` | 是否去除边框与标题栏 |

### 容错机制

- 未配置或配置为空时，输出警告并使用主显示器默认分辨率。
- 默认配置铺满主屏、保留边框、不置顶。
- 无论配置是否存在，都至少保证主显示器可用。
- `DisplayIndex` 越界时跳过该项并告警，继续处理其余有效配置。
- `Enabled=false` 时所有布局 API 静默跳过（仅一条 Info 日志）。
- Editor 及非 Windows 平台仅输出警告，不执行任何窗口操作。
- 副屏句柄配对失败（无几何匹配且无剩余窗口）时告警，不影响已配对屏幕的布局应用。

### 副屏句柄几何配对

`RefreshHandles` 发现窗口后，副屏分配按两级策略：

1. **几何配对**（优先）：对每个副屏配置取目标点 `(X, Y)`，用 `MonitorFromWindow` + `GetMonitorInfo` 查询每个候选窗口所在显示器的桌面矩形，目标点落在哪个窗口的显示器内就与谁配对。多副屏下不再依赖窗口枚举顺序，配错窗口的风险大幅降低。
2. **顺序配对**（回退）：几何查询完全不可用（如 `GetMonitorInfo` 失败）时回退为按枚举顺序与索引升序配对，即旧行为；部分配不上时剩余窗口按顺序补齐并告警。

每次配对都会输出日志：几何配对成功为 `几何配对：Display=x -> hWnd=y（目标点... 位于显示器...）`，回退时有对应 Warning。

### 已知限制

- 仅 Windows Standalone 打包后真实生效；Editor 下模块整体 no-op（防止误操作编辑器窗口），多屏行为必须在打包后实测。
- 必须为窗口化才生效。全屏 `FullScreenWindow` / `ExclusiveFullScreen` 下 `SetWindowPos` 会被覆盖。
- 模块已在应用前自动切 `Windowed`，但如果 Player Settings 强制全屏或外部又切回全屏，位置和大小仍可能失效。这是有意保留的行为：即使单屏场景也允许用本模块纠正窗口位置/大小，不需要时用 `Enabled=false` 关闭。
- Display 激活必须在运行早期、渲染前进行，且激活后不可关闭，这是 Unity 限制。
- 几何配对以配置的 `(X, Y)` 为准；若配置目标点与实际显示器布局不符（如分辨率变更），会退回顺序配对。

### 排查方式

打包后无效果时，优先看日志：

- `配置 Enabled=false，窗口布局已被禁用`：确认总开关状态，误关时改 `ScreenConfig.toml` 或 persistentDataPath 覆盖配置。
- `当前可用显示器数量 Display.displays.Length=N`：确认 N 是否符合预期。
- `窗口发现：FindUnityWindows 命中 X 个`：X=0 表示没找到 Unity 窗口，可能是类名或进程过滤异常。
- `切换为 Windowed`：确认是否成功从全屏切窗口化。
- `几何配对：Display=x -> hWnd=y` / `回退为按枚举顺序配对`：确认副屏配对方式与结果。
- `映射 Display=x -> hWnd=y`：确认 Display 和窗口句柄是否配对正确。
- `已应用：Display=... Rect=...` 或 `SetWindowPos 失败 ... Win32Error=...`：确认是否真正下发成功。

### 关键文件

- `UnityProject/Assets/TEngine/Runtime/Module/ScreenModule/ScreenModule.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/ScreenModule/WindowsScreenNative.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/ScreenModule/ScreenConfig.cs`
- `UnityProject/Assets/TEngine/Runtime/Module/ScreenModule/IScreenModule.cs`
- `UnityProject/Assets/StreamingAssets/Configs/ScreenConfig.toml`
