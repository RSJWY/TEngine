# TEngine_Fantasy 分支结构与侵入性分析

> 日期：2026-08-28
> 背景：在 main 分支上（不切换）对比 TEngine_Fantasy 分支，了解 Fantasy 在 TEngine 中的集成方式和侵入程度。

## 结论

Fantasy 是**外挂式**集成，对 TEngine 框架核心侵入极小（8 文件 ~290 行），不改任何框架核心运行时逻辑。主要成本是 AOT 程序集拆分和 Fantasy 自身的更新补丁维护。

## 分支差异概览

- 分支分叉点：`4f2ec2df`（共同祖先）
- main 独有提交：DGame 模块迁移系列（Anim / FrameAnim / GameObjectPool / Utility 散件 / UI 组件扩展）
- TEngine_Fantasy 独有提交：Fantasy 网络框架集成 + 服务端工程 + UI 扩展组件
- diff 统计：1527 文件变更，+127861 / -159 行

## 目录结构

```
TEngine_Fantasy 分支相对 main 新增/变更：
├── GameServer/                          [全新] Fantasy 服务端框架（C# 服务端工程）
│   ├── Server/
│   │   ├── Entity/                      实体层（含协议生成代码、Luban）
│   │   ├── Hotfix/                      热更层（Handler/System）
│   │   └── Main/                        服务端入口
│   └── Tools/
│       ├── NetworkProtocol/             [唯一源] proto/config 协议定义
│       └── ProtocolExportTool/           协议导出工具
├── UnityProject/Assets/GameScripts/HotFix/
│   ├── Fantasy.Unity/                   [全新] Fantasy 客户端运行时（独立程序集）
│   │   ├── Runtime/Core/                Fantasy 核心：Scene/Network/Serialize/Pool/Log/Platform
│   │   ├── Editor/Runtime/              Fantasy Unity Editor 工具
│   │   ├── Runtime/Plugins/             预编译 DLL（MemoryPack/WebSocket/Pipelines）
│   │   ├── csc.rsp                      编译响应文件
│   │   └── package.json                 UPM 包定义 com.fantasy.unity
│   ├── GameLogic/                       [少量改动] 业务层接入点
│   │   ├── DataCenter/
│   │   │   ├── GameClient.cs             [新] 封装 Fantasy Scene/Session 的网络客户端
│   │   │   ├── ClientConnectWatcher.cs  [新] 连接断线监测
│   │   │   ├── DataCenterModule.cs      [新] 数据中心模块
│   │   │   ├── DataCenterSys.cs         [新] 数据中心系统
│   │   │   └── ReadMe.md               Fantasy 更新补丁清单
│   │   ├── GameApp.cs                   [改 +12行] StartGameLogic 里 await GameClient.InitAsync
│   │   ├── GameLogic.asmdef             [改 +1行] 加 Fantasy.Unity 程序集引用
│   │   ├── Module/UIModule/             [新] SuperScrollView/SwitchPage 等 UI 扩展
│   │   └── UI/LoginUI/                 [改] 示例登录界面
│   └── GameProto/                       [少量改动]
│       ├── GameProto.asmdef             [改 +1行] 加 Fantasy.Unity 程序集引用
│       └── Generate/NetworkProtoco/    [新] 协议生成代码
├── UnityProject/Assets/TEngine.AOT/     [新目录] 把原 GameScripts 下的 Launcher/Procedure 迁入 AOT 程序集
└── UnityProject/Assets/TEngine/         [极小改动] 见下
```

## 对 TEngine 框架本体的侵入（仅 8 文件，约 290 行）

| 文件 | 改动 | 性质 |
|------|------|------|
| `TEngine/Editor/Utility/Type.cs` | +2行 注册 `TEngine.AOT` 程序集名 | 兼容 AOT 拆分 |
| `TEngine/Runtime/Core/Utility/Utility.Http.cs` | +2行 `#pragma warning disable CS0618` | 消警告 |
| `TEngine/Runtime/DebuggerModule.QualityInformationWindow.cs` | +4行 | Debugger 显示扩展 |
| `TEngine/Runtime/DebuggerModule.ScreenInformationWindow.cs` | +4行 | Debugger 显示扩展 |
| `TEngine/Runtime/Utility/DUnityUtil.cs` | +253行 新增工具类 | 纯新增文件 |
| `TEngine/Settings/UpdateSetting.asset` | +3行 | 配置项 |

**TEngine 核心运行时（`Runtime/Core/`）零改动**——没有改 GameModule、UI、Resource、Event、FSM 等任何框架核心逻辑。

## 集成方式

