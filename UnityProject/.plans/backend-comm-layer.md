# 后端通讯层（Backend Comm Layer）— 粗略计划草案

> **状态**：粗略记录，待阶段三完成后单独制定详细计划
> **创建时间**：2026-10-05
> **关联计划**：`purrnet-ds-stage3.md`（阶段三：DS 启动流程分支）
> **Plan 模式**：本文件仅为意向记录，未实施

---

## 一、背景

PurrNet 只解决 **玩家客户端 ↔ DS** 的游戏内网络（RPC/状态同步）。
本计划解决 **DS ↔ 后端**、**客户端 ↔ 后端** 的另一层通讯，性质与 PurrNet 完全不同。

```
[玩家客户端] ←PurrNet(RPC/同步)→ [DS 无头 Unity] ←后端通讯→ [Nakama/轻量后端]
                ① 玩家↔DS                          ② DS↔后端
[玩家客户端] ←────────────────后端通讯────────────→ [Nakama/轻量后端]
                ③ 客户端↔后端（获取 DS 列表等）
```

## 二、需求

1. **后端选型**：可能用 Nakama，但更倾向自研轻量后端
2. **通讯性质**：双向
   - DS → 后端：汇报自己状态（负载/在线人数/场景/matchId/健康度）
   - 后端 → DS：下发业务内容（调度指令/配置变更/踢人/场景切换）
3. **客户端也连后端**：获取 DS 列表、登录认证、业务数据
4. **协议倾向**：WebSocket（双向、轻量、Unity 实现简单）；纯上报场景可用 HTTP

## 三、核心原则

- **框架不参与通讯实现**：复用 TEngine 已有基础设施（RuntimeConfigModule/DeployConfig、UniTask、TimerModule、GameEvent、AsyncOperationModule、ClientSaveDataMgr），业务侧自己实现 client
- **业务主导**：接口定义、具体实现（Nakama/HTTP/WebSocket）都在 `GameLogic/Module/BackendModule/` 下
- **DS 与客户端共用**：同一套 `IBackendClient` 接口，DS/客户端按需选实现和调用时机
- **配置承载**：后端地址/端口/authToken 走 `DeployConfig.Backend` 段（TOML/JSON 现场配置）

## 四、草案目录结构

```
Assets/GameScripts/HotFix/GameLogic/Module/BackendModule/
  ├─ IBackendClient.cs              # 业务定义接口（Connect/Disconnect/Send/Recv/事件）
  ├─ IBackendModule.cs              # TEngine 模块门面接口
  ├─ BackendModule.cs               # TEngine 模块实现（生命周期 + 转发）
  ├─ BackendService.cs              # 业务侧服务（按 DeployConfig.Backend.Type 选 client）
  └─ Clients/
      ├─ NakamaBackendClient.cs     # 业务实现（按需，用 Nakama .NET SDK）
      ├─ HttpBackendClient.cs       # 业务实现（按需，轻量上报/拉取）
      └─ WebSocketBackendClient.cs  # 业务实现（按需，双向实时，推荐）
```

## 五、待细化项（制定详细计划时确认）

- [ ] 后端选型最终决定（Nakama vs 自研 vs 并存）
- [ ] 通讯协议最终决定（WebSocket vs HTTP 长轮询 vs 混合）
- [ ] 消息格式（JSON vs Protobuf vs MessagePack）
- [ ] DS 注册/心跳协议细节（频率/字段/重连退避）
- [ ] 后端下发指令的协议（调度/配置推送/踢人等）
- [ ] 客户端登录/获取 DS 列表的协议
- [ ] 鉴权方式（token/JWT/会话）
- [ ] 是否需要 `DeployConfig.Backend` 配置段（vs 业务自读 TOML）
- [ ] 是否纳入 TEngine 模块门面（`GameModule.Backend`）还是纯业务服务单例
- [ ] 与阶段三 `StartDedicatedServer()` 的集成点（业务侧加一行 `BackendService.Instance.ConnectAndRegisterAsync()`）

## 六、与已有计划的关系

- **不依赖阶段三**：可独立开发，但 DS 实际跑起来需要阶段三完成
- **集成点**：阶段三的 `GameApp.StartDedicatedServer()` 业务扩展时加后端连接调用
- **不修改框架**：本计划不动 TEngine.Runtime/Editor/PurrNet 包，纯业务侧 + 可选 DeployConfig 配置段

## 七、优先级

中。阶段三（DS 跑起来）+ 阶段四（PurrNet 体系对接）之后做。
若业务急需"DS 真正能被调度"，可提前到阶段三完成后立即做。
