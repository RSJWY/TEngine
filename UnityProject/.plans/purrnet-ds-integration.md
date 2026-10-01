# PurrNet 集成与 DS（专用服务器）打包对接计划

> **分支**：`Tengine_PurrNet`
> **Unity 版本**：6000.3.25f1
> **PurrNet 版本**：v1.23.0-beta.24（fork yooasset 分支）
> **创建时间**：2026-09-27
> **状态**：阶段一已完成（2026-09-27），阶段二~四待执行

---

## 一、当前现状

### 1.1 PurrNet 包

| 项 | 值 |
|---|---|
| manifest.json | `dev.purrnet.purrnet` → `https://github.com/RSJWY/PurrNet.git?path=/Assets/PurrNet#yooasset` |
| PurrNetSettings.asset | `stripServerCode: false`、`stripCodeMode: 0`(DoNotStrip)、`guardFailureAction: 0` |
| YOOASSET_PURRNET_SUPPORT | 已自动定义（asmdef versionDefines 检测 `com.tuyoogame.yooasset`） |
| NetworkManager.YooAsset.cs | 已存在（SpawnYooAsset / SpawnYooAssetAsync / DespawnYooAsset） |
| ScenesModule.YooAsset.cs | 已存在（YooAsset 场景加载/卸载/事件） |

### 1.2 业务代码

| 项 | 状态 |
|---|---|
| GameLogic.asmdef | **已引用 PurrNet.Runtime**（GUID `6e20f757a1bae164fa42750dd2b27dcb`） |
| 业务代码 PurrNet 引用 | GameLogic 已可引用 PurrNet 命名空间 |
| NetworkModule 封装 | **已完成**（`Module/NetworkModule/INetworkModule.cs` + `NetworkModule.cs`） |
| GameModule.Network 访问器 | **已完成** |
| 场景中的 NetworkManager | **无**（main.unity 和 MainScene.unity 均未挂载，按需注入或懒查找） |

### 1.3 启动流程

```
GameEntry.Awake
  → Settings.ProcedureSetting.StartProcedure()
    → ProcedureLaunch（语言/声音/多开/DeployConfig）
      → ProcedureSplash（闪屏）
        → ProcedureInitPackage（初始化 YooAsset 包）
          → ProcedureInitResources（版本检查/资源清单）
            → ProcedureCreateDownloader → ProcedureDownloadOver → ProcedureClearCache
              → ProcedurePreload（预加载配置资产）
                → ProcedureLoadAssembly（HybridCLR 热更 DLL + AOT 元数据 + Obfuz 密钥）
                  → ProcedureStartGame（隐藏加载 UI）
                    → GameApp.Entrance()（反射调用）
                      → StartGameLogic()
                        → RegisterModule(IUIJumpControl) + RegisterModule(IGameSceneModule)
                        → GameModule.Screen.ApplyAll()
                        → GameModule.GameScene.LoadScene(SceneType.MainScene)
```

### 1.4 构建工具

| 项 | 状态 |
|---|---|
| BuildCommand.cs | 支持 `EnableHeadlessMode`（已废弃），**未支持** `StandaloneBuildSubtarget.Server` |
| BuildCLI (Python) | **无** subtarget/server/dedicated 概念 |
| ProjectSettings | `dedicatedServerOptimizations: 0`，无 `UNITY_SERVER` define |
| EditorBuildSettings | 仅 `Assets/Scenes/main.unity` |

### 1.5 关键约束

- PurrNet NetworkManager 是 **MonoBehaviour**，需挂载在场景中的 GameObject 上
- PurrNet 的 ILPostProcessor（Codegen）在**编辑器编译阶段**织入，处理所有非 Editor/非 Unity.* 程序集
- Obfuz 在**构建后处理**（`IPostBuildPlayerScriptDLLs`）混淆 DLL，时机在 ILPP 之后
- HybridCLR 热更 DLL 在运行时通过 `Assembly.Load` 加载，ILPP 已在编译期完成织入
- PurrNet 的 `ApplicationContext.isServerBuild` 通过 `#if UNITY_SERVER && !UNITY_EDITOR` 判断
- PurrNet 的 `StartFlags.ServerBuild` 在 DS 构建时自动触发 `StartServer()`

---

## 二、目标

1. **PurrNet 与 TEngine Module 体系完整对接**——不是只有联机，而是网络生命周期、场景协调、对象池桥接全链路打通
2. **DS 打包管线**——BuildCommand/BuildCLI 支持 Server subtarget，能打出 DS 可执行文件
3. **DS 启动流程分支**——DS 模式下跳过客户端专属流程，热更完成后前往业务指定的场景
4. **场景传参交给业务定义**——框架侧只提供命令行解析工具和 DS 判断工具，业务在 GameApp 里决定加载哪个场景

---

## 三、实施步骤

### 阶段一：基础引用打通（前置必须）✅ 已完成

> **完成时间**：2026-09-27
> **验证**：Unity 编译零 Error，`read_console` 无 Error

#### 步骤 1.1：GameLogic.asmdef 添加 PurrNet.Runtime 引用 ✅

**文件**：`Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef`

**改动**：在 `references` 数组末尾添加：
```json
"GUID:6e20f757a1bae164fa42750dd2b27dcb"
```

**验证结果**：
- PurrNet.Runtime 的 GUID 已确认为 `6e20f757a1bae164fa42750dd2b27dcb`（查 `.asmdef.meta`）
- Unity 编译无报错
- PurrNet 的 ILPP 自动处理 GameLogic 程序集

#### 步骤 1.2：创建 INetworkModule 接口 ✅

**新增文件**：`Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/INetworkModule.cs`

**实际实现**（与草案的差异已标注）：

