# PurrNet 网络模块封装

## 概述

本 Fork 集成了 [PurrNet](https://purrnet.dev)（v1.23.0-beta.24，fork yooasset 分支）作为网络框架，并在 TEngine Module 体系内封装为 `INetworkModule` / `NetworkModule`，通过 `GameModule.Network` 访问。

当前已完成**阶段一：基础引用打通**。DS（专用服务器）打包管线和启动流程分支见 [计划文件](../../UnityProject/.plans/purrnet-ds-integration.md)。

## 关键文件

| 文件 | 说明 |
| --- | --- |
| `Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef` | 添加 `PurrNet.Runtime` 引用（GUID `6e20f757a1bae164fa42750dd2b27dcb`） |
| `Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/INetworkModule.cs` | 网络模块接口 |
| `Assets/GameScripts/HotFix/GameLogic/Module/NetworkModule/NetworkModule.cs` | 网络模块实现 |
| `Assets/GameScripts/HotFix/GameLogic/GameModule.cs` | `Network` 访问器 + `Shutdown` 清理 |
| `Assets/GameScripts/HotFix/GameLogic/GameApp.cs` | 模块注册 |

## INetworkModule 接口

```csharp
public interface INetworkModule
{
    // 状态查询
    PurrNet.NetworkManager NetworkManager { get; }
    bool IsServer { get; }
    bool IsClient { get; }
    bool IsHost { get; }
    bool IsDedicatedServerBuild { get; }
    bool IsAutoStarted { get; }

    // 操作
    void StartServer();
    void StartClient();
    void StartHost();
    void StopNetwork();

    // 注入
    void BindNetworkManager(PurrNet.NetworkManager networkManager);
}
```

### 成员说明

| 成员 | 说明 | 源码依据 |
| --- | --- | --- |
| `NetworkManager` | PurrNet NM 实例（逃生口，业务可直接访问 NM 挂事件/调 SpawnYooAsset） | `NetworkManager.cs:33`（`main` 静态属性） |
| `IsServer` / `IsClient` / `IsHost` | 当前网络角色 | `NetworkManager.cs:482/491/509` |
| `IsDedicatedServerBuild` | 是否 DS 构建（编译期判断） | `ApplicationContext.cs:20-28` |
| `IsAutoStarted` | PurrNet AutoStart 是否已触发（DS/客户端构建时 `Start()` 自动启动） | `NetworkManager.cs:1738` |
| `StartServer()` / `StartClient()` / `StartHost()` | 手动启动网络 | `NetworkManager.cs:2109/2394/2275` |
| `StopNetwork()` | 停止网络 | `NetworkManager.cs:2520/2529` |
| `BindNetworkManager(NM)` | 业务实例化 NM 预制体后注入 | — |

## NetworkManager 获取方式（三层）

1. **OnInit 主动查一次**：`NetworkManager.main` → fallback `FindFirstObjectByType`，找到打 Info 日志，找不到也只打 Info（不报错）
2. **懒加载兜底**：首次访问查询属性或操作方法时，`EnsureNetworkManager()` 懒查找并缓存
3. **预制体注入**：业务 `LoadGameObjectAsync` 出 NM 预制体后，调 `BindNetworkManager(nm)` 注入

## 使用示例

### 方式一：场景预挂 NetworkManager

```csharp
// 场景中预挂 NM，OnInit 自动找到
if (GameModule.Network.IsServer)
    Log.Info("服务器已运行");
```

### 方式二：动态实例化预制体

```csharp
var go = await GameModule.Resource.LoadGameObjectAsync("NetworkManagerPrefab");
var nm = go.GetComponent<PurrNet.NetworkManager>();
GameModule.Network.BindNetworkManager(nm);
GameModule.Network.StartHost();
```

### 方式三：一直不加载（非联机场景）

```csharp
// 所有查询返回 false，操作打 Error，不崩溃，不影响其他模块
GameModule.Network.IsServer;  // false
```

## Shutdown 行为

仅清 `_networkManager` 引用，不调 `StopNetwork`。NM 的 `OnDestroy` 会自行断连（PurrNet 源码 `:1995-2030` 保证）。

## PurrNet 自动启动机制

PurrNet 通过 `StartFlags` 控制自动启动（源码 `ShouldStart`，`:1638-1647`）：

- **编辑器内**：`StartFlags.Editor` 走 `ApplicationContext.isMainEditor`（`isEditor && !isClone`），与 `UNITY_SERVER` 无关
- **DS 构建时**：`UNITY_SERVER` define 注入 → `ApplicationContext.isServerBuild=true` → `StartFlags.ServerBuild` 匹配 → `AutoStart()` 调 `StartServer()`
- **客户端构建时**：`ApplicationContext.isClientBuild=true` → `StartFlags.ClientBuild` 匹配 → `AutoStart()` 调 `StartClient()`

**注意**：`ApplicationContext.isServerBuild` 在非 Editor batchmode 下也为 true（`Application.isBatchMode`），即普通 `-batchmode` 运行也会被当作 DS。

## 后续阶段（未完成）

- 阶段二：DS 打包管线（BuildCommand/BuildCLI 支持 `--subtarget Server`）
- 阶段三：DS 启动流程分支（`DedicatedServerLauncher` + Procedure DS 分支）
- 阶段四：PurrNet + TEngine 体系对接（对象池桥接、场景加载协调、NetworkRules、网络 Prefab 注册、CodeStripping）

详见 [计划文件](../../UnityProject/.plans/purrnet-ds-integration.md)。
