# 运行时配置

本页记录 fork 中围绕轻量配置、部署配置和 TOML 序列化的改动。

## 轻量运行时配置模块

### 背景

项目保留原有 Luban `ConfigSystem`，但部署配置、工具配置和小型业务配置不一定适合进入配置表体系。`RuntimeConfigModule` 用于补充这类轻量文本配置读取需求。

TOML 作为默认人工编辑格式，适合部署地址、调试开关、多屏窗口等简单配置；JSON 仍可用于结构较复杂、机器生成或已有兼容需求的配置文件。

### 改动摘要

- `JsonConfigModule` 通用化并重命名为 `RuntimeConfigModule`。
- 对外接口从 `IJsonConfigModule` 改为 `IRuntimeConfigModule`。
- 热更层统一通过 `GameModule.Config` 访问运行时配置模块。
- 从 `StreamingAssets/Configs` 读取配置。
- 默认按 `config_manifest.toml` 清单声明需要加载的配置文件。
- 清单读取保留 `config_manifest.json` 回退，便于旧包过渡。
- 支持 `.toml` 与 `.json` 混用，按文件扩展名选择 `Utility.Toml` 或 `Utility.Json` 反序列化。
- 支持统一加载并缓存原始配置文本。
- 支持强类型 `Get<T>` / `TryGet<T>`。
- 支持原始文本 `GetText` / `TryGetText`。
- 支持 `Contains`、`Clear`、`ReloadAsync`。
- 内置对象缓存，缓存键为 `"配置名:类型全名"`，同名配置可按不同类型分别缓存。
- 远程或 Android 路径含 `://` 时走 `UnityWebRequest`。
- 本地路径切线程池使用 `File` 同步读取，读完切回主线程。
- JSON 序列化默认切换为 Newtonsoft，同时保留 `DefaultJsonHelper` 可回退。

### 使用方式

```csharp
await GameModule.Config.LoadAllAsync();
var cfg = GameModule.Config.Get<DeployConfig>();
```

目录结构：

```text
Assets/StreamingAssets/Configs/
├── config_manifest.toml
├── DeployConfig.toml
└── ComplexConfig.json
```

清单示例：

```toml
files = [
  "DeployConfig.toml",
  "ComplexConfig.json",
]
```

TOML 配置示例：

```toml
ResDownloadPath = "http://127.0.0.1:80/ProjectHotupdate"
FallbackResDownloadPath = "http://127.0.0.1:80/ProjectHotupdate"
DebuggerActiveWindow = "OnlyOpenWhenDevelopment"
```

### 注意事项

- `config_manifest.toml` 写错或清单中配置文件缺失时，`LoadAllAsync()` 会抛异常。
- `ProcedureLaunch` 会捕获运行时配置加载异常，并回退 `UpdateSetting` / Inspector 默认值继续启动。
- 单个配置文件的 TOML/JSON 语法错误通常在 `TryGet<T>()` 解析时暴露；解析失败会记录 warning 并返回 `false`。
- 少填字段通常使用 DTO 字段默认值或初始化值。
- 字段名拼错通常等价于未填写该字段，需要调用方或后续校验逻辑兜底。
- 类型写错会导致解析失败，调用方应按 `TryGet<T>() == false` 处理 fallback。

### 关键文件

- `Assets/TEngine/Runtime/Module/RuntimeConfigModule/IRuntimeConfigModule.cs`
- `Assets/TEngine/Runtime/Module/RuntimeConfigModule/RuntimeConfigModule.cs`
- `Assets/TEngine/Runtime/Module/RuntimeConfigModule/RuntimeConfigManifest.cs`
- `Assets/TEngine/Runtime/Extension/Json/NewtonsoftJsonHelper.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-06-02-json-config-deploy-summary.md`

## 部署配置覆盖热更地址

### 背景

打包后经常需要按现场环境调整资源服务器地址。如果地址只在 Inspector 或 Prefab 中配置，每次调整都要重新出包。

### 改动摘要

- 新增明文配置 `StreamingAssets/Configs/DeployConfig.toml`。
- `DeployConfig` 提供 `ResDownloadPath` / `FallbackResDownloadPath`。
- `UpdateSetting.GetResDownLoadPath()` / `GetFallbackResDownLoadPath()` 优先读取部署配置。
- 配置为空、模块未加载或解析异常时回退 Inspector 默认值。
- `ProcedureLaunch` 在资源初始化之前加载部署配置，确保获取远程地址时已读到现场地址。

### 配置示例

```toml
ResDownloadPath = "http://127.0.0.1:8081"
FallbackResDownloadPath = "http://127.0.0.1:8082"
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

`Debugger.Start()` 早于 `RuntimeConfigModule` 加载完成，因此不能在 `Start` 内直接读部署配置。

当前流程是：

```text
Debugger.Start()
  └─ 先按 Inspector 默认策略初始化

ProcedureLaunch.LoadDeployConfigAsync()
  └─ 部署配置加载完成后，二次覆盖 Debugger 激活策略
```

这与 `UpdateSetting` 消费 `ResDownloadPath` 的时机保持一致。

### 配置示例

```toml
ResDownloadPath = "http://127.0.0.1:80/ProjectHotupdate"
FallbackResDownloadPath = "http://127.0.0.1:80/ProjectHotupdate"
DebuggerActiveWindow = "OnlyOpenWhenDevelopment"
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

`Utility.Toml` 不替换 Luban `ConfigSystem`。它提供 TOML 文本和对象之间的序列化能力，文件加载和缓存由 `RuntimeConfigModule` 负责。

### 关键文件

- `Assets/TEngine/Runtime/Extension/Toml/Utility.Toml.cs`
- `Assets/TEngine/Runtime/Extension/Toml/Utility.Toml.ITomlHelper.cs`
- `Assets/TEngine/Runtime/Extension/Toml/TomlynTomlHelper.cs`
- `Assets/TEngine/Runtime/Module/RootModule.cs`
