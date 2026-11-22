using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using TEngine;
using UnityEngine;

namespace GameLogic.NetworkTest
{
    /// <summary>
    /// Scenario 顺序握手（移植自 PurrNet 官方 ScenarioSequencer）：服务器逐个下发开始，
    /// 客户端完成后回执 ack，服务器等全员 ack 后再进下一个。
    /// <para>超时 5 分钟；静态状态禁域重载兼容。</para>
    /// </summary>
    public static class NetTestSequencer
    {
        private const float ScenarioTimeoutSeconds = 5f * 60f;

        private static readonly Dictionary<int, HashSet<PlayerID>> _acksByIndex = new();
        private static int _latestStartedIndex = -1;
        private static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRunState()
        {
            _acksByIndex.Clear();
            _latestStartedIndex = -1;
            _subscribed = false;
        }

        /// <summary>服务器调用：广播第 index 个 Scenario 开始。</summary>
        public static void IssueStart(NetTestContext ctx, int index)
        {
            if (!ctx.isServer)
            {
                return;
            }

            if (index > _latestStartedIndex)
            {
                _latestStartedIndex = index;
            }

            ctx.networkManager.SendToAll(new NetTestStartMsg { scenarioIndex = index });
            Log.Info($"[NetTest] IssueStart({index}) 已广播。");
        }

        /// <summary>任意端调用：等待本端收到第 index 个开始信号（服务器自身立即通过）。</summary>
        public static async UniTask WaitForStart(NetTestContext ctx, int index)
        {
            EnsureSubscribed(ctx);

            try
            {
                await NetTestWait.WaitWithTimeout(
                    () => _latestStartedIndex >= index || !ctx.networkManager.isClient,
                    ScenarioTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (System.TimeoutException)
            {
                Log.Error($"[NetTest] WaitForStart 超时：waitingFor={index}, latest={_latestStartedIndex}");
                throw;
            }
        }

        /// <summary>客户端调用：第 index 个完成，向服务器回执。</summary>
        public static void AckDone(NetTestContext ctx, int index)
        {
            if (ctx.role != NetTestRole.Client)
            {
                return;
            }

            ctx.networkManager.SendToServer(new NetTestAckMsg { scenarioIndex = index });
        }

        /// <summary>服务器调用：等待第 index 个的全员 ack。</summary>
        public static async UniTask WaitForAllAcks(NetTestContext ctx, int index)
        {
            if (!ctx.isServer)
            {
                return;
            }

            EnsureSubscribed(ctx);

            try
            {
                await NetTestWait.WaitWithTimeout(
                    () => AllConnectedClientsAcked(ctx, index),
                    ScenarioTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (System.TimeoutException)
            {
                _acksByIndex.TryGetValue(index, out var set);
                int expected = ctx.expectedConnections - (ctx.role == NetTestRole.Host ? 1 : 0);
                Log.Error($"[NetTest] WaitForAllAcks({index}) 超时：acks={set?.Count ?? 0}/{expected}");
                throw;
            }

            _acksByIndex.Remove(index);
        }

        private static bool AllConnectedClientsAcked(NetTestContext ctx, int index)
        {
            _acksByIndex.TryGetValue(index, out var acks);
            var connected = ctx.networkManager.players;
            var localId = ctx.networkManager.localPlayer;
            for (int i = 0; i < connected.Count; i++)
            {
                var player = connected[i];
                if (ctx.role == NetTestRole.Host && player == localId)
                {
                    continue;
                }

                if (acks == null || !acks.Contains(player))
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureSubscribed(NetTestContext ctx)
        {
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            var nm = ctx.networkManager;

            nm.Unsubscribe<NetTestStartMsg>(OnStart, false);
            nm.Subscribe<NetTestStartMsg>(OnStart, false);
            nm.Unsubscribe<NetTestAckMsg>(OnAck, true);
            nm.Subscribe<NetTestAckMsg>(OnAck, true);
        }

        private static void OnStart(PlayerID player, NetTestStartMsg msg, bool asServer)
        {
            if (asServer)
            {
                return;
            }

            if (msg.scenarioIndex > _latestStartedIndex)
            {
                _latestStartedIndex = msg.scenarioIndex;
                Log.Info($"[NetTest] 收到 Start({msg.scenarioIndex})。");
            }
        }

        private static void OnAck(PlayerID player, NetTestAckMsg msg, bool asServer)
        {
            if (!asServer)
            {
                return;
            }

            if (!_acksByIndex.TryGetValue(msg.scenarioIndex, out var set))
            {
                set = new HashSet<PlayerID>();
                _acksByIndex[msg.scenarioIndex] = set;
            }

            set.Add(player);
        }
    }
}
