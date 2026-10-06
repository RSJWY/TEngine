# PurrNet DS 集成 — 阶段三：DS 启动流程分支

> **来源计划**：`E:\Unity\tengine-purrnet\UnityProject\.plans\purrnet-ds-integration.md`
> **状态**：阶段一、二已完成；本文件为阶段三最终定稿计划
> **创建时间**：2026-10-05
> **Plan 模式**：本文件仅为计划，未实施；实施需切换 agent

---

## 一、阶段三目标

让 DS 构建产物能真正跑起来：
1. 框架侧提供 DS 命令行解析工具（场景/端口/地址）+ DS 模式判断
2. TEngine 启动流程（Procedure 链）在 DS 包里跳过客户端专属初始化（UI/语言/声音/多屏/Splash 进度 UI）
3. 热更代码加载（`ProcedureLoadAssembly`）必须执行，不能跳过
4. 业务入口（`GameApp.StartGameLogic`）按命令行参数加载场景，NM 启动交给业务（自由度最大化）
5. 新建专用 DS 场景 `dedicated_server.unity`，挂 NM + Transport，加入 EditorBuildSettings
6. 改框架：`ScreenModule.OnInit` 在 DS 下避免触发不支持的特性

---

## 二、磁盘核实结果（2026-10-05）

| 项 | 状态 |
|---|---|
| `Assets/Launcher/Scripts/DedicatedServerLauncher.cs` | **不存在**（需新建） |
| `Assets/Launcher/Scripts/MultiInstanceLauncher.cs` | 存在（参考风格） |
| `Assets/GameScripts/Procedure/ProcedureLaunch.cs` | 无 DS 分支，`OnEnter` 调 `LauncherMgr.Initialize()` + 语言/声音 + `LoadDeployConfigAsync` |
| `Assets/GameScripts/Procedure/ProcedureSplash.cs` | 无 DS 分支，`OnUpdate` 直接 `ChangeState<ProcedureInitPackage>` |
| `Assets/GameScripts/Procedure/ProcedurePreload.cs` | 无 DS 分支，`OnEnter` 调 `LauncherMgr.ShowUI<LoadUpdateUI>` + `PreloadResources()` |
| `Assets/GameScripts/HotFix/GameLogic/GameApp.cs` | `StartGameLogic` 注册三模块后直接 `Screen.ApplyAll()` + `LoadScene(MainScene)`，无 DS 分支 |
| `GameModule.Scene`（ISceneModule） | 存在，`LoadSceneAsync(string location, ...)` 签名匹配 |
| `GameModule.GameScene`（IGameSceneModule） | 存在，`LoadScene(SceneType)` 可用 |
| `Assets/Scenes/main.unity` | 存在（客户端用，DS 不复用） |
| `Assets/Scenes/dedicated_server.unity` | **不存在**（需新建） |
| `EditorBuildSettings.scenes` | 当前只有 `main.unity`，需加入 `dedicated_server.unity` |
| `ScreenModule.OnInit` | 待读源码确认改动点（框架级改动，用户已授权"改框架"） |

---

## 三、实施步骤（6 步）

### 步骤 3.1：新建 `DedicatedServerLauncher.cs`

**文件**：`Assets/Launcher/Scripts/DedicatedServerLauncher.cs`

**风格**：仿 `MultiInstanceLauncher.cs`（静态类 + `const` 参数名 + `ResolveXxx` + `#if UNITY_STANDALONE || UNITY_EDITOR` 平台守卫）

**成员**：
| 成员 | 类型 | 说明 |
|---|---|---|
| `SceneArgumentName` | const string | `"--scene"` |
| `SceneTypeArgumentName` | const string | `"--scene-type"` |
| `PortArgumentName` | const string | `"--port"` |
| `AddressArgumentName` | const string | `"--address"` |
| `IsDedicatedServerBuild` | static bool | `#if UNITY_SERVER && !UNITY_EDITOR` → true（严格口径，框架流程判断用，与 PurrNet `isServerBuild` 同口径但独立） |
| `ResolveStartupScene()` | static string | 解析 `--scene`，未传返回 null |
| `ResolveStartupSceneType()` | static string | 解析 `--scene-type`，未传返回 null |
| `ResolvePort()` | static int | 解析 `--port`，默认 7777（业务实际通过命令行下发覆盖） |
| `ResolveAddress()` | static string | 解析 `--address`，默认 "localhost" |
| `ResolveArgument(string)` | private static | 内部工具，按参数名查下一个 token |

