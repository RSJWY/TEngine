# AGENTS.md — 项目工作目录入口

> **本目录即项目根目录。** 上级仓库目录在实际使用中会被删除、不做保留，仓库级公共说明仅作为 Wiki 参考。
> AI 工具在本目录下工作时，是在**为实际项目编写业务代码**，而非修改 TEngine 框架本身。
> 框架级的改动（TEngine.Runtime、TEngine.Editor、内置模块实现）除非用户明确声明"改框架"，否则一律视为业务侧消费框架，不主动修改框架源码。

## 工程事实

- 项目根是本目录，CLI 为 `Tools/unity.exe`，解决方案为 `UnityProject.sln`。
- 配置源位于仓库根 `Configs/GameConfig`，不是 Unity 项目内的 `Configs`。
- Unity 版本取 `ProjectSettings/ProjectVersion.txt`；Pipeline 能力以本项目注册命令及包内 `Documentation~` 为准。
- `.codex/skills` 是技能唯一源。不要复制到第二套目录，也不要假设所有 Codex 客户端都会自动发现此目录。

## AI 协助开发声明

**前提：由你提交代码时触发。**
凡使用了 AI 辅助生成或修改的代码，必须在 **commit 信息结尾**（以及对应 **PR 描述**中）按以下格式注明所使用的工具环境和模型：

```
assisted-by：{agent_name}：{model}
```

- `{agent_name}`：使用的 AI 编码工具/环境，如 `opencode`、`codex`、`cursor`
- `{model}`：实际使用的模型（含供应商，格式可参考 `供应商/模型`），如 `Zhipu/GLM-5.3[Max]`

示例：

```
git commit -m "feat(channel): support batch model pulling

assisted-by：opencode：Zhipu/GLM-5.3[Max]"
```

多个模型/工具参与时逐行列出。纯人工改动无需此声明，但需在 PR 描述中说明。

## 编码边界

- 业务模块通过 `GameModule` 访问；框架启动层按其现有模块初始化方式工作。
- 业务异步使用 UniTask，明确取消、失败和资源归属。不要把框架现存的同步 API 当作不存在。
- Sprite 优先使用 `SetSprite`；实例化资源使用 `LoadGameObjectAsync`；普通 Asset 加载与释放配对。
- `Assets/GameScripts/GameEntry.cs`、`Procedure` 和 `Assets/Launcher` 属于主包；热更业务位于 `Assets/GameScripts/HotFix`。以 asmdef 验证边界，不虚构 `GameScripts/Main`。
- UI 使用 `AddUIEvent` 管理监听；非 UI 类管理自身订阅。隐藏不等于销毁。
- 修改配置加载器、生成类时追溯 `Configs/GameConfig` 中模板；不直接修补生成产物。
- 不直接编辑 Scene/Prefab YAML、GUID 或 `.meta` 来替代资源数据库操作；新增源文件的 `.meta` 由 Unity 生成。

## 验证选择

```powershell
python .codex/scripts/workflow.py doctor
python .codex/scripts/workflow.py check
python .codex/scripts/workflow.py verify --profile docs
python .codex/scripts/workflow.py verify --profile code
python .codex/scripts/workflow.py verify --profile unity
python .codex/scripts/workflow.py verify --profile full
```

`docs` 不连接 Editor；`code` 包含 docs 检查及完整解决方案构建；`unity` 包含 Editor 编译、指定资源和明确测试集；
`full` 组合全部并运行隔离的真实 Luban 普通/分片导出、当前配置源生成校验、浏览器图片/字体/多分辨率回归。
C# / 资源变更交付需同时取得 .NET 与相关 Unity 验证证据。纯文档或纯 Python 修改不必形式化触发 Unity。
Editor 断开、忙、多实例或选中零用例时，必需验证返回阻塞/失败，不能降级声称验收通过。

Pipeline 描述文件含鉴权信息，禁止读出到日志、提交或分享。不得连接本机其他工程代替本项目验证。



## 会话总结索引规则

1. `conversation-summaries/` 是会话总结的唯一存储目录；新增总结统一放这里，文件名建议 `YYYY-MM-DD-<主题>-summary.md`。
2. `conversation-summaries/INDEX.md` 是会话总结的唯一索引文件。禁止创建 `INDEX-YYYY-MM-DD.md`、`YYYY-MM-DD-INDEX.md` 或其他按日期拆分的索引文件。
3. 每次新增会话总结时，必须同步更新 `INDEX.md`：按日期倒序在顶部追加条目（同日内新条目排在前）。
4. 索引条目只包含：文档相对链接、5~12 个关键词（以模块名/类名/功能点为主）、一句话结论（不超过 40 字）；详细内容保留在总结文档中。
5. 需要回顾历史实现时，先查 `INDEX.md` 按关键词定位，再按需打开对应总结文档，不要全文通读整个目录。
6. 代码研究类文档不属于会话总结，仍存放于 `conversation-summaries/code-research/` 并维护其独立索引，规范见下方「会话研究索引规则」。
上级目录是整个我fork的项目的根目录，readme也在这里；当前目录为unity项目目录。
一般情况下，新功能不需要开分支，除非特大变动，也必须经过用户同意才行！

如果添加了新的 fork 定制功能，经用户同意后，在上级目录（git 仓库根目录）按分层文档规则更新说明：README.md 的「🛠️ 本 Fork 的定制改动」只维护简短概览；Books/Fork-定制改动说明.md 只作为兼容索引入口；详细说明写入Books/Fork/ 下对应专题文档，并同步更新 Books/Fork/CHANGELOG.md；具体写法遵循 .claude/skills/fork-docs/SKILL.md 或 .codex/skills/fork-docs/SKILL.md（根据你属于哪个cli工具claude还是codex，两个skill是一样的）。

commit提交时，以中文为主，英文为辅。如果用户让你写总结，则记得同时推送到远端。
用户让你存储记忆时，是存储在项目级的记忆里，跟随仓库走。
重点：**当前项目未使用Luban，除非用户主动使用，否则不考虑和Luban沾边！**


本项目有时会在svn下使用，此时就不要死磕git相关功能。

如果用户让你研究了某一项内容，一定要详细研究相关代码，必要时可使用联网搜索，并请及时记录研究结果到 `conversation-summaries/code-research/` 目录下，并做好简洁的关键词索引记录。

## 会话研究索引规则

1. `conversation-summaries/code-research/` 是代码研究文档的唯一目录，不得创建 `code-researc` 等近似或拼写错误的并行目录。
2. `conversation-summaries/code-research/INDEX.md` 是代码研究的唯一索引文件。禁止创建 `INDEX-YYYY-MM-DD.md`、`YYYY-MM-DD-INDEX.md` 或其他按日期拆分的索引文件。
3. 新增研究文档时，必须先检查并更新现有 `INDEX.md`，按日期倒序追加条目；不得因当天新增研究而创建新的索引文件。
4. 索引只记录文档链接、关键词和一句话结论，详细内容保留在对应研究文档中。

## 核心原则（编码红线）
1. **Editor脚本**：editor代码尽量不要在热更代码中使用（除非万不得已，主要是防止热更代码引用editor程序导致的打包问题，但你要考虑这个因素后再使用！），所有editor下的窗口，优先考虑使用odinx插件提供的功能！
2. **Editor下自定义热更脚本的Inspector**：能通过在"Assets/Editor"下对热更脚本自定义 Inspector，就自定义，但是注意目录要规范，不要混在一块！
