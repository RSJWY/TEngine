# ScreenModule 窗口布局控制模块 会话总结

**日期**：2026-06-03  
**状态**：单屏已验证可用；多屏功能已实现但**未测试**，需要多显示器环境打包实测。

---

## 背景

参考 [RSJWYFamework/Screen](https://github.com/RSJWY/RSJWYFamework/tree/main/Assets/RSJWYFamework/Runtime/Screen)，在本项目实现 Windows Standalone 下的多屏窗口控制（位置/大小/置顶/无边框）。

---

## 最终文件结构

```
Assets/TEngine/Runtime/Module/ScreenModule/   ← AOT 层（TEngine.Runtime 程序集）
├── IScreenModule.cs        接口（namespace TEngine）
├── ScreenModule.cs         模块实现
├── ScreenConfig.cs         配置模型（ScreenConfig / ScreenSetting）
└── WindowsScreenNative.cs  user32 / kernel32 P/Invoke 封装

Assets/StreamingAssets/Configs/
├── ScreenConfig.json       多屏布局配置（已提交，主屏 1920×1080 示例）
└── config_manifest.json    已追加 ScreenConfig.json

Assets/GameScripts/HotFix/GameLogic/GameApp.cs     热更入口，首次访问触发模块创建
Assets/GameScripts/HotFix/GameLogic/GameModule.cs  新增 GameModule.Screen 访问器
```

---

## 核心设计决策

### 1. 放在 AOT 层而非热更层

`DllImport` 原生互操作在 HybridCLR 解释域中调用不稳定（最初误放热更层，打包无效）。  
改为放在 `TEngine.Runtime`（AOT，IL2CPP 直接编译），热更层仅通过 `TEngine.IScreenModule` 接口调用。

### 2. 标准 `GetModule<IScreenModule>()` 注册

模块在 AOT 层，`Type.GetType("TEngine.ScreenModule, TEngine.Runtime")` 可正常解析，无需 `RegisterModule` 预创建实例。  
热更入口首次访问 `GameModule.Screen` 时自动触发创建和 `OnInit`。

### 3. 全屏模式是打包后"无效果"的根因

`ProjectSettings fullscreenMode: 3`（`FullScreenWindow`），`SetWindowPos` 在全屏下被 Unity/OS 覆盖。  
`EnsureWindowedMode()` 在应用布局前先调 `Screen.SetResolution(..., FullScreenMode.Windowed)`，再等 `WAIT_FRAMES=3` 帧让模式切换生效，之后再查找句柄和下发 `SetWindowPos`。

### 4. `FindWindowEx` 枚举替代 Z-order 遍历

旧：`GetWindow(GetActiveWindow(), GW_HWNDFIRST)` 起点经常拿不到，且用了 `EnumThreadWindows` 回调委托（HybridCLR 反向 P/Invoke 不可靠）。  
新：`FindWindowEx(Zero, prev, "UnityWndClass", null)` 循环枚举所有顶层 Unity 类名窗口 + `GetWindowThreadProcessId` 按当前进程过滤，全正向调用。

### 5. 64 位安全的样式读写

改用 `GetWindowLongPtr` / `SetWindowLongPtr`（`EntryPoint` 分别指向 64/32 位实现），避免 32 位 `GetWindowLong` 在 64 位进程截断指针。

---

## 调用流程

```
GameApp.StartGameLogic()
  └─ GameModule.Screen（首次访问）
       └─ ModuleSystem.GetModule<IScreenModule>()
            └─ ScreenModule.OnInit()
                 ├─ LoadConfig()          → GameModule.JsonConfig.TryGet<ScreenConfig>()
                 │                          未配置 → BuildDefaultConfig()（主显示器分辨率）
                 └─ ApplyAllAsync()
                      ├─ EnsureWindowedMode()   → Screen.SetResolution(..., Windowed)
                      ├─ ActivateDisplays()      → Display.displays[i].Activate(w,h,60)
                      ├─ await DelayFrame(3)
                      ├─ RefreshHandles()        → FindUnityWindows() + 映射 DisplayIndex→hWnd
                      └─ ApplySetting × N        → SetWindowLayout / SetWindowPos
```

---

## 详细日志关键字（排查时搜索）

| 日志关键词 | 含义 |
|---|---|
| `已从 ScreenConfig.json 读取配置` | 配置正常加载 |
| `未找到有效的 ScreenConfig.json` | 回退默认配置，会告警 |
| `当前可用显示器数量 Display.displays.Length=N` | 系统可用显示器数 |
| `切换为 Windowed` | 全屏→窗口化切换 |
| `FindUnityWindows 命中 X 个` | X=0 说明句柄发现失败 |
| `映射 Display=x -> hWnd=y` | 最终 DisplayIndex→句柄映射 |
| `已应用：Display=... Rect=(...)` | 布局成功下发 |
| `SetWindowPos 失败，Win32Error=...` | Win32 错误码 |
| `ApplyAll 完成：成功应用 N/M 个` | 汇总结果 |

---

## ⚠️ 未测试项：多屏

**已实现但未验证**：

- `Display.displays[i].Activate()` 副屏激活逻辑
- `DisplayIndex → 窗口句柄` 副屏映射（按窗口发现顺序与已激活副屏配置升序配对）
- 多屏布局 JSON 配置中多条 `ScreenSetting` 的应用

**已知风险**：副屏映射依赖 `FindWindowEx` 发现顺序，多副屏场景可能与 `DisplayIndex` 对不上，需打 Windows 包 + 多显示器实测。必要时可改为 `MonitorFromWindow` 按显示器矩形精确匹配。

**测试时**：改 `ScreenConfig.json` 加第二条 `DisplayIndex=1` 配置，观察日志 `映射 Display=1 -> hWnd=...` 是否命中，窗口是否出现在第二块屏。

---

## 容错机制

| 情况 | 行为 |
|---|---|
| 未配置 / 配置为空 | 警告 + 使用 `Screen.currentResolution` 铺满主屏 |
| `DisplayIndex` 越界 | 跳过该项并警告 |
| 非 Windows 平台 | 仅输出警告，不执行任何操作 |
| `SetWindowPos` 失败 | 警告 + 打印 Win32Error，其余屏继续处理 |

---

## 关键文件

- `Assets/TEngine/Runtime/Module/ScreenModule/ScreenModule.cs`
- `Assets/TEngine/Runtime/Module/ScreenModule/WindowsScreenNative.cs`
- `Assets/StreamingAssets/Configs/ScreenConfig.json`
- `Assets/GameScripts/HotFix/GameLogic/GameApp.cs`（接入点）
- `Books/Fork-定制改动说明.md`（含排查清单）
