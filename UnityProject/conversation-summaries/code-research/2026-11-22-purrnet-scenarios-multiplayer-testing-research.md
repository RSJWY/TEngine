# PurrNet 原始项目多人联机 Scenario 测试机制研究

日期：2026-11-22  
研究对象：`E:\Unity\PurrNet`（Unity `6000.5.11f1`）  
对照项目：`E:\Unity\tengine-purrnet\UnityProject`（Unity `6000.3.25f1`）

## 结论

PurrNet 原始项目确实使用 Unity 6 的 **Multiplayer Play Mode（MPPM）Scenarios**，但 MPPM 只负责在编辑器中启动主实例和克隆实例。真正的自动化测试不是 MPPM 或 Unity Test Framework 自带的 `[UnityTest]`，而是原项目自己实现的一套 `Scenario` 运行框架。

它有两条执行路径，共用同一批 Scenario：

1. **编辑器调试**：MPPM Scenario 启动主编辑器和多个 Editor Instance；主编辑器作为 Host，克隆作为 Client。
2. **CI/真实 Player**：先构建独立 Player，再用 shell 并行启动 Server/Host 与多个 Client 进程，不依赖 MPPM。

核心是由 `Bootstrap` 启动网络并逐个调度 Scenario，由 RPC 完成开始、回执、barrier 和结束握手，最后每个进程输出 JSON 结果；Player 以退出码表示整轮成功或失败。

## 1. 编辑器路径：MPPM 负责多实例，Bootstrap 负责测试

### 1.1 依赖和配置资产

原项目 `Packages/manifest.json` 使用：

- `com.unity.multiplayer.playmode`: `2.0.2`
- `com.unity.test-framework`: `1.7.0`
- `com.unity.dedicated-server` 未在原项目 manifest 中列出；本项目已安装 `2.0.2`

原项目已有两个 MPPM `ScenarioConfig`：

- `Assets/Settings/PlayMode/Test.asset`：Main Editor + Player 2 + Player 3
- `Assets/Settings/PlayMode/Test - Host.asset`：Main Editor + Player 2

两者的 `m_EditorClassIdentifier` 都是 `Unity.Multiplayer.PlayMode.Editor.ScenarioConfig`，主实例初始场景都指向 `Assets/PlayModeTests/Bootstrap.unity`。

MPPM 2.0.2 包内文档给出的入口是 **Window → Play Mode → Scenarios**，进入 Play Mode 后会自动构建并运行配置中的实例。这里的 Scenario 是“多实例运行配置”，不是测试断言本身。

### 1.2 测试场景与角色分配

`Assets/PlayModeTests/Bootstrap.unity` 是第 0 个构建场景，挂载 `NetworkManager`、`Bootstrap` 和大量 Scenario 组件。`Bootstrap` 在 `Awake` 通过 `GetComponentsInChildren<Scenario>()` 收集测试，因此增删测试主要是增删场景中的组件，不是靠 NUnit 反射发现。

关键代码：

- `Assets/PlayModeTests/Scripts/Bootstrap.cs:71-83`：读取参数并收集 Scenario。
- `Assets/PlayModeTests/Scripts/Bootstrap.cs:281-307`：解析 Server/Client/Host。
- `Assets/PurrNet/Runtime/Utils/ClonesContext.cs:13-28`：通过当前 `Assets` 目录是否为 reparse point 判断 MPPM 克隆。

无 `-role` 参数时：

- 主编辑器采用 Inspector 的 `_editorRole`；当前场景序列化值为 `2`，即 `NetworkRole.Host`。
- MPPM 克隆始终采用 `NetworkRole.Client`。
- 当前场景 `_editorExpectedConnections=3`，并设置了 `_editorScenarioFilter=YooAssetSceneTransferScenario`，所以当前磁盘配置默认只跑这个单项测试；清空过滤才跑完整套件。

编辑器下每轮结束只把 JSON 打到 Console，失败时额外输出 `Failed test results`，不会以退出码结束 Editor（`Bootstrap.cs:521-528`）。

## 2. CI 路径：真实 Player 多进程

### 2.1 构建入口

- 本地 Windows：`Assets/Editor/LocalTestBuild.cs`，入口 `LocalTestBuild.BuildWindowsPlayer`，默认输出 `Builds/LocalNetworkTests/PurrNetTests.exe`，默认 Development，可用 `-purrRelease` 改为 Release。
- CI Linux：`Assets/Editor/CIBuild.cs`，入口 `CIBuild.BuildLinuxPlayer`，显式使用 IL2CPP，以覆盖 IL 到 C++ 的代码生成兼容性。

两者都直接使用当前 `EditorBuildSettings.scenes` 中启用的场景。

### 2.2 进程编排

`.github/workflows/network-tests.yml` 对 `server` 和 `host` 两种模式分别运行：

- `server`：1 个纯 Server + N 个 Client，`-count N`。
- `host`：1 个 Host + N-1 个外部 Client，`-count N`；Host 的本地 Client 也计入总玩家数。