**关键决策**：
- 严格口径 `#if UNITY_SERVER && !UNITY_EDITOR`：框架流程只在真 DS 包跳过客户端初始化；普通客户端即使 batchmode 也走完整流程（普通客户端支持 host 模式，与 DS 代码路径不同）
- `IsDedicatedServerBuild` 是**框架流程判断工具**，与 PurrNet 的 `ApplicationContext.isServerBuild` 同口径但独立——框架侧只管"跳不跳 UI/语言/声音"，PurrNet 侧管"NM 是否 AutoStart"
- NetworkModule（阶段一）封装的 `IsDedicatedServerBuild` 是转发 PurrNet 的，供业务查询；本步骤的是框架流程用，不重复

**平台守卫**：`#if UNITY_STANDALONE || UNITY_EDITOR` 包住命令行解析代码（与 `MultiInstanceLauncher` 一致）

---

### 步骤 3.2：`ProcedureLaunch.cs` 加 DS 分支

**文件**：`Assets/GameScripts/Procedure/ProcedureLaunch.cs`

**改动位置**：`OnEnter`（当前第 27-58 行）

**改动**：
```csharp
protected override void OnEnter(ProcedureOwner procedureOwner)
{
    base.OnEnter(procedureOwner);

    // DS 模式：跳过客户端 UI/语言/声音初始化，保留 Obfuz/多开/DeployConfig
    if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
    {
        Log.Info("[ProcedureLaunch] Dedicated Server 模式，跳过客户端 UI 初始化。");

#if ENABLE_OBFUZ && !UNITY_EDITOR
        if (ObfuzRuntimeInitializer.CheckFailureAndReport())
        {
            return;
        }
#endif

        // 桌面多开：DS 同机多实例部署会用到 --yoo-instance
        string instanceId = MultiInstanceLauncher.ResolveInstanceId();
        if (!string.IsNullOrEmpty(instanceId))
        {
            _resourceModule.InstanceId = instanceId;
            Log.Info($"桌面多开实例标识：{instanceId}，资源缓存将隔离到 instance-{instanceId} 目录。");
        }

        LoadDeployConfigAsync().Forget();
        return;
    }

    // ===== 原有客户端流程保持不变 =====
    LauncherMgr.Initialize();
    // ... (InitLanguageSettings / InitSoundSettings / MultiInstanceLauncher / Obfuz / LoadDeployConfigAsync)
}
```

**保留项**（DS 分支内仍执行）：
- Obfuz 密钥检查（混淆 DLL 在 DS 上同样运行）
- `MultiInstanceLauncher.ResolveInstanceId()`（DS 同机多实例部署会用）
- `LoadDeployConfigAsync()`（DS 仍需读取服务器地址/端口配置）

**跳过项**：
- `LauncherMgr.Initialize()`（无 UI）
- `InitLanguageSettings()` / `InitSoundSettings()`（DS 无本地化/声音需求）

**流程路径**：DS 走 `OnUpdate` 路径（等 `_deployConfigLoaded` 后进 `ProcedureSplash`），不直接 `ChangeState` 跳过，保持 FSM 流程完整

---

### 步骤 3.3：`ProcedureSplash.cs` 加 DS 分支

**文件**：`Assets/GameScripts/Procedure/ProcedureSplash.cs`

**改动**：显式加 DS 分支（仅日志，便于未来扩展）：
```csharp
protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
{
    base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

    if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
    {
        Log.Info("[ProcedureSplash] Dedicated Server 模式，跳过 Splash。");
    }

    ChangeState<ProcedureInitPackage>(procedureOwner);
}
```

---

### 步骤 3.4：`ProcedurePreload.cs` 加 DS 分支

**文件**：`Assets/GameScripts/Procedure/ProcedurePreload.cs`

**改动位置**：`OnEnter`（当前第 41-52 行）+ `OnUpdate`（第 54-100 行）

**`OnEnter` 改动**：
```csharp
protected override void OnEnter(ProcedureOwner procedureOwner)
{
    base.OnEnter(procedureOwner);

    if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
    {
        Log.Info("[ProcedurePreload] Dedicated Server 模式，跳过加载 UI 显示，仍执行预加载。");
        _loadedFlag.Clear();
        PreloadResources();  // DS 仍预加载配置资产（PRELOAD 标签）
        return;
    }

    // ===== 原有客户端流程 =====
    _loadedFlag.Clear();
    LauncherMgr.ShowUI<LoadUpdateUI>(...);
    GameEvent.Send("UILoadUpdate.RefreshVersion");
    PreloadResources();
}
```

