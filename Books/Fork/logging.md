# 日志系统

本页记录 fork 中围绕日志桥接、日志落盘和日志查看工具的改动。

## TouchSocket 日志桥接与落盘

### 背景

项目引入 `TouchSocket.Core` 后，需要让 TouchSocket 日志进入 Unity Console，同时把 Unity、Task、UniTask 的日志和未观察异常统一落盘，便于编辑器和打包后排查。

### 改动摘要

- `TouchSocketContainerUnityDebugLogger` 继承 `LoggerBase`，将 TouchSocket 日志按级别映射到 `Debug.Log`、`LogWarning`、`LogError`，统一带 `[TouchSocket]` 前缀。
- `AddUnityDebugLogger()` 扩展支持给 `IRegistrator` / `LoggerGroup` 注册 Unity Console 日志器，可指定日志级别。
- `UnityLoggerBridge` 通过 `[RuntimeInitializeOnLoadMethod(BeforeSplashScreen)]` 自动初始化，监听 `Application.logMessageReceivedThreaded`、`TaskScheduler.UnobservedTaskException`、`UniTaskScheduler.UnobservedTaskException`，经 TouchSocket `FileLogger` 落盘。
- 默认落盘到 `Application.persistentDataPath/Logs/yyyy-MM-dd/`，单文件 1 MB 滚动，保留最近 3 天日志目录。
- 加入线程级重入保护，避免日志系统递归。
- 使用 `SubsystemRegistration` 重置静态状态，兼容关闭 Domain Reload 的 Editor Play Mode。
- `DefaultLogHelper` 的 `LogRedirection` 过滤列表加入桥接脚本，避免 Console 双击日志时跳到桥接层。

### 使用方式

```csharp
// TouchSocket 配置处注册 Unity Console 日志器，可选指定级别。
registrator.AddUnityDebugLogger();
registrator.AddUnityDebugLogger(LogLevel.Trace);

// Unity/TEngine 普通日志无需额外注册，UnityLoggerBridge 自动落盘。
```

### 关键文件

- `Assets/TEngine/Runtime/Core/Log/TouchSocketContainerUnityDebugLogger.cs`
- `Assets/TEngine/Runtime/Core/Log/TouchSocketUnityLoggerExtensions.cs`
- `Assets/TEngine/Runtime/Core/Log/UnityLoggerBridge.cs`
- `Assets/TEngine/Runtime/Core/Log/EarlyLogBuffer.cs`
- `Assets/TEngine/Runtime/Core/Log/Log.cs`（`EarlyInfo` / `EarlyWarning` / `EarlyError`）
- `Assets/TEngine/Runtime/Core/Utility/DefaultHelper/DefaultLogHelper.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-06-02-touchsocket-logger-summary.md`

## 早期日志缓冲 EarlyLog

### 背景

`UnityLoggerBridge` 在 `BeforeSplashScreen` 才订阅 `Application.logMessageReceivedThreaded` 并创建 `FileLogger`，而 Obfuz 静态密钥初始化等早期流程在 `AfterAssembliesLoaded` 阶段（更早于 `BeforeSplashScreen`）已经执行。这一阶段的 `Log.Info` / `Log.Fatal` 虽然会经 `Debug.Log` 进 Unity Console 和 Player.log，但不会被 TouchSocket 文件日志捕获，导致打包后 `persistentDataPath/Logs/yyyy-MM-dd/0000.log` 缺失启动早期关键日志（如 Obfuz 密钥初始化成功/失败记录）。

### 改动摘要

- 新增 `EarlyLogBuffer`：线程安全内存缓冲队列（`ConcurrentQueue`，容量上限 256 条，超限丢弃最旧条目）。
- `Log` 新增三个专用 API：`EarlyInfo(string)` / `EarlyWarning(string)` / `EarlyError(string)`。
  - 调用时先 `EarlyLogBuffer.Enqueue` 留副本，再走对应级别的常规 `Info` / `Warning` / `Fatal` 路径输出到 Console。
  - 均带 `[Conditional]` 编译符号，与同级别常规 API 对齐。
- `UnityLoggerBridge.Init()` 末尾调 `EarlyLogBuffer.Flush(s_FileLogger)`：把缓冲的早期日志补写到 TouchSocket 文件日志，每条加 `[Early]` 前缀标识（落盘时间晚于实际发生时间），然后关闭缓冲区。
- `Flush` 后 `s_Closed = true`，后续 `Enqueue` 直接丢弃（no-op），避免日志在文件里重复落盘——Init 之后调 `Log.EarlyInfo` 等价于 `Log.Info`。
- `UnityLoggerBridge.Shutdown()` 调 `EarlyLogBuffer.Reset()`：清空缓冲区并重新开放（`s_Closed = false`），兼容关闭 Domain Reload 的编辑器播放模式，防止上次会话残留。

