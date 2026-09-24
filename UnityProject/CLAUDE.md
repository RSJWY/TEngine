# CLAUDE.md

本文件仅作为 Claude Code 的兼容入口。

**所有工作规范以 [AGENTS.md](./AGENTS.md) 为准。**

## 技能与扩展（Claude Code 需手动读取）

`.codex/skills/` 是本项目技能唯一源，不做第二套目录，Claude Code 不会自动发现。
执行任务前先读取对应 `SKILL.md`，再按需读取其 `references/`：

| 任务 | 技能入口 |
|---|---|
| TEngine 框架、UIWindow、事件、资源、HybridCLR | `.codex/skills/tengine-dev/SKILL.md` |
| Unity Editor/Player 查询、资产操作、测试、构建预检 | `.codex/skills/unity-cli/SKILL.md` |
| Luban 配置结构、数据、导出及分片（本项目默认未启用，用户主动要求时才使用） | `.codex/skills/luban-dev/SKILL.md` |
| HTML/UI-DSL 转 UGUI Prefab | `.codex/skills/html-to-ugui/SKILL.md` |
| 本 Fork 定制改动文档维护（README 概览、Books/Fork、CHANGELOG） | `.codex/skills/fork-docs/SKILL.md` |

同理：子代理定义在 `.codex/agents/*.md`，命令流程说明在 `.codex/commands/**/*.md`，
Claude Code 下均不会出现在内置菜单，需要时直接读取文件并按其内容执行。
