# TEngine Log Viewer

TEngine Unity 日志查看工具。把 `UnityLoggerBridge` 落盘的 `.log` 文件以图形界面展示，支持级别筛选、关键词检索、堆栈折叠。

基于 Go + [Wails v2](https://wails.io) 构建，编译为单体 exe，无需运行时依赖（Windows 10/11 自带 WebView2）。

## 功能

- 打开 / 拖入 `.log` 文件查看
- 按日志级别筛选（DEBUG / INFO / WARNING / ERROR）
- 关键词实时检索并高亮（匹配消息与堆栈）
- 自动剥离 Unity 富文本标签（`<color>`、`<b>` 等）
- 堆栈默认折叠，点击单条日志展开
- 兼容编辑器与打包后两种堆栈格式
- 一键打开默认日志目录（`%LOCALAPPDATA%\DefaultCompany\hotUnity\Logs`）

## 日志格式

解析 `UnityLoggerBridge` 输出的格式：

```
2026-06-03 09:15:04.4188 | Debug | <富文本>消息内容</富文本>
堆栈行 1
堆栈行 2
（空行分隔下一条）
```

- 编辑器堆栈带路径行号：`TEngine.Xxx:Method (...) (at Assets/.../File.cs:123)`
- 打包后堆栈无路径：`TEngine.Xxx:Method(Type, Type)`

两种格式均可正确解析与分组。

## 构建

需要先安装 Go 1.21+ 与 Wails CLI：

```bash
go install github.com/wailsapp/wails/v2/cmd/wails@latest
```

然后在本目录执行：

```bash
# Windows
build.bat

# Linux / macOS
./build.sh
```

或直接：

```bash
wails build -clean
```

产物位于 `build/bin/LogViewer.exe`。

> 注意：`frontend/wailsjs/` 是 Wails 构建时自动生成的绑定代码，已被 `.gitignore` 忽略。首次 clone 后直接执行 `wails build` 即可重新生成，无需手动创建。

## 开发

```bash
wails dev
```

热重载模式，修改前端代码即时生效。

## 目录结构

```
LogViewer/
├── main.go              # Wails 应用入口、后端 API（打开文件/拖拽/默认目录）
├── parser/parser.go     # 日志解析核心（剥离富文本、堆栈分组、过滤）
├── frontend/
│   ├── index.html       # 界面结构
│   ├── style.css        # 样式（深色主题）
│   └── app.js           # 前端逻辑（筛选/检索/渲染）
├── build/               # 构建资源（图标、manifest）
├── wails.json           # Wails 项目配置
├── build.bat / build.sh # 一键构建脚本
└── go.mod
```
