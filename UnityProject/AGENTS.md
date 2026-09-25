# AGENTS.md — 项目工作目录入口

> **本目录即项目根目录。** 上级仓库目录在实际使用中会被删除、不做保留，仓库级公共说明仅作为 Wiki 参考。
> AI 工具在本目录下工作时，是在**为实际项目编写业务代码**，而非修改 TEngine 框架本身。
> 框架级的改动（TEngine.Runtime、TEngine.Editor、内置模块实现）除非用户明确声明"改框架"，否则一律视为业务侧消费框架，不主动修改框架源码。
> 当前如果是修改框架，则同步查看仓库根目录（本目录的上级）的`AGENTS.md`了解这个fork框架的一些内容

## 工程事实
- 如果当前项目使用的unity版本不是unity6+(unity6000+)，则unity cli工具将无效！！
- 项目根是本目录，CLI 为 `Tools/unity.exe`，解决方案为 `UnityProject.sln`。
- 配置源位于仓库根 `Configs/GameConfig`，不是 Unity 项目内的 `Configs`。
- Unity 版本取 `ProjectSettings/ProjectVersion.txt`；Pipeline 能力以本项目注册命令及包内 `Documentation~` 为准。
- `.codex/skills` 是技能唯一源。不要复制到第二套目录，也不要假设所有 Codex 客户端都会自动发现此目录。
- 中文 Windows 下 PowerShell 终端默认 `[Console]::OutputEncoding` 为 GBK（cp936），输出中文目录/文件名会乱码；执行涉及中文路径的命令前先设置 `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8`。

## Wiki 目录

- `repowiki/` 是本项目的内嵌 Wiki 目录，**随项目走、不依赖仓库根目录存在**。
- 仓库根目录的 `Books/Fork/`、`README.md`、`Books/Fork-定制改动说明.md` 等 fork 文档**仅存在于克隆仓库中**；上级仓库目录在实际使用中会被删除，最终项目里只有 `repowiki/`。
- 因此，fork 定制功能的详细说明如果需要随项目分发，必须同步写入 `repowiki/` 对应页面；`Books/Fork/` 下的文档只作为仓库开发期的权威源。
- `repowiki/zh/content/` 按功能域分目录（项目概述、核心架构、模块系统、资源管理、UI系统、事件系统、热更新系统、编辑器工具、API参考 等），新增页面时归入对应目录并保持导航链接可达。
- `repowiki/zh/content/项目概述/当前Fork定制功能.md` 是 fork 定制功能的 Wiki 入口页，新增或变更 fork 功能时需同步更新此页的描述和文档链接。

## AI 协助开发声明

### 仓库commit提交时追加模型信息
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
- 新增需要混淆类型名的热更业务模块（继承 `TEngine.Module`）时，必须在热更入口首次获取前通过 `ModuleSystem.RegisterModule<T>(module)` 显式注册，并按 `OnInit` 的依赖顺序排列；增删模块时同步更新注册清单。显式注册仅用于启动初始化，业务访问仍走 `GameModule`，不要依赖 `IXxx` → `Xxx` 按名自动查找。
- 业务异步使用 UniTask，明确取消、失败和资源归属。不要把框架现存的同步 API 当作不存在。
- Sprite 优先使用 `SetSprite`；实例化资源使用 `LoadGameObjectAsync`；普通 Asset 加载与释放配对。
- `Assets/GameScripts/GameEntry.cs`、`Procedure` 和 `Assets/Launcher` 属于主包；热更业务位于 `Assets/GameScripts/HotFix`。以 asmdef 验证边界，不虚构 `GameScripts/Main`。
- UI 使用 `AddUIEvent` 管理监听；非 UI 类管理自身订阅。隐藏不等于销毁。
- 修改配置加载器、生成类时追溯 `Configs/GameConfig` 中模板；不直接修补生成产物。
- **除非用户主动要求**，不直接编辑 Scene/Prefab YAML、GUID 或 `.meta` 来替代资源数据库操作；新增源文件的 `.meta` 由 Unity 生成。
- **除非用户主动要求**，UI部分不要通过代码运行时修改美化，这样不便于微调UI的prefab；UI必须落盘为prefab，便于用户修改（可以通过创建UI生成脚本来生成UI结构），UI要保证符合Tengine规范
- 修改代码时始终以磁盘当前文件为准，先读取再修改；只做必要的最小修改，不删除、覆盖或重构无关代码，并检查 Diff 确保用户已有修改不丢失。
- 优先搜索并复用项目已有的类、方法、工具和封装，禁止重复实现已有功能；修改前先确认项目中是否已有可复用实现。
- 代码提交不要改一下提交一下，用户允许提交的时候再提交

