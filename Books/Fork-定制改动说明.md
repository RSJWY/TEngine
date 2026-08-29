# 本 Fork 定制改动说明

> 本文件保留为历史入口。详细内容已拆分到 `Books/Fork/` 目录，避免后续改动全部堆在一个长文档里。

## 快速入口

- [Fork 定制改动总览](Fork/README.md)
- [Fork 改动时间线](Fork/CHANGELOG.md)

## 专题文档

| 主题 | 内容 | 文档 |
| --- | --- | --- |
| YooAsset 3.0.5 迁移 | 无兼容层迁移、运行模式修复、ArchiveFileBuildPipeline 与加密归档加载 | [yooasset-3-migration.md](Fork/yooasset-3-migration.md) |
| 日志系统 | TouchSocket 日志桥接、Unity 日志落盘、LogViewer | [logging.md](Fork/logging.md) |
| 事件系统 | 按事件 ID 批量移除监听 | [event-system.md](Fork/event-system.md) |
| 数据绑定 | 纯数据 DataBinding 运行时、生成器和 Odin 面板 | [data-binding.md](Fork/data-binding.md) |
| 运行时配置 | `JsonConfigModule`、`DeployConfig`、`Utility.Toml` | [runtime-config.md](Fork/runtime-config.md) |
| 热更新 | `CodePackage`、归档二进制加载、AOT 元数据、版本确认流程 | [hot-update.md](Fork/hot-update.md) |
| 资源打包 | 按包构建、ArchiveFile 管线、发布整理、打包工具 Odin 化 | [resource-build.md](Fork/resource-build.md) |
| 场景系统 | DynamicSpawn 通用化、加载进度下沉到 `GameSceneModule` | [scene-system.md](Fork/scene-system.md) |
| 窗口管理 | Windows Standalone 多显示器窗口布局控制 | [window-management.md](Fork/window-management.md) |
| 代码混淆 | Obfuz 接入、dnlib 冲突解决、本地包同步脚本 | [obfuscation.md](Fork/obfuscation.md) |
| 运行时工具 | `GameTickWatcher` 逻辑计时器（独立 `RuntimeTools` 程序集） | [runtime-tools.md](Fork/runtime-tools.md) |
| 计时器模块 | `TimerModule` 链表化、坏帧安全、限定循环次数 | [timer-module.md](Fork/timer-module.md) |
| 存档与数据中心 | `ClientSaveDataMgr` 存档框架、`DataCenterSys` 玩家数据中枢 | [save-data.md](Fork/save-data.md) |
| UI 组件扩展 | `UIButton`/`UIImage`/`UIText`/`RichTextItem` + `ListPool` 公共化 | [ui-expansion.md](Fork/ui-expansion.md) |
| 帧动画模块 | 序列帧动画（场景版+UI版+RawImage版），手写替代 SourceGenerator | [frame-anim.md](Fork/frame-anim.md) |
| GameObject 对象池 | 基于 YooAsset location 的异步实例化池，预热/回收/自动销毁 | [game-object-pool.md](Fork/game-object-pool.md) |
| 动画模块 | 基于 PlayableGraph 的代码驱动 3D 动画图，多层级混合/权重过渡 | [anim-module.md](Fork/anim-module.md) |

## 维护规则

新增 fork 改动时：

1. 在对应专题文档中补充设计、使用方式、关键文件和注意事项。
2. 在 [Fork 改动时间线](Fork/CHANGELOG.md) 追加日期记录。
3. 只有出现新的大方向时，才更新仓库根目录 `README.md` 的 fork 概览。

更细的开发和排查过程仍记录在 `UnityProject/conversation-summaries/` 下对应日期的会话总结中。
