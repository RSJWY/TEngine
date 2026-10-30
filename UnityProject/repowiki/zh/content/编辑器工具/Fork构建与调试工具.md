# Fork 构建与调试工具

本文汇总当前 fork 的打包窗口、发布目录、Windows/Linux 安装包（Inno Setup / debkit+WSL）、AOT/Obfuz 辅助工具、场景枚举和调试入口。详细迁移历史见 [Fork 定制改动总览](../../../../../Books/Fork/README.md)。

## 打包工具窗口

菜单：`Build/打包工具窗口`。

窗口基于 Odin，构建配置与运行时包配置共同读写 `UpdateSetting.RuntimePackages`。不要维护第二份资源包列表。

执行流程：

```text
编译热更 DLL -> 构建资源包 -> 发布整理 -> 最小包处理
-> 构建 Player -> 编译安装包（Windows: Inno Setup / Linux: debkit 经 WSL 打 deb）
```

规则：

- 每个包独立选择构建管线、加密、初始化、manifest 更新、下载和版本保存策略。
- 资源包表格可按包勾选「清单加密」（`ManifestEncrypted`），与构建端清单 ChaCha20 加密注入联动。
- `BuiltinBuildPipeline` 只用于旧序列化数据兼容，新配置不要选择。
- 程序集包默认使用 `ArchiveFileBuildPipeline + ChaCha20`。
- 「高级」页提供「在构建输出目录生成 Catalog」开关：开启后构建完成时在 AB 输出目录额外生成 `BuiltinCatalog.bytes/json`，整目录复制即可用于 `OfflinePlayMode`（依赖 YooAsset 3.0.6+ 友元程序集）。
- 包配置表格采用延迟保存；关闭窗口、显式保存或开始构建前会落盘。
- AssetBundle 或 Player 构建失败必须中断后续安装包阶段。

操作区按「构建 / 打开目录 / 热更DLL / 设置 / 构建日志」分区组织；「打开目录」提供 AB 输出、Player 输出与发布目录直达按钮。

## BuildCLI（外置 Python 构建工具）

位置：`UnityProject/BuildCLI/`（Python 3.10+，PySide6 GUI，随项目走）。启动：`UnityProject\BuildCLI\build_gui.bat`。

不打开 Unity Editor 的命令行/GUI 构建入口，与打包窗口共用同一套 `ReleaseTools` 构建链路（Unity 侧唯一入口 `Assets/TEngine/Editor/ReleaseTools/CLIBridge.cs`，`-tengineConfig=<json>` 驱动，退出码回传结果），不会另建平行构建实现。

- GUI 覆盖打包窗口「构建」相关能力：热更 DLL（编译拷贝 / GenerateAll / 同步 AOT 清单 / 拷贝 AOT DLL）、构建 AB / 一键 AB+Player / 仅 Player / 发布整理 / 切换平台、统一/独立版本号模式（可从上次构建读取每包版本）、AB 输出目录、发布整理、高级项。
- 项目目录与 `Unity.exe` 可配置，目录校验与编辑器版本联动；配置存 `BuildCLI/presets/*.json`，对 Unity `.asset` 只读（「从 Unity 窗口配置读取」为单向导入）。
- `--no-gui` 纯命令行模式适合 CI：`python -m tengine_build --no-gui run --action buildAb --target StandaloneWindows64 --preset 名字`。
- batchmode 是独立 Unity 进程，**必须先关闭已打开的 Editor**（Library 锁互斥）；切平台走独立进程冷启动，避开窗口模式切平台的 domain reload。
- Player 构建需要 GPU，不使用 `-nographics`。
- 每次构建在 `BuildCLI/logs/<时间戳>/` 留存 `unity.log` + `build_request.json`。
- 安装包已纳入 CLI：`buildInstaller` 动作用 `--iss-script` 选 iss 配置；`buildLinuxInstaller` 动作用 `--debkit-conf` / `--debkit-distro` 选 conf 与 WSL 发行版。三个字段随预设与 `_last_session` 保存。

### 批量执行（多步骤队列）

预设保持纯表单快照；新增独立的**批量任务**（`BuildCLI/batches/*.json`，有序动作队列 + 绑定预设 + 失败即停）。`generateAll` / `switchPlatform` 触发域重载，各自独立成一个 Unity 进程；其余动作合并到**同一进程内顺序执行**（`CLIBridge` 收 `actions[]` 循环 `Execute`）。首包 4 步从 4 次冷启动降到 2 次，日常热更 3 步降到 1 次。