```csharp
public interface INetworkModule
{
    PurrNet.NetworkManager NetworkManager { get; }
    bool IsServer { get; }
    bool IsClient { get; }
    bool IsHost { get; }
    bool IsDedicatedServerBuild { get; }
    bool IsAutoStarted { get; }  // 新增：PurrNet AutoStart 是否已触发

    void StartServer();
    void StartClient();          // 修正：无参（PurrNet StartClient() 本身无参，地址配在 transport Inspector）
    void StartHost();
    void StopNetwork();

    void BindNetworkManager(PurrNet.NetworkManager networkManager);  // 新增：预制体注入入口
}
```

**与草案的差异**：
1. `StartClient` 改为无参——PurrNet 的 `StartClient()` 本身无参（源码 `:2394`），地址配置在 `UDPTransport` Inspector
2. 新增 `IsAutoStarted`——业务据此判断"网络已是活的"还是"需要手动 Start"
3. 新增 `BindNetworkManager`——业务动态实例化 NM 预制体后注入，跳过懒查找

#### 步骤 1.3：创建 NetworkModule 实现 ✅

**新增文件**：`Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkModule.cs`

**关键设计**（基于源码核实）：

1. **OnInit 主动查一次 + 懒加载兜底**：OnInit 优先 `NetworkManager.main`（源码 `:33/837`），fallback `FindFirstObjectByType`。找不到打 Info 日志（不报错），等注入或懒查找。
2. **懒加载**：所有查询属性（`IsServer` 等）和操作方法（`StartServer` 等）内部调 `EnsureNetworkManager()`，首次访问时懒查找并缓存。
3. **IsAutoStarted 实时查询**：不靠 OnInit 缓存，实时读 `NM.isServer || NM.isClient`。
4. **BindNetworkManager 注入**：业务 `LoadGameObjectAsync` 出 NM 预制体后调用。
5. **Shutdown 只清引用**：不调 StopNetwork，NM 的 OnDestroy 自行断连（源码 `:1995-2030`）。

**NM 获取方式**：`NetworkManager.main` → `FindFirstObjectByType` → `BindNetworkManager` 注入

#### 步骤 1.4：GameModule 添加 Network 访问器 ✅

**文件**：`Assets/GameScripts/HotFix/GameLogic/GameModule.cs`

**改动**：添加 `Network` 属性 + `Shutdown` 清理 `_network = null`

#### 步骤 1.5：GameApp 注册 NetworkModule ✅

**文件**：`Assets/GameScripts/HotFix/GameLogic/GameApp.cs`

**改动**：`StartGameLogic()` 中添加 `ModuleSystem.RegisterModule<INetworkModule>(new NetworkModule());`

**验证结果**：
- Unity 编译零 Error
- `GameModule.Network` 可访问
- 无 NetworkManager 场景下 OnInit 打 Info 日志，不崩溃
- `FindObjectOfType` warning 已修正为 `FindFirstObjectByType`

---

### 阶段二：DS 打包管线

#### 步骤 2.1：ReleaseTools.BuildImp 支持 Server subtarget

> **修正说明**（2026-10-01 研究后调整）：原计划改 `com.unity.pipeline` 的 `BuildCommand`，但实际 Player/DS 构建链路是 `BuildCLI → CLIBridge.Run → ReleaseTools.BuildImp → BuildPipeline.BuildPlayer`，与 pipeline 包的 `BuildCommand`（HTTP 远程控制）是两条独立路径。`com.unity.pipeline` 包**不考虑改动**。

**前提核实**（已通过 Unity 反射确认）：
- `UnityEditor.StandaloneBuildSubtarget` 枚举存在（`UnityEditor.CoreModule`），值为 `Default/Player/Server`，命名空间是 `UnityEditor`（非 `UnityEditor.Build`）
- `EditorUserBuildSettings.standaloneBuildSubtarget` 属性可读写，类型为 `StandaloneBuildSubtarget`
- DS subtarget 构建时 Unity 自动注入 `UNITY_SERVER` define

**文件 1**：`Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`
改动：`BuildConfig` 类（:20）添加字段
```csharp
/// <summary>Standalone 构建子目标（"Server"=专用服务器，"Player"=普通客户端，""=默认）。
/// 仅对 Standalone 平台生效，Unity 6+ 通过 EditorUserBuildSettings.standaloneBuildSubtarget 设置。</summary>
public string subtarget = "";
```

**文件 2**：`Assets/TEngine/Editor/ReleaseTools/CLIBridge.cs`
改动：
1. `BuildRequestDTO`（:30）添加字段 `public string subtarget = "";`
2. `ToBuildConfig`（:469）中传递：`config.subtarget = request.subtarget ?? string.Empty;`
3. `Execute` 的 `case "buildPlayer"`（:323）改为将 subtarget 传入 `BuildImp`：
```csharp
case "buildPlayer":
{
    var config = ToBuildConfig(request);
    var playerTarget = ParseBuildTarget(request.playerPlatform);
    if (!ReleaseTools.BuildImp(
            BuildConfig.GetBuildTargetGroup(playerTarget),
            playerTarget,
            request.playerOutputPath,
            config.subtarget))
    {
        Debug.LogError("[TEngineCLI] Player 构建失败。");
        return false;
    }
    FillPlayerRecord(config, result);
    Debug.Log("[TEngineCLI] ========== Player 构建完成 ==========");
    return true;
}
```
4. `case "build"` / `case "buildAb"` 的 `withPlayer` 分支同样需将 subtarget 透传至 `BuildImp`（`ReleaseTools.BuildWithConfig` 内部调用 `BuildImp` 时读取 `config.subtarget`）。

