# 2026-05-30 资源包发布整理流程优化会话总结

## 背景

本次会话围绕资源包构建产物“如何快速整理并上传”展开，重点目标包括：

1. 为 TEngine 打包工具增加构建后的发布整理能力，减少手工拷贝。
2. 避免运行时平台目录名与 YooAsset 构建目录名不一致导致的 404。
3. 支持对历史已构建版本执行“仅发布整理”，方便重复上传。
4. 优化打包工具界面的提示表达，避免用户误把目录规则中的包名占位符理解为输入项。

项目规则要求 L2-L4 任务先查询 `tengine-dev` skill。本会话已查询资源加载、热更工作流与架构相关规范，并结合实际代码确认构建输出与远端目录拼接逻辑。

## 本次改动

### 1. 新增发布整理配置与构建后自动整理

主要内容：

- 在 `BuildConfig` 中新增发布整理相关配置：
  - `EnablePublishCopy`
  - `PublishRoot`
  - `CleanPublishPackageDirectory`
- 在打包窗口中新增“发布整理”面板。
- 构建成功后，按包将构建结果整理到统一发布目录。

关键文件：

- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`

### 2. 统一发布目录的平台名规则

问题现象：

- YooAsset/Unity 构建输出目录使用 `BuildTarget` 风格平台名，例如：
  - `StandaloneWindows64`
  - `StandaloneOSX`
  - `iOS`
- 运行时远端地址使用 `UpdateSetting.GetPlatformName()` 返回的平台名，例如：
  - `Windows64`
  - `MacOS`
  - `IOS`
- 两套命名不一致时，如果直接按构建目录名上传，会导致远端访问 404。

修复内容：

- 在编辑器构建工具中新增 `GetRemotePlatformName(BuildTarget)`，发布整理目标目录统一使用运行时远端平台名。
- 保留构建源目录仍按 `BuildTarget` 路径读取。
- 同时补齐运行时 `Linux` 平台名分支。

关键文件：

- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`

### 3. 支持“仅执行发布整理”

主要内容：

- 在打包工具按钮区新增 `仅执行发布整理`。
- 面向历史已构建目录执行整理，不必重新打包。
- 版本选择规则：
  - 优先使用当前界面填写的 `资源版本号`
  - 若未命中且只有 1 个公共版本，则直接使用
  - 若存在多个公共版本，则弹出菜单手动选择
- 版本目录扫描会忽略 `OutputCache`。
- 只允许整理所有启用资源包都同时存在的“公共版本”，避免只整理半套资源。

关键文件：

- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`

### 4. 补充发布目录与源目录日志

主要内容：

- 构建时若启用发布整理，会在窗口日志中输出：
  - 发布目录
  - 发布平台目录
- 仅执行发布整理时，会输出：
  - 构建输出目录
  - 发布目录
  - 选中的整理版本

关键文件：

- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`

### 5. 优化发布整理面板文案，避免包名占位符误解

问题现象：

- 原先界面用 `<PackageName>` 展示目标目录，容易被误解为一个可填写输入项。

优化内容：

- 将预览改为“输出规则”：
  - `{PublishRoot}/{ProjectName}/{RemotePlatformName}/{资源包名}`
- 增加“当前包示例”，直接列出当前启用资源包的实际目录示例。
- 增加提示说明：
  - 发布整理源目录来自打包工具的 `AB输出目录/BuildConfig.OutputRoot`
  - 实际读取路径是 `{OutputRoot}/{BuildTarget}/{资源包名}/{Version}`
  - 不是去读取 YooAsset 的其他默认输出目录
- 修复该处新增 LINQ 调用缺少 `using System.Linq;` 的编译错误。

关键文件：

- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`

## 影响与结论

### 关于发布整理源目录

发布整理读取的是 **TEngine 打包工具配置的 AB 输出目录**，不是单独查找另一套 YooAsset 默认输出目录。

源目录规则：

- `{OutputRoot}/{BuildTarget}/{PackageName}/{Version}`

目标目录规则：

- `{PublishRoot}/{ProjectName}/{RemotePlatformName}/{PackageName}`

### 关于多版本处理

- 构建完成后自动发布整理：直接使用本次构建返回的版本目录。
- 仅执行发布整理：
  - 优先使用界面上的当前版本号
  - 否则在公共版本中自动或手动选择
- 最终整理的是“版本目录里的内容”，不会把版本号目录层原样保留到发布目录中。

### 关于用户误操作风险

- 发布整理面板中的“资源包名”不再表现为可填写占位符。
- 真正决定目录层的是 `UpdateSetting.RuntimePackages` 中的包名配置。
- 如果包名配置与服务器目录不一致，仍会导致远端 404。

## 验证情况

已做：

- 检查 `UpdateSetting.GetPlatformName()` 与构建输出平台名差异。
- 检查当前磁盘构建目录层级，确认实际结构为：
  - `Builds/StandaloneWindows64/DefaultPackage/<Version>`
  - `Builds/StandaloneWindows64/CodePackage/<Version>`
- 多次执行 `git diff --check`，未发现本次目标文件格式问题。
- 修复并确认 `BuildPipelineWindow.cs` 的 `System.Linq` 缺失问题。

未做：

- 没有在 Unity 编辑器内实际点击“仅执行发布整理”验证菜单行为。
- 没有执行完整 Unity 编译或运行时热更新流程验证。
- 没有验证服务端上传后的真实访问链路。

## 本次提交记录

本会话产生的相关提交：

- `af9481d2` 添加资源包发布整理配置。
- `096d1472` 完善资源包发布整理流程。
- `c77a3419` 补充发布整理源目录提示。

## 当前工作区注意事项

本次会话期间工作区仍存在未纳入本次功能提交的其他改动，例如：

- `Assets/AssetRaw/UI/BattleMainUI.prefab`
- `Assets/GameScripts/HotFix/GameLogic/GameApp.cs`
- `Assets/Scenes/main.unity`
- `Assets/TEngine/Settings/UpdateSetting.asset`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/HybridCLRSettings.asset`
- `Assets/AssetRaw/DLL/`
- `Publish/`

后续如果继续提交，应继续只挑选与本次资源包发布整理相关的文件，避免误带入其他 Unity 场景、配置或构建产物。