**`OnUpdate` 改动**（DS 分支跳过 UI 进度刷新，但仍走 `ChangeProcedureToLoadAssembly`）：
```csharp
protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
{
    base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

    // DS 模式：跳过 UI 进度刷新，但仍按加载完成度进入 LoadAssembly
    if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
    {
        var totalCount = _loadedFlag.Count <= 0 ? 1 : _loadedFlag.Count;
        var loadCount = _loadedFlag.Count <= 0 ? 1 : 0;
        foreach (var kv in _loadedFlag)
        {
            if (!kv.Value) break;
            loadCount++;
        }
        if (loadCount < totalCount) return;
        ChangeProcedureToLoadAssembly();
        return;
    }

    // ===== 原有客户端 UI 进度刷新流程 =====
    // ...
}
```

**关键决策**：
- DS **仍调 `PreloadResources()`**（Q8）：预加载 PRELOAD 标签的配置资产
- `ProcedureLoadAssembly` 必须执行（Q9，热更代码初始化不能跳过）
- DS 由 `OnUpdate` 的 `ChangeProcedureToLoadAssembly` 正常进入 LoadAssembly

---

### 步骤 3.5：`GameApp.StartGameLogic()` 加 DS 分支

**文件**：`Assets/GameScripts/HotFix/GameLogic/GameApp.cs`

**改动位置**：`StartGameLogic`（当前第 41-51 行）

**改动**：
```csharp
private static void StartGameLogic()
{
    ModuleSystem.RegisterModule<IUIJumpControl>(new UIJumpControl());
    ModuleSystem.RegisterModule<IGameSceneModule>(new GameSceneModule());
    ModuleSystem.RegisterModule<INetworkModule>(new NetworkModule());

    if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
    {
        StartDedicatedServer();
        return;
    }

    GameModule.Screen.ApplyAll();
    GameModule.GameScene.LoadScene(SceneType.MainScene);
}

private static void StartDedicatedServer()
{
    Log.Warning("======= Dedicated Server 启动 =======");

    string sceneName = Launcher.DedicatedServerLauncher.ResolveStartupScene();
    string sceneTypeStr = Launcher.DedicatedServerLauncher.ResolveStartupSceneType();

    if (!string.IsNullOrEmpty(sceneTypeStr)
        && System.Enum.TryParse<SceneType>(sceneTypeStr, true, out var sceneType))
    {
        Log.Info($"[DS] 按命令行 scene-type 加载场景：{sceneType}");
        GameModule.GameScene.LoadScene(sceneType);
    }
    else if (!string.IsNullOrEmpty(sceneName))
    {
        Log.Info($"[DS] 按命令行 scene 加载场景：{sceneName}");
        GameModule.Scene.LoadSceneAsync(sceneName).Forget();
    }
    else
    {
        Log.Warning("[DS] 未指定启动场景，加载默认 MainScene。");
        GameModule.GameScene.LoadScene(SceneType.MainScene);
    }

    // NetworkManager 启动由业务自行处理：
    //   方式 A：场景预挂 NM（dedicated_server.unity），PurrNet StartFlags.ServerBuild 自动启动
    //   方式 B：LoadGameObjectAsync 出 NM 预制体 → BindNetworkManager → StartServer
    // 框架侧不主动调用 StartServer()，最大化业务自由度。
    Log.Info("[DS] NetworkManager 启动由业务自行处理（场景预挂或代码动态注入）。");
}
```

**关键决策**：
- DS 分支不调 `GameModule.Screen.ApplyAll()`（Q11：DS 不需要多屏布局）
- DS 分支**不调** `GameModule.Network.StartServer()`（Q12：NM 启动交给业务，框架只提供工具）
  - 业务方式 A：场景预挂 NM，PurrNet `StartFlags.ServerBuild` 自动启动
  - 业务方式 B：`LoadGameObjectAsync` NM 预制体 → `BindNetworkManager` → `StartServer`
- 未指定场景时 fallback 到 MainScene（Q13）
- `ScreenModule` 模块仍注册（避免其他模块访问 `GameModule.Screen` 时 NRE），框架级 `OnInit` 改动见步骤 3.6b

---

### 步骤 3.7：`ProcedureInitResources.cs` 加 DS 分支（补漏）

