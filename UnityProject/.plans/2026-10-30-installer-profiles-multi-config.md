# 安装包多配置：Windows 多 iss 脚本 + Linux 多 conf 构建配置

- 日期：2026-10-30
- 状态：**已实施**（2026-10-30）。命名最终定为：`setup.iss` 唯一模板，配置名 `<cfg>` 对应编辑脚本 `setup.<cfg>.iss`（默认 `generated`，即历史 `setup.generated.iss`）；Linux 为 `appConfigs/<cfg>.conf`。切换配置按落盘脚本载入面板，构建只回写所选编辑脚本。验证：`verify --profile code` → `solution-build: passed`；`compileall` 通过；CLI dry-run 含三个新字段。
- 目标：两份平台各自用**原生文件格式**承载变体，工具只负责「选哪一份 + 编译」，差异直接 `git diff` 文件即可

## 1. 当前实现（已核对）

### Windows / Inno Setup

| 项 | 现状 |
|---|---|
| 模板 | `Releases/Windows/setup.iss`（唯一，构建流程不回写） |
| 编译脚本 | `Releases/Windows/setup.generated.iss`（`EnsureGeneratedIss` 仅在不存在时从模板复制；`RegenerateGeneratedIss` 强制重生成） |
| 参数注入 | `SyncIssDefines(IssInstallerConfig)` 用正则逐行替换 6 个 `#define`：`MyAppName`、`MyAppEnglishName`、`MyAppVersion`（有值才写）、`MyAppPublisher`、`MyAppExeName`、`MyAppPassword`（`BrandWatermark` 也参与，见 `ExtractIssConfig`） |
| 编译 | `CompileSetup` 硬编码 `iscc "<GeneratedIssPath>"`，工作目录 `Releases/Windows`，产物 `Releases/Windows/setup/` |
| 反向读取 | `ExtractIssConfig(issPath, warnings)` + `TryGetIssDefine(issPath, key)` —— **已支持任意 iss 路径**，可直接复用 |
| 面板 | 「安装包配置」页：平台选择 / InnoSetup 安装包（AppName 等 6 项）/ ISCC 编译（模板·生成脚本路径只读、「打开模板」「打开生成脚本」「从模板重新生成」） |

卡点：`IssPath` / `GeneratedIssPath` / `CompileSetup` 三处把 iss 写死成单一 `setup`，无法并存第二份脚本。

### Linux / debkit

| 项 | 现状 |
|---|---|
| 工具目录 | `Releases/Linux`（`debkit.sh`、`appConfigs/`、`templates/`、`lib/`、`output/`） |
| 配置 | `appConfigs/<包名>.conf`，`DebkitConfig.GetConfPath(packageName)`；仓库里已有 `appConfigs/example.conf` 作示例 |
| 生成 | `WriteConfig(DebkitConfData)` 由面板字段 `Render()` 覆盖写入，会**抹掉手工改动** |
| 反向读取 | `TryReadConfig(path, out DebkitConfData)` 已存在 |
| 执行 | `BuildWslCommand(pkg)` → `debkit.sh -c appConfigs/<pkg>.conf -w ~/debkit-work`；脚本本身**接受任意 conf 路径** |

卡点：conf 文件名被包名绑定（`SanitizePackageName` 归一化），一份包名只能有一个 conf；面板字段改一次就覆盖。

## 2. 设计

原则：**文件即变体**。不改 iss/conf 的内部格式，只把「路径」从常量变成选择项；新增文件即新增变体，删除即删除，`git diff` 即差异。

### 2.1 Windows：多份 `.iss`

