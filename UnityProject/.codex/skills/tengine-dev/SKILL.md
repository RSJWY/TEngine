---
name: tengine-dev
description: TEngine Unity fork 开发指导。用于 TEngine、UIWindow/UIWidget、GameEvent、GameModule、资源加载、YooAsset 3、HybridCLR、CodePackage、ArchiveFileBuildPipeline、Obfuz、运行时配置、场景流程、ScreenModule、GameObjectPool、AnimModule、FrameAnim、DataBinding、客户端存档、打包发布和相关 Editor 工具开发。项目默认不使用 Luban，只有用户明确要求配置表时才读取 Luban 规范。
---

# TEngine Fork 开发指导

本 skill 面向当前仓库的 TEngine fork，而不是只描述上游 TEngine。reference 已内置必要的 fork 知识，不依赖仓库外层 `Books/Fork/`。

## 使用原则

1. 先按下表读取与任务直接相关的 reference，不要全量加载。
2. reference 与代码冲突时，以当前代码签名和序列化配置为准，并同步修正文档。
3. fork 专属功能必须读取对应的 `fork-*.md`，不能按上游 TEngine 经验猜测。
4. 当前项目默认不使用 Luban。只有用户明确要求配置表、数据表或 Luban 时才读取 `luban-config.md`。

## 核心红线

1. **异步优先**：IO 和资源操作使用 `UniTask`，禁止用 Coroutine 替代框架异步 API。
2. **模块访问**：热更业务通过 `GameModule.XXX` 访问模块，不直接调用 `ModuleSystem.GetModule<T>()`。
3. **资源生命周期**：`LoadAssetAsync` 与 `UnloadAsset` 成对；GameObject 使用 `LoadGameObjectAsync` 或 `GameModule.GameObjectPool`。
4. **热更边界**：`Assets/GameScripts/Procedure/` 和 Assembly-CSharp 不热更；`Assets/GameScripts/HotFix/` 属于热更代码。
5. **事件解耦**：模块间使用 `GameEvent`；UI 内监听使用 `AddUIEvent` 自动清理。
6. **YooAsset 版本**：使用 YooAsset 3.x 原生 API，禁止引入 `YOOASSET_LEGACY_API` 或恢复 2.x 兼容层。
7. **Editor 隔离**：Editor 脚本放 `Assets/Editor/` 或 `Assets/TEngine/Editor/`，不要让热更程序集引用 `UnityEditor`。
8. **Luban 边界**：不要为普通运行时配置或 UI 功能主动引入 Luban；轻量配置优先使用 `GameModule.Config`。

## 文档路由

| 任务类型 | 必读文档 | 按需补充 |
| --- | --- | --- |
| UI 窗口、Widget、生命周期 | [ui-lifecycle.md](references/ui-lifecycle.md) | [ui-patterns.md](references/ui-patterns.md)、[fork-gameplay-ui.md](references/fork-gameplay-ui.md) |
| 事件系统 | [event-system.md](references/event-system.md) | [event-antipatterns.md](references/event-antipatterns.md) |
| 资源加载与释放 | [resource-api.md](references/resource-api.md) | [resource-patterns.md](references/resource-patterns.md)、[fork-resource-hotupdate.md](references/fork-resource-hotupdate.md) |
| HybridCLR、CodePackage、AOT、Obfuz | [fork-resource-hotupdate.md](references/fork-resource-hotupdate.md) | [hotfix-workflow.md](references/hotfix-workflow.md) |
| 模块 API、运行时配置、对象池、动画、场景、窗口 | [fork-runtime-modules.md](references/fork-runtime-modules.md) | [modules.md](references/modules.md) |
| DataBinding、存档、帧动画、UI 扩展、Utility | [fork-gameplay-ui.md](references/fork-gameplay-ui.md) | [ui-patterns.md](references/ui-patterns.md) |
| 构建窗口、发布整理、Inno Setup、场景枚举、调试工具 | [fork-editor-workflows.md](references/fork-editor-workflows.md) | [fork-resource-hotupdate.md](references/fork-resource-hotupdate.md) |
| FSM、Procedure | [fsm-patterns.md](references/fsm-patterns.md) | [hotfix-workflow.md](references/hotfix-workflow.md) |
| 项目结构与程序集 | [architecture.md](references/architecture.md) | [fork-runtime-modules.md](references/fork-runtime-modules.md) |
| 命名与代码规范 | [naming-rules.md](references/naming-rules.md) | - |
| 故障排查 | [troubleshooting.md](references/troubleshooting.md) | 对应主题 reference |
| Luban 配置表（仅明确要求时） | [luban-config.md](references/luban-config.md) | - |
| MCP 场景、GameObject、UI、脚本、Editor | [mcp-tools.md](references/mcp-tools.md) | - |
| MCP 材质、Shader、动画、VFX | [mcp-visual.md](references/mcp-visual.md) | - |
