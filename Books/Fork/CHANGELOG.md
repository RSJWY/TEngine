# Fork 改动时间线

本文件按时间记录 fork 中的重要定制改动。专题设计和使用说明见同目录下对应文档。

## 2026-08-16

- `RuntimeConfigModule` 加载容错：单个配置失败（缺失、重名、格式不支持）只记录错误并跳过，不再中断整体加载；`IsLoaded` 改为"一次加载流程完成"语义，下游统一按 `TryGet` 兜底默认值。
- 配置名支持 `Configs` 相对子目录路径：缓存键保留目录、只去扩展名（如 `sub/Foo`），不同子目录同名文件不再冲突。
- 配置扩展名格式校验前置到读文件之前，避免读完内容才发现格式不支持。

## 2026-08-15

- 接入 Obfuz 代码混淆（含 `obfuz4hybridclr` 扩展）：HybridCLR 与 Obfuz 转为 `Packages/` 本地包以解决双 dnlib 冲突（移除 HybridCLR 的 dnlib，全项目共用 Obfuz 定制 dnlib），新增 `Packages/sync-*-local` 一键同步脚本，支持指定版本、自动解析最新稳定 tag 和 gitee 镜像切换。

## 2026-08-13

- `CodePackage` 资源目录拆分为 `AOT/`、`HotDll/`、`PDB/` 三子目录：`UpdateSetting` 新增可配置子路径字段，`BuildDLLCommand` 拷贝目标与 manifest 路径同步适配，新增 pdb 符号拷贝逻辑，YooAsset 收集器三分组各收集对应子目录，`.gitignore` 扩展忽略规则。

## 2026-08-07

- 修复 `GameSceneModule` 阶段 1 固定 5s 超时误杀大场景冷启动慢加载：改为停滞超时 60s + 绝对超时 180s 双门槛，冷启动 progress=0 期间不累计停滞，超时日志补全排查字段。
- 新增场景枚举自动生成工具 `SceneEnumConfig`：扫描场景资源自动生成 `SceneType`/`SceneConstName`/`SceneTypeMapping`，替代手工同步 4 处；GUID 追踪改名、枚举值顺序稳定、Odin 表格编辑，含重复 key 三层防护。
- `SceneEnumConfig` 增强：同步目录联动 YooAsset Scenes Group（避免配置脱节）、场景增删改自动提示同步、生成前校验场景在收集范围内。

## 2026-07-01

- 新增纯数据 DataBinding 运行时与 Editor 生成器，菜单和 Odin 面板统一中文化。
- DataBinding 生成代码补充公开同步函数注释，生成器面板支持单模型重新生成。
- 补充 DataBinding Attribute 用途、生成行为和限制说明。
- 移除 `DataBindFormat`，DataBinding 只同步源字段原值，展示格式由订阅方或 UI 层决定。

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