- 位置：`Releases/Windows/*.iss`，**排除** `*.generated.iss`。默认/兼容项仍是 `setup.iss`。
- 派生规则：`<name>.iss` → `<name>.generated.iss`（同一目录）。模板永不回写，改的始终是 generated 副本——**保留现有不变量**。
- `InnoSetupBuilder` 改造（只参数化，不改语义）：
  ```
  ListIssScripts()                      // 枚举 *.iss，排除 *.generated.iss，返回文件名
  GetIssPath(scriptName)                // "" → setup.iss
  GetGeneratedIssPath(scriptName)       // "a.iss" → "a.generated.iss"
  EnsureGeneratedIss(scriptName, cfg)   // 不存在才从模板复制，再 SyncIssDefines
  RegenerateGeneratedIss(scriptName, cfg)
  BuildInstaller(cfg, scriptName, iscc) // → EnsureGeneratedIss(name) → SyncIssDefines → CompileSetup(generated, iscc)
  ResolveInstallerVersion() / ExtractIssConfig(path)  // 原样复用
  ```
- 6 个 `#define` 的注入**行为不变**（仍以面板值为准回写 generated 副本）。为让每份脚本自带的身份不被意外抹掉，面板在切换脚本时提供只读摘要 + 一个显式「把所选脚本的 6 项载入面板」按钮（复用 `ExtractIssConfig`）。即：先载入、再回写，等于写回原值；不点就沿用当前面板值。
- 面板改动：`InnoSetup 安装包` 区顶部加「iss 脚本」`ValueDropdown`（`ListIssScripts()`）+ 三个按钮（打开所选脚本 / 载入所选脚本参数 / 从所选脚本重新生成）；`模板脚本`/`实际编译脚本` 两行只读文本改为按所选脚本显示。

### 2.2 Linux：多份 `.conf`

- 位置：`Releases/Linux/appConfigs/*.conf`（已有 `example.conf`）。
- `DebkitConfig` 改造：
  ```
  ListConfFiles()                      // 枚举 appConfigs/*.conf
  GetConfPathByFile(fileName)          // appConfigs/<file>，做了 .. 与非法字符防护
  WriteConfig(data, confFileName)      // 新增重载；旧 WriteConfig(data) 保留（= 按包名，兼容）
  BuildWslCommand(confFileName)        // debkit.sh -c appConfigs/<file> -w ~/debkit-work
  BuildWindowsWslCommand(confFileName, distro)
  ```
- 面板改动：debkit 区顶部加「构建配置」`ValueDropdown`（`ListConfFiles()`）+ 按钮：`从面板新建`（弹文件名，写新 conf）、`覆盖所选`（现有 WriteConfig 语义）、`重新载入所选`（`TryReadConfig` 回填面板）、`打开配置文件`。
- 默认值：`DebkitConfFile` 为空时回退 `<SanitizePackageName(包名)>.conf`，行为与今天一致。

### 2.3 设置与通道

| 文件 | 改动 |
|---|---|
| `BuildConfig.cs` | `+ public string InstallerIssScript = "";` `+ public string DebkitConfFile = "";` |
| `BuildPipelineSetting.cs` | 同上两字段，`ApplyDefaults()` 同步 |
| `BuildPipelineWindow.cs` | 两处下拉 + 按钮组（见 2.1 / 2.2） |
| `CLIBridge.cs` | `BuildRequestDTO`：`+ issScript`、`+ debkitConfFile`、`+ installerPlatform("windows"/"linux"/"both")`；`ExecuteInstaller` 传 `issScript`；**新增动作 `buildLinuxInstaller`**（`WriteConfig` 不参与——直接 `BuildWslCommand(confFile)` 执行，或按 `installerPlatform` 串联）；结果 DTO 加 `linuxInstallerOutputPath` |
| `BuildCLI/tengine_build/config_store.py` | `BuildFormState` 加 `installerIssScript`、`debkitConfFile`、`installerPlatform`；`list_iss_scripts()` / `list_conf_files()` 直接扫 `Releases/Windows/*.iss`、`Releases/Linux/appConfigs/*.conf` |
| `BuildCLI/tengine_build/dto.py` | `ALL_ACTIONS` + `buildLinuxInstaller`；两个字段随表单下发 |
| `BuildCLI/tengine_build/app.py` | 安装包区两个下拉（iss / conf）+ 平台选择 |
| `BuildCLI/tengine_build/cli.py` | `--iss-script`、`--debkit-conf`、`--installer-platform` |

