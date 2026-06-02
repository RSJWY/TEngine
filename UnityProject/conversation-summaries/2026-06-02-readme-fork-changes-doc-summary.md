# 2026-06-02 README 定制改动章节 + Books 子文档会话总结

## 背景

承接同日上一会话（`2026-06-02-jsonconfigmodule-comments-and-commit-summary.md`）。用户要求：

1. 在 README 开头汇总本 fork 相对上游做的所有改动。
2. 把这套清单写进项目记忆，约定后续持续更新。
3. 进一步参照原项目“README 主文档 + 子 md”的形式，补一个详细子说明文档。

## 目录结构确认

- git 根目录 = 完整项目根 = `E:/WorkSpace/TEngine`（**不是** `UnityProject`，后者只是 Unity 工程子目录）。
- README 在根目录：`E:/WorkSpace/TEngine/README.md`。
- 原项目子文档统一放在 `Books/` 下（如 `Books/3-6-配置表模块.md`），README 用相对链接引用。本次沿用该约定。

## 改动来源

通过 `git log` 区分本 fork 提交与上游，并通读 `UnityProject/conversation-summaries/` 各篇总结，归纳出三大主题的定制能力，内容均来自提交历史与既有总结，未编造。

## 已完成改动

### 1. README 新增「🛠️ 本 Fork 的定制改动」章节

- 位置：徽章之后、「📖 简介」之前。
- 「📚 目录」中加入锚点入口 `#️-本-fork-的定制改动`。
- 按主题归类、最新在前：
  - 运行时配置：JsonConfigModule、DeployConfig 部署地址覆盖
  - 热更新：多包架构(CodePackage)、XXTEA 代码包加密、版本确认下载流程、AOT 元数据热更清单、PlayerPrefs 版本清理工具
  - 资源打包：按包构建管线、发布整理流程
- 提交：`050369d7 README 增加本 Fork 定制改动章节`（仅 README.md，27 行新增）。

### 2. 新增 Books 子文档

- 文件：`Books/Fork-定制改动说明.md`
- 风格参照原 Books 文档：emoji 分节、目录锚点、代码块、关键文件清单、指向 conversation-summaries 的引用。
- 每项含：特性说明、使用方式/配置示例、关键文件路径、对应总结文件名。
- README 章节末尾引用改为链接到该子文档，形成「概述在 README、详细在 Books」的两级结构。
- 提交：`1815cb58 新增 Fork 定制改动说明子文档`（README.md + 子文档，224 insertions）。

### 3. 更新项目记忆

- 文件：`C:\Users\...\.claude\projects\E--WorkSpace-TEngine\memory\readme-custom-changes-section.md`，并登记到 `MEMORY.md`。
- 记录要点：
  - git 根在上一级 `E:/WorkSpace/TEngine`，非 UnityProject。
  - 定制章节位置 + 子文档位置。
  - **维护约定：每完成新改动须两处同步** —— ① `Books/Fork-定制改动说明.md` 追加详细条目；② README 章节追加一行精简概述；并在 conversation-summaries 留总结。

## 推送结果

均已同步到 `origin/main`（`git@github.com:RSJWY/TEngine.git`）：

```text
1815cb58 新增 Fork 定制改动说明子文档
050369d7 README 增加本 Fork 定制改动章节
700fd9c1 完善 JsonConfigModule 注释
86def98f 支持部署配置覆盖热更地址
```

## 记忆 vs 总结（本会话澄清）

用户询问二者区别，已说明：

- **记忆**（`.claude/projects/.../memory/`）：仓库外、不进 git、跨会话给 AI 用的长期规则/偏好。
- **总结**（`UnityProject/conversation-summaries/`）：仓库内、进 git、面向人和项目追溯的单次会话流水记录。

## 后续建议

1. 新增定制功能时按记忆约定两处同步（Books 子文档 + README 章节），并补会话总结。
2. 仍未提交的无关改动（场景调试设置、BattleMainUI.prefab、HybridCLRSettings、AssetRaw/DLL 产物、本地 settings）保持不动，除非用户明确要求。