## 已集成的第三方 UI 效果插件

- **UIEffect**（`com.coffee.ui-effect@5.11.7`）：挂在 `Graphic` 上，材质级 shader 注入实现色调/颜色/模糊/溶解/阴影/渐变/描边/细节等视觉效果。配套 Tweener 动画、Preset 预设、Replica 批量副本。
- **UISoftMask**（`com.coffee.softmask-for-ugui@3.6.5`）：替代原生 `Mask`，RenderTexture 软遮罩，支持羽化/抗锯齿/嵌套/打洞（`MaskingShape`）/精确点击（`AlphaHitTestTarget`）。
- 两者 shader 内嵌协作，软遮罩内可正常用 UIEffect 效果。详见 `repowiki/zh/content/UI系统/第三方UI效果插件.md`。
- 注意：自研 `UIEffectSortingOrder`（`GameLogic/Module/UIModule/Expansion/Utility/`）名字相似但功能不同（Canvas sortingOrder 同步），不要混淆。

## Fork UI 组件扩展

- UI 优先使用 fork 扩展组件：`UIButton`、`UIText`、`UITMPText`、`UIImage`、`UIRawImage`，替代原生 `Button`/`Text`/`TextMeshProUGUI`/`Image`/`RawImage`。
- 脚本生成器已集成上述五项（`UIComponentName` 枚举 25–29，前缀 `m_uiBtn`/`m_uiText`/`m_uiTmp`/`m_uiImg`/`m_uiRimg`）。
- 详见 `repowiki/zh/content/UI系统/UI脚本生成器.md` 和 `Books/Fork/ui-expansion.md`。

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

## 暂存任务（计划任务）

- `tasks/` 用于暂存用户明确提出、但尚未要求立即执行的任务。
- AI 不得自行执行暂存任务；只有用户明确要求执行时才开始。
- 执行前先读取任务文件及相关代码，按当前磁盘状态实施。
- 完成后删除任务文件或标记为已完成。

## 核心原则（编码红线）

### 一、通用编码红线

1. **异步优先**：IO 操作用 `UniTask`，禁止同步加载/Coroutine；取消语义要明确，失败分支要处理，资源归属要清晰
2. **模块访问**：通过 `GameModule.XXX` 访问，而非 `ModuleSystem.GetModule<T>()`；不要绕过模块门面直接反射或 new 内部类型
3. **资源必须释放**：`LoadAssetAsync` 对应 `UnloadAsset`，GameObject 用 `LoadGameObjectAsync`；Sprite 优先 `SetSprite`；加载与释放必须配对，避免野引用
4. **热更边界**：`GameScripts/Main` 不热更，`GameScripts/HotFix/` 全部热更；以 asmdef 验证边界，不虚构程序集
5. **事件解耦**：模块间用 `GameEvent`，UI 内部用 `AddUIEvent`；监听必须在销毁时注销，隐藏不等于销毁
6. **Editor 脚本**：editor 代码尽量不要在热更代码中使用（防止热更代码引用 editor 程序集导致打包失败），所有 editor 下窗口优先使用 Odin 插件功能
7. **Editor 下自定义热更脚本的 Inspector**：能通过在 `Assets/Editor` 下对热更脚本自定义 Inspector 就自定义，但目录要规范，不要混在一块
8. **配置不补丁生成产物**：修改配置加载器、生成类时追溯 `Configs/GameConfig` 中模板，不直接修补生成产物
9. **不主动改框架**：框架级改动（TEngine.Runtime、TEngine.Editor、内置模块实现）除非用户明确声明"改框架"，否则一律视为业务侧消费框架，不主动修改框架源码

