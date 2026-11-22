using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using TEngine;

namespace GameLogic.NetworkTest
{
    /// <summary>验证 ServerRpc / ObserversRpc / TargetRpc 的调用方向、参数、发送者与定向隔离。</summary>
    public sealed class RpcAttributeScenario : NetTestScenario
    {
        private const int BarrierServerRpc = 200;
        private const int BarrierObserversRpc = 201;
        private const int BarrierTargetRpc = 202;
        private const int ServerEchoValue = 101;
        private const int ObserversValue = 202;
        private const int TargetValue = 303;

        private static readonly Dictionary<ulong, int> _serverEchoBySender = new();
        private static int? _observersReceived;
        private static ulong? _targetPlayerId;
        private static int? _targetReceived;

        public override string Name => "RpcAttribute（ServerRpc/ObserversRpc/TargetRpc）";

        public override void Setup(NetTestContext ctx, NetworkManager manager)
        {
            EnsureGeneratedRegistration();
            _serverEchoBySender.Clear();
            _observersReceived = null;
            _targetPlayerId = null;
            _targetReceived = null;
        }

        public override async UniTask<NetTestResult> RunScenario(NetTestContext ctx)
        {
            return await RunSplit(ctx, RunClient, RunServer);
        }

        private static async UniTask<NetTestResult> RunClient(NetTestContext ctx)
        {
            int echo = await ServerEcho(ServerEchoValue);
            if (echo != ServerEchoValue)
            {
                return NetTestResult.Fail($"ServerRpc 回包={echo}，期望 {ServerEchoValue}");
            }

            await NetTestBarrier.Wait(ctx, BarrierServerRpc);

            try
            {
                await NetTestWait.WaitWithTimeout(() => _observersReceived.HasValue, 30f, ctx.cancellationToken);
            }
            catch (System.TimeoutException)
            {
                return NetTestResult.Fail("ObserversRpc 等待超时（30s）");
            }

            if (_observersReceived != ObserversValue)
            {
                return NetTestResult.Fail($"ObserversRpc 参数={_observersReceived}，期望 {ObserversValue}");
            }

            await NetTestBarrier.Wait(ctx, BarrierObserversRpc);

            try
            {
                await NetTestWait.WaitWithTimeout(() => _targetPlayerId.HasValue, 30f, ctx.cancellationToken);
            }
            catch (System.TimeoutException)
            {
                return NetTestResult.Fail("TargetRpc 目标 ID 广播等待超时（30s）");
            }

            bool isTarget = ctx.networkManager.localPlayer.id.value == _targetPlayerId.Value;
            if (isTarget)
            {
                try
                {
                    await NetTestWait.WaitWithTimeout(() => _targetReceived.HasValue, 30f, ctx.cancellationToken);
                }
                catch (System.TimeoutException)
                {
                    return NetTestResult.Fail("目标 Client 等待 TargetRpc 超时（30s）");
                }
            }

            await NetTestBarrier.Wait(ctx, BarrierTargetRpc);

            if (isTarget && _targetReceived != TargetValue)
            {
                return NetTestResult.Fail($"TargetRpc 参数={_targetReceived}，期望 {TargetValue}");
            }

            if (!isTarget && _targetReceived.HasValue)
            {
                return NetTestResult.Fail($"非目标 Client 错收 TargetRpc：{_targetReceived}");
            }

            return NetTestResult.Ok($"ServerEcho={echo}, Observers={_observersReceived}, isTarget={isTarget}");
        }

        private static async UniTask<NetTestResult> RunServer(NetTestContext ctx)
        {
            await NetTestBarrier.Wait(ctx, BarrierServerRpc);

            if (_serverEchoBySender.Count != ctx.expectedConnections)
            {
                return NetTestResult.Fail($"ServerRpc 不同发送者数量={_serverEchoBySender.Count}，期望 {ctx.expectedConnections}");
            }

            foreach (var pair in _serverEchoBySender)
            {
                if (pair.Value != ServerEchoValue)
                {
                    return NetTestResult.Fail($"ServerRpc 发送者 {pair.Key} 的参数={pair.Value}，期望 {ServerEchoValue}");
                }
            }

            BroadcastObservers(ObserversValue);
            await NetTestBarrier.Wait(ctx, BarrierObserversRpc);

            PlayerID? target = FindExternalClient(ctx);
            if (!target.HasValue)
            {
                return NetTestResult.Fail("TargetRpc 找不到外部 Client");
            }

            BroadcastTargetPlayer(target.Value.id.value);
            int targetReply = await SendTarget(target.Value, TargetValue);
            if (targetReply != TargetValue)
            {
                return NetTestResult.Fail($"TargetRpc 回包={targetReply}，期望 {TargetValue}");
            }

            await NetTestBarrier.Wait(ctx, BarrierTargetRpc);
            return NetTestResult.Ok($"ServerRpcSenders={_serverEchoBySender.Count}, Target={target.Value}");
        }

        private static PlayerID? FindExternalClient(NetTestContext ctx)
        {
            var players = ctx.networkManager.players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerID player = players[i];
                if (player.isServer)
                {
                    continue;
                }

                if (ctx.role == NetTestRole.Host && player == ctx.networkManager.localPlayer)
                {
                    continue;
                }

                return player;
            }

            return null;
        }

        private static void EnsureGeneratedRegistration()
        {
            const string serializer = "GameLogic.NetworkTest.PurrNet.CodeGen.Serializers.GameLogic_NetworkTest_RpcAttributeScenario_Serializer";
            const string manifest = "GameLogic.NetworkTest.PurrNet.CodeGen.Manifest.GameLogic_NetworkTest_RpcAttributeScenario_RPCManifest";
            InvokeGeneratedRegister(serializer);
            InvokeGeneratedRegister(manifest);

            int manifestCount = 0;
            for (int i = 0; i < 4; i++)
            {
                if (Manifest.TryGet(typeof(RpcAttributeScenario), i, out _))
                {
                    manifestCount++;
                }
            }

            bool hasherRegistered = PurrNet.Utils.Hasher.IsRegistered(typeof(RpcAttributeScenario));
            Log.Info($"[NetTest] RPC 注册自检：Hasher={hasherRegistered}, Manifest={manifestCount}/4");
            if (!hasherRegistered || manifestCount != 4)
            {
                throw new System.InvalidOperationException($"[NetTest] RPC 显式注册失败：Hasher={hasherRegistered}, Manifest={manifestCount}/4");
            }
        }

        private static void InvokeGeneratedRegister(string typeName)
        {
            var type = typeof(RpcAttributeScenario).Assembly.GetType(typeName);
            var method = type?.GetMethod("Register",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            if (method == null)
            {
                throw new System.InvalidOperationException($"[NetTest] 找不到生成的注册方法：{typeName}.Register");
            }

            method.Invoke(null, null);
        }

        [ServerRpc(requireOwnership: false)]
        private static Task<int> ServerEcho(int value, RPCInfo info = default)
        {
            _serverEchoBySender[info.sender.id.value] = value;
            return Task.FromResult(value);
        }

        [ObserversRpc(runLocally: true)]
        private static void BroadcastObservers(int value)
        {
            _observersReceived = value;
        }

        [ObserversRpc(runLocally: true)]
        private static void BroadcastTargetPlayer(ulong targetPlayerId)
        {
            _targetPlayerId = targetPlayerId;
        }

        [TargetRpc]
        private static Task<int> SendTarget(PlayerID target, int value)
        {
            _targetReceived = value;
            return Task.FromResult(value);
        }
    }
}