**文件 3**：`Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
改动：`BuildImp`（:838）增加 `subtarget` 参数，构建前设置 `EditorUserBuildSettings.standaloneBuildSubtarget`，构建后恢复原值：
```csharp
public static bool BuildImp(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget,
    string locationPathName, string subtarget = "")
{
    var prevSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
    try
    {
        if (!string.IsNullOrWhiteSpace(subtarget)
            && Enum.TryParse<StandaloneBuildSubtarget>(subtarget, true, out var st))
        {
            EditorUserBuildSettings.standaloneBuildSubtarget = st;
            Debug.Log($"[BuildImp] 设置 standaloneBuildSubtarget = {st}");
        }
        // ===== 原有逻辑不变 =====
        EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
        AssetDatabase.Refresh();
        if (!string.IsNullOrWhiteSpace(locationPathName) && !Path.IsPathRooted(locationPathName))
            locationPathName = Path.GetFullPath(Path.Combine(Application.dataPath, "..", locationPathName));
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Select(scene => scene.path).ToArray(),
            locationPathName = locationPathName,
            targetGroup = buildTargetGroup,
            target = buildTarget,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build success: {summary.totalSize / 1024 / 1024} MB, {summary.outputPath}");
            return true;
        }
        Debug.LogError($"Build Failed: {summary.result}");
        return false;
        // ===== 原有逻辑结束 =====
    }
    finally
    {
        EditorUserBuildSettings.standaloneBuildSubtarget = prevSubtarget;
    }
}
```
**兼容性**：原 `BuildImp(group, target, path)` 的调用方（`BuildPipelineWindow` GUI 等）保持兼容——新重载 `subtarget` 默认 `""`，等价于原行为；原三参数签名改为委托到新签名即可。

**验证**：
- Unity 编译零 Error，`read_console` 无 Error
- `dry_run` 请求 JSON 含 `subtarget` 字段
- 实际 DS 构建产物以 `-batchmode` 运行时 `ApplicationContext.isServerBuild == true`

#### 步骤 2.2：BuildCLI（Python）传递 subtarget

**文件**（3 个）：

**文件 1**：`BuildCLI/tengine_build/config_store.py`
改动：`BuildFormState`（:53）添加字段
```python
# Standalone 构建子目标（"Server"=专用服务器，"Player"=普通客户端，""=默认）
# Unity 6+ 通过 EditorUserBuildSettings.standaloneBuildSubtarget 设置
subtarget: str = ""
```
位置：放在 `playerOutputPath` 字段之后（Player 分组内），逻辑上归属 Player 构建配置。
**注意**：`subtarget` 不加入 `_LOCAL_ONLY_FIELDS`（dto.py:14），需下发给 CLIBridge。

**文件 2**：`BuildCLI/tengine_build/cli.py`
改动：`run` 子命令（run_p）添加参数
```python
run_p.add_argument("--subtarget", default=None, choices=["Server", "Player"],
                   help="构建子目标（Server=专用服务器，Player=普通客户端）；仅 Standalone 平台生效")
```
并在 `_assemble_state`（:54）中透传：
```python
if getattr(args, "subtarget", None):
    state.subtarget = args.subtarget
```

**文件 3**：`BuildCLI/tengine_build/dto.py`
**无需改动**：`dump_request`（:28）通过 `asdict(state)` 已包含新字段，`_LOCAL_ONLY_FIELDS`（:14）不含 `subtarget`，自动透传到 CLIBridge 的 `BuildRequestDTO`。

**验证**：
```powershell
# dry_run 验证 subtarget 透传
python -m tengine_build run --target StandaloneLinux64 --subtarget Server --action buildPlayer --dry-run
# 确认输出 JSON 含 "subtarget": "Server"

# 不传 --subtarget 时为空字符串（默认行为，向后兼容）
python -m tengine_build run --target StandaloneWindows64 --action buildPlayer --dry-run
```

#### 步骤 2.3：PurrNetSettings 配置 DS 裁剪策略

**文件**：`ProjectSettings/PurrNetSettings.asset`

**当前值**：
```json
{
  "stripCodeMode": 0,        // DoNotStrip
  "stripServerCode": false,  // 不裁剪服务器代码
  ...
}
```

**建议配置**：
- `stripCodeMode`：保持 `DoNotStrip`（开发期不裁剪，上线前再切）
- `stripServerCode`：保持 `false`（开发期不裁剪）
- 上线前根据需要改为 `true` 以裁剪客户端构建中的服务器 RPC（减小包体）

**注意**：
- `stripServerCode=true` 会在客户端构建中裁剪 `runLocally:false` 的 ServerRPC
- `[ServerOnly(StripCodeModeOverride.Settings)]` 标注的方法/字段按 `stripCodeMode` 裁剪
- DS 构建不需要裁剪客户端代码（DS 包不包含客户端逻辑时由 IL2CPP managed code stripping 处理）

#### 步骤 2.4：Obfuz + PurrNet ILPP 共存验证与排除规则

**现状核实**（读取 `ProjectSettings/Obfuz.asset` 确认）：
- `assembliesToObfuscate`：仅 `GameLogic`、`GameProto`、`TEngine.CryptoKeys`
- PurrNet 程序集（`PurrNet.Runtime`）**不在混淆范围**，本身不会被混淆
- `obfuscateObfuzRuntime: 1`（Obfuz.Runtime 被混淆，与 PurrNet 无关）
- 现有排除规则文件：`symbol-preserve-module-virtuals.xml`（保留 Module 虚方法名）、`field-encrypt-cryptokeys.xml`

1. **时机分析**（已确认不冲突）：
   - PurrNet ILPP：编辑器编译阶段（ILPostProcessor），织入 RPC/序列化器/模块注册代码到 GameLogic.dll
   - Obfuz：构建后处理（`ObfuscationProcess.OnPostBuildPlayerScriptDLLs`，`Obfuz.asset` 中 `obfuscationProcessCallbackOrder: 10000`），混淆 GameLogic.dll 中的方法名/字段名
   - ILPP 先于 Obfuz，织入代码已生成完整，Obfuz 会看到织入后的完整 DLL

2. **实际风险**（比原计划评估更精确）：
   - PurrNet 程序集本身不被混淆 → PurrNet 内部 RPC 收发逻辑不受影响
   - **但 GameLogic 被 Obfuz 混淆**：PurrNet ILPP 织入 GameLogic.dll 的 RPC 句柄方法（如 `HandleRPCGenerated_N`、`_Original_N`、`RPCGenerated_*`）在混淆范围内
   - 若这些方法名被混淆，PurrNet 运行时通过反射/委托按名查找的 RPC 句柄会失配，导致 RPC 收发失败
   - `[ObfuzIgnore]` 特性已在本项目使用（参考 `GameApp.cs`），但 ILPP 生成的方法不带 `[ObfuzIgnore]`

3. **方案**：新增 Obfuz XML 规则文件，按方法名模式排除 PurrNet 生成方法

**新增文件**：`Assets/Obfuz/Rules/symbol-preserve-purrnet.xml`
```xml
<?xml version="1.0" encoding="utf-8"?>
<obfuz>
  <assembly name="GameLogic">
    <type name="*">
      <!-- PurrNet ILPP 生成的 RPC 句柄方法，混淆会导致网络收发失败 -->
      <method name="HandleRPCGenerated_*" obName="false" />
      <method name="_Original_*" obName="false" />
      <method name="RPCGenerated_*" obName="false" />
    </type>
  </assembly>
