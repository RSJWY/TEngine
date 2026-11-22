using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

namespace GameLogic.NetworkTest
{
    /// <summary>验证 SyncVar 初始状态、服务器变更、观察者收敛与 onChanged 回调。</summary>
    public sealed class SyncVarAttributeScenario : NetTestScenario
    {
        private const int BarrierSpawned = 300;
        private const int BarrierInitial = 301;
        private const int BarrierFinal = 302;
        private const float StateTimeoutSeconds = 30f;

        private SyncVarAttributeIdentity _prefab;

        public override string Name => "SyncVarAttribute（初始值/增量/onChanged）";

        public override void Setup(NetTestContext ctx, NetworkManager manager)
        {
            var go = new GameObject("NetTest_SyncVarAttribute");
            _prefab = go.AddComponent<SyncVarAttributeIdentity>();
            go.SetActive(false);
            SyncVarAttributeIdentity.ResetAll();
            manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
        }

        public override async UniTask<NetTestResult> RunScenario(NetTestContext ctx)
        {
            if (ctx.isServer)
            {
                Object.Instantiate(_prefab);
            }

            try
            {
                await NetTestWait.WaitWithTimeout(
                    () => SyncVarAttributeIdentity.LocalInstance != null,
                    StateTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (System.TimeoutException)
            {
                return NetTestResult.Fail("SyncVar 测试对象生成超时（30s）");
            }

            await NetTestBarrier.Wait(ctx, BarrierSpawned);
            var instance = SyncVarAttributeIdentity.LocalInstance;

            if (ctx.isClient)
            {
                try
                {
                    await NetTestWait.WaitWithTimeout(instance.MatchesInitial, StateTimeoutSeconds, ctx.cancellationToken);
                }
                catch (System.TimeoutException)
                {
                    return NetTestResult.Fail($"SyncVar 初始值={instance.Value}，期望 {SyncVarAttributeIdentity.InitialValue}");
                }

                if (ctx.role == NetTestRole.Client && instance.IsController(false))
                {
                    return NetTestResult.Fail("纯 Client 不应控制 server-auth SyncVar");
                }
            }

            await NetTestBarrier.Wait(ctx, BarrierInitial);

            if (ctx.isServer)
            {
                instance.RunServerOps();
                if (!instance.MatchesFinal())
                {
                    return NetTestResult.Fail($"服务器本地 SyncVar/onChanged 未收口：Value={instance.Value}, LastChanged={instance.LastChangedValue}, ChangeCount={instance.ChangeCount}");
                }
            }

            if (ctx.isClient)
            {
                try
                {
                    await NetTestWait.WaitWithTimeout(instance.MatchesFinal, StateTimeoutSeconds, ctx.cancellationToken);
                }
                catch (System.TimeoutException)
                {
                    return NetTestResult.Fail($"客户端 SyncVar/onChanged 未收敛：Value={instance.Value}, LastChanged={instance.LastChangedValue}, ChangeCount={instance.ChangeCount}");
                }
            }

            await NetTestBarrier.Wait(ctx, BarrierFinal);
            return NetTestResult.Ok($"Value={instance.Value}, LastChanged={instance.LastChangedValue}, ChangeCount={instance.ChangeCount}");
        }
    }
}
