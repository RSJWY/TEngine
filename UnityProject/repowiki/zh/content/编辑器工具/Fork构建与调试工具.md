# Fork 构建与调试工具

本文汇总当前 fork 的打包窗口、发布目录、Inno Setup、AOT/Obfuz 辅助工具、场景枚举和调试入口。详细迁移历史见 [Fork 定制改动总览](../../../../../Books/Fork/README.md)。

## 打包工具窗口

菜单：`Build/打包工具窗口`。

窗口基于 Odin，构建配置与运行时包配置共同读写 `UpdateSetting.RuntimePackages`。不要维护第二份资源包列表。

执行流程：

```text
编译热更 DLL -> 构建资源包 -> 发布整理 -> 最小包处理
-> 构建 Player -> 编译 Inno Setup 安装包
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
- 安装包（InnoSetup）构建暂未纳入，仍走打包窗口。

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
│   ├── setup.generated.iss
│   ├── build/
│   └── setup/
├── Linux/build/
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

菜单：`TEngine/场景枚举配置`。

工作流：

1. 同步 YooAsset `Scenes` Group 收集目录。
2. 检查场景新增、改名和删除。
3. 编辑稳定的 `EnumName`、`EnumValue` 和备注。
4. 生成 `SceneType.g.cs`、`SceneConstName.g.cs` 和 `SceneTypeMapping.g.cs`。

约束：

- 场景使用 GUID 跟踪，改名不改变已经发布的枚举值。
- 新值按 `max + 1` 分配；删除的枚举值不复用。
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
- 自动生成文件、构建产物和 `setup.generated.iss` 不作为手工源文件维护。