**文件**：`Assets/GameScripts/Procedure/ProcedureInitResources.cs`

**背景**：阶段三初版计划漏列此文件。`ProcedureInitResources.OnEnter` 第 35 行无条件调用 `LauncherMgr.ShowUI<LoadUpdateUI>("初始化资源中...")`，而 DS 模式下 `ProcedureLaunch` 的 DS 分支已跳过 `LauncherMgr.Initialize()`，`m_uiRoot` 保持为 null。`LauncherMgr.ShowUI` 内部 `Object.Instantiate(obj)` 后调用 `uiWindow.transform.SetParent(m_uiRoot.transform)` 时抛 `NullReferenceException`，被 `ProcedureInitPackage.InitPackage` 的 catch 块捕获，以 `OnInitPackageFailed` 报"DefaultPackage init failed: Object reference not set to an instance of an object"退出。日志见 `Logs/2026-10-06/0000.log`。

**改动点（4 处）**：

1. **`OnEnter`**（原第 35 行）：`LauncherMgr.ShowUI<LoadUpdateUI>("初始化资源中...")` 加 DS 守卫
```csharp
if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
{
    Log.Info("[ProcedureInitResources] Dedicated Server 模式，跳过加载 UI 显示，仍执行资源初始化。");
}
else
{
    LauncherMgr.ShowUI<LoadUpdateUI>("初始化资源中...");
}
```

2. **`InitResources` 循环内**（原第 99 行）：`LauncherMgr.ShowUI<LoadUpdateUI>($"更新清单文件...")` 加 DS 守卫
```csharp
if (!Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
{
    LauncherMgr.ShowUI<LoadUpdateUI>($"更新清单文件...({runtimePackage.PackageName})");
}
```

3. **`InitResources` 内资源模式不匹配分支**（原第 205 行）：`LauncherMgr.ShowMessageBox(errorMessage, Application.Quit)` 加 DS 守卫
```csharp
if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
{
    Log.Fatal($"[ProcedureInitResources] DS 资源模式不匹配，退出。...");
    Application.Quit(1);
    return;
}
LauncherMgr.ShowMessageBox(errorMessage, Application.Quit);
```

4. **`OnInitResourcesError`**（原第 287 行）：`LauncherMgr.ShowMessageBox` 加 DS 守卫，参考 `ProcedureInitPackage.OnInitPackageFailed` 的 DS 分支
```csharp
if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
{
    Log.Fatal($"[ProcedureInitResources] DS 资源初始化失败，退出。包名：{packageName}，原因：{message}");
    Application.Quit(1);
    return;
}
LauncherMgr.ShowMessageBox(...);
```

**保留项**：DS 仍执行 `InitResources` 资源清单加载逻辑（OfflinePlayMode 走本地清单加载分支，不触网，无 UI 依赖）。

**未改（HostPlay/WebPlay 专属路径）**：`ConfirmPackageVersion`（第 221 行）、`HandleLocalPackageVersionFallback`（第 304 行）的 `ShowMessageBox` 仅在可更新模式下触发，DS 当前固定走 OfflinePlayMode，按最小改动原则暂不加守卫；若未来 DS 切到 HostPlay 再补。

---

### 步骤 3.6：新建 DS 场景 + EditorBuildSettings（unitycli + MCP）

**新增场景**：`Assets/Scenes/dedicated_server.unity`

**操作方式**：用 Unity CLI / unityMCP 操作创建（用户已确认 unitycli 和 MCP 已启用）

**场景内容**：
| GameObject | 组件 | 配置 |
|---|---|---|
| Camera | Camera | 必备（场景规范） |
| Directional Light | Light (Directional) | 必备（场景规范） |
| NetworkManager | PurrNet.NetworkManager | `startServerFlags` 含 `StartFlags.ServerBuild` |
| NetworkManager 子物体 | UDPTransport（LiteNetLib） | 端口默认 7777（Inspector 配置，业务可改） |
| NetworkManager | NetworkRules | 用默认预设，不创建自定义预设（步骤 4.3 再做） |
| NetworkManager | VisibilityRules | 用默认预设 |

**EditorBuildSettings 改动**：
- 把 `Assets/Scenes/dedicated_server.unity` 加入 `EditorBuildSettings.scenes`
- Q17：PurrNet 要求所有玩家同场景，DS 也是，必须加入构建场景列表