</obfuz>
```

**修改文件**：`ProjectSettings/Obfuz.asset`
改动：在 `obfuscationPassSettings.ruleFiles` 列表（当前只有 `symbol-preserve-module-virtuals.xml`）追加：
```yaml
  obfuscationPassSettings:
    enabledPasses: 3149059
    ruleFiles:
    - Assets/Obfuz/Rules/symbol-preserve-module-virtuals.xml
    - Assets/Obfuz/Rules/symbol-preserve-purrnet.xml   # 新增
```

**注意**：
- 实际方法名模式需在首次 Development build 后从 PurrNet ILPP 产物或构建日志确认，上述模式基于计划文档先前的研究推断
- 若 PurrNet ILPP 使用 `[ObfuzIgnore]` 或其他保留特性标注生成方法，则无需额外规则（需核实 PurrNet ILPP 源码）
- Obfuz XML 规则的 `obName="false"` 表示不重命名方法名，方法体仍可被其他 pass 混淆（控制流/常量加密等）

4. **验证方式**：
   - 做一次完整 Development build，检查构建日志无 Obfuz 报错
   - 用 dnSpy/ILSpy 反汇编 GameLogic.dll，确认 `HandleRPCGenerated_*` 方法名未被混淆
   - 运行构建产物，确认网络功能正常（RPC 可收发）

#### 步骤 2.5：ProjectSettings 启用 Dedicated Server 优化（可选）

**文件**：`ProjectSettings/ProjectSettings.asset`

**当前**：`dedicatedServerOptimizations: 0`

**建议**：上线前改为 `1`（启用 Unity 6 的 dedicated server 优化：剥离渲染/音频/输入等）
- 开发期保持 `0`，避免影响 Editor 调试

#### 步骤 2.6：DS 构建预设（新增）

**新增文件**：`BuildCLI/presets/dedicated_server_linux.json`
```json
{
  "buildTarget": "StandaloneLinux64",
  "subtarget": "Server",
  "action": "buildPlayer",
  "playerOutputPath": "./Releases/DedicatedServer/build/",
  "buildHotFixDll": true,
  "compressOption": "LZ4"
}
```
**用途**：`python -m tengine_build run --preset dedicated_server_linux --json` 一键打 DS 包。
**注意**：预设里 `subtarget` 字段随 `BuildFormState` 持久化（步骤 2.2 已加字段），`load_preset` 通过 `form_from_json` 自动解析。

---

### 阶段三：DS 启动流程分支

#### 步骤 3.1：创建 DS 命令行解析工具

**新增文件**：`Assets/Launcher/Scripts/DedicatedServerLauncher.cs`

**设计要点**：
```csharp
namespace Launcher
{
    /// <summary>
    /// 专用服务器启动器。解析命令行参数，提供 DS 模式判断和启动场景指定。
    /// </summary>
    public static class DedicatedServerLauncher
    {
        public const string SceneArgumentName = "--scene";
        public const string SceneTypeArgumentName = "--scene-type";
        public const string PortArgumentName = "--port";
        public const string AddressArgumentName = "--address";

        /// <summary>是否为专用服务器构建（UNITY_SERVER define）。</summary>
        public static bool IsDedicatedServerBuild =>
#if UNITY_SERVER && !UNITY_EDITOR
            true;
#else
            false;
#endif

        /// <summary>解析命令行指定的启动场景名。未传参时返回 null。</summary>
        public static string ResolveStartupScene()
        {
            return ResolveArgument(SceneArgumentName);
        }

        /// <summary>解析命令行指定的启动场景类型。未传参时返回 null。</summary>
        public static string ResolveStartupSceneType()
        {
            return ResolveArgument(SceneTypeArgumentName);
        }

        /// <summary>解析命令行指定的端口号。未传参时返回默认 7777。</summary>
        public static int ResolvePort()
        {
            var portStr = ResolveArgument(PortArgumentName);
            return int.TryParse(portStr, out var port) ? port : 7777;
        }

        /// <summary>解析命令行指定的服务器地址。未传参时返回 localhost。</summary>
        public static string ResolveAddress()
        {
            return ResolveArgument(AddressArgumentName) ?? "localhost";
        }

