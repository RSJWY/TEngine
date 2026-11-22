using System.Threading;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using TEngine;
using UnityEngine;

namespace GameLogic.NetworkTest
{
    /// <summary>
    /// 联机测试驱动器（移植自 PurrNet 官方 PlayModeTests 的 Bootstrap 调度思想）。
    /// <para>角色直接读取 NetworkManager 的实际运行状态，避免 MPPM Tag 与网络状态不一致。
    /// 期望连接数：<c>-count N</c>；Host 默认 2（含本地 Client），Server 默认 1。
    /// Runner 不负责起网络——由 GameApp 的 MPPM/Player 分流先启动。</para>
    /// </summary>
    public static class NetTestRunner
    {
        /// <summary>整轮测试总超时（秒）。</summary>
        private const float TotalTimeoutSeconds = 10f * 60f;
        private const float ConnectionTimeoutSeconds = 60f;
        private static bool _runScheduled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRunState()
        {
            _runScheduled = false;
        }

        /// <summary>读取真包的 -role host/client 测试角色；Editor 与未传参时返回 null。</summary>
        public static NetTestRole? ResolvePlayerTestRole()
        {
            if (Application.isEditor)
            {
                return null;
            }

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != "-role")
                {
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    throw new System.ArgumentException("[NetTest] -role 缺少参数，仅支持 host 或 client。");
                }

                switch (args[i + 1].ToLowerInvariant())
                {
                    case "host":
                        return NetTestRole.Host;
                    case "client":
                        return NetTestRole.Client;
                    default:
                        throw new System.ArgumentException($"[NetTest] 不支持的 -role={args[i + 1]}，仅支持 host 或 client。");
                }
            }

            return null;
        }

        /// <summary>在离线 NM 上补充测试用运行时 PrefabProvider；已配置时保持原配置。</summary>
        public static void EnsurePrefabProvider(NetworkManager nm)
        {
            if (nm == null || nm.prefabProvider != null)
            {
                return;
            }

            if (!nm.isOffline)
            {
                Log.Error("[NetTest] PrefabProvider 必须在 StartHost/StartClient 前配置。");
                return;
            }

            var provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
            provider.name = "NetTestRuntimePrefabs";
            provider.autoGenerate = false;
            nm.SetPrefabProvider(provider);
            Log.Info("[NetTest] 已创建运行时测试 PrefabProvider。");
        }

        /// <summary>等待本端连接且服务器等到全部玩家后，启动一轮测试；重复调用只保留首轮。</summary>
        public static void RunAllWhenReady(NetworkManager nm)
        {
            if (nm == null)
            {
                Log.Error("[NetTest] NetworkManager 为空，无法安排联机测试。");
                return;
            }

            if (_runScheduled)
            {
                return;
            }

            _runScheduled = true;
            Log.Warning($"[NetTest] 已安排联机测试，等待连接就绪：server={nm.isServer}, client={nm.isClient}, players={nm.playerCount}, clientState={nm.clientState}");
            RunWhenReadyAsync(nm).Forget();
        }

        private static async UniTask RunWhenReadyAsync(NetworkManager nm)
        {
            try
            {
                using var cts = new CancellationTokenSource((int)(ConnectionTimeoutSeconds * 1000));
                await NetTestWait.WaitWithTimeout(
                    () => IsReady(nm),
                    ConnectionTimeoutSeconds,
                    cts.Token);
                RunAll(nm);
            }
            catch (System.Exception e)
            {
                _runScheduled = false;
                Log.Error($"[NetTest] 等待联机就绪失败：{e}\n" +
                          $"server={nm.isServer}, client={nm.isClient}, host={nm.isHost}, players={nm.playerCount}, " +
                          $"clientState={nm.clientState}, serverState={nm.serverState}");
            }
        }

        private static bool IsReady(NetworkManager nm)
        {
            if (nm == null || (!nm.isServer && !nm.isClient))
            {
                return false;
            }

            if (nm.isClient && nm.clientState != ConnectionState.Connected)
            {
                return false;
            }

            int expectedConnections = ResolveExpectedConnections(ResolveRole(nm));
            return !nm.isServer || nm.playerCount >= expectedConnections;
        }

        /// <summary>开始整轮测试（异步 fire-and-forget，结果走日志）。</summary>
        public static void RunAll(NetworkManager nm)
        {
            NetTestMessages.EnsureRegistered();

            if (nm == null || (!nm.isServer && !nm.isClient))
            {
                Log.Error("[NetTest] 网络未启动，无法运行联机测试。请先 StartHost/StartServer/StartClient。");
                return;
            }

            RunAllAsync(nm).Forget();
        }

        private static async UniTask RunAllAsync(NetworkManager nm)
        {
            NetTestRole role = ResolveRole(nm);
            using var runCts = new CancellationTokenSource((int)(TotalTimeoutSeconds * 1000));
            var ctx = new NetTestContext
            {
                role = role,
                expectedConnections = ResolveExpectedConnections(role),
                networkManager = nm,
                cancellationToken = runCts.Token,
            };

            var scenarios = new NetTestScenario[]
            {
                new HostClientSyncScenario(),
                new RpcAttributeScenario(),
                new SyncVarAttributeScenario(),
            };

            Log.Warning($"======= [NetTest] 开始整轮联机测试：role={ctx.role}, expectedConnections={ctx.expectedConnections}, scenarios={scenarios.Length} =======");

            int pass = 0;
            int fail = 0;

            for (int i = 0; i < scenarios.Length; i++)
            {
                NetTestScenario scenario = scenarios[i];
                scenario.Setup(ctx, nm);

                // 顺序握手：服务器发令；客户端等令
                if (ctx.isServer)
                {
                    NetTestSequencer.IssueStart(ctx, i);
                }

                await NetTestSequencer.WaitForStart(ctx, i);

                NetTestResult result;
                string error = null;
                try
                {
                    result = await scenario.RunScenario(ctx);
                }
                catch (System.Exception e)
                {
                    result = NetTestResult.Fail($"{e.GetType().Name}: {e.Message}");
                    error = e.StackTrace;
                }

                if (result.success)
                {
                    pass++;
                    Log.Warning($"[NetTest] [{i}] {scenario.Name} => PASS {result.message}");
                }
                else
                {
                    fail++;
                    Log.Error($"[NetTest] [{i}] {scenario.Name} => FAIL {result.message}\n{error}");
                }

                // 回执与等待：客户端回执；服务器等全员
                if (ctx.isClient)
                {
                    NetTestSequencer.AckDone(ctx, i);
                }

                if (ctx.isServer)
                {
                    await NetTestSequencer.WaitForAllAcks(ctx, i);
                }
            }

            Log.Warning($"======= [NetTest] 整轮结束：PASS={pass}, FAIL={fail} =======");
        }

        private static NetTestRole ResolveRole(NetworkManager nm)
        {
            if (nm.isHost)
            {
                return NetTestRole.Host;
            }

            return nm.isServer ? NetTestRole.Server : NetTestRole.Client;
        }

        private static int ResolveExpectedConnections(NetTestRole role)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-count" && int.TryParse(args[i + 1], out int n) && n > 0)
                {
                    return n;
                }
            }
#endif
            return role == NetTestRole.Server ? 1 : 2;
        }
    }
}
