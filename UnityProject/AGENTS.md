# AGENTS.md — 项目工作目录入口

> **本目录即项目根目录。** 上级仓库目录在实际使用中会被删除、不做保留，仓库级公共说明仅作为 Wiki 参考。
> AI 工具在本目录下工作时，是在**为实际项目编写业务代码**，而非修改 TEngine 框架本身。
> 框架级的改动（TEngine.Runtime、TEngine.Editor、内置模块实现）除非用户明确声明"改框架"，否则一律视为业务侧消费框架，不主动修改框架源码。

## 工程事实

- 项目根是本目录，CLI 为 `Tools/unity.exe`，解决方案为 `UnityProject.sln`。
- 配置源位于仓库根 `Configs/GameConfig`，不是 Unity 项目内的 `Configs`。
- Unity 版本取 `ProjectSettings/ProjectVersion.txt`；Pipeline 能力以本项目注册命令及包内 `Documentation~` 为准。
- `.codex/skills` 是技能唯一源。不要复制到第二套目录，也不要假设所有 Codex 客户端都会自动发现此目录。

## 工具调用规范（防止并行 JSON 拼接错误）

- **每个工具调用必须是独立的 tool_use block**，严禁将多个工具调用的 JSON 参数拼接成单一字符串。
- 并行调用多个工具时，在一个 message 中输出多个独立的 `<tool_use>` 块，每个块各含一个完整的 JSON 对象。
- 单个 `<tool_use>` 块内只允许一个工具 + 一组参数，禁止塞入多个 `{"filePath": "..."}` 对象。
- 如果不确定并行是否安全，改为串行：一次一个工具调用，等返回后再发下一个。
- 同类型、同参数结构的并行调用（如多个 Read）尤其容易出错，优先考虑串行或限制在 2 个以内。

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
