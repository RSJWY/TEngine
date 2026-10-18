# AI 开发工作流

本文是 AI 辅助开发的**完整说明**。规范约束以仓库根 `AGENTS.md` 和 `UnityProject/AGENTS.md` 为准，本文只解释工具链怎么用、证据怎么留。

## 一、三层结构

| 层 | 位置 | 职责 |
|---|---|---|
| 规范 | `AGENTS.md`、`UnityProject/AGENTS.md` | 编码红线、授权边界、文档规则 |
| 技能 | `UnityProject/.codex/skills/` | 按任务读取的操作手册与权威资料索引 |
| 验证 | `UnityProject/.codex/scripts/workflow.py` | 可执行的检查、验证、审批与证据留存 |

`.codex/skills/` 是技能唯一源，不再维护第二套目录。任何 CLI 工具都需要**显式读取** `SKILL.md`，不要假设会被自动发现。

## 二、技能索引

| 任务 | 入口 |
|---|---|
| 框架、UIWindow、事件、资源、HybridCLR | `.codex/skills/tengine-dev/SKILL.md` |
| Editor/Player 查询、资产操作、测试、构建预检 | `.codex/skills/unity-cli/SKILL.md` |
| 配置结构与导出（本项目未启用，仅按需） | `.codex/skills/luban-dev/SKILL.md` |
| HTML/UI-DSL 转 UGUI Prefab | `.codex/skills/html-to-ugui/SKILL.md` |
| Fork 定制改动文档维护 | `.codex/skills/fork-docs/SKILL.md` |

先读 `SKILL.md`，再按需读它 `references/` 下的细分文档，不要一次性全文通读。

## 三、环境自检

从 Unity 项目根执行：

```powershell
python .codex/scripts/workflow.py doctor
```

`doctor` 只做只读预检，产出两阶段结论：

- `environment`：Python、.NET、Git、Unity 版本、Pipeline 包版本与来源、CLI 定位结果。
- `editor-connection`：能否唯一绑定到本项目正在运行的 Editor，且状态为 ready。

### Unity CLI 定位规则

CLI 依次按以下顺序解析，命中即用：

1. `Tools/unity.exe`（项目内固定版本，优先级最高）
2. PATH 上的 `unity`（且其父目录不是 `Editor`，避免误用 Editor 自带同名可执行文件）

`com.unity.pipeline` **是包，不是 CLI 入口**。包可能以注册表或嵌入形式安装，其 `package.json` 位于 `Library/PackageCache/` 或 `Packages/`，`doctor` 会同时报告 `pipelineSource` 和 `pipelinePath`，便于区分。

## 四、验证 profile

```powershell
python .codex/scripts/workflow.py check
python .codex/scripts/workflow.py verify --profile docs
python .codex/scripts/workflow.py verify --profile code
python .codex/scripts/workflow.py verify --profile unity
python .codex/scripts/workflow.py verify --profile full
```

| Profile | 覆盖内容 | 是否连接 Editor |
|---|---|---|
| `docs` | 技能与文档结构检查、工作流单元测试 | 否 |
| `code` | `docs` + 完整解决方案构建 | 否 |
| `unity` | Editor 编译、资产校验（需 `--asset`）、EditMode/PlayMode 测试 | 是 |
| `full` | 组合以上，并运行浏览器与配置导出的隔离回归 | 部分 |

常用参数：

- `--test-filter`：默认 `TEngine.Workflow`，只选本次工作流用例，**不等于**项目全部业务测试。
- `--filter-type`：`assembly` / `testName` / `category`。
- `--timeout`：单阶段超时秒数。
- `--asset`：可重复，指定要校验的资产路径；不指定时该阶段标记 `skipped`，不能算通过。

### 选择哪一档

- 纯文档、纯 Python 改动：`docs`。
- C# 改动：至少 `code`；涉及场景、Prefab、组件、资源数据库时必须补 `unity`。
- 发布前或跨模块大改：`full`。

## 五、证据与退出码

每次调用都会写入 `UnityProject/.codex/runs/<run-id>/`：

- `report.json`：结构化阶段结果、脱敏后的命令、输出、时间戳与退出码。
- `report.md`：可直接阅读的摘要。

退出码：`0` 通过、`1` 失败、`2` 阻塞。**中断的运行即使已完成阶段都通过，整体也判为阻塞**，不能被 `report` 提升为通过。

```powershell
python .codex/scripts/workflow.py report --run <run 目录>
```

没有证据的检查不能标成通过；Editor 断开、忙、多实例或零用例都必须如实报 blocked。

## 六、Unity 操作

同一个 Editor 还有第二条驱动通道（Unity MCP），它与 CLI 的能力不等价：MCP 不留证据、没有审批链，只适合探索性查询与 API 反射。**通道选择与写入纪律见 [`.codex/skills/unity-cli/references/channels.md`](../UnityProject/.codex/skills/unity-cli/references/channels.md)**，本节只讲 CLI 侧流程。

