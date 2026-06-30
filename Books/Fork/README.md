# Fork 定制改动总览

本目录记录当前 fork 相对上游 [ALEXTANGXIAO/TEngine](https://github.com/ALEXTANGXIAO/TEngine) 的定制改动。

文档按用途分层维护：

- `README.md`：只放总览和导航，适合快速了解 fork 改了哪些方向。
- `CHANGELOG.md`：按时间记录新增、调整和修复，适合查看最近变更。
- 专题文档：按系统归档详细设计、使用方式、关键文件和注意事项。

## 改动索引

| 主题 | 说明 | 详细文档 |
| --- | --- | --- |
| 日志系统 | TouchSocket 日志桥接、Unity 日志落盘、日志查看工具 | [logging.md](logging.md) |
| 事件系统 | 按事件 ID 批量移除监听 | [event-system.md](event-system.md) |
| 运行时配置 | JsonConfig、DeployConfig、TOML 序列化 | [runtime-config.md](runtime-config.md) |
| 热更新 | CodePackage、XXTEA、版本确认、AOT 元数据 | [hot-update.md](hot-update.md) |
| 资源打包 | 按包构建、发布整理、打包工具优化 | [resource-build.md](resource-build.md) |
| 场景系统 | DynamicSpawn 通用化、GameSceneModule 进度下沉 | [scene-system.md](scene-system.md) |
| 窗口管理 | Windows Standalone 窗口布局控制 | [window-management.md](window-management.md) |

## 最近重点

- 日志系统新增 Unity/Task/UniTask 到 TouchSocket `FileLogger` 的统一落盘链路，并补充独立 LogViewer 工具。
- 运行时配置新增 `JsonConfigModule`、`DeployConfig` 和 `Utility.Toml`，用于处理轻量部署配置和可读配置文本。
- 热更新和资源打包侧补强 CodePackage、AOT 元数据清单校验、按包构建管线和打包工具体验。
- 场景系统将加载进度状态机从 UI 下沉到 `GameSceneModule`，并将 DynamicSpawn 示例脚本通用化。
- Windows Standalone 新增 `ScreenModule`，用于控制窗口位置、尺寸、置顶和无边框。

## 维护规则

新增 fork 改动时，优先更新对应专题文档，并在 [CHANGELOG.md](CHANGELOG.md) 追加时间线记录。

只有出现新的大方向时，才更新仓库根目录 `README.md` 的 fork 概览。原 `Books/Fork-定制改动说明.md` 保留为兼容入口，内容指向本目录。
