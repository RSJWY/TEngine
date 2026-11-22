using Cysharp.Threading.Tasks;
using PurrNet;
using TEngine;
using UnityEngine.SceneManagement;

namespace GameLogic.NetworkTest
{
    /// <summary>
    /// C-2 断言版：Host/Server 权威切场景 ↔ 客户端被动跟随，双向各一轮。
    /// <para>服务器段：LoadSceneServer → 等服务器侧收口 → 断言 Unity 活动场景 → barrier 对齐；
    /// 客户端段：等 OnSceneReady(目标) → 断言 CurrentSceneType 与 Unity 活动场景 → barrier 对齐。
    /// barrierId：100=MainScene→network_test，101=network_test→MainScene。</para>
    /// </summary>
    public sealed class HostClientSyncScenario : NetTestScenario
    {
        private const int BarrierForward = 100;
        private const int BarrierBackward = 101;

        private SceneType? _readyScene;

        public override string Name => "HostClientSync（双向场景同步）";

        public override void Setup(NetTestContext ctx, NetworkManager manager)
        {
            // 客户端（含 Host 的客户端半边）监听 OnSceneReady 作为被动加载完成信号
            GameEvent.AddEventListener<SceneType>(IGameSceneEvent_Event.OnSceneReady, OnSceneReady);
        }

        public override async UniTask<NetTestResult> RunScenario(NetTestContext ctx)
        {
            try
            {
                NetTestResult forward = await RunSplit(ctx,
                    ClientRound(SceneType.network_test, BarrierForward, "MainScene→network_test"),
                    ServerRound(SceneType.network_test, BarrierForward, "MainScene→network_test"));
                if (!forward.success)
                {
                    return forward;
                }

                NetTestResult backward = await RunSplit(ctx,
                    ClientRound(SceneType.MainScene, BarrierBackward, "network_test→MainScene"),
                    ServerRound(SceneType.MainScene, BarrierBackward, "network_test→MainScene"));
                return backward.success
                    ? NetTestResult.Ok("双向切换均断言通过")
                    : backward;
            }
            finally
            {
                GameEvent.RemoveEventListener<SceneType>(IGameSceneEvent_Event.OnSceneReady, OnSceneReady);
            }
        }

        // RunSplit 的签名是 (ctx, client, server)，这里用参数化包装固定目标场景
        private System.Func<NetTestContext, UniTask<NetTestResult>> ClientRound(SceneType target, int barrierId, string label)
        {
            return async ctx =>
            {
                Log.Info($"[NetTest] [{label}] 客户端段：等待 OnSceneReady({target})。");
                _readyScene = null;

                try
                {
                    await NetTestWait.WaitWithTimeout(() => _readyScene.HasValue && _readyScene.Value == target, 90f, ctx.cancellationToken);
                }
                catch (System.TimeoutException)
                {
                    return NetTestResult.Fail($"[{label}] 客户端等待 OnSceneReady({target}) 超时（90s）");
                }

                var current = GameModule.GameScene.CurrentSceneType;
                if (!current.HasValue || current.Value != target)
                {
                    return NetTestResult.Fail($"[{label}] 客户端 CurrentSceneType={current}，期望 {target}");
                }

                NetTestResult activeSceneResult = ValidateActiveScene(target, label, "客户端");
                if (!activeSceneResult.success)
                {
                    return activeSceneResult;
                }

                Log.Info($"[NetTest] [{label}] 客户端断言通过：OnSceneReady + CurrentSceneType={target} + ActiveScene={SceneManager.GetActiveScene().name}。");
                await NetTestBarrier.Wait(ctx, barrierId);
                return NetTestResult.Ok();
            };
        }

        private System.Func<NetTestContext, UniTask<NetTestResult>> ServerRound(SceneType target, int barrierId, string label)
        {
            return async ctx =>
            {
                var coord = GameModule.Network.SceneCoordinator;
                if (coord == null)
                {
                    return NetTestResult.Fail($"[{label}] SceneCoordinator 为空（NM 未就绪）");
                }

                Log.Info($"[NetTest] [{label}] 服务器段：LoadSceneServer({target})。");
                if (!coord.LoadSceneServer(target))
                {
                    return NetTestResult.Fail($"[{label}] LoadSceneServer({target}) 被拒绝（IsTransitioning 或权限）");
                }

                try
                {
                    await NetTestWait.WaitWithTimeout(
                        () => GameModule.GameScene.CurrentSceneType.HasValue
                             && GameModule.GameScene.CurrentSceneType.Value == target
                             && !coord.IsTransitioning,
                        90f,
                        ctx.cancellationToken);
                }
                catch (System.TimeoutException)
                {
                    return NetTestResult.Fail($"[{label}] 服务器等待加载收口超时（90s）：Current={GameModule.GameScene.CurrentSceneType}, Transitioning={coord.IsTransitioning}");
                }

                NetTestResult activeSceneResult = ValidateActiveScene(target, label, "服务器");
                if (!activeSceneResult.success)
                {
                    return activeSceneResult;
                }

                Log.Info($"[NetTest] [{label}] 服务器断言通过：CurrentSceneType={target} + ActiveScene={SceneManager.GetActiveScene().name} 且已收口。");
                await NetTestBarrier.Wait(ctx, barrierId);
                return NetTestResult.Ok();
            };
        }

        private static NetTestResult ValidateActiveScene(SceneType target, string label, string segment)
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return NetTestResult.Fail($"[{label}] {segment} Unity 活动场景无效或尚未加载：Valid={activeScene.IsValid()}, Loaded={activeScene.isLoaded}");
            }

            string expectedName = GameModule.GameScene.GetSceneName(target);
            if (activeScene.name != expectedName)
            {
                return NetTestResult.Fail($"[{label}] {segment} Unity ActiveScene={activeScene.name}，期望 {expectedName}（SceneType={target}）");
            }

            return NetTestResult.Ok();
        }

        private void OnSceneReady(SceneType sceneType)
        {
            _readyScene = sceneType;
        }
    }
}