        private static string ResolveArgument(string argName)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == argName && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return args[i + 1];
            }
            return null;
        }
    }
}
```

**注意**：
- 放在主包（`Assets/Launcher/Scripts/`），与 `MultiInstanceLauncher` 同级
- `IsDedicatedServerBuild` 使用 `#if UNITY_SERVER` 编译期判断，DS 构建时自动为 true
- 框架只提供工具，**不决定加载哪个场景**——业务在 GameApp 里读取并决定

#### 步骤 3.2：ProcedureLaunch 添加 DS 分支

**文件**：`Assets/GameScripts/Procedure/ProcedureLaunch.cs`

**改动**：在 `OnEnter` 中添加 DS 分支：
```csharp
protected override void OnEnter(ProcedureOwner procedureOwner)
{
    base.OnEnter(procedureOwner);

    // DS 模式：跳过 Launcher UI、声音、多屏等客户端专属初始化
    if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
    {
        Log.Info("[ProcedureLaunch] Dedicated Server 模式，跳过客户端 UI 初始化。");
        // DS 直接加载部署配置后跳过 Splash
        LoadDeployConfigAsync().Forget();
        return;
    }

    // 原有客户端流程
    LauncherMgr.Initialize();
    // ... 原有逻辑保持不变
}
```

**注意**：
- DS 模式不初始化 LauncherMgr（无 UI）
- DS 模式仍需加载 DeployConfig（可能包含服务器地址/端口配置）
- Obfuz 密钥检查在 DS 模式同样需要（混淆 DLL 在 DS 上同样运行）

#### 步骤 3.3：ProcedureSplash 添加 DS 分支

**文件**：`Assets/GameScripts/Procedure/ProcedureSplash.cs`

**改动**：DS 模式直接跳到 InitPackage：
```csharp
protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
{
    base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

    if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
    {
        // DS 模式跳过 Splash 动画
        ChangeState<ProcedureInitPackage>(procedureOwner);
        return;
    }

    // 原有客户端流程
    ChangeState<ProcedureInitPackage>(procedureOwner);
}
```

**注意**：实际上客户端也是直接跳，这里只是确保 DS 分支显式处理。后续可加 DS 专属的启动日志。

#### 步骤 3.4：ProcedurePreload 添加 DS 分支

**文件**：`Assets/GameScripts/Procedure/ProcedurePreload.cs`

**改动**：DS 模式跳过预加载（DS 不需要预加载 UI 资产/配置资产等客户端资源）：
```csharp
protected override void OnEnter(ProcedureOwner procedureOwner)
{
    base.OnEnter(procedureOwner);

    if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
    {
        Log.Info("[ProcedurePreload] Dedicated Server 模式，跳过预加载。");
        ChangeState<ProcedureLoadAssembly>(procedureOwner);
        return;
    }

    // 原有客户端预加载流程
    // ...
}
```

#### 步骤 3.5：GameApp.Entrance 添加 DS 分支

**文件**：`Assets/GameScripts/HotFix/GameLogic/GameApp.cs`

**改动**：DS 模式下不加载 MainScene，而是加载命令行指定的场景：
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

    // 原有客户端流程
    GameModule.Screen.ApplyAll();
    GameModule.GameScene.LoadScene(SceneType.MainScene);
}