1. **Fantasy 作为独立热更程序集** `Fantasy.Unity` 放在 `HotFix/` 下，通过 asmdef 引用关系接入，不污染 TEngine.Runtime
2. **业务接入点单一**：只有 `GameApp.StartGameLogic()` 加了 `await GameClient.Instance.InitAsync(_hotfixAssembly)` 一处调用
3. **AOT 拆分**：把 Launcher/Procedure 从 `GameScripts/` 迁到 `TEngine.AOT/` 程序集（因为 Fantasy 热更需要 Assembly-CSharp 不进热更）
4. **附带 UI 组件**：SuperScrollView、SwitchPage 等是 Fantasy 示例带来的 UI 扩展，与 Fantasy 框架本身无强依赖，可按需删

## 客户端/服务端共用内容

**唯一源头**：`GameServer/Tools/NetworkProtocol/` 下的 proto/config 定义文件。

```
GameServer/Tools/NetworkProtocol/
├── Outer/OuterMessage.proto     唯一协议源文件（proto3 定义）
├── Inner/InnerMessage.proto     内部协议
├── RouteType.Config             路由类型定义
├── RoamingType.Config
└── OpCode.Cache                 opcode 缓存
```

### 生成流向（由 ProtocolExportTool 一键生成到两端）

```
GameServer/Tools/NetworkProtocol/Outer/OuterMessage.proto  （唯一源）
                    │
            ProtocolExportTool 导出
                    │
        ┌───────────┴───────────┐
        ▼                       ▼
  服务端生成                    客户端生成
GameServer/Server/Entity/       UnityProject/Assets/GameScripts/HotFix/
  Generate/NetworkProtocol/       GameProto/Generate/NetworkProtoco/
  ├── OuterMessage.cs              ├── OuterMessage.cs        ← 消息体（同份）
  ├── OuterOpcode.cs               ├── OuterOpcode.cs         ← opcode 常量（同份）
  └── RouteType.cs                 └── RouteType.cs          ← 路由类型（同份）
                                   └── NetworkProtocolHelper.cs ← 客户端专属扩展方法
```

### 共享内容（客户端服务端完全一致，工具自动生成）

| 文件 | 内容 |
|------|------|
| `OuterMessage.cs` | 消息类定义（`C2A_LoginRequest` 等） |
| `OuterOpcode.cs` | 协议号常量 |
| `RouteType.cs` | 路由类型常量 |

### 客户端独有

- `NetworkProtocolHelper.cs` — 扩展方法，给 `Session` 加 `C2A_LoginRequest(...)` 便捷调用
- `LubanLib/` — Luban 配置表运行时（`BeanBase.cs`、`ByteBuf.cs` 等）

**两端没有直接引用关系，靠导出工具保持同步。**

## 风险点 / 维护成本

1. **Fantasy 更新需手动补丁**（见 `GameLogic/DataCenter/ReadMe.md`）：
   - `Scene.cs` 中 `MessageDispatcherComponent` 属性改 public
   - `MessageDispatcherComponent.cs` 添加 `#if FANTASY_UNITY` 客户端消息注册回调
   - `ProtoBufHelper.cs` 添加协议收发日志回调
   - `Log.cs` 的 `[Conditional]` 宏需对齐 TEngine.Log
2. **Fantasy.Unity 热更问题**：默认随热更包热更，但作者建议改为不热更（需带着 Assembly-CSharp 一起热更才行）
3. **Obfuz 引用**：如果项目用了 Obfuz，需在 `Fantasy.Unity.asmdef` 和 `Fantasy.Editor.asmdef` 中加 `Obfuz.Runtime` 引用

## Fantasy.Unity 程序集内部结构

```
Fantasy.Unity/Runtime/Core/
├── Platform/
│   ├── Unity/Entry.cs          MonoBehaviour 入口，Initialize + Update 驱动 ThreadScheduler
│   ├── Unity/FantasyRuntime.cs 650行 运行时核心
│   ├── Console/                 控制台平台
│   └── Net/                     服务端平台（含服务发现 ServiceDiscovery）
├── Network/
│   ├── Protocol/               KCP/TCP/WebSocket/HTTP 协议实现
│   ├── Message/                消息派发/调度/PacketParser
│   ├── Session/                会话管理/心跳
│   ├── Route/                  路由组件
│   ├── Roaming/                漫游组件（服务端侧）
│   ├── Sphere/                 事件广播（服务端侧）
│   └── Addressable/            寻址组件（服务端侧）
├── Scene/                      Scene 生命周期 + 多线程调度器
├── Serialize/                  ProtoBuf/MemoryPack/BSON 三套序列化
├── Pool/                       对象池
└── Log/                        日志系统（需对齐 TEngine 宏）
```
