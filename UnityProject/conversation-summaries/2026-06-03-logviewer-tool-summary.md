# LogViewer 日志查看工具会话总结

日期：2026-06-03

## 背景

`UnityLoggerBridge` 落盘的 `.log` 文件混杂 Unity 富文本标签（`<color>` / `<b>`）与大段堆栈，直接阅读很难受。用户希望做一个图形界面工具：可选择或拖入日志文件，在界面上查看、检索，并打包为单体 exe。工具放在仓库根 `Tools/`（不进 Unity 工程）。

## 技术选型

- 经讨论排除 TUI（部分终端支持差）与 HTML 报告（使用麻烦），也评估并放弃了 Python（PyInstaller 产物大、启动慢、易误报）。
- 最终采用 **Go + Wails v2**：`go build` 出单体 exe（约 11MB），无运行时依赖，Windows 10/11 自带 WebView2。
- 环境已装 Go 1.26；本次额外 `go install` 了 Wails CLI v2.12.0。

## 实现

目录：`Tools/LogViewer/`

- `main.go`：Wails 应用入口与后端 API。
  - `LoadLogFile` 解析文件、`OpenFileDialog` 弹原生文件对话框、`OpenDefaultLogDir` 打开默认日志目录、`GetDefaultLogPath` 返回 `%LOCALAPPDATA%\DefaultCompany\hotUnity\Logs`。
  - 启用 `options.DragAndDrop{EnableFileDrop: true}`，`startup` 中用 `wailsruntime.OnFileDrop` 监听拖拽并通过 `EventsEmit("file:dropped", path)` 通知前端。
  - 标准库 `runtime` 用别名 `goruntime` 导入，避免与 Wails `runtime` 包冲突。
- `parser/parser.go`：日志解析核心。
  - 头部正则 `^(时间戳)\s*\|\s*(级别)\s*\|\s*(消息)$`，空行分隔条目。
  - `stripRichText` 去 `<...>` 标签；`stripRedundantPrefix` 去 `[INFO] ►` 标记与 `- ` 连接符。
  - `normalizeLevel` 归一级别（Trace→DEBUG、Information→INFO、Warn→WARNING、Fatal/Critical→ERROR）。
  - 非头部行归入 `Stack`，兼容编辑器（`... (at Assets/.../X.cs:123)`）与打包后（`X:Method(Type, Type)`）两种格式。
- `frontend/`：纯静态前端，深色主题。
  - `index.html` 用 `<script type="module" src="app.js">` 引入。
  - `app.js` 为 ES module，`import` Wails 生成的 `wailsjs/go/main/App.js` 与 `wailsjs/runtime/runtime.js`；实现级别筛选、关键词防抖检索高亮、堆栈点击折叠、`DocumentFragment` 批量渲染。

## 关键修正（踩坑）

1. 标准库 `runtime` 无 `Getenv`，应为 `os.Getenv`。
2. `parser.go` 误引入未使用的 `time`，编译失败。
3. 前端最初用经典 `<script src>` 加载 Wails 绑定（实为 ES module）→ 改 `type="module"` 并用 `import`。
4. 文件对话框不是前端 `window.runtime.OpenFileDialog`，而是后端 `runtime.OpenFileDialog`，改为暴露后端方法。
5. 浏览器拖拽拿不到 `file.path`，改用 Wails `OnFileDrop` + `EnableFileDrop`。
6. `OpenFileDialog` 的 `DefaultDirectory` 不存在会报错，先 `os.Stat` 判断，不存在传空。
7. 前缀清理顺序问题导致消息残留 `- `，重排 `stripRedundantPrefix` 逻辑。

## 验证

- `wails build -clean` 编译通过，产物 `build/bin/LogViewer.exe`（约 11MB）。
- 用真实日志 `2026-06-03/0000.log`（同时含编辑器与打包格式）解析：137 条，级别统计 DEBUG 104 / WARNING 25 / ERROR 8，首条与末条消息干净、堆栈正确分组。

## .gitignore

`Tools/LogViewer/.gitignore` 忽略：`build/bin/`（exe 产物）、`frontend/wailsjs/`（Wails 自动生成绑定）、`node_modules/`、`parsetest_main.go`、系统文件与 `*.log`。

纳入版本控制：源码（main.go / parser.go / frontend 三件套）、`go.mod`/`go.sum`、`wails.json`、构建脚本、`build/appicon.png` 与 `build/windows/`（图标与 manifest）。

> clone 后首次 `wails build` 会自动重新生成 `frontend/wailsjs/`，无需手动创建。

## 文档同步

按 README 维护规则两处同步：

- `README.md`「🧾 日志系统」新增 LogViewer 一行概述。
- `Books/Fork-定制改动说明.md`「日志系统」新增「3. 日志查看工具 LogViewer」章节并更新目录锚点。