- GUI「批量执行」标签页：任务下拉 + 步骤编辑器（左列可用动作双击添加、右列队列上移/下移/移除）+ ▶ 运行 + 横幅显示 `第 2/4 步：热更DLL（段 2/2）`，失败停在出错步骤。
- CLI：`--batch 名称` 加载任务队列，或可重复 `--action hotfixDll --action buildAb` 临时组队。单 `action` 请求向后兼容。

详细说明见 [resource-build.md](../../../../../Books/Fork/resource-build.md) 的「批量执行」一节。

详细说明见 [resource-build.md](../../../../../Books/Fork/resource-build.md) 的「BuildCLI」一节。

## 构建产物

```text
Releases/
├── Bundles/
├── Windows/
│   ├── setup.iss
│   ├── setup.<配置名>.iss    # 每份配置的编辑脚本，默认配置名 generated
│   ├── build/
│   └── setup/
├── Linux/
│   ├── build/               # Linux Player 产物
│   ├── debkit.sh            # 一键打 deb 入口
│   ├── appConfigs/           # deb 变体配置（*.conf）
│   └── output/               # .deb 产物
├── Android/build/
├── IOS/build/
├── MacOS/build/
├── WebGL/build/
└── Publish/{平台}/{包名}/
```

- AssetBundle 默认输出到 `Releases/Bundles/`。
- 发布整理默认输出到 `Releases/Publish/`。
- 所有平台 Player 统一输出到 `Releases/{平台}/build/`，使用项目根相对路径（`./` 前缀）。
- 旧的 `Output/Player/{平台}/` 路径会在打开窗口时自动迁移到 `Releases/{平台}/build/`。
- 发布目录使用运行时平台名，例如 `Windows64`，不是 Unity 的 `StandaloneWindows64`。

## Inno Setup

- `Releases/Windows/setup.iss` 是版本控制模板。
- `setup.generated.iss` 是实际编译脚本，日常参数只写入 generated 文件。
- `MyAppId` 只在模板中手工维护，决定安装包升级身份。
- `MyAppEnglishName` 用于默认安装目录，`MyAppName` 用于展示。
- ISCC 查找顺序为：用户路径 -> 注册表 -> PATH -> Program Files。
- 超时、异常和构建失败路径都必须清理 Unity 进度条。

详细说明见 [resource-build.md](../../../../../Books/Fork/resource-build.md)。

## Linux 安装包（debkit + WSL）

- `Releases/Linux/debkit.sh` 把 Linux Player 目录封装成 `.deb`：装完自带开始菜单项、桌面快捷方式、可开关的开机自启、画质档位与图形接口切换；`lib/`、`templates/`、`appConfigs/` 随脚本分发，用法见目录内 README/NOTES。
- 打包窗口「安装包配置 → 平台选择 Linux」提供 WSL 发行版下拉（调 `wsl.exe -l -v` 探测，UTF-16LE 解码、缓存 10 分钟、带「刷新列表」）与「构建安装包」按钮：写 `appConfigs/<包名>.conf` 后在选定发行版执行 debkit，stdout/stderr 分帧回显到构建日志，结束后校验并报出 `output/<包名>_<版本>_<架构>.deb` 路径与体积。
- 「快速构建」的 Linux 安装包阶段走同一条执行链路；打包命令同时给出 Windows 侧（`wsl.exe -d ... -- bash -lc`）与 WSL 内两种形态。
- 前置条件：本机 WSL 至少一个发行版；`Releases/Linux/build/` 已有 Linux Player 产物（缺失时先构建 Linux Player）。
- CLI：`python -m tengine_build --no-gui run --action buildLinuxInstaller --debkit-conf example.conf --debkit-distro ai-dev`。

## 安装包多配置（setup.<配置名>.iss / appConfigs/*.conf）

- **Windows**：`setup.iss` 是唯一模板，永不回写；每份配置对应一份编辑脚本 `setup.<配置名>.iss`（默认配置名 `generated`）。面板「iss 配置」下拉切换后，先按落盘脚本载入 6 项 `#define`（应用名 / 英文名 / 发布者 / 密码 / 水印），版本与 exe 名由构建自动写入；构建只回写 `setup.<配置名>.iss`，因此选中哪份就改哪份。
- **Linux**：`Releases/Linux/appConfigs/*.conf` 每份 conf 是一个变体。面板「构建配置」下拉切换后按落盘文件回填字段；「写入配置 / 从配置回读 / 打包」三处都作用于当前选中的 conf，实际执行为 `debkit.sh -c appConfigs/<配置名>.conf`。
- **从模板创建（两平台一致）**：两个下拉旁边的「从模板创建」按钮会先弹命名框，填好配置名后——Windows 以 `setup.iss` 为模板生成 `setup.<配置名>.iss` 并切换过去（同名只切换、不覆盖）；Linux 以 `example.conf` 的键集为模板、用当前面板字段生成 `appConfigs/<配置名>.conf` 并切换过去（同名会先确认是否覆盖）。
- 两个选择分别保存在 `BuildPipelineSetting.asset` 的 `InstallerIssScript`（配置名）/ `DebkitConfFile`，BuildCLI 预设同名保存。

