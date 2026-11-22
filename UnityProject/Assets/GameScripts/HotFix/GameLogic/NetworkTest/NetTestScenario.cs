using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using TEngine;

namespace GameLogic.NetworkTest
{
    /// <summary>
    /// 联机测试 Scenario 基类（移植自 PurrNet 官方 PlayModeTests 的 Scenario 体系）。
    /// <para>每个 Scenario 描述一段双端联机断言；<see cref="RunSplit"/> 按角色把测试逻辑分成
    /// client/server 两段，Host 上并发执行（client 段先启动以便先注册回调）。</para>
    /// </summary>
    public abstract class NetTestScenario
    {
        /// <summary>场景显示名（日志/汇总用）。</summary>
        public abstract string Name { get; }

        /// <summary>网络启动后、开跑前调用（注册监听等）。</summary>
        public virtual void Setup(NetTestContext ctx, NetworkManager manager)
        {
        }

        /// <summary>执行本 Scenario，返回结构化结果。</summary>
        public abstract UniTask<NetTestResult> RunScenario(NetTestContext ctx);

        /// <summary>
        /// 按角色分流 client/server 两段逻辑。Host 上两段并发跑在同一线程，
        /// client 段先调用以便先注册回调/观察器，再开始 server 段动作。
        /// </summary>
        protected static async UniTask<NetTestResult> RunSplit(
            NetTestContext ctx,
            Func<NetTestContext, UniTask<NetTestResult>> client,
            Func<NetTestContext, UniTask<NetTestResult>> server)
        {
            if (ctx.role == NetTestRole.Host)
            {
                UniTask<NetTestResult> clientTask = client(ctx);
                UniTask<NetTestResult> serverTask = server(ctx);
                var (cr, sr) = await UniTask.WhenAll(clientTask, serverTask);

                if (cr.success && sr.success)
                {
                    return NetTestResult.Ok();
                }

                if (!cr.success && !sr.success)
                {
                    return NetTestResult.Fail($"client: {cr.message} | server: {sr.message}");
                }

                return !cr.success
                    ? NetTestResult.Fail($"client: {cr.message}")
                    : NetTestResult.Fail($"server: {sr.message}");
            }

            if (ctx.isServer)
            {
                return await server(ctx);
            }

            return await client(ctx);
        }
    }
}
