# 2026-06-02 轻量 JSON 配置模块与部署地址覆盖会话总结

## 背景

项目原本自带 Luban `ConfigSystem`，用户明确说明：**不移除、不替换现有 ConfigSystem**，而是新增一套轻量 JSON 配置模块，用于从 `StreamingAssets` 下读取 JSON 配置。

本会话中用户进一步说明真实需求：`UpdateSetting` 里的两个资源服务器地址在打包后需要现场修改：

```csharp
[SerializeField]
private string ResDownLoadPath = "http://127.0.0.1:8081";

[SerializeField]
private string FallbackResDownLoadPath = "http://127.0.0.1:8082";
```

因此轻量 JSON 配置模块的首个实际用途是：**让打包后的 PC 现场能通过明文 JSON 覆盖热更资源服务器地址**。

## 已查询和确认的规范

按照项目 `CLAUDE.md` 强制流程，本任务属于 L3/L4，已触发 `tengine-dev` skill，并直接阅读了相关代码确认：

- `Assets/TEngine/Runtime/Core/ModuleSystem.cs`
- `Assets/TEngine/Runtime/Core/Module.cs`
- `Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs`
- `Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs`
- `Assets/GameScripts/HotFix/GameLogic/SingletonSystem/Singleton.cs`
- `Assets/GameScripts/HotFix/GameLogic/SingletonSystem/SingletonSystem.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/GameScripts/Procedure/ProcedureLaunch.cs`
- `Assets/GameScripts/Procedure/ProcedureInitPackage.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `Assets/TEngine/Runtime/Extension/Json/Utility.Json.cs`
- `Assets/TEngine/Runtime/Extension/Json/DefaultJsonHelper.cs`
- `Assets/TEngine/Runtime/Module/RootModule.cs`

关键结论：

1. **业务访问模块必须通过 `GameModule.XXX`**，业务代码禁止直接 `ModuleSystem.GetModule<T>()`。
2. `GameModule` 内部封装可以调用 `ModuleSystem.GetModule<T>()`，这与现有 `GameModule.Resource` / `Audio` / `Timer` 一致。
3. 热更层 `GameLogic` 自定义模块规范是 `Singleton<T>`（如 `UIModule : Singleton<UIModule>`），由 `SingletonSystem` 管理。
4. TEngine 框架层 `TEngine.Runtime` 内置模块规范是 `Module + IxxxModule + ModuleSystem`，例如 `TimerModule` / `SceneModule`。
5. 用户最终明确选择：**JsonConfigModule 放到 `TEngine.Runtime` 内，非热更**。

## 已完成并提交到 GitHub 的内容

已提交并推送：

- commit：`5d71680e 新增轻量 JSON 配置模块并接入 Newtonsoft`
- 推送目标：`origin/main`

该提交包含：

### 1. Newtonsoft 接入 `Utility.Json`

新增：

- `Assets/TEngine/Runtime/Extension/Json/NewtonsoftJsonHelper.cs`
- `Assets/TEngine/Runtime/Extension/Json/NewtonsoftJsonHelper.cs.meta`

修改：

- `Assets/TEngine/Runtime/Module/RootModule.cs`
  - 默认 `jsonHelperTypeName` 从 `TEngine.DefaultJsonHelper` 改为 `TEngine.NewtonsoftJsonHelper`
- `Assets/TEngine/Settings/Prefab/GameEntry.prefab`
  - 序列化字段 `jsonHelperTypeName` 同步改为 `TEngine.NewtonsoftJsonHelper`

保留：

- `DefaultJsonHelper` 未删除，可回退。

注意：`Newtonsoft.Json.dll` 是通过 `Assets/Plugins/netstandard2.0/` 导入，不是 UPM 包。`Packages/manifest.json` 中没有重复的 `com.unity.nuget.newtonsoft-json`。

### 2. 新增框架层 `JsonConfigModule`

最终位置：

```text
Assets/TEngine/Runtime/Module/JsonConfigModule/
├── IJsonConfigModule.cs
├── IJsonConfigModule.cs.meta
├── JsonConfigManifest.cs
├── JsonConfigManifest.cs.meta
├── JsonConfigModule.cs
└── JsonConfigModule.cs.meta
```

最终规范：

```csharp
namespace TEngine
{
    public interface IJsonConfigModule { ... }

