using System.Threading;
using Cysharp.Threading.Tasks;
using PurrNet;
using TEngine;

namespace GameLogic.NetworkTest
{
    /// <summary>联机测试端角色：纯服务器（DS 网络形态）/ 纯客户端 / 主机（服务器+客户端同进程）。</summary>
    public enum NetTestRole
    {
        Server,
        Client,
        Host,
    }

    /// <summary>单个测试 Scenario 的运行上下文：角色、期望连接数、NM、取消令牌。</summary>
    public struct NetTestContext
    {
        public NetTestRole role;
        public int expectedConnections;
        public NetworkManager networkManager;
        public CancellationToken cancellationToken;

        public bool isServer => role == NetTestRole.Server || role == NetTestRole.Host;
        public bool isClient => role == NetTestRole.Client || role == NetTestRole.Host;
    }

    /// <summary>Scenario 结构化结果：success + 说明。Runner 汇总输出。</summary>
    public struct NetTestResult
    {
        public bool success;
        public string message;

        public static NetTestResult Ok()
        {
            return new NetTestResult { success = true };
        }

        public static NetTestResult Ok(string message)
        {
            return new NetTestResult { success = true, message = message };
        }

        public static NetTestResult Fail(string message)
        {
            return new NetTestResult { success = false, message = message };
        }
    }

    // ===== 跨端握手消息 =====
    // 注意：PurrNet ILPP 只织入部分 Subscribe/Send 调用点的消息类型（见 NetworkSceneCoordinator
    // 静态构造注释），本程序集的自定义消息必须在首次使用前手动 Hasher.PrepareType 注册，
    // 否则联机启动即抛 "Type is not registered"。注册统一在 NetTestMessages.EnsureRegistered()。

    /// <summary>服务器 → 全员：第 N 个 Scenario 开始（客户端据此启动对应测试逻辑）。</summary>
    public struct NetTestStartMsg
    {
        public int scenarioIndex;
    }

    /// <summary>客户端 → 服务器：第 N 个 Scenario 本端完成（服务器聚合并等待全员）。</summary>
    public struct NetTestAckMsg
    {
        public int scenarioIndex;
    }

    /// <summary>客户端 → 服务器：barrier 到达报到（服务器等到齐后统一放行）。</summary>
    public struct NetTestBarrierMsg
    {
        public int barrierId;
    }

    /// <summary>服务器 → 全员：barrier 放行。</summary>
    public struct NetTestProceedMsg
    {
        public int barrierId;
    }

    /// <summary>
    /// 跨端握手消息注册（幂等，重复调用安全）。
    /// <para><b>必须同时注册 Hasher 与 Packer</b>：ILPP 只织入 PlayersManager/BroadcastModule/
    /// PlayersBroadcaster 三类调用点的消息类型；本程序集经 NetworkManager.Subscribe/Send 转发层的
    /// 消息没有编译期生成的序列化器，运行时会走 Packer 的 Fallback 读写——而 Fallback 的写端
    /// （PackUIntegers 变长）与读端（NativePacker 定长）格式不匹配，字段会被静默读成 0
    /// （2026-11-22 NetTest 联调实证）。手动注册后走精确字段读写。</para>
    /// </summary>
    public static class NetTestMessages
    {
        private static bool _registered;

        public static void EnsureRegistered()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;

            PurrNet.Utils.Hasher.PrepareType<NetTestStartMsg>();
            PurrNet.Utils.Hasher.PrepareType<NetTestAckMsg>();
            PurrNet.Utils.Hasher.PrepareType<NetTestBarrierMsg>();
            PurrNet.Utils.Hasher.PrepareType<NetTestProceedMsg>();

            // 注意：ReadFunc<T> 带 ref 参数，lambda 不支持 ref 形参，必须用显式方法组。
            PurrNet.Packing.Packer<NetTestStartMsg>.RegisterWriter(WriteStartMsg);
            PurrNet.Packing.Packer<NetTestStartMsg>.RegisterReader(ReadStartMsg);
            PurrNet.Packing.Packer<NetTestAckMsg>.RegisterWriter(WriteAckMsg);
            PurrNet.Packing.Packer<NetTestAckMsg>.RegisterReader(ReadAckMsg);
            PurrNet.Packing.Packer<NetTestBarrierMsg>.RegisterWriter(WriteBarrierMsg);
            PurrNet.Packing.Packer<NetTestBarrierMsg>.RegisterReader(ReadBarrierMsg);
            PurrNet.Packing.Packer<NetTestProceedMsg>.RegisterWriter(WriteProceedMsg);
            PurrNet.Packing.Packer<NetTestProceedMsg>.RegisterReader(ReadProceedMsg);

            Log.Info("[NetTest] 跨端握手消息已注册 Hasher + Packer。");
        }

        private static void WriteStartMsg(PurrNet.Packing.BitPacker packer, NetTestStartMsg v) => PurrNet.Packing.Packer<int>.Write(packer, v.scenarioIndex);
        private static void ReadStartMsg(PurrNet.Packing.BitPacker packer, ref NetTestStartMsg v)
        {
            int i = 0;
            PurrNet.Packing.Packer<int>.Read(packer, ref i);
            v.scenarioIndex = i;
        }

        private static void WriteAckMsg(PurrNet.Packing.BitPacker packer, NetTestAckMsg v) => PurrNet.Packing.Packer<int>.Write(packer, v.scenarioIndex);
        private static void ReadAckMsg(PurrNet.Packing.BitPacker packer, ref NetTestAckMsg v)
        {
            int i = 0;
            PurrNet.Packing.Packer<int>.Read(packer, ref i);
            v.scenarioIndex = i;
        }

        private static void WriteBarrierMsg(PurrNet.Packing.BitPacker packer, NetTestBarrierMsg v) => PurrNet.Packing.Packer<int>.Write(packer, v.barrierId);
        private static void ReadBarrierMsg(PurrNet.Packing.BitPacker packer, ref NetTestBarrierMsg v)
        {
            int i = 0;
            PurrNet.Packing.Packer<int>.Read(packer, ref i);
            v.barrierId = i;
        }

        private static void WriteProceedMsg(PurrNet.Packing.BitPacker packer, NetTestProceedMsg v) => PurrNet.Packing.Packer<int>.Write(packer, v.barrierId);
        private static void ReadProceedMsg(PurrNet.Packing.BitPacker packer, ref NetTestProceedMsg v)
        {
            int i = 0;
            PurrNet.Packing.Packer<int>.Read(packer, ref i);
            v.barrierId = i;
        }
    }

    /// <summary>UniTask 等待工具：条件轮询 + 超时 + 取消。</summary>
    public static class NetTestWait
    {
        public static async UniTask WaitWithTimeout(System.Func<bool> untilTrue, double timeoutSeconds, CancellationToken ct)
        {
            double start = UnityEngine.Time.realtimeSinceStartupAsDouble;
            while (!untilTrue())
            {
                await UniTask.NextFrame();
                if (UnityEngine.Time.realtimeSinceStartupAsDouble - start > timeoutSeconds)
                {
                    throw new System.TimeoutException();
                }

                ct.ThrowIfCancellationRequested();
            }
        }
    }
}