### 二、Fork 定制模块红线

10. **YooAsset 3 无兼容层**：本 fork 已完成 YooAsset 3.0.5 无兼容层迁移，禁止回退到旧版 API 或引入兼容层；资源加载走 `ArchiveFileBuildPipeline` 与加密归档加载，不要混用上游的 `Imp_` 前缀实现
11. **热更独立 CodePackage**：本 fork 的 HybridCLR 热更使用独立 `CodePackage`、归档二进制加载与 AOT 元数据清单，不要引入上游的 `HybridCLRBridge` 旧流程；版本确认流程必须走本 fork 的 `VersionConfirm` 链路
12. **Obfuz 混淆独占**：代码混淆统一走 Obfuz（dnlib 冲突已解决），不要引入其它混淆方案；多态 DLL 热更产物必须与本地包同步脚本保持一致，不要手改产物
13. **RuntimeConfig 与 DeployConfig**：运行时配置询问用户是否可以走 `RuntimeConfigModule` + `DeployConfig`（TOML/JSON 轻量配置）
14. **DataBinding 纯数据**：本 fork 的 DataBinding 是纯数据运行时 + 生成器 + Odin 面板，不要引入上游的 GameObject 绑定变体；绑定路径以生成器为准，不手写
15. **TimerModule 链表化**：计时器统一走 `TimerModule`（链表化、坏帧安全、限定循环次数），不要自造 `Invoke`/`InvokeRepeating` 替代；逻辑计时用 `GameTickWatcher`（`RuntimeTools` 程序集）
16. **GameObject 对象池基于 location**：对象池基于 YooAsset location 的异步实例化池（预热/回收/自动销毁），不要用 `Instantiate`/`Destroy` 绕过池
17. **除非用户要求，否则3D 动画图默认不走 PlayableGraph**：3D 动画图基于 PlayableGraph 代码驱动（多层级混合/权重过渡），不要用 Animator Controller 覆盖；帧动画走本 fork 的序列帧模块（场景版/UI版/RawImage版），不要用 SourceGenerator
18. **桌面多开走 `--yoo-instance`**：桌面多开通过命令行 `--yoo-instance` 驱动 YooAsset 多实例缓存隔离，不要在业务代码里手动 `new YooAssetsDriver` 造实例（尤其是作为`专用服务器`使用时）
19. **自定义异步操作走 `AsyncOperationModule`**：模块级自定义异步操作走 `AsyncOperationModule`（支持协程/UniTask/abort-on-cancel），不依赖 YooAsset 的异步操作体系，不要混用，（**如果必须采用异步封装时再用这个**）
20. **存档与数据中心**：存档统一走 `ClientSaveDataMgr`，玩家数据中枢走 `DataCenterSys`，不要自造存档格式或绕过数据中心直接读写文件
21. **窗口布局走 `ScreenModule`**：Windows Standalone 多显示器窗口布局控制走 `ScreenModule`，不要用 `Screen.SetResolution` 或原生 `Screen` API 替代
22. **事件批量移除**：`GameEvent.RemoveAllListeners` 支持按事件 ID 批量移除监听，优先使用批量接口，不要逐个 RemoveListener
23. **日志走 TouchSocket 桥接**：日志统一走 TouchSocket 日志桥接 + Unity 日志落盘 + LogViewer，不要用 `Debug.Log` 直接做业务日志输出

## 资源加载与释放规则：
通过 TEngine 封装的 API 加载资源时必须遵守引用计数配对：
- LoadGameObject / LoadGameObjectAsync：返回的 GameObject 自带 AssetsReference，Destroy 时自动归计数，无需手动释放。
- LoadAsset / LoadAssetAsync（含回调和泛型重载）：返回裸 UnityEngine.Object（Sprite、Material、AudioClip 等），用完必须手动调用 ResourceModule.UnloadAsset(asset) 归计数。
- （开发辅助）计数未归零的资源不会被对象池回收，会导致资源泄漏。切换场景前检查 Debugger → Profiler/Object Pool 中 Count > 0 的条目定位泄漏源。