### 命令发现与查询

```powershell
python .codex/scripts/workflow.py unity list
python .codex/scripts/workflow.py unity query editor_status
python .codex/scripts/workflow.py unity query console
```

`unity list` 读取当前 Editor 实际注册的命令清单，实时描述优先于本文档示例。`query` 只允许执行 `.codex/scripts/workflow_lib/unity.py` 中 `READ_ONLY` 白名单内的命令。

参数通过 JSON 文件传入，避免 PowerShell 转义差异：

```powershell
python .codex/scripts/workflow.py unity query get_scene_hierarchy --params-file params.json
```

### 写入：预览 → 授权 → 执行

写入类命令不在白名单内直通，必须走三段式：

```powershell
# 1. 预览：可 dry_run 的命令会真的跑一次 dry_run，其余只展示静态范围
python .codex/scripts/workflow.py unity preview <命令名> --params-file request.json

# 2. 展示预览内容，得到用户真实确认后再记录授权
python .codex/scripts/workflow.py approve --plan <preview.json> --by <批准人> --reason <确认出处> --high-risk

# 3. 执行
python .codex/scripts/workflow.py unity apply --plan <preview.json> --approval <approval.json>
```

审批绑定项目、命令、参数、输入文件内容与预览时的工作树指纹，并有有效期。代码、输入或目标变化后必须重新预览。

一次审批只允许一次尝试：网络超时或中途失败同样消耗审批，先检查实际状态再决定下一步，**不要自动重放**。

`com.unity.pipeline` 的部分设置类命令会触发全局资源保存，不保证只保存目标字段。预览时要说明可能连带保存的脏资源；存在未处理的用户改动时先停下。

### Player 验证

```powershell
python .codex/scripts/workflow.py unity runtime-status --runtime-path <Player 路径>
```

必须显式指定 Player 路径，并校验其 `workingDirectory`。Player 的结果不能替代 Editor 验证。

## 七、授权边界

- 普通源码修改按用户任务直接实施。
- Unity 写入先预览具体目标与范围，再集中确认。
- 删除、覆盖、包依赖变更、项目设置变更、平台切换、真实构建：**逐项单独确认**，计划获准不等于获准发布。
- 不自动安装或升级工具，不提交、推送、上传、签名或发布。
- `--high-risk` 是记录，不是安全沙箱，不能防护有本地写权限的调用者。

## 八、依赖

工作流核心只用 Python 3.11+ 标准库。`check` 需要真实解析器：

```powershell
pip install -r .codex/scripts/requirements.txt
```

必需：`PyYAML`、`markdown-it-py`。可选（仅对应领域使用）：`openpyxl`、`playwright`。

若系统 Python 由包管理器托管（例如 uv 安装的 Python 会拒绝 `pip install`），在项目内建隔离环境：

```powershell
uv venv .codex/.venv --python 3.13
uv pip install --python .codex/.venv/Scripts/python.exe "PyYAML>=6.0,<7" "markdown-it-py>=3.0,<5"
.codex/.venv/Scripts/python.exe .codex/scripts/workflow.py check
```

`.codex/.venv/` 不要提交。

## 九、故障边界

| 现象 | 处理 |
|---|---|
| `STATUS_NO_INSTANCES` | 打开本工程并确认 Editor 已就绪；入口不会启动、安装或重置 Editor |
| 域重载 | 只对查询与轮询有限重试，重新确认实例；不重复发送起始命令 |
| 找不到测试用例 | 检查测试程序集导入与注册清单，不要改成运行无关测试凑数 |
| 结果显示完成但业务失败 | 判为失败。传输成功不是测试、构建或烘焙成功 |
| 多实例或工程不匹配 | 报阻塞，不连接其他工程兜底 |
| 超时 | 结果未知，先查状态，禁止自动重放写入 |
| `solution-build` 报 MSB1025 / `UnauthorizedAccessException` | 系统临时目录对子进程不可写。工作流会自动改写到 `.codex/tmp/` 并在命令证据里记录 `tempDir`；若仍失败，检查该目录权限而不是改代码 |

截图只能作为证据附属品，不等于视觉验收通过；输出报告必须区分机器验证与人工确认。

## 十、常见任务速查

```powershell
# 改完 C# 先过一遍
python .codex/scripts/workflow.py verify --profile code

# 改了 UI Prefab / 场景，补 Editor 侧证据
python .codex/scripts/workflow.py verify --profile unity --asset Assets/AssetRaw/UI/xxx.prefab

# 看 Editor 当前状态与报错
python .codex/scripts/workflow.py unity query editor_status
python .codex/scripts/workflow.py unity query console
```

交付说明必须包含：改了什么、实际执行了哪些验证、剩余风险与阻塞。没跑过的检查不要写成通过。