## 3. 实施步骤

**P1 Windows 多 iss**
1. `InnoSetupBuilder` 路径参数化（`ListIssScripts` / `GetIssPath` / `GetGeneratedIssPath` / 三个入口方法带 `scriptName`），`CompileSetup` 改为按生成路径编译。
2. `BuildConfig` + `BuildPipelineSetting` 加 `InstallerIssScript`。
3. 窗口 Inno 区加下拉与按钮；`模板脚本`/`实际编译脚本` 改为按所选脚本显示。
4. 验证：放第二份 `setup-demo.iss`（复制 setup.iss 后改 MyAppName）→ 下拉可见 → 分别生成 `setup-demo.generated.iss` 且 `#define` 正确 → 面板切换不污染另一份。

**P2 Linux 多 conf**
5. `DebkitConfig` 加 `ListConfFiles` / `GetConfPathByFile` / `WriteConfig(data, file)` / `BuildWslCommand(file)` 重载，旧签名保留转调。
6. `BuildPipelineSetting` 加 `DebkitConfFile`；窗口 debkit 区加下拉与 4 个按钮。
7. 验证：`example.conf` 出现在下拉；「从面板新建」产出第二份 conf 且两份互不影响；`TryReadConfig` 回填正确。

**P3 CLI 通道**
8. `CLIBridge` 字段 + `buildLinuxInstaller` 动作 + 结果路径。
9. BuildCLI 表单/下拉/命令行参数 + `buildLinuxInstaller` 动作。
10. 验证：`--no-gui run --action buildInstaller --iss-script setup-demo.iss` 请求 JSON 正确；`--action buildLinuxInstaller --debkit-conf example.conf` 拼出正确 WSL 命令（无 iscc / 无 WSL 时应为明确报错，不是崩溃）。

**P4 文档**
11. `repowiki/zh/content/编辑器工具/Fork构建与调试工具.md`、`部署发布/部署发布.md` 增「多份 iss / 多份 conf」用法与命名约定。
12. 经用户同意后更新上级 `Books/Fork/*` + `Books/Fork/CHANGELOG.md` + `README.md` 概览。
13. `conversation-summaries/2026-10-30-installer-multi-config-summary.md` + `INDEX.md`。

## 4. 验证

| 项 | 命令 | 期望 |
|---|---|---|
| .NET 编译 | `python .codex/scripts/workflow.py verify --profile code` | 通过 |
| Unity 验证 | `python .codex/scripts/workflow.py verify --profile unity` | Editor 忙/断开则记阻塞，不谎称通过 |
| Python | `python -m compileall BuildCLI/tengine_build` | 通过 |
| Windows 安装包 | `--no-gui run --action buildInstaller --iss-script <name>` | `Releases/Windows/setup/*.exe` |
| Linux 安装包 | `--no-gui run --action buildLinuxInstaller --debkit-conf <file>` | `Releases/Linux/output/*.deb` |

环境事实（已核实）：`E:\Unity\Editor\2022.3.62f3\Editor\Unity.exe` 存在；Editor 当前运行中（`Temp/UnityLockfile`）；WSL 有 `ai-dev` / `TestLinux`；`debkit.sh` 就绪；**ISCC 缺失**（`C:\ISCC.exe` 不存在、`Program Files (x86)\Inno Setup 6` 未安装）→ Windows 端到端验收需先提供 iscc 路径，否则只做到「生成 iss + 明确报错」为止。

## 5. 边界

- 不改 `setup.iss` 与 `debkit.sh` 本体，不改 iss/conf 格式。
- 保留「模板不回写」不变量：一切写入只落在 `*.generated.iss`。
- 旧行为兜底：未选脚本/未选 conf 时分别回退 `setup.iss` 与 `<包名>.conf`，CLI 旧请求无新字段时行为不变。
- 不动用户工作区现有未提交改动（`BuildPipelineSetting.asset` 等）。
- 不做：iss/conf 的继承或 include 合并、跨平台共用一份变体定义、批量矩阵出包。