private static void StartDedicatedServer()
{
    Log.Warning("======= Dedicated Server 启动 =======");

    // 1. 解析命令行指定的场景
    string sceneName = Launcher.DedicatedServerLauncher.ResolveStartupScene();
    string sceneTypeStr = Launcher.DedicatedServerLauncher.ResolveStartupSceneType();

    // 2. 加载指定场景（业务自定义逻辑）
    //    方式 A：按场景名加载（直接走 PurrNet ScenesModule 的 YooAsset 场景加载）
    //    方式 B：按 SceneType 枚举加载（走 GameSceneModule.LoadScene）
    //    方式 C：业务完全自定义（例如先跑大厅逻辑再切战斗）
    //
    //    以下为示例：优先按 scene-type 加载，其次按 scene 名加载，都没有则加载默认主场景
    if (!string.IsNullOrEmpty(sceneTypeStr) && System.Enum.TryParse<SceneType>(sceneTypeStr, true, out var sceneType))
    {
        Log.Info($"[DS] 按命令行 scene-type 加载场景：{sceneType}");
        GameModule.GameScene.LoadScene(sceneType);
    }
    else if (!string.IsNullOrEmpty(sceneName))
    {
        Log.Info($"[DS] 按命令行 scene 加载场景：{sceneName}");
        // 走 TEngine SceneModule 直接加载
        GameModule.Scene.LoadSceneAsync(sceneName).Forget();
    }
    else
    {
        Log.Warning("[DS] 未指定启动场景，加载默认 MainScene。");
        GameModule.GameScene.LoadScene(SceneType.MainScene);
    }

    // 3. NetworkManager 在场景中预挂载，PurrNet StartFlags.ServerBuild 会自动启动服务器
    //    如需手动控制，可通过 GameModule.Network.StartServer() 调用
}
```

**注意**：
- **场景传参交给业务定义**——框架侧只提供 `DedicatedServerLauncher` 工具，业务在 `StartDedicatedServer()` 里决定逻辑
- DS 模式下 NetworkManager 在场景中预挂载，`StartFlags.ServerBuild` 自动启动服务器
- 业务可在此处根据 DS 需求做额外初始化（如加载服务器配置、注册网络 prefab 等）

#### 步骤 3.6：DS 场景准备

**需要新增**：一个 DS 启动场景，挂载 NetworkManager + UDPTransport

**方案**：
1. 创建 `Assets/Scenes/dedicated_server.unity`（或复用 main.unity 添加 NetworkManager）
2. 场景中挂载：
   - `NetworkManager`（PurrNet）+ `UDPTransport`（LiteNetLib）
   - `NetworkRules`（Unsafe 预设，开发期）
   - `VisibilityRules`（默认预设）
3. 将此场景加入 EditorBuildSettings（或通过命令行 `--scene` 指定）

**注意**：
- DS 场景需要配置 transport 端口（默认 7777）
- NetworkRules 开发期用 `Unsafe`（人人可执行），上线前切 `ServerStrict`
- DS 场景中的 NetworkManager 的 `startServerFlags` 需包含 `StartFlags.ServerBuild`

---

### 阶段四：PurrNet + TEngine 体系对接（后续逐步做）

#### 步骤 4.1：对象池桥接

**背景**：PurrNet 的 `Instantiate/Destroy` 是网络生成/销毁，TEngine 有 `GameObjectPoolModule`（基于 YooAsset location 的异步实例化池）

**方案**：
- PurrNet 的 `SpawnYooAsset` / `SpawnYooAssetAsync` 已与 YooAsset 集成
- 网络生成的对象走 PurrNet Spawn（需要 NetworkIdentity）
- 非网络对象仍走 TEngine `GameObjectPoolModule`
- 后续可考虑实现 `INetworkPrefabPool` 接口桥接两者

**优先级**：低（当前无网络业务代码，暂不需要）

#### 步骤 4.2：场景加载协调

**背景**：
- PurrNet `ScenesModule`：服务器权威场景 ID 分配、spawn/despawn、观察者管理，已有 YooAsset 集成
- TEngine `GameSceneModule`：客户端驱动、三段式进度（预热/加载/收尾）、加载页 UI

**方案**：
- DS 端：走 PurrNet `ScenesModule`（服务器权威场景加载）
- 客户端端：走 TEngine `GameSceneModule`（加载页 UI）+ PurrNet 同步（接收服务器场景事件）
- 需在 `NetworkModule` 中提供场景事件桥接（PurrNet scene event → TEngine GameEvent）

**优先级**：中（有实际联机场景时做）

#### 步骤 4.3：NetworkRules 配置

**需要新增**：
1. `Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkRules/` 目录
2. 放置 Rules 预设资产：
   - `UnsafeRules.asset`（开发期：人人可 spawn/调 RPC）
   - `ServerStrictRules.asset`（上线：服务器权威）
3. NetworkManager Inspector 中引用

**优先级**：低（开发期用 Unsafe 即可）

#### 步骤 4.4：网络 Prefab 注册

**需要新增**：
1. 创建 `YooAssetNetworkPrefabs` 资产（ScriptableObject）
2. 配置 packageName/location → 网络 prefab 映射
3. NetworkManager Inspector 中引用

**注意**：
- prefab 的 location 需与 YooAsset 收集器配置一致
- `preloadAtStartup` 控制是否启动时预加载所有网络 prefab

**优先级**：中（有网络 prefab 时做）

#### 步骤 4.5：CodeStripping 配置（上线前）

**当前**：`stripCodeMode: DoNotStrip`

**上线前配置**：
- 客户端构建：`stripServerCode: true`（裁剪 ServerRPC 的 `runLocally:false` 方法体）
- DS 构建：不裁剪（DS 需要完整服务器代码）
- `[ServerOnly]` 标注的方法按 `stripCodeMode` 裁剪

**优先级**：低（上线前做）

---

## 四、验证计划

### 4.1 阶段一验证

```powershell
# 编译检查
python .codex/scripts/workflow.py verify --profile code

# Unity 编译检查
# read_console 确认无 Error
# GameModule.Network 可访问
```

### 4.2 阶段二验证

```powershell
# 1. C# 编译检查（BuildConfig/CLIBridge/ReleaseTools 改动）
python .codex/scripts/workflow.py verify --profile code

# 2. dry_run 验证 subtarget 透传（不启动 Unity）
python -m tengine_build run --target StandaloneLinux64 --subtarget Server --action buildPlayer --dry-run
# 确认输出 JSON 含 "subtarget": "Server"

# 3. 实际 DS 构建验证（需用户授权）
python -m tengine_build run --target StandaloneLinux64 --subtarget Server --action buildPlayer --json
# 或用预设：python -m tengine_build run --preset dedicated_server_linux --json

# 4. 运行产物确认 DS 模式
# ./<产物> -batchmode -scene MainScene
# 确认：ApplicationContext.isServerBuild == true（日志可见 PurrNet 自动 StartServer）

