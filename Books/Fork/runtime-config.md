# 运行时配置

本页记录 fork 中围绕轻量配置、部署配置和 TOML 序列化的改动。

## 轻量 JSON 配置模块

### 背景

项目保留原有 Luban `ConfigSystem`，但部署配置、工具配置和小型业务配置不一定适合进入配置表体系。`JsonConfigModule` 用于补充这类轻量 JSON 文件读取需求。

### 改动摘要

- 在 `TEngine.Runtime` 内新增 `JsonConfigModule`。
- 从 `StreamingAssets/Configs` 读取配置。
- 按 `config_manifest.json` 清单声明需要加载的 JSON 文件。
- 支持统一加载并缓存 JSON 配置。
- 支持强类型 `Get<T>` / `TryGet<T>`。
- 支持原始文本 `GetJson` / `TryGetJson`。
- 支持 `Contains`、`Clear`、`ReloadAsync`。
- 内置对象缓存，缓存键为 `"配置名:类型全名"`，同名配置可按不同类型分别缓存。
- 远程或 Android 路径含 `://` 时走 `UnityWebRequest`。
- 本地路径切线程池使用 `File` 同步读取，读完切回主线程。
- JSON 序列化默认切换为 Newtonsoft，同时保留 `DefaultJsonHelper` 可回退。

### 使用方式

```csharp
await GameModule.JsonConfig.LoadAllAsync();
var cfg = GameModule.JsonConfig.Get<DeployConfig>();
```

目录结构：

```text
Assets/StreamingAssets/Configs/
├── config_manifest.json
└── DeployConfig.json
```

清单示例：

```json
{
  "files": [
    "DeployConfig.json"
  ]
}
```

### 关键文件

- `Assets/TEngine/Runtime/Module/JsonConfigModule/IJsonConfigModule.cs`
- `Assets/TEngine/Runtime/Module/JsonConfigModule/JsonConfigModule.cs`
- `Assets/TEngine/Runtime/Module/JsonConfigModule/JsonConfigManifest.cs`
- `Assets/TEngine/Runtime/Extension/Json/NewtonsoftJsonHelper.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-06-02-json-config-deploy-summary.md`

## 部署配置覆盖热更地址

### 背景

打包后经常需要按现场环境调整资源服务器地址。如果地址只在 Inspector 或 Prefab 中配置，每次调整都要重新出包。

### 改动摘要

- 新增明文配置 `StreamingAssets/Configs/DeployConfig.json`。
- `DeployConfig` 提供 `ResDownloadPath` / `FallbackResDownloadPath`。
- `UpdateSetting.GetResDownLoadPath()` / `GetFallbackResDownLoadPath()` 优先读取部署配置。
- 配置为空、模块未加载或解析异常时回退 Inspector 默认值。
- `ProcedureLaunch` 在资源初始化之前加载部署配置，确保获取远程地址时已读到现场地址。

### 配置示例

```json
{
  "ResDownloadPath": "http://127.0.0.1:8081",
  "FallbackResDownloadPath": "http://127.0.0.1:8082"
}
```

### 关键文件

- `Assets/TEngine/Runtime/Core/DeployConfig.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/GameScripts/Procedure/ProcedureLaunch.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-06-02-json-config-deploy-summary.md`
- `UnityProject/conversation-summaries/2026-05-30-runtime-config-management-summary.md`

## 部署配置控制调试器开关

### 背景

`Debugger` 是场景内 MonoBehaviour，原激活策略来自 Inspector。现场环境需要打包后控制是否弹出调试器时，直接改 Inspector 不现实。

### 改动摘要

- `DeployConfig` 新增 `DebuggerActiveWindow` 字段。
- 字段取值为 `DebuggerActiveWindowType` 枚举名：
  - `AlwaysOpen`
  - `OnlyOpenWhenDevelopment`
  - `OnlyOpenInEditor`
  - `AlwaysClose`
- 字段解析大小写不敏感。
- `Debugger` 将原 `Start()` 内的激活策略 switch 抽为公共方法 `ApplyActiveWindowType(DebuggerActiveWindowType)`。
- `Start` 仍按 Inspector 的 `activeWindow` 字段初始化，外部可二次覆盖。
- `ProcedureLaunch.LoadDeployConfigAsync` 在配置加载完成后调用 `ApplyDebuggerConfig()`。
- 字段留空、值无法解析或场景内无 `Debugger` 时回退 Inspector 默认行为。

### 时序说明

`Debugger.Start()` 早于 `JsonConfigModule` 加载完成，因此不能在 `Start` 内直接读部署配置。

当前流程是：

```text
Debugger.Start()
  └─ 先按 Inspector 默认策略初始化

ProcedureLaunch.LoadDeployConfigAsync()
  └─ 部署配置加载完成后，二次覆盖 Debugger 激活策略
```

这与 `UpdateSetting` 消费 `ResDownloadPath` 的时机保持一致。

### 配置示例

```json
{
  "ResDownloadPath": "http://127.0.0.1:80/ProjectHotupdate",
  "FallbackResDownloadPath": "http://127.0.0.1:80/ProjectHotupdate",
  "DebuggerActiveWindow": "OnlyOpenWhenDevelopment"
}
```

### 关键文件

- `Assets/TEngine/Runtime/Core/DeployConfig.cs`
- `Assets/TEngine/Runtime/Module/DebugerModule/Debugger.cs`
- `Assets/GameScripts/Procedure/ProcedureLaunch.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-06-04-deployconfig-debugger-toggle-summary.md`

## TOML 序列化扩展

### 背景

JSON 对机器友好，但人工维护配置时可读性一般。TOML 更适合轻量配置、工具配置和需要人工编辑的结构化文本。

### 改动摘要

- 集成 `Tomlyn.2.9.0`。
- 在 `TEngine.Runtime` 内新增 `Utility.Toml` 门面。
- 默认 helper 为 `TomlynTomlHelper`。
- 支持对象与 TOML 文本互转：
  - `ToToml(object)`
  - `ToObject<T>(string)`
  - `ToObject(Type, string)`
- `Utility.Toml.ITomlHelper` 保留可替换接口。
- `TomlynTomlHelper` 内部使用 `TomlSerializer.Serialize` / `TomlSerializer.Deserialize`。
- 异常统一包装为 `GameFrameworkException`。
- 支持透传 `TomlSerializerOptions` 作为 `settings` 参数。
- 传入非 Tomlyn 选项类型时明确报错。
- `RootModule` 默认 `tomlHelperTypeName` 为 `TEngine.TomlynTomlHelper`，初始化时可按类型名注入 helper。

### 使用方式

```csharp
using TEngine;

public sealed class AppConfig
{
    public string Title { get; set; }
    public int Version { get; set; }
    public bool Enabled { get; set; }
}

string toml = @"
Title = ""Demo""
Version = 1
Enabled = true
";

AppConfig config = Utility.Toml.ToObject<AppConfig>(toml);
```

### 定位

`Utility.Toml` 不替换 Luban `ConfigSystem`，也不接管 `JsonConfigModule` 的文件加载和缓存职责。它只提供 TOML 文本和对象之间的序列化能力。

### 关键文件

- `Assets/TEngine/Runtime/Core/Utility/Utility.Toml.cs`
- `Assets/TEngine/Runtime/Extension/Toml/TomlynTomlHelper.cs`
- `Assets/TEngine/Runtime/Base/RootModule.cs`
