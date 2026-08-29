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
- `BuiltinBuildPipeline` 只用于旧序列化数据兼容，新配置不要选择。
- 程序集包默认使用 `ArchiveFileBuildPipeline + ChaCha20`。
- 包配置表格采用延迟保存；关闭窗口、显式保存或开始构建前会落盘。
- AssetBundle 或 Player 构建失败必须中断后续安装包阶段。

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
└── Publish/{平台}/{包名}/
```

- AssetBundle 默认输出到 `Releases/Bundles/`。
- 发布整理默认输出到 `Releases/Publish/`。
- Windows/Linux Player 输出到 `Releases/{平台}/build/`。
- Android/iOS/MacOS/WebGL Player 保持 `Output/Player/{平台}/`。
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