# 5. Obfuz 排除规则验证（Development build 后）
# 反汇编 GameLogic.dll，确认 HandleRPCGenerated_* 方法名未被混淆
```

### 4.3 阶段三验证

```powershell
# DS 构建产物运行测试
# ./GameServer.exe -batchmode -scene MainScene
# 确认：
# 1. 热更代码正常加载
# 2. NetworkManager 自动启动服务器
# 3. 场景正常加载
# 4. 无 UI/渲染相关错误（headless 模式）
```

### 4.4 完整验证

```powershell
python .codex/scripts/workflow.py verify --profile full
```

---

## 五、文件变更清单

### 新增文件

| 文件 | 所属阶段 |
|---|---|
| `Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/INetworkModule.cs` | 阶段一 |
| `Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkModule.cs` | 阶段一 |
| `Assets/Launcher/Scripts/DedicatedServerLauncher.cs` | 阶段三 |
| `Assets/Scenes/dedicated_server.unity`（或复用 main.unity） | 阶段三 |
| `Assets/Obfuz/Rules/symbol-preserve-purrnet.xml` | 阶段二 |
| `BuildCLI/presets/dedicated_server_linux.json` | 阶段二 |

### 修改文件

| 文件 | 所属阶段 | 改动概述 |
|---|---|---|
| `Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef` | 阶段一 | 添加 PurrNet.Runtime 引用 |
| `Assets/GameScripts/HotFix/GameLogic/GameModule.cs` | 阶段一 | 添加 Network 访问器 + Shutdown 清理 |
| `Assets/GameScripts/HotFix/GameLogic/GameApp.cs` | 阶段一+三 | 注册 NetworkModule + DS 分支 |
| `Assets/GameScripts/Procedure/ProcedureLaunch.cs` | 阶段三 | DS 跳过客户端 UI |
| `Assets/GameScripts/Procedure/ProcedureSplash.cs` | 阶段三 | DS 跳过 Splash |
| `Assets/GameScripts/Procedure/ProcedurePreload.cs` | 阶段三 | DS 跳过预加载 |
| `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs` | 阶段二 | BuildConfig 添加 subtarget 字段 |
| `Assets/TEngine/Editor/ReleaseTools/CLIBridge.cs` | 阶段二 | BuildRequestDTO 添加 subtarget + buildPlayer 透传 |
| `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs` | 阶段二 | BuildImp 设置/恢复 standaloneBuildSubtarget |
| `BuildCLI/tengine_build/config_store.py` | 阶段二 | BuildFormState 添加 subtarget 字段 |
| `BuildCLI/tengine_build/cli.py` | 阶段二 | run 子命令添加 --subtarget 参数 |
| `ProjectSettings/Obfuz.asset` | 阶段二 | obfuscationPassSettings.ruleFiles 追加 PurrNet 排除规则 |

### 不修改的文件

| 文件 | 原因 |
|---|---|
| PurrNet 包内所有文件 | 框架级，不主动修改 |
| TEngine.Runtime / TEngine.Editor | 框架级，不主动修改（阶段二改的是 ReleaseTools，属于 TEngine.Editor 但用户明确授权的构建工具改动） |
| `Packages/com.unity.pipeline/**`（含 BuildCommand.cs） | 用户明确排除，DS 构建不走 pipeline 包的 HTTP 路径 |
| `BuildCLI/tengine_build/dto.py` | `asdict(state)` 自动透传新字段，无需改动 |
| `BuildCLI/tengine_build/unity_runner.py` | 不涉及 subtarget 逻辑，无需改动 |
| ProjectSettings/PurrNetSettings.asset | 开发期保持默认，上线前再改 |
| ProjectSettings/ProjectSettings.asset | 不主动改 scriptingDefineSymbols |

---

## 六、风险与注意事项

### 6.1 Obfuz 混淆与 PurrNet ILPP

- **时机不冲突**：ILPP（编译期）先于 Obfuz（构建后处理，`obfuscationProcessCallbackOrder: 10000`）
- **混淆范围**：Obfuz 只混淆 `GameLogic`/`GameProto`/`TEngine.CryptoKeys`（见 `Obfuz.asset`），PurrNet 程序集本身不被混淆
- **风险**：GameLogic 内由 PurrNet ILPP 织入的 RPC 句柄方法（`HandleRPCGenerated_N` 等）在混淆范围内，方法名被混淆会导致 PurrNet 运行时按名查找失配
- **缓解**：步骤 2.4 新增 `symbol-preserve-purrnet.xml` 排除规则，按方法名模式保留
- **验证**：Development build 后反汇编 GameLogic.dll，确认 RPC 句柄方法名未被混淆 + 运行测试 RPC 收发

### 6.2 HybridCLR 热更 DLL 与 PurrNet ILPP

- **已确认**：PurrNet ILPP 在编辑器编译阶段处理 GameLogic 程序集，织入代码已存在于编译产物中
- **热更加载**：`Assembly.Load(dllBytes)` 加载的 DLL 已包含织入代码
- **风险**：低（ILPP 产物是编译后的完整 DLL）

### 6.3 DS 模式下的 UI 系统

- **问题**：DS 模式无 UI，但 TEngine 的某些模块可能依赖 UIModule
- **缓解**：DS 流程分支中跳过 LauncherMgr.Initialize()，不初始化 UI
- **注意**：GameModule.UI 在 DS 模式下仍可访问（UIModule.Instance），但不应调用 ShowUI

### 6.4 DS 模式下的资源加载

- **问题**：DS 模式仍需加载场景资源（场景中有 NetworkManager）
- **已确认**：YooAsset 在 DS 模式下正常工作（DS 仍加载资源包）
- **注意**：DS 不需要预加载 UI 资产，但场景资产必须可用

### 6.5 DS 场景设计

- **方案选择**：
  - A：单独 DS 场景（`dedicated_server.unity`），仅挂 NetworkManager + Transport
  - B：复用 main.unity，在 main.unity 中预挂 NetworkManager
- **推荐**：方案 A（DS 场景独立，不污染客户端场景）
- **注意**：DS 场景需加入 EditorBuildSettings 或通过命令行 `--scene` 指定

### 6.6 PurrNet 版本稳定性

- **当前版本**：v1.23.0-beta.24（仍处 beta）
- **风险**：beta 版本可能有 breaking change
- **缓解**：使用 fork 的 yooasset 分支（已验证与本项目 YooAsset 3 兼容）
- **注意**：升级 PurrNet 版本前需做兼容性验证

---

## 七、执行顺序建议

```
阶段一：基础引用打通（前置必须）
  ├─ 1.1 GameLogic.asmdef 添加 PurrNet.Runtime 引用
  ├─ 1.2 创建 INetworkModule 接口
  ├─ 1.3 创建 NetworkModule 实现
  ├─ 1.4 GameModule 添加 Network 访问器
  └─ 1.5 GameApp 注册 NetworkModule
     ↓ 验证：编译通过 + GameModule.Network 可访问

阶段二：DS 打包管线（能打出 DS 包）
  ├─ 2.1 ReleaseTools.BuildImp 支持 Server subtarget（BuildConfig/CLIBridge/ReleaseTools）
  ├─ 2.2 BuildCLI（Python）传递 subtarget（config_store/cli）
  ├─ 2.3 PurrNetSettings 配置（开发期保持默认）
  ├─ 2.4 Obfuz 排除 PurrNet 生成方法（新增 XML 规则 + Obfuz.asset 追加）
  ├─ 2.5 ProjectSettings DS 优化（可选，上线前）
  └─ 2.6 DS 构建预设（dedicated_server_linux.json）
     ↓ 验证：dry_run 正确 + 实际构建出 DS 可执行文件 + Obfuz 不破坏 RPC

阶段三：DS 启动流程分支（DS 能跑起来）
  ├─ 3.1 创建 DedicatedServerLauncher 命令行工具
  ├─ 3.2 ProcedureLaunch DS 分支
  ├─ 3.3 ProcedureSplash DS 分支
  ├─ 3.4 ProcedurePreload DS 分支
  ├─ 3.5 GameApp.Entrance DS 分支（场景传参交给业务）
  └─ 3.6 DS 场景准备（NetworkManager + Transport）
     ↓ 验证：DS 运行 → 热更加载 → 场景加载 → 服务器启动

阶段四：PurrNet + TEngine 体系对接（后续逐步做）
  ├─ 4.1 对象池桥接（低优先级）
  ├─ 4.2 场景加载协调（中优先级）
  ├─ 4.3 NetworkRules 配置（低优先级）
  ├─ 4.4 网络 Prefab 注册（中优先级）
  └─ 4.5 CodeStripping 配置（上线前）
```

---

## 八、补充说明

### 8.1 关于"场景传参交给业务定义"

框架侧提供：
- `DedicatedServerLauncher.ResolveStartupScene()`：解析 `--scene <名称>`
- `DedicatedServerLauncher.ResolveStartupSceneType()`：解析 `--scene-type <枚举>`
- `DedicatedServerLauncher.IsDedicatedServerBuild`：DS 模式判断

业务侧在 `GameApp.StartDedicatedServer()` 中决定：
- 是直接进战斗场景？还是先跑大厅？
- 是走 `GameSceneModule.LoadScene(SceneType)` 还是走 PurrNet `ScenesModule`？
- 需要做哪些 DS 专属初始化？

### 8.2 关于"不是只有联机"

PurrNet 集成不仅是为了联机功能，还包括：
- **NetworkStateMachine**：状态机同步（GameMode/角色状态）
- **NetworkTransform/Animator/Rigidbody**：组件级同步
- **NetworkReflection**：任意字段自动同步
- **NetworkAudioSource**：音频同步
- **CodeStripping**：客户端构建裁剪服务器代码减小包体
- **PurrDiction**（可选）：客户端预测+回滚（动作类玩法）

这些功能在阶段一打通基础引用后，业务可按需逐步引入，不需要一次性全部集成。

### 8.3 关于 NetworkManager 挂载方式

**当前**：场景中无 NetworkManager

**方案**：
- **推荐**：在 DS 场景和客户端场景中分别预挂载 NetworkManager（Inspector 配置 Transport/Rules）
- **备选**：代码动态创建（`new GameObject().AddComponent<NetworkManager>()`），但 Inspector 配置更直观
- **注意**：NetworkManager 是 MonoBehaviour + 单例（`NetworkManager.main`），场景中只能有一个

### 8.4 关于 PurrNet 的自动启动机制

PurrNet 通过 `StartFlags` 控制自动启动（源码 `NetworkManager.cs:1638-1647` 的 `ShouldStart`）：
- `startServerFlags = ServerBuild | Editor`：DS 构建和编辑器中自动启动服务器
- `startClientFlags = ClientBuild | Editor | Clone`：客户端构建和编辑器克隆中自动启动客户端

**编辑器内**：`StartFlags.Editor` 走 `ApplicationContext.isMainEditor`（源码 `ApplicationContext.cs:36`，`isEditor && !isClone`），这是独立机制，与 `UNITY_SERVER` define 无关。

**DS 构建时**：
1. `UNITY_SERVER` define 被注入（Unity DS 构建行为）
2. `ApplicationContext.isServerBuild = true`（源码 `ApplicationContext.cs:20-21`，`#if UNITY_SERVER && !UNITY_EDITOR`）
3. `ShouldStart(_startServerFlags)` 返回 true（`ServerBuild` flag 匹配）
4. `AutoStart()` 调用 `StartServer()`（源码 `:1738-1754`）
5. 服务器自动运行，无需手动调用

**客户端构建时**：
1. `UNITY_SERVER` 未定义
2. `ApplicationContext.isClientBuild = true`（源码 `ApplicationContext.cs:24-25`，`!Application.isBatchMode`）
3. `ShouldStart(_startClientFlags)` 返回 true（`ClientBuild` flag 匹配）
4. `AutoStart()` 调用 `StartClient()`
5. 客户端自动连接（需配置 transport 地址）

**注意**：`ApplicationContext.isServerBuild` 在非 Editor batchmode 下也为 true（源码 `:24`，`Application.isBatchMode`），即普通 `-batchmode` 运行也会被当作 DS。

---

## 九、参考文档

- [PurrNet 深度研究报告](../conversation-summaries/code-research/2026-09-04-purrnet-深度研究报告.md)
- [PurrNet 源码架构详细报告](../conversation-summaries/code-research/2026-09-04-purrnet-01-源码架构详细报告.md)
- [PurrNet 官方文档详细报告](../conversation-summaries/code-research/2026-09-04-purrnet-02-官方文档详细报告.md)
- [PurrNet 免费生态包详细报告](../conversation-summaries/code-research/2026-09-04-purrnet-03-免费生态包详细报告.md)
- PurrNet 官方文档：https://purrnet.dev/docs
- Unity 6 Dedicated Server 文档：https://docs.unity3d.com/6000.0/Documentation/Manual/dedicated-server.html