服务器先启动，等待 `connectionDelaySeconds` 后再启动客户端。所有进程使用相同 `-scenario` 过滤参数，保证两端执行相同测试。Linux 下每个 Player 都由 `xvfb-run` 包装，避免 Unity 6 Linux Player 的启动崩溃。

典型参数：

```bash
PurrNetTests -batchmode -nographics \
  -role host -count 3 \
  -results test-results/host.json \
  -logFile test-logs/host.log

PurrNetTests -batchmode -nographics \
  -role client \
  -results test-results/client-1.json \
  -logFile test-logs/client-1.log
```

工作流等待所有进程结束，任一非零退出码都会令测试失败；还会检查 JSON 中的 `result.success`、日志中的致命错误，并上传每个进程的 JSON 和日志。

另有 `.github/workflows/build-compatibility-tests.yml`，可分别使用服务器构建和客户端构建，并显式传入 `-serverHost 127.0.0.1`、`-port`、`-connectTimeout`，用于跨产物兼容性验证。

## 3. 自研 Scenario 框架怎样运行

### 3.1 基本协议

`Scenario` 是 `MonoBehaviour` 基类：

```csharp
public abstract UniTask<ScenarioResult> RunScenario(ScenarioContext ctx);
```

`ScenarioContext` 提供角色、期望连接数、`NetworkManager` 和取消令牌。`RunSplit` 将测试拆成 client/server 两段：

- 纯 Server 只跑 server 段。
- 纯 Client 只跑 client 段。
- Host 同时启动两段并等待双方完成；client 段先启动，以便先注册回调。

`ScenarioResult` 只包含 `success` 和 `message`，`ScenarioDetails` 再附加名称、耗时、收发字节数和可选 benchmark 指标。

### 3.2 启动与顺序调度

`Bootstrap` 自身也是第 0 个 Scenario：

1. 各端先执行所有 Scenario 的 `Setup`。
2. 第 0 项根据角色调用 `StartServer`/`StartClient`。
3. 等本端连接成功，Server/Host 再等 `playerCount >= expectedConnections`。
4. 从第 1 项开始，Server/Host 通过 `ScenarioSequencer.IssueStart(i)` 广播开始。
5. 每端运行相同下标的 Scenario。
6. 纯 Client 完成后通过 `AckDone` 回执；Server/Host 等所有外部 Client 回执后再进入下一项。
7. 末尾广播 SequenceComplete，并进行有超时的结束握手。

因此不是服务器跑完后客户端自行随意跑，而是每个 Scenario 都有跨端屏障和完整回执。

### 3.3 Scenario 内 barrier

`ScenarioBarrier.Wait(ctx, barrierId, timeout)` 用于一个 Scenario 内更细粒度的跨端同步：

- client 半边通过 ServerRpc 报告到达。
- server 半边等 `expectedConnections` 个 client 半边到达，再通过 ObserversRpc 放行。
- Host 的本地 client 半边也计入总数。
- `_waitsByBarrier` 缓存同一个 `Task`，使 Host 并发执行的 client/server 两半共享一次等待，避免重复报到或迟到一半重新执行已完成的 barrier。
- 超时也广播放行，避免一个缺失进程把其余进程永久卡住。

`Assets/Tests/BitPacker.Tests/ScenarioBarrierTests.cs` 是普通 Unity Test Framework 测试，用来单测这个辅助组件；它证明“PlayModeTests 的 Scenario 套件本身不是 NUnit 测试”和“辅助逻辑仍可用 NUnit 单测”可以同时成立。

### 3.4 超时、状态清理和结果

- 每个 Scenario 的开始/回执最长等待 5 分钟，结束握手 10 秒。
- 单项测试内部再设置 spawn、状态收敛、RPC 等具体超时。
- `Bootstrap`、`ScenarioSequencer`、`ScenarioBarrier` 都用 `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` 清静态状态，兼容禁用 Domain Reload。
- 每个进程都能写出自己的结果数组；未执行或失败项都会被判为失败。
- Player 最终调用 `Application.Quit(0/-1)`，让外部进程编排能得到可靠退出码。

源码当前可检索到 125 个直接继承 `Scenario` 的类文件，覆盖 RPC、SyncTypes、生命周期、场景切换、序列化、传输和组件行为；实际运行数量仍由场景中启用的组件和 Scenario 过滤决定。

## 4. 一个 Scenario 通常怎么写

以 `SyncVarServerAuthScenario` 为例：

1. `Setup` 动态创建测试 NetworkIdentity  prefab，并注册到 `prefabProvider`。
2. Server/Host 生成对象，各端等待本地实例出现。
3. client 半边验证初始值和最终值，再通过 RPC 报告状态。
4. server 半边等待所有 client 就绪，修改变量，验证本地值并等待所有 client 收敛。
5. 双方用完成信号收口，把超时和值不一致转成 `ScenarioResult.Fail`。

这说明它不是只检查“两个进程是否连上”，而是让两端对同一网络行为分别断言，再由协议保证两端都完成。

## 5. 本项目现状与关键差异