    internal sealed class JsonConfigModule : Module, IJsonConfigModule
    {
        public override void OnInit() { }
        public override void Shutdown() { Clear(); }
    }
}
```

访问入口：

```csharp
public static IJsonConfigModule JsonConfig => _jsonConfig ??= Get<IJsonConfigModule>();
```

在：

- `Assets/GameScripts/HotFix/GameLogic/GameModule.cs`

说明：

- 最早曾在 `GameLogic/Module/JsonConfigModule` 下实现为 `Singleton<JsonConfigModule>`，后用户指出 TEngine 内有程序集，最终按用户要求迁移至 `TEngine.Runtime`。
- 热更层旧目录已删除。
- 最终 `GameModule.JsonConfig` 与 `GameModule.Resource` / `Audio` / `Timer` 访问风格一致。

## 本会话后半段已实现但尚未提交的改动

用户初步测试通过后，继续要求实现部署地址覆盖功能，并确认：

- 方案选 **A：仅 StreamingAssets**
- 不考虑 Android 平台
- `StreamingAssets/Configs` 目录以后还要继续放配置，所以不能被 git 忽略

### 1. `.gitignore` 调整

原来有两处忽略 `StreamingAssets`：

```gitignore
/[Aa]ssets/StreamingAssets
/[Aa]ssets/StreamingAssets.meta
...
[Aa]ssets/StreamingAssets/
[Aa]ssets/StreamingAssets.meta
```

已调整为放行 `Assets/StreamingAssets/Configs/`，同时继续忽略其他 StreamingAssets 内容（如 `package/` YooAsset 产物）。

注意：被忽略目录要恢复子目录，必须把父目录规则从整体忽略改成 `/*` 留口子，再写 `!Configs/` 例外。

验证结果：

- `Assets/StreamingAssets/Configs/DeployConfig.json` 已放行
- `Assets/StreamingAssets/Configs/config_manifest.json` 已放行
- `Assets/StreamingAssets/package/foo` 仍忽略
- `Assets/StreamingAssets/package.meta` 仍忽略

### 2. 新增 `DeployConfig`

新增文件：

- `Assets/TEngine/Runtime/Core/DeployConfig.cs`
- `Assets/TEngine/Runtime/Core/DeployConfig.cs.meta`

内容：

```csharp
namespace TEngine
{
    [Serializable]
    public sealed class DeployConfig
    {
        public string ResDownloadPath;
        public string FallbackResDownloadPath;
    }
}
```

用途：让 `StreamingAssets/Configs/DeployConfig.json` 覆盖 `UpdateSetting` 里的两个服务器地址。

### 3. `UpdateSetting` 支持外部覆盖

修改：

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`

逻辑：

- `GetResDownLoadPath()` 优先读 `DeployConfig.ResDownloadPath`
- `GetFallbackResDownLoadPath()` 优先读 `DeployConfig.FallbackResDownloadPath`
- 读不到、模块未加载、字段为空、解析异常时，回退 Inspector 默认字段：
  - `ResDownLoadPath`
  - `FallbackResDownLoadPath`

新增私有方法：

```csharp
private static DeployConfig GetDeployOverride()
{
    try
    {
        var configModule = ModuleSystem.GetModule<IJsonConfigModule>();
        if (configModule == null || !configModule.IsLoaded)
        {
            return null;
        }

        return configModule.TryGet<DeployConfig>(out var deployConfig, "DeployConfig") ? deployConfig : null;
    }
    catch (Exception)
    {
        return null;
    }
}
```

说明：

- 这里在 `UpdateSetting` 内部使用 `ModuleSystem.GetModule<IJsonConfigModule>()`，属于框架内部/主包流程，不是业务代码直接使用，符合规范。

### 4. `ProcedureLaunch` 在资源初始化前加载部署配置

修改：

- `Assets/GameScripts/Procedure/ProcedureLaunch.cs`

关键点：

- 增加 `using System;`
- 增加 `using Cysharp.Threading.Tasks;`
- 增加字段：

```csharp
private bool _deployConfigLoaded;
```

- `OnEnter` 中启动：

```csharp
LoadDeployConfigAsync().Forget();
```

- `OnUpdate` 等 `_deployConfigLoaded == true` 才切 `ProcedureSplash`
- 新增：

```csharp
private async UniTaskVoid LoadDeployConfigAsync()
{
    try
    {
        await ModuleSystem.GetModule<IJsonConfigModule>().LoadAllAsync();
    }
    catch (Exception exception)
    {
        Log.Error("Load deploy config failed, fallback to UpdateSetting defaults. reason {0}", exception.ToString());
    }

    _deployConfigLoaded = true;
}
```

意义：确保 `ProcedureInitPackage` / `ResourceModule.InitPackage()` 获取远程地址前，部署配置已经加载完。

### 5. `GameApp` 清理重复加载

修改：

- `Assets/GameScripts/HotFix/GameLogic/GameApp.cs`

早前曾在热更入口 `StartGameLogicAsync()` 里加载 JsonConfig。现在部署配置已在主包 `ProcedureLaunch` 加载，`GameApp` 里不再重复加载，恢复为：

```csharp
private static void StartGameLogic()
{
    // 部署配置已在主包 ProcedureLaunch（资源初始化前）加载，此处直接使用 GameModule.JsonConfig 即可
    GameModule.UI.ShowUIAsync<BattleMainUI>();
}
```

并移除了 `System` / `Cysharp.Threading.Tasks` using。

### 6. `StreamingAssets/Configs` 配置文件

新增/调整：

```text
Assets/StreamingAssets/Configs/
├── config_manifest.json
├── config_manifest.json.meta
├── DeployConfig.json
└── DeployConfig.json.meta
```

`config_manifest.json` 最终应为：

```json
{
  "files": [
    "DeployConfig.json"
  ]
}
```

`DeployConfig.json`：

```json
{
  "ResDownloadPath": "http://127.0.0.1:8081",
  "FallbackResDownloadPath": "http://127.0.0.1:8082"
}
```

注意：

- 之前曾新增过 `DebugConfig.json` 和 `DebugConfig.json.meta` 作为示例。
- 用户询问用途后已解释：`DebugConfig.json` 只是演示用，对部署地址覆盖无用。
- 已删除 `DebugConfig.json` 和 `DebugConfig.json.meta`，并从 `config_manifest.json` 移除。
- 本次请求被用户中断时，删除和 manifest 修改已经执行完成，但还未提交。

## 校验状态

已做校验：

- `git diff --check`：无实际格式错误，只有 LF/CRLF 警告
- Unity `.meta` GUID 重复检查：`dups: 0`
- `GameApp` 中已无 `JsonConfig` / `UniTaskVoid` / `StartGameLogicAsync` 残留（只剩一条注释提到 `GameModule.JsonConfig`）
- `UpdateSetting` 和 `ProcedureLaunch` 已能 grep 到关键引用：
  - `DeployConfig`
  - `IJsonConfigModule`
  - `LoadDeployConfigAsync`
- 用户反馈：**“初步测试可以了。”**

## 当前 Git 状态（写总结前）

当前最近提交：

```text
5d71680e 新增轻量 JSON 配置模块并接入 Newtonsoft
b48868fd 完善热更版本记录清理窗口
2c1fca99 添加热更版本记录清理菜单
```

当前未提交的本次相关文件：

- `.gitignore`
- `Assets/GameScripts/HotFix/GameLogic/GameApp.cs`
- `Assets/GameScripts/Procedure/ProcedureLaunch.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/TEngine/Runtime/Core/DeployConfig.cs`
- `Assets/TEngine/Runtime/Core/DeployConfig.cs.meta`
- `Assets/StreamingAssets/Configs/config_manifest.json`
- `Assets/StreamingAssets/Configs/config_manifest.json.meta`
- `Assets/StreamingAssets/Configs/DeployConfig.json`
- `Assets/StreamingAssets/Configs/DeployConfig.json.meta`
- `Assets/StreamingAssets/Configs.meta`
- 可能还有 `Assets/StreamingAssets.meta`（当时未确认是否需纳入；`StreamingAssets` 目录原本存在但 meta 未跟踪）

当前未提交但与本次无关/需排除的文件：

- `.claude/settings.local.json`
- `Assets/AssetRaw/UI/BattleMainUI.prefab`
- `Assets/Scenes/main.unity`
- `ProjectSettings/HybridCLRSettings.asset`
- `Assets/AssetRaw/DLL/*.dll.bytes` 及其 `.meta`（HybridCLR 产物）
- `Publish/`

## 后续建议

1. 用户原本要求“提交 GitHub”，但请求被中断后转为“总结本会话”。后续如果继续，建议先检查当前 `git status`，只暂存上述本次相关文件，避免把无关场景、Prefab、DLL 产物提交进去。
2. `DebugConfig.json` 已删除，`config_manifest.json` 只保留 `DeployConfig.json`。若后续还要加入其他配置，记得同时写入 `config_manifest.json` 的 `files` 列表。
3. 因 `StreamingAssets/Configs` 已被 `.gitignore` 放行，后续该目录里的业务配置会进入版本控制；`StreamingAssets/package` 等 YooAsset 产物仍应保持忽略。
4. 如果用户需要最终提交，建议提交信息可用：

```text
支持部署配置覆盖热更地址

- 放行 StreamingAssets/Configs 目录用于入库部署配置
- 新增 DeployConfig 并在 UpdateSetting 中优先读取外部地址
- ProcedureLaunch 在资源初始化前加载 JsonConfigModule
- 保留 Inspector 默认地址作为兜底
```