### 行为对照

| 调用时机 | Console | Player.log | TouchSocket 文件 | Debugger 窗口 |
| --- | --- | --- | --- | --- |
| `UnityLoggerBridge.Init` **之前**（如 `AfterAssembliesLoaded`） | ✅ | ✅ | ✅（Flush 补写，带 `[Early]` 前缀） | ❌ |
| `UnityLoggerBridge.Init` **之后** | ✅ | ✅ | ✅（正常落盘） | ✅ |

> Debugger 窗口不显示启动早期日志：Debugger 模块激活晚于 `BeforeSplashScreen`，且 Early 缓冲只补写文件不重放 `Debug.Log`。启动早期日志查文件即可。

### 使用方式

```csharp
// 启动早期（UnityLoggerBridge.Init 之前）需要留痕的代码用 Early 系列：
Log.EarlyInfo("[Obfuz] Static secret key initialized (AfterAssembliesLoaded).");
Log.EarlyError("[Obfuz] Static secret key load failed.");

// Init 之后 Early 系列等价于普通 API，但仍建议正常运行期使用常规 API（语义更清晰）：
Log.Info("normal log");
```

### 设计约束

- **仅建议启动早期、低频使用**：缓冲区有 256 条上限，正常运行期日志频率高，不适合走缓冲。
- **不补到 Debugger**：Early 缓冲只补写到 `FileLogger`，不重放 `Debug.Log`，避免重复进 Console。
- **线程安全**：`Enqueue` / `Flush` / `Reset` 均线程安全，`s_Closed` 用 `volatile` 保证可见性。

### 关键文件

- `Assets/TEngine/Runtime/Core/Log/EarlyLogBuffer.cs`
- `Assets/TEngine/Runtime/Core/Log/Log.cs`（`EarlyInfo` / `EarlyWarning` / `EarlyError`）
- `Assets/TEngine/Runtime/Core/Log/UnityLoggerBridge.cs`（`Init` 末尾 Flush、`Shutdown` 里 Reset）

### 相关记录

- `UnityProject/conversation-summaries/2026-09-25-early-log-buffer-summary.md`

## Editor 打开日志目录菜单

### 背景

`UnityLoggerBridge` 落盘后，需要在编辑器中快速定位日志目录，减少手动查找 `persistentDataPath` 的成本。

### 改动摘要

- 在 `OpenFolderHelper` 新增菜单入口：`TEngine/Open Folder/Log Files Path`。
- 默认打开 `Application.persistentDataPath/Logs`。
- 目录尚未生成时回退打开 `Persistent Data Path`，避免路径不存在时报错。

### 关键文件

- `Assets/TEngine/Editor/Utility/OpenFolderHelper.cs`

## 日志查看工具 LogViewer

### 背景

Unity 落盘日志中包含富文本标签、堆栈和不同运行环境下的格式差异，直接用文本编辑器查看效率较低。

### 改动摘要

- 仓库根 `Tools/LogViewer/` 下新增独立桌面工具。
- 基于 Go + Wails v2 构建，编译为单体 `exe`。
- 支持打开或拖入 `.log` 文件查看。
- 支持 DEBUG / INFO / WARNING / ERROR 级别筛选。
- 支持关键词实时检索和高亮。
- 自动剥离 Unity 富文本标签，如 `<color>`、`<b>`。
- 自动清理 `[INFO] ►` 等冗余前缀。
- 堆栈默认折叠，点击展开。
- 兼容编辑器堆栈和打包后无路径堆栈格式。
- 提供一键打开默认日志目录 `%LOCALAPPDATA%\DefaultCompany\hotUnity\Logs`。

### 构建方式

```bash
go install github.com/wailsapp/wails/v2/cmd/wails@latest
cd Tools/LogViewer
build.bat
```

也可以使用：

```bash
./build.sh
wails build -clean
```

产物为：

```text
Tools/LogViewer/build/bin/LogViewer.exe
```

`frontend/wailsjs/` 为 Wails 构建时自动生成的绑定，已被 `.gitignore` 忽略。clone 后首次 `wails build` 会重新生成。

### 关键文件

- `Tools/LogViewer/main.go`
- `Tools/LogViewer/parser/parser.go`
- `Tools/LogViewer/frontend/index.html`
- `Tools/LogViewer/frontend/style.css`
- `Tools/LogViewer/frontend/app.js`

### 相关记录

- `UnityProject/conversation-summaries/2026-06-03-logviewer-tool-summary.md`
