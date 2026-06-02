# TouchSocket 日志桥接功能会话总结

日期：2026-06-02

## 背景

本次会话为当前 TEngine Unity 项目新增基于 `TouchSocket.Core` 的日志输出能力。用户提供旧项目参考脚本方向：`TouchSocketContainerUnityDebugLogger.cs` 与 `UnityLoggerBridge.cs`，并确认采用“两者都接入”的方案：TouchSocket 日志进入 Unity Console，Unity/Task/UniTask 日志通过 TouchSocket `FileLogger` 落盘。

## 关键实现

### 1. TouchSocket 日志输出到 Unity Console

新增：

- `Assets/TEngine/Runtime/Core/Log/TouchSocketContainerUnityDebugLogger.cs`
- `Assets/TEngine/Runtime/Core/Log/TouchSocketUnityLoggerExtensions.cs`

实现内容：

- `TouchSocketContainerUnityDebugLogger` 继承 `TouchSocket.Core.LoggerBase`。
- `Trace/Debug/Info` 映射到 `Debug.Log`。
- `Warning` 映射到 `Debug.LogWarning`。
- `Error/Critical` 映射到 `Debug.LogError`。
- 日志统一带 `[TouchSocket]` 前缀。
- `AddUnityDebugLogger()` 扩展支持给 `IRegistrator` / `LoggerGroup` 注册 Unity Console 日志器。

### 2. Unity 日志与异常落盘

新增：

- `Assets/TEngine/Runtime/Core/Log/UnityLoggerBridge.cs`

实现内容：

- 使用 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]` 自动初始化。
- 监听 `Application.logMessageReceivedThreaded` 捕获 Unity Console 日志。
- 监听 `TaskScheduler.UnobservedTaskException`。
- 监听 `Cysharp.Threading.Tasks.UniTaskScheduler.UnobservedTaskException`。
- 通过 `TouchSocket.Core.FileLogger` 写入本地文件。
- 默认路径：`Application.persistentDataPath/Logs/yyyy-MM-dd/`。
- 默认单文件大小：1 MB。
- 默认保留最近 3 天日志目录。
- 加入线程级重入保护，避免日志系统递归。
- 加入 `SubsystemRegistration` 静态状态重置，兼容关闭 Domain Reload 的 Editor Play Mode。

### 3. Editor Console 快速跳转过滤

修改：

- `Assets/TEngine/Runtime/Core/Utility/DefaultHelper/DefaultLogHelper.cs`

在 `LogRedirection` 过滤列表中加入新增日志桥接脚本，避免 Unity Console 双击日志时优先跳到桥接层。

### 4. TouchSocket/NuGet 依赖

用户已添加 TouchSocket/NuGet 依赖，本次保留并纳入提交：

- `Assets/NuGet.config`
- `Assets/packages.config`
- `Assets/Packages/`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `Packages/TouchSocket.Core.4.2.12/`

其中 `Assets/Packages/TouchSocket.Core.4.2.12/lib/netstandard2.1/TouchSocket.Core.dll` 是日志实现直接引用的核心依赖。

## 验证

已执行：

```bash
dotnet build TEngine.Runtime.csproj --no-restore -v:minimal
```

结果：

- `TEngine.Runtime` 构建成功。
- 0 个错误。
- 仅有 TextMeshPro 自带 `Texture2D.Resize` 过时警告，与本次改动无关。

## 使用说明

TouchSocket 配置处可注册 Unity Console 日志器：

```csharp
registrator.AddUnityDebugLogger();
```

或指定日志级别：

```csharp
registrator.AddUnityDebugLogger(LogLevel.Trace);
```

Unity/TEngine 普通日志不需要额外注册，`UnityLoggerBridge` 会自动监听并落盘。

## 提交、推送与后续合并复盘

本次没有直接提交到 `main`，而是按 Claude Code 默认安全工作流从 `main` 切出功能分支：

- 分支：`feat/touchsocket-logger`
- 提交：`f1f6a3da 新增 TouchSocket 日志桥接`
- 远端：`origin/feat/touchsocket-logger`
- PR 创建地址：`https://github.com/RSJWY/TEngine/pull/new/feat/touchsocket-logger`

### 为什么开分支

- 当前工作区存在大量非本次日志功能改动，包括场景、Prefab、热更 DLL、`Publish/`、项目设置等。
- 为避免直接污染 `main`，只将本次 TouchSocket 日志相关文件精确暂存并提交到功能分支。
- 后续可通过 PR 进行合并，便于确认依赖包入库、日志自动落盘等行为是否符合预期。

### 本次提交前做过的分拣

提交前检查暂存区共 303 个文件，误暂存检查结果：`bad_count 0`。

明确避开的无关文件/目录包括：

- `UnityProject/Assets/Scenes/`
- `UnityProject/Assets/AssetRaw/UI/*.prefab`
- `UnityProject/Assets/AssetRaw/DLL/*.dll.bytes`
- `UnityProject/Publish/`
- `UnityProject/ProjectSettings/HybridCLRSettings.asset`
- `UnityProject/CLAUDE.md`
- `UnityProject/.claude/settings.local.json`

### 后续合并时建议重点看

不需要逐行审查所有 NuGet 依赖文件，建议重点确认：

1. 是否接受 NuGetForUnity 下载的依赖包以 `Assets/Packages/` 形式入库。
2. 是否接受 `UnityLoggerBridge` 在 `BeforeSplashScreen` 自动初始化，默认捕获所有 Unity Console 日志并落盘。
3. 日志目录、保留天数、单文件大小是否符合预期：
   - `Application.persistentDataPath/Logs/yyyy-MM-dd/`
   - 3 天保留
   - 1 MB 滚动
4. TouchSocket 日志当前只提供 `AddUnityDebugLogger()` 扩展注册，没有强行接入某个网络模块初始化点。
5. 进入 Unity Play Mode 后，可通过 `Debug.Log(...)` / `Log.Info(...)` 验证本地日志文件是否生成。

### README 状态

根目录 `README.md` 已在上一提交中新增「日志系统」条目，说明 TouchSocket 日志桥接、Unity/Task/UniTask 日志落盘、重入保护、过期日志清理与 Editor Console 跳转过滤能力。后续合并该分支时 README 会一并进入 `main`。

## 注意事项

- 本次没有替换 `RootModule.logHelperTypeName`，仍保留 `TEngine.DefaultLogHelper`，降低场景/Prefab 序列化变更风险。
- Unity 2021.3.45f1 当前环境未发现 `UnityEngine.HideInCallstackAttribute`，因此没有强行使用该特性。
- 当前工作区仍存在其他未纳入本次提交的改动，如场景、Prefab、热更 DLL、Publish 等，需要后续另行处理。