详细说明见 [resource-build.md](../../../../../Books/Fork/resource-build.md)。

## 构建模式与 Obfuz

| 工具 | 菜单 |
| --- | --- |
| 构建模式窗口 | `TEngine/Build/构建模式窗口` |
| 混淆配置窗口 | `TEngine/Build/混淆配置窗口` |

- `ENABLE_RELEASE` 表示 dev/release 模式。
- `ENABLE_OBFUZ` 控制是否执行混淆，两者相互独立。
- dev 模式可生成和加载 PDB；release 模式不生成 PDB。
- 多态参数首次发布或修改后必须执行 `HybridCLR/ObfuzExtension/GenerateAll`。

详细说明见 [obfuscation.md](../../../../../Books/Fork/obfuscation.md)。

## AOT 与热更新工具

- 同步 AOT 清单：`HybridCLR/Build/Sync AOT Metadata Manifest`。
- 编译拷贝入口会先同步并校验 AOT 清单，再处理 dev/release、Obfuz 和多态 DLL。
- 热更新版本记录：`TEngine/HotUpdate/Package Version PlayerPrefs`。
- 版本工具只删除 `RuntimePackages` 展示的 `VersionKey`，不要使用 `PlayerPrefs.DeleteAll()`。

## SceneEnumConfig

菜单：`TEngine/场景枚举配置`，打开 Odin 配置窗口 `SceneEnumConfigWindow` 直接编辑配置（无需再去 Assets 里找 .asset 资源）；窗口「体检」页列出阻断性错误与警告。

配置资产固定存放 `Assets/Editor/SceneTools/SceneEnumGenerator/SceneEnumConfig.asset`（与工具代码同目录，Editor-only，不落 `Resources`、不进 Player 包）。引用方统一走 `SceneEnumConfig.Load()` / `LoadOrCreate()`，路径常量 `SceneEnumConfig.ConfigAssetPath`。

工作流：

1. 同步 YooAsset `Scenes` Group 收集目录。
2. 检查场景新增、改名和删除。
3. 编辑稳定的 `EnumName`、`EnumValue` 和备注。
4. 生成 `SceneType.g.cs`、`SceneConstName.g.cs`、`SceneTypeMapping.g.cs` 和 `SceneTypeDisplayName.g.cs`（落盘到 `Module/GameScene/Generated/`）。

约束：

- 场景使用 GUID 跟踪，改名不改变已经发布的枚举值。
- 枚举值由场景 GUID 确定性派生（非本地计数器），多人各自新增场景不会撞值；删除的枚举值不会被复用（值随身份固定）。
- 合并他人的场景改动后，需点一次「同步场景资源」+「生成枚举代码」再提交。
- 生成前确认场景位于 YooAsset 收集范围内。
- 修改生成器后等待 Editor 程序集重编译完成，再执行生成。

详细说明见 [scene-system.md](../../../../../Books/Fork/scene-system.md)。

## 常用调试入口

| 功能 | 菜单或位置 |
| --- | --- |
| GameObjectPool 调试 | `TEngine Tools/Debugger/GameObject Pool` |
| 日志目录 | `TEngine/Open Folder/Log Files Path` |
| DataBinding 生成 | `Tools/数据绑定/生成` |
| DataBinding 面板 | `Tools/数据绑定/生成器面板` |

构建窗口中的 AOT 同步、DLL 编译和发布整理应复用 `ReleaseTools` 与 `BuildDLLCommand`，不要另建平行构建链路。

## Editor 代码边界

- 热更组件 Inspector 放在 `Assets/Editor/`。
- 框架构建工具放在 `Assets/TEngine/Editor/`。
- 运行时程序集不能引用 Odin Editor 类型。
- 自动生成文件、构建产物和 `setup.generated.iss`（默认配置的编辑脚本，由面板/构建写入）不作为手工源文件维护；其余 `setup.<配置名>.iss` 变体与 `appConfigs/*.conf` 可直接手工编辑。
