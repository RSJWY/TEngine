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

## 注意事项

- 本次没有替换 `RootModule.logHelperTypeName`，仍保留 `TEngine.DefaultLogHelper`，降低场景/Prefab 序列化变更风险。
- Unity 2021.3.45f1 当前环境未发现 `UnityEngine.HideInCallstackAttribute`，因此没有强行使用该特性。
- 当前工作区仍存在其他未纳入本次提交的改动，如场景、Prefab、热更 DLL、Publish 等，需要后续另行处理。
