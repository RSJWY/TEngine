# 2026-05-30 运行时部署配置管理方案会话总结

## 背景

项目当前通过 `UpdateSetting.asset` 配置热更远程目录。项目主要在局域网内使用，默认远程更新目录通常够用；但实际可能存在多端/多现场部署，导致打包时的热更地址、发布目录、现场实际访问地址不一致。

用户希望在尽量不大改框架的前提下，便捷修改热更地址，并进一步希望设计一个统一配置管理模块，避免后续每类配置都重复编写冗长的文件读取、解析和转换逻辑。

## 已查询规范

按项目 `CLAUDE.md` 强制工作流，本问题判定为 L4 架构/模块设计任务，已触发 `tengine-dev` skill，涉及主题：

- `resource-api.md`：YooAsset 远端服务地址、资源加载/释放规范。
- `hotfix-workflow.md`：主包流程、热更边界、热更 DLL 加载时机。
- `modules.md`：模块访问方式与 `GameModule` 使用规范。
- `naming-rules.md`：模块/系统命名与禁止模式。
- `luban-config.md`：Luban `ConfigSystem` 适用范围。

## 已确认的当前代码结构

### 热更地址现有链路

- `Assets/TEngine/Settings/UpdateSetting.asset`
  - `ResDownLoadPath: http://127.0.0.1:80/ProjectHotupdate`
  - `FallbackResDownLoadPath: http://127.0.0.1:80/ProjectHotupdate`

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
  - `GetResDownLoadPath()`：基于 `ResDownLoadPath + projectName + platform` 拼出根目录。
  - `GetFallbackResDownLoadPath()`：基于备用地址拼出根目录。
  - `GetPackageHostServerURL(packageName)`：再拼包名。
  - `GetPackageFallbackHostServerURL(packageName)`：再拼包名。

- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
  - HostPlayMode 初始化时读取：`Settings.UpdateSetting.GetPackageHostServerURL(packageName)`。
  - WebPlayMode 初始化时也读取同一组 URL。
  - YooAsset 远端服务由 `new RemoteServices(defaultHostServer, fallbackHostServer)` 创建。

### 配置系统现状

当前项目没有正式落地的通用运行时配置管理模块。

已有 Luban/`ConfigSystem` 方向主要适合游戏数据表，例如道具、关卡、技能、数值表等。规范里也明确 Luban `ConfigSystem` 位于 `GameProto`，用于数据表访问，不适合热更地址这类启动前就必须读取的外部部署配置。

热更地址必须在 `ProcedureInitPackage` / `ResourceModule.InitPackage()` 前可用，而热更 DLL 此时尚未加载，因此部署配置系统应放在主包侧，不应放在 `GameScripts/HotFix/GameLogic`。

## 推荐设计

将配置拆成两类：

1. **游戏数据表配置**
   - 继续走 Luban `ConfigSystem`。
   - 适合道具、关卡、技能、数值表。
   - 后续可按规范补 `ConfigSystem` 和业务 `XxxConfigMgr`。

2. **运行时部署配置 / 外部配置文件**
   - 新增轻量 `RuntimeConfigSystem` 或 `RuntimeConfigModule`。
   - 适合热更地址、现场编号、日志开关、本地调试参数等。
   - 位于主包侧，保证资源包初始化前可读取。

## 未完成方案：RuntimeConfigSystem

建议最小实现放在主包侧，例如：

```text
Assets/GameScripts/RuntimeConfig/
├── RuntimeConfigSystem.cs       // 统一入口
├── RuntimeConfigLoader.cs       // 文件读取
├── RuntimeConfigSerializer.cs   // JSON 转对象
└── DeployConfig.cs              // 热更/现场部署配置 DTO
```

外部配置文件示例：

```json
{
  "resDownloadPath": "http://192.168.1.100/ProjectHotupdate",
  "fallbackResDownloadPath": "http://192.168.1.101/ProjectHotupdate"
}
```

建议 API：

```csharp
public static class RuntimeConfigSystem
{
    public static UniTask LoadAsync();
    public static T Get<T>() where T : class, new();
    public static bool TryGet<T>(out T config) where T : class, new();
    public static void Clear();
}
```

部署配置 DTO：

```csharp
public sealed class DeployConfig
{
    public string ResDownloadPath;
    public string FallbackResDownloadPath;
}
```

建议启动顺序：

```text
ProcedureLaunch / ProcedureSplash
        ↓
RuntimeConfigSystem.LoadAsync()
        ↓
ProcedureInitPackage
        ↓
ResourceModule.InitPackage()
```

然后只修改 `UpdateSetting` 的地址取值逻辑，让外部配置优先，`UpdateSetting.asset` 作为兜底：

```csharp
public string GetResDownLoadPath()
{
    var overridePath = RuntimeConfigSystem.Get<DeployConfig>()?.ResDownloadPath;
    var root = string.IsNullOrWhiteSpace(overridePath) ? ResDownLoadPath : overridePath;
    return Path.Combine(root, GetProjectName(), GetPlatformName()).Replace("\\", "/");
}
```

备用地址同理。

## 设计原则

- 不改 `ResourceModule` 和 YooAsset 初始化流程。
- 不把部署配置放进热更 DLL，因为资源包初始化早于热更 DLL 加载。
- `UpdateSetting.asset` 保留默认值和编辑器调试能力。
- 外部配置只在启动时读取一次，先不做热重载和文件监听。
- 新增配置类型只新增 DTO，不再到处重复写文件读取和解析代码。
- 配置加载 IO 按规范使用 `UniTask`，避免同步大 IO 或 Coroutine。

## 后续开发建议

下一步可以直接实现：

1. 新增主包侧 `RuntimeConfigSystem`。
2. 支持从可部署目录读取 `RuntimeConfig.json` 或 `DeployConfig.json`。
3. 新增 `DeployConfig` 并作为首个配置类型。
4. 在 `ProcedureInitPackage` 前调用 `RuntimeConfigSystem.LoadAsync()`。
5. 修改 `UpdateSetting.GetResDownLoadPath()` 和 `GetFallbackResDownLoadPath()`，优先使用外部配置。
6. 保持找不到配置文件时静默回退 `UpdateSetting.asset`，避免影响现有编辑器和本地流程。

## 注意事项

- 如后续修改 `UpdateSetting` 增加序列化字段，需要同步检查 Inspector/Editor 显示与旧字段引用，避免编辑器序列化出问题。
- 若配置读取路径涉及不同平台，应优先明确 PC/Android/iOS/WebGL 的可写/可替换目录差异。
- 如果只支持 Windows 局域网部署，可优先读取可执行文件同级目录，方案更简单。