**注意**：
- `dedicated_server.unity` 是**框架内置测试场景**，业务实际部署时可自行改造或新建专用场景
- NetworkRules / VisibilityRules 用默认，预设资产创建留到阶段四步骤 4.3
- DS 场景的 NM `startServerFlags` 含 `ServerBuild`，DS 包运行时 PurrNet `AutoStart` 自动 `StartServer()`

---

### 步骤 3.6b：框架级改动 — `ScreenModule.OnInit` DS 保护

**授权**：用户已明确授权"改框架"（针对 `ScreenModule.OnInit`）

**待办**：
1. 读取 `Assets/TEngine/Runtime/Module/ScreenModule/` 下 `ScreenModule.cs` 源码，定位 `OnInit`
2. 分析 `OnInit` 在 DS 模式下会触发哪些不支持的特性（多屏布局、显示器枚举、窗口控制等）
3. 在 `OnInit` 开头加 DS 分支：DS 模式下跳过多屏/显示器相关初始化，只保留最小必要逻辑
4. 改动范围最小化，不删除原有逻辑，用 `#if UNITY_SERVER && !UNITY_EDITOR` 或 `DedicatedServerLauncher.IsDedicatedServerBuild` 守卫

**注意**：
- 这是框架级改动（TEngine.Runtime），需在实施时先读源码再改
- 改动后需同步更新 `Books/Fork/` 下相关文档（经用户同意）
- 不主动改其他框架模块，只改 `ScreenModule.OnInit`

---

## 四、文件变更清单

### 新增文件
| 文件 | 步骤 |
|---|---|
| `Assets/Launcher/Scripts/DedicatedServerLauncher.cs` | 3.1 |
| `Assets/Scenes/dedicated_server.unity` | 3.6 |
| `Assets/Scenes/dedicated_server.unity.meta` | 3.6（Unity 生成） |

### 修改文件
| 文件 | 步骤 | 改动 |
|---|---|---|
| `Assets/GameScripts/Procedure/ProcedureLaunch.cs` | 3.2 | OnEnter 加 DS 分支 |
| `Assets/GameScripts/Procedure/ProcedureSplash.cs` | 3.3 | OnUpdate 加 DS 分支（仅日志） |
| `Assets/GameScripts/Procedure/ProcedurePreload.cs` | 3.4 | OnEnter + OnUpdate 加 DS 分支 |
| `Assets/GameScripts/Procedure/ProcedureInitResources.cs` | 3.7（补漏） | OnEnter / InitResources 循环 / 模式不匹配 / OnInitResourcesError 加 DS 守卫 |
| `Assets/GameScripts/HotFix/GameLogic/GameApp.cs` | 3.5 | StartGameLogic 加 DS 分支 + StartDedicatedServer |
| `Assets/TEngine/Runtime/Module/ScreenModule/ScreenModule.cs` | 3.6b | OnInit 加 DS 保护（框架级改动，已授权） |
| `ProjectSettings/EditorBuildSettings.asset` | 3.6 | 加入 dedicated_server.unity |

### 不修改
- PurrNet 包内所有文件（框架级，不主动修改）
- `ProjectSettings/PurrNetSettings.asset`（开发期保持默认）
- `NetworkModule.cs` / `INetworkModule.cs`（阶段一已完成，不动）

---

## 五、关键决策记录

| 决策点 | 选择 | 理由 |
|---|---|---|
| `IsDedicatedServerBuild` 口径 | 严格 `#if UNITY_SERVER && !UNITY_EDITOR` | 框架流程判断工具，与 PurrNet `isServerBuild` 同口径但独立；普通客户端支持 host 模式，与 DS 代码路径不同 |
| DS 流程跳过 LauncherMgr/UI/语言/声音 | 是 | DS 无 UI/本地化/声音需求 |
| DS 保留 Obfuz 检查 | 是 | 混淆 DLL 在 DS 上同样运行 |
| DS 保留 MultiInstanceLauncher | 是 | DS 同机多实例部署会用 `--yoo-instance` |
| DS 保留 DeployConfig 加载 | 是 | DS 仍需读取服务器地址/端口配置 |
| DS 走 OnUpdate 路径进 Splash | 是 | 保持 FSM 流程完整，不跨状态跳转 |
| ProcedureSplash 显式加 DS 分支 | 是 | 仅日志，便于未来扩展 |
| DS 仍执行 PreloadResources | 是 | 预加载 PRELOAD 标签的配置资产 |
| ProcedureInitResources DS 守卫（补漏） | 是（4 处） | LauncherMgr 未初始化，所有 ShowUI/ShowMessageBox 必须加 DS 守卫，否则 NRE 退出（详见步骤 3.7） |
| ProcedureLoadAssembly 必须执行 | 是 | 热更代码初始化不能跳过 |
| DS 不调 Screen.ApplyAll | 是 | DS 无多屏布局需求 |
| ScreenModule 模块仍注册 | 是 | 避免其他模块访问 GameModule.Screen 时 NRE |
| ScreenModule.OnInit 加 DS 保护 | 是（改框架，已授权） | 防 DS 触发不支持的特性 |
| DS 不调 Network.StartServer | 是 | NM 启动交给业务，框架只提供工具，自由度最大化 |
| 未指定场景 fallback MainScene | 是 | 便于开发期快速测试 |
| 新建专用 dedicated_server.unity | 是 | 框架内置测试场景，不污染客户端 main.unity |
| dedicated_server.unity 加入 EditorBuildSettings | 是 | PurrNet 要求所有玩家同场景，DS 也是 |
| NetworkRules 用默认预设 | 是 | 自定义预设留到阶段四 4.3 |