本项目已经具备大部分基础设施：

- `Packages/manifest.json` 已安装相同版本 `com.unity.multiplayer.playmode=2.0.2`。
- `Assets/Editor/MppmBootstrapProbe.cs` 已能通过反射创建并启动 host+client 或 ds+client 双实例 Scenario。
- `Assets/GameScripts/HotFix/GameLogic/NetworkTest/` 已有 `NetTestScenario`、`NetTestRunner`、`NetTestSequencer`、`NetTestBarrier` 和 `HostClientSyncScenario`。
- 为适配热更程序集的 PurrNet ILPP 限制，当前实现把原项目的 RPC 控制消息换成 `NetworkManager.Subscribe/Send`，并手动注册 Hasher 与 Packer；这是合理的项目级适配。

但当前还不能把它当成已经可运行的测试体系：

### 5.1 没有落盘的 MPPM Scenario 配置

对 `Assets/**/*.asset` 检索 `Unity.Multiplayer.PlayMode.Editor.ScenarioConfig` 没有任何匹配，`Assets/Settings/` 当前也只有 `PlayMode.meta`，`ProjectSettings/VirtualProjectsConfig.json` 的 `PlayerTags` 为空。

`MppmBootstrapProbe` 调用 `ScenarioFactory.CreateScenario` 后立刻 `LoadScenario`/`StartScenario`，创建的是供当前会话使用的临时对象，并不等同于原项目 `Assets/Settings/PlayMode/Test.asset`、`Test - Host.asset` 那样可在 Scenarios 窗口选择、检查和重复运行的持久配置。因此本项目当前既没有图形化 Scenario 资产，也没有固定保存的实例数量、初始场景、角色和 PlayerTag 配置。

### 5.2 Runner 没有启动入口

全项目 C# 搜索只有 `NetTestRunner.RunAll` 的声明，没有任何调用。即使 MPPM 双实例和网络启动成功，`HostClientSyncScenario` 也不会自动执行。

### 5.3 DS 角色判定不一致

`NetTestRunner.ResolveRole()` 在没有命令行 `-role` 时，把所有非克隆编辑器都判为 Host；ds+client 形态中的主编辑器因此仍会得到 Host 上下文，而不是 Server。

同时，`GameApp.ResolveMppmTag()` 在检测到主编辑器后立即返回 `"host"`，不会读取主实例的 `"ds"` Tag；因此 `StartGameLogic()` 中的 `ResolveMppmTag() == "ds"` 分支按当前实现无法由该 Tag 触发。若另开“DS 模拟模式”可以进入 DS 启动分支，但 NetTestRunner 的角色仍需单独修正。

原项目的做法是角色解析只发生一次，同一个 `_role` 同时决定网络启动和 ScenarioContext，不会出现“实际只启动 Server，测试却按 Host 跑”的分裂。

### 5.4 Host barrier 计数与原项目不同

当前 `NetTestBarrier`：

- Host 的 client/server 两个 `RunSplit` 半边都会调用 `Wait`，且没有原项目的共享 `_waitsByBarrier`。
- server 半边却按 `expectedConnections - 1` 等待。

在 Host + 1 外部 Client、`expectedConnections=2` 时，Host 自己的一次本地报到就可能满足 `1` 并提前放行，无法保证外部 Client 已到达；两个 Host 半边还可能重复报到。原项目等待完整的 `expectedConnections`，并用共享 Task 合并 Host 的重复调用，且有针对性单元测试。

### 5.5 结果目前只进日志

当前 Runner 只有 PASS/FAIL 日志，没有原项目的每进程 JSON、单项过滤和 Player 退出码。这不阻塞编辑器内第一轮调试，但还不足以直接接入无人值守的多进程回归。

## 6. 建议的复用顺序

1. **先只打通 editor host+client 两实例**，不要一开始就引入 CI 和多客户端矩阵。
2. 在 Host/Server 等到期望玩家连接、Client 完成连接后，分别且仅调用一次 `NetTestRunner.RunAll`。
3. 让角色由实际网络启动方式或统一入口决定：host→Host、ds→Server、clone→Client；不要仅凭“是否主编辑器”决定。
4. 按原项目语义修正 Host barrier：统一计算 client 半边数量，并合并 Host 的并发 Wait；为该项目辅助逻辑补测试时再使用 Unity Test Framework。
5. 先让 `HostClientSyncScenario` 的双向场景切换产生可信 PASS/FAIL，再扩展 RPC、生成/销毁、断线重连等用例。
6. 编辑器链路稳定后，再补 `-scenario`、每进程 JSON 和退出码，最后复用原项目的 Player 多进程编排思路做 CI。

## 7. 本次验证边界

本次是对原项目当前磁盘源码、场景/配置资产、MPPM 2.0.2 包内文档和 GitHub Actions 工作流的静态研究；未启动原项目 Unity，也未实际运行其网络测试。关于本项目问题的结论来自当前源码调用关系和计数逻辑，未改动业务代码，也未把任何联机运行标记为已通过。
