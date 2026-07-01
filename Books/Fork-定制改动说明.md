# 本 Fork 定制改动说明

> 本文件保留为历史入口。详细内容已拆分到 `Books/Fork/` 目录，避免后续改动全部堆在一个长文档里。

## 快速入口

- [Fork 定制改动总览](Fork/README.md)
- [Fork 改动时间线](Fork/CHANGELOG.md)

## 专题文档

| 主题 | 内容 | 文档 |
| --- | --- | --- |
| 日志系统 | TouchSocket 日志桥接、Unity 日志落盘、LogViewer | [logging.md](Fork/logging.md) |
| 事件系统 | 按事件 ID 批量移除监听 | [event-system.md](Fork/event-system.md) |
| 数据绑定 | 纯数据 DataBinding 运行时、生成器和 Odin 面板 | [data-binding.md](Fork/data-binding.md) |
| 运行时配置 | `JsonConfigModule`、`DeployConfig`、`Utility.Toml` | [runtime-config.md](Fork/runtime-config.md) |
| 热更新 | `CodePackage`、XXTEA、AOT 元数据、版本确认流程 | [hot-update.md](Fork/hot-update.md) |
| 资源打包 | 按包构建、发布整理、打包工具 Odin 化 | [resource-build.md](Fork/resource-build.md) |
| 场景系统 | DynamicSpawn 通用化、加载进度下沉到 `GameSceneModule` | [scene-system.md](Fork/scene-system.md) |
| 窗口管理 | Windows Standalone 多显示器窗口布局控制 | [window-management.md](Fork/window-management.md) |

## 维护规则

新增 fork 改动时：

1. 在对应专题文档中补充设计、使用方式、关键文件和注意事项。
2. 在 [Fork 改动时间线](Fork/CHANGELOG.md) 追加日期记录。
3. 只有出现新的大方向时，才更新仓库根目录 `README.md` 的 fork 概览。

更细的开发和排查过程仍记录在 `UnityProject/conversation-summaries/` 下对应日期的会话总结中。
