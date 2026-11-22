using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using TEngine;
using UnityEngine;

namespace GameLogic.NetworkTest
{
    /// <summary>
    /// 跨端 barrier（移植自 PurrNet 官方 ScenarioBarrier）：全员到齐才放行。
    /// <para>客户端到达时向服务器报到（<see cref="NetTestBarrierMsg"/>）；服务器等到
    /// <paramref name="NetTestContext.expectedConnections"/> 个到达后向全员广播放行
    /// （<see cref="NetTestProceedMsg"/>）。Host 上 SendToServer 走本地回路，自己会计入到达数。</para>
    /// <para>barrierId 为 Scenario 内常量，需在一次运行内全局唯一；同一 id 在一次运行内只用一次。</para>
    /// </summary>
    public static class NetTestBarrier
    {
        private static readonly Dictionary<int, int> _arrivedByBarrier = new();
        private static readonly Dictionary<int, Task> _waitsByBarrier = new();
        private static readonly HashSet<int> _proceeded = new();
        private static bool _subscribed;

        // 静态状态在禁域重载下跨 Play 会话存活，必须重置，否则下一次运行的客户端会看到旧放行记录。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRunState()
        {
            _arrivedByBarrier.Clear();
            _waitsByBarrier.Clear();
            _proceeded.Clear();
            _subscribed = false;
        }

        /// <summary>barrier 等待：客户端报到后等放行；服务器等到齐后放行。Host 两半共享同一次等待。</summary>
        public static async UniTask Wait(NetTestContext ctx, int barrierId, float timeoutSeconds = 60f)
        {
            EnsureSubscribed(ctx);

            if (_waitsByBarrier.TryGetValue(barrierId, out var existing))
            {
                await existing;
                return;
            }

            var task = WaitImpl(ctx, barrierId, timeoutSeconds).AsTask();
            _waitsByBarrier[barrierId] = task;
            await task;
        }

        private static async UniTask WaitImpl(NetTestContext ctx, int barrierId, float timeoutSeconds)
        {
            if (ctx.isClient)
            {
                ctx.networkManager.SendToServer(new NetTestBarrierMsg { barrierId = barrierId });
            }

            if (ctx.isServer)
            {
                try
                {
                    await NetTestWait.WaitWithTimeout(
                        () => _arrivedByBarrier.TryGetValue(barrierId, out int c) && c >= ctx.expectedConnections,
                        timeoutSeconds,
                        ctx.cancellationToken);
                }
                catch (System.TimeoutException)
                {
                    _arrivedByBarrier.TryGetValue(barrierId, out int arrived);
                    Log.Error($"[NetTest] barrier={barrierId} 等待超时：arrived={arrived}/{ctx.expectedConnections} role={ctx.role}");
                    throw;
                }
                finally
                {
                    _arrivedByBarrier.Remove(barrierId);
                    _proceeded.Add(barrierId);
                    ctx.networkManager.SendToAll(new NetTestProceedMsg { barrierId = barrierId });
                }
            }

            if (ctx.isClient)
            {
                await NetTestWait.WaitWithTimeout(
                    () => _proceeded.Contains(barrierId),
                    timeoutSeconds,
                    ctx.cancellationToken);
            }
        }

        private static void EnsureSubscribed(NetTestContext ctx)
        {
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            var nm = ctx.networkManager;

            // 幂等订阅：先退订再订阅，防止重复挂接
            nm.Unsubscribe<NetTestBarrierMsg>(OnBarrierArrived, true);
            nm.Subscribe<NetTestBarrierMsg>(OnBarrierArrived, true);
            nm.Unsubscribe<NetTestProceedMsg>(OnProceed, false);
            nm.Subscribe<NetTestProceedMsg>(OnProceed, false);
        }

        private static void OnBarrierArrived(PlayerID player, NetTestBarrierMsg msg, bool asServer)
        {
            if (!asServer)
            {
                return;
            }

            _arrivedByBarrier.TryGetValue(msg.barrierId, out int c);
            _arrivedByBarrier[msg.barrierId] = c + 1;
            Log.Info($"[NetTest] barrier={msg.barrierId} 到达 {c + 1}（来自 {player}）");
        }

        private static void OnProceed(PlayerID player, NetTestProceedMsg msg, bool asServer)
        {
            if (asServer)
            {
                return;
            }

            _proceeded.Add(msg.barrierId);
        }
    }
}
