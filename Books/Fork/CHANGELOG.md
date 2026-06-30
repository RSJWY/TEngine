# Fork 改动时间线

本文件按时间记录 fork 中的重要定制改动。专题设计和使用说明见同目录下对应文档。

## 2026-06-30

- 热更清单加载按 PlayMode 分流：Editor/Offline 只读本地包，Host/Web 保留远端失败回退。
- `JsonConfigModule` 通用化为 `RuntimeConfigModule`，默认清单和轻量配置切换为 TOML。
- 场景加载进度从 `SwitchUI` / `LoadingUI` 下沉到 `GameSceneModule`，UI 降为纯展示。
- 新增 `DisplayProgress`，由 `SwitchUI` 每帧读取并渲染进度条和百分比。
- 场景加载终结顺序调整为 `回调 -> 关加载页 -> OnSceneReady`，对齐 `DynamicSceneSpawner` 契约。
- Fork 改动说明文档改为分层结构：README 概览、索引页、专题文档和时间线。

## 2026-06-27

- `HangarSceneSpawner` 通用化为 `SpawnPointSceneSpawner`。
- `HangarManager` 调整为 `ExampleSceneGameManager` 示例脚本。
- `BuildPipelineWindow` 迁移为 `OdinEditorWindow`。
- 打包工具增加构建流程预览、资源包表格延迟落盘、状态缓存和日志刷新节流。

## 2026-06-04

- `DeployConfig` 新增 `DebuggerActiveWindow` 字段。
- `Debugger` 抽出 `ApplyActiveWindowType`，支持部署配置二次覆盖调试器激活策略。

## 2026-06-03

- 新增 `Tools/LogViewer/` 桌面日志查看工具。
- 支持日志打开、拖入、级别筛选、关键词高亮、富文本标签剥离和堆栈折叠。

## 2026-06-02

- 新增 TouchSocket 与 Unity Console 的日志桥接。
- 新增 `UnityLoggerBridge`，统一落盘 Unity、Task、UniTask 日志与未观察异常。
- 新增 `JsonConfigModule`，用于从 `StreamingAssets/Configs` 加载轻量 JSON 配置。
- 新增 `DeployConfig`，支持打包后覆盖热更资源服务器地址。

## 2026-06-01

- 新增 `TEngine/HotUpdate/Package Version PlayerPrefs` 工具。
- 支持按 `RuntimePackages` 的 `VersionKey` 清理热更新版本记录。

## 2026-05-30

- 新增按包构建管线，资源包可分别选择 YooAsset 构建管线。
- 新增发布整理流程，统一发布目录与运行时远端平台名。
- 新增代码包 XXTEA 加密，仅对 `CodePackage` 等指定包应用。
- 恢复版本确认与下载流程：有本地版本可取消，无本地版本强制更新。
- 增强部署配置和运行时配置管理流程。

## 2026-05-28

- 热更 DLL 从 `DefaultPackage` 拆分到独立 `CodePackage`。
- `UpdateSetting` 引入 `RuntimePackages`，运行时初始化、清单更新和下载器创建改为按包执行。

## 未单独标注日期

- 新增 `AOTMetadataManifest`，支持 AOT 元数据清单随 `CodePackage` 热更。
- 增加 AOT 元数据打包期校验，缺少 `AOTGenericReferences.PatchedAOTAssemblyList` 必需程序集时中断构建。
- 新增 `Utility.Toml` 和默认 `TomlynTomlHelper`，提供 TOML 序列化门面。
- 新增 `ScreenModule`，支持 Windows Standalone 下控制多显示器窗口位置、大小、置顶和无边框。
- 新增 `GameEvent.RemoveAllListeners`，支持按事件 ID 批量移除监听。