---

## 六、验证计划

### 6.1 编译验证
```powershell
# C# 编译检查
python .codex/scripts/workflow.py verify --profile code
# Unity 编译检查（read_console 确认无 Error）
```

### 6.2 DS 构建产物运行验证
```powershell
# DS 构建（已阶段二完成）
python -m tengine_build run --preset dedicated_server_linux --json

# 运行产物
./<DS产物> -batchmode -scene dedicated_server --port 7777 --address 0.0.0.0
```

**确认项**：
1. 启动日志可见 `[ProcedureLaunch] Dedicated Server 模式，跳过客户端 UI 初始化。`
2. 启动日志可见 `[ProcedurePreload] Dedicated Server 模式，跳过加载 UI 显示，仍执行预加载。`
3. 启动日志可见 `======= Dedicated Server 启动 =======`
4. 热更代码正常加载（`ProcedureLoadAssembly` 执行）
5. 场景正常加载（`dedicated_server.unity` 或命令行指定场景）
6. NetworkManager 自动启动服务器（PurrNet `StartFlags.ServerBuild` 触发，日志可见 `StartServer`）
7. `ApplicationContext.isServerBuild == true`
8. 无 UI/渲染/多屏相关错误（headless 模式）
9. `ScreenModule.OnInit` DS 保护生效，无多屏初始化报错

### 6.3 完整验证
```powershell
python .codex/scripts/workflow.py verify --profile full
```

---

## 七、实施顺序

```
3.1 DedicatedServerLauncher.cs（独立，无依赖）
  ↓
3.6b ScreenModule.OnInit DS 保护（框架级，独立改）
  ↓
3.2 ProcedureLaunch DS 分支
  ↓
3.3 ProcedureSplash DS 分支
  ↓
3.4 ProcedurePreload DS 分支
  ↓
3.7 ProcedureInitResources DS 守卫（补漏，4 处）
  ↓
3.5 GameApp.StartGameLogic DS 分支
  ↓
3.6 dedicated_server.unity 场景创建 + EditorBuildSettings（用 unitycli/MCP）
  ↓
验证：编译 → DS 构建 → 运行测试
```

**注意**：3.1 和 3.6b 可独立先做；3.2-3.5 有顺序依赖（Procedure 链上下游）；3.6 需 Unity Editor 连接。

---

## 八、风险与注意事项

1. **ScreenModule.OnInit 改动是框架级**：需先读源码再改，改动最小化，不删除原有逻辑，用编译守卫或 `IsDedicatedServerBuild` 判断
2. **DS 场景创建依赖 Unity Editor**：需 unitycli/MCP 连接，无法纯代码创建带 NM 的场景
3. **PurrNet StartFlags 配置**：NM 的 `startServerFlags` 必须含 `ServerBuild`，否则 DS 包不会自动启动服务器
4. **EditorBuildSettings 加入 DS 场景**：普通客户端构建也会包含此场景（增加包体），但 PurrNet 要求同场景，必须加入
5. **DS 模式下的 UI 系统**：DS 流程跳过 LauncherMgr，但 `GameModule.UI` 仍可访问（UIModule.Instance），不应调用 ShowUI
6. **NM 启动方式选择**：框架不强制，业务可选场景预挂（A）或代码动态注入（B）；dedicated_server.unity 默认用 A
