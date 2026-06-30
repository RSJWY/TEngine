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
- 基于 TEngine `Module` 生命周期：`OnInit` 读配置并按需应用，`Shutdown` 清缓存。
- Win32 句柄发现使用 `FindWindowEx` 循环枚举顶层 `UnityWndClass` 窗口，再用 `GetWindowThreadProcessId` 按进程过滤。
- 不使用 native -> managed 回调委托。
- 样式读写使用 `GetWindowLongPtr` / `SetWindowLongPtr`，兼容 64 位。
- 应用布局前自动切窗口化：全屏模式下 `SetWindowPos` 会被 Unity 或 OS 覆盖。
- 底层 `WindowsScreenNative` 整文件使用 `#if UNITY_STANDALONE_WIN || UNITY_EDITOR` 包裹。
- 其他平台提供安全空实现，非 Windows 平台调用 API 仅输出警告。
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
Assets/StreamingAssets/Configs/ScreenConfig.json
```

`ScreenConfig.json` 已登记进 `config_manifest.json`。

### 使用方式

```csharp
// 已在 GameApp.StartGameLogic() 首次访问时自动创建并按配置应用。
GameModule.Screen.ApplyAll();
GameModule.Screen.ApplyScreen(0);
GameModule.Screen.SetTopmost(1, true);
bool ok = GameModule.Screen.IsSupported;
```

### 配置示例

```json
{
  "ApplyOnInit": true,
  "Screens": [
    {
      "DisplayIndex": 0,
      "Activate": true,
      "X": 0,
      "Y": 0,
      "Width": 1920,
      "Height": 1080,
      "Topmost": false,
      "Borderless": false
    }
  ]
}
```

### 配置字段

| 字段 | 含义 |
| --- | --- |
| `ApplyOnInit` | 模块初始化时是否自动应用配置 |
| `DisplayIndex` | Unity Display 索引，0 为主屏，1/2/... 为副屏 |
| `Activate` | 是否激活该 Display，副屏必须激活才会创建窗口 |
| `X` / `Y` | 窗口位置，屏幕坐标系 |
| `Width` / `Height` | 窗口宽高，单位像素 |
| `Topmost` | 是否强制置顶 |
| `Borderless` | 是否去除边框与标题栏 |

### 容错机制

- 未配置或配置为空时，输出警告并使用主显示器默认分辨率。
- 默认配置居中、保留边框、不置顶。
- 无论配置是否存在，都至少保证主显示器可用。
- `DisplayIndex` 越界时跳过该项并告警，继续处理其余有效配置。
- 非 Windows 平台仅输出警告，不执行任何窗口操作。

### 已知限制

- 仅 Windows Standalone 真实生效。
- Editor 下 `GetActiveWindow` 拿到的是编辑器或 Game 窗口，多屏行为无法在 Editor 完整验证。
- 必须打 Windows 包并在多显示器环境实测。
- 必须为窗口化才生效。全屏 `FullScreenWindow` / `ExclusiveFullScreen` 下 `SetWindowPos` 会被覆盖。
- 模块已在应用前自动切 `Windowed`，但如果 Player Settings 强制全屏或外部又切回全屏，位置和大小仍可能失效。
- `DisplayIndex -> 窗口句柄` 映射：主屏取当前激活窗口，副屏按窗口发现顺序与已激活的副屏配置按索引升序依次配对。
- 多副屏场景中该顺序可能需要打包后实测校正，必要时可改用 `MonitorFromWindow` 按显示器矩形精确匹配。
- Display 激活必须在运行早期、渲染前进行，且激活后不可关闭，这是 Unity 限制。

### 排查方式

打包后无效果时，优先看日志：

- `当前可用显示器数量 Display.displays.Length=N`：确认 N 是否符合预期。
- `窗口发现：FindUnityWindows 命中 X 个`：X=0 表示没找到 Unity 窗口，可能是类名或进程过滤异常。
- `切换为 Windowed`：确认是否成功从全屏切窗口化。
- `映射 Display=x -> hWnd=y`：确认 Display 和窗口句柄是否配对正确。
- `已应用：Display=... Rect=...` 或 `SetWindowPos 失败 ... Win32Error=...`：确认是否真正下发成功。
