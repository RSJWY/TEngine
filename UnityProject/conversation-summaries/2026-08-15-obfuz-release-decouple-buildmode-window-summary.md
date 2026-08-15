# dev/release 与 Obfuz 解耦 + 构建模式面板 + pdb 可配置开关

日期：2026-08-15
关联：issue #3（pdb + 模式框架）后续解耦改造；issue #4（Obfuz 正式接入）前置

## 背景

issue #3 阶段一（2026-08-13，commit 52274358）把 `IsDevelopmentBuild` 与 `ENABLE_OBFUZ` 强绑定，导致 release 必须开混淆、dev 必带 pdb。本次改造将三个维度彻底解耦，并提供 Editor 操作面板。

## 最终目标矩阵（用户拍板）

| ENABLE_RELEASE | ENABLE_OBFUZ | pdb 开关 | 结果 |
|---|---|---|---|
| 关 | 关 | 可配置（GeneratePdb） | dev：不混淆，pdb 可配 |
| 开 | 开 | 无效（release 强制无 pdb） | 高防护 release：混淆、无 pdb |
| 开 | 关 | 无效 | 低防护 release：不混淆、无 pdb（本次新增能力） |

## 三宏语义分层（核心设计）

| 宏 | 来源 | 语义 |
|---|---|---|
| `OBFUZ_INSTALLED` | asmdef versionDefines 自动 | Obfuz 包在不在（管编译门控） |
| `ENABLE_OBFUZ` | 手动开关 | 混淆开不开（只管混淆） |
| `ENABLE_RELEASE` | 手动开关 | dev/release 模式（管 pdb、PackageNote、启动校验） |

要点：
- Obfuz 官方包（3.1.0 + obfuz4hybridclr）自身不定义任何宏，三个宏全由项目侧定义，已逐包核查 asmdef 与源码证实。
- `OBFUZ_INSTALLED` 解决了鸡蛋悖论：Obfuz 开关菜单原先被 `#if ENABLE_OBFUZ` 包住，宏一关菜单消失无法重开；改用包存在性门控后菜单始终可见。
- `ENABLE_RELEASE` 命名理由：对齐 `ENABLE_HYBRIDCLR`/`ENABLE_OBFUZ` 惯例；不定义=dev 是安全极性（忘配宏只落在 dev）；否决 `FORCE_RELEASE`（无覆盖语义）、`ENABLE_DEV`（默认变 release 不安全）。
- `ENABLE_OBFUZ` 宏与 `Obfuz.asset` 的 `buildPipelineSettings.enable` 由 `SetObfuz` 方法同步；绕过面板/菜单手改一边会脱节，以面板/菜单为准。

## 实施内容

### 解耦（另一会话落地，本会话验收通过）

- `Assets/TEngine/Editor/TEngine.Editor.asmdef`：versionDefines 加 `com.code-philosophy.obfuz` → `OBFUZ_INSTALLED`（obfuz4hybridclr 依赖 obfuz，一条即可）。
- `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`：using 与 Obfuz 菜单换 `OBFUZ_INSTALLED` 门控；新增 `ENABLE_RELEASE` 常量；抽公共方法 `SetReleaseMode/SetObfuz/IsReleaseModeActive/IsObfuzActive`；新增菜单 `TEngine/Define Symbols/Enable|Disable Release Mode`；混淆分支 `CompileDll` 补 developmentBuild 传参。
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`：`IsDevelopmentBuild` 判定宏 `ENABLE_OBFUZ` → `ENABLE_RELEASE`。
- 新增 `Assets/TEngine/Editor/ReleaseTools/BuildModeWindow.cs`：Odin 独立面板（`TEngine/Build/构建模式窗口`）。
- 临时方案文件 `.claude/plans/obfuz-release-decouple-plan.md` 与旧稿 `issue3-pdb-and-build-mode-plan.md` 均已删除（临时参考性质，不存档）。

### pdb 可配置开关（本会话）

- `UpdateSetting.cs:169-176`：新增序列化字段 `GeneratePdb`（默认开）与聚合属性 `WillGeneratePdb = IsDevelopmentBuild && GeneratePdb`。配置非宏，切换不重编译。
- `BuildDLLCommand.cs`：三处 `CompileDll` 传参（`:220`、`:230`、混淆分支 `:242`）改用 `WillGeneratePdb`；新增 `IsPdbEnabled/SetPdbEnabled`（写 UpdateSetting + SaveAssets）。
- `ReleaseTools.cs:183`：pdb 残留检测从「仅 release」扩为「任何不产 pdb 的配置」（release 或 pdb 关），弹窗文案泛化。
- `ProcedureLoadAssembly.cs:100`：运行时 pdb 加载门控改 `WillGeneratePdb`（缺失静默回退逻辑不变）。
- 面板美化：状态着色（GUIColor：绿=dev/正常、橙=release/混淆、灰=禁用）、动态按钮文案（`$xxxLabel` 显示动作而非状态）、开关横排（HorizontalGroup）、release 下 pdb 按钮 EnableIf 置灰、新增「当前组合」行（真机调试/高防护发布/低防护发布/非常规组合识别）、InfoBox 常驻提醒（预设不动 pdb；exe 与资源包宏状态需一致）。

## 已知行为/限制

- Unity `DevelopmentBuild` 编译选项同时管 pdb 生成与热更代码的 `DEBUG/TRACE` 宏：dev + pdb 关 时热更代码不带 DEBUG 编译。拆开需另做 define 注入，未做。
- HybridCLR 自带菜单 `HybridCLR/CompileDll/*` 不经本体系（用 Build Settings 的 Development Build 勾），且不拷 pdb 到 `AssetRaw/DLL/PDB/`。正确入口是 `HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath`。两组菜单行为不一致是已知缝隙，待 fork 文档标注或后续接管。
- 切换宏触发全量重编译、面板窗口重建，属正常现象。

## 测试状态：未测（待用户在 Unity 实测）

测试清单（按序）：
1. 面板 UI：状态区着色与组合识别、pdb 切换不重编译立即刷新、release 下 pdb 置灰 + InfoBox、Obfuz 菜单始终可见。
2. pdb 链路：dev+pdb 开 → BuildAssets 菜单 → Console `development:True` + 拷贝日志，`HybridCLRData/HotUpdateDlls/` 与 `AssetRaw/DLL/PDB/` 时间戳刷新；pdb 关重跑 → `development:False`，**重点查 `HybridCLRData` 旧 pdb 是否残留**（残留则需加清理逻辑）；pdb 关打 CodePackage → 应弹残留清理弹窗。
3. 运行时：dev 包 Console 日志 `loaded with PDB` / 不带 pdb 两种各验一次。
4. 三场景出包回归 + 启动模式校验（release exe + dev 包应拦截）。

## 遗留工作（issue #4 本体，下一步）

Obfuz 正式接入仍未做，见 `conversation-summaries/code-research/2026-08-14-obfuz-runtime-and-scope-research.md` 待实现清单：
- EncryptionService 静态/动态密钥初始化（静态 Scope 建议 `RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)`；动态 Secret 在热更 DLL 加载前就绪）
- 更换默认密钥（现为包默认 `Code Philosophy-Static/Dynamic`）、生成 GenerateSecretKeyFile / GenerateEncryptionVM 产物并归档
- 专用混淆规则 XML 替代 `enabledPasses: All` 空规则
- `GameApp.Entrance` 最前注册需按原始全名反射的类型（`RegisterReflectionType`）
- 用户提到还需先搞 Obfuz 初始化脚本，测试与此一并安排

fork 说明（`Books/Fork-定制改动说明.md` / README）待用户确认测试通过后另行安排。
