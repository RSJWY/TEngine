---
name: fork-docs
description: 维护本仓库 fork 定制改动文档。用于更新 README 的 fork 概览、Books/Fork-定制改动说明.md、Books/Fork/ 专题文档、Fork CHANGELOG，或记录新的 fork 定制功能、工具、热更新、资源打包、运行时配置、场景、窗口、日志相关改动。
---

# Fork Docs

用于维护本仓库的 fork 定制改动说明。

## 文档分层

fork 文档固定分三层：

1. 根目录 `README.md`：只放公开概览。
2. `Books/Fork-定制改动说明.md`：只做旧链接兼容索引。
3. `Books/Fork/`：放详细专题文档和时间线。

不要把长功能说明重新塞回根 `README.md` 或 `Books/Fork-定制改动说明.md`。

## README 写法

只有新增大方向，或现有方向含义明显变化时，才更新 `../README.md`。

`🛠️ 本 Fork 的定制改动` 章节只保留：

- 一段很短的 fork 定位说明。
- 一个紧凑的主题表格。
- 指向 `Books/Fork/README.md` 和 `Books/Fork/CHANGELOG.md` 的链接。

不要在 README 里写：

- 单个功能的实现细节。
- 很长的功能 bullet list。
- 代码示例。
- 排查过程。
- conversation summary 链接。

## 旧入口写法

`../Books/Fork-定制改动说明.md` 必须保持为稳定入口。

它只应该包含：

- `Fork/README.md` 和 `Fork/CHANGELOG.md` 链接。
- 指向各个 `Fork/*.md` 专题文档的表格。
- 简短维护规则。

不要把它再次扩展成详细说明长文。

## 专题文档归类

详细内容放到 `../Books/Fork/`。

按主题使用这些文件：

- `logging.md`：TouchSocket 日志、Unity 日志落盘、LogViewer。
- `event-system.md`：`GameEvent`、事件分发器定制。
- `runtime-config.md`：`JsonConfigModule`、`DeployConfig`、TOML、轻量配置。
- `hot-update.md`：HybridCLR、`CodePackage`、AOT 元数据、更新流程、热更新 PlayerPrefs 工具。
- `resource-build.md`：YooAsset 构建管线、发布整理、打包窗口。
- `scene-system.md`：DynamicSpawn、场景加载进度、`GameSceneModule`。
- `window-management.md`：`ScreenModule`、多显示器窗口控制。

如果新改动不适合现有专题，只有在它会长期成为一个独立文档类别时，才新增专题文件。新增后同步更新两个索引页。

## 专题条目模板

新增功能说明时，使用这个结构：

```markdown
## 功能名

### 背景

为什么这个 fork 需要这项改动。

### 改动摘要

- 改了什么。
- 行为有什么变化。
- 哪些东西明确保持不变。

### 使用方式

怎么使用。只有确实有帮助时才放短代码或配置示例。

### 注意事项

兼容性限制、生命周期约束、平台限制、常见坑点。

### 关键文件

- `path/to/file.cs`

### 相关记录

- `UnityProject/conversation-summaries/yyyy-mm-dd-summary.md`
```

已有中文文档继续使用中文标题。除非所在文件本身已经是英文，否则不要混用英文标题。

## CHANGELOG 写法

每次记录 fork 改动时，都更新 `../Books/Fork/CHANGELOG.md`。

按日期倒序分组：

```markdown
## YYYY-MM-DD

- 面向读者的简短改动摘要。
```

每条 bullet 保持简短。设计细节写进专题文档，不写进 changelog。

## 文字风格

- 优先使用简洁中文。
- API、文件名、配置键、菜单路径、类名使用反引号。
- README 和索引页要适合快速扫读。
- 详细文档聚焦背景、行为、使用、约束、关键文件。
- 只在专题文档或 changelog 中按需链接 `UnityProject/conversation-summaries/`。
- 不要在 README、索引页、专题文档之间重复同一段详细说明。

## 更新流程

记录新的 fork 改动时：

1. 先判断应进入 `Books/Fork/` 下哪个专题文件。
2. 按专题条目模板新增或更新功能说明。
3. 在 `Books/Fork/CHANGELOG.md` 追加日期记录。
4. 只有主题列表或近期重点变化时，才更新 `Books/Fork/README.md`。
5. 只有高层主题表变化时，才更新根 `README.md`。
6. 除非新增或重命名专题文件，否则保持 `Books/Fork-定制改动说明.md` 只是索引。
