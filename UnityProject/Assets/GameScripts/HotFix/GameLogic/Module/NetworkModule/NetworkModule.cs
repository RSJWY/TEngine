using PurrNet.Utils;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 网络模块。封装 PurrNet NetworkManager 生命周期。
    /// </summary>
    /// <remarks>
    /// <b>懒加载设计</b>：OnInit 不查找 NetworkManager，仅打初始化日志。
    /// 业务首次访问查询属性或操作方法时懒查找（<see cref="EnsureNetworkManager"/>），
    /// 查一次缓存到 <c>_networkManager</c>。非联机场景永不查找，零开销。
    /// <para>
    /// <b>预制体注入</b>：业务可动态实例化 NetworkManager 预制体后，
    /// 调 <see cref="BindNetworkManager"/> 注入实例，跳过懒查找。
    /// </para>
    /// <para>
    /// <b>自动启动机制</b>：PurrNet 在 NetworkManager.Start() 中根据 StartFlags 和
    /// <see cref="ApplicationContext"/> 自动判断是否启动服务器/客户端（早于热更模块注册）。
    /// 本模块不干预自动启动流程，<see cref="IsAutoStarted"/> 实时查询 NM 状态。
    /// </para>
    /// <para>
    /// <b>Shutdown 行为</b>：仅清引用，不调 StopNetwork。
    /// NetworkManager 的 OnDestroy 会自行断连（PurrNet 源码保证）。
    /// </para>
    /// </remarks>
    public sealed class NetworkModule : Module, INetworkModule
    {
        private PurrNet.NetworkManager _networkManager;

        /// <inheritdoc />
        public PurrNet.NetworkManager NetworkManager
        {
            get
            {
                EnsureNetworkManager();
                return _networkManager;
            }
        }

        /// <inheritdoc />
        public bool IsServer => _networkManager != null && _networkManager.isServer;

        /// <inheritdoc />
        public bool IsClient => _networkManager != null && _networkManager.isClient;

        /// <inheritdoc />
        public bool IsHost => _networkManager != null && _networkManager.isHost;

        /// <inheritdoc />
        public bool IsDedicatedServerBuild => ApplicationContext.isServerBuild;

        /// <inheritdoc />
        public bool IsAutoStarted => _networkManager != null
            && (_networkManager.isServer || _networkManager.isClient);

        /// <inheritdoc />
        public override void OnInit()
        {
            _networkManager = ResolveNetworkManager();

            if (_networkManager != null)
            {
                Log.Info($"[NetworkModule] 初始化时已找到 NetworkManager。IsServer={_networkManager.isServer}, IsClient={_networkManager.isClient}");
            }
            else
            {
                Log.Info("[NetworkModule] 初始化时未找到 NetworkManager，等待注入或首次访问时懒查找。");
            }
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            _networkManager = null;
        }

        /// <inheritdoc />
        public void BindNetworkManager(PurrNet.NetworkManager networkManager)
        {
            _networkManager = networkManager;

            if (_networkManager != null)
            {
                Log.Info($"[NetworkModule] NetworkManager 已注入。IsServer={_networkManager.isServer}, IsClient={_networkManager.isClient}");
            }
            else
            {
                Log.Warning("[NetworkModule] BindNetworkManager 传入 null，已清空绑定。");
            }
        }

        /// <inheritdoc />
        public void StartServer()
        {
            if (!EnsureNetworkManager())
                return;

            _networkManager.StartServer();
        }

        /// <inheritdoc />
        public void StartClient()
        {
            if (!EnsureNetworkManager())
                return;

            _networkManager.StartClient();
        }

        /// <inheritdoc />
        public void StartHost()
        {
            if (!EnsureNetworkManager())
                return;

            _networkManager.StartHost();
        }

        /// <inheritdoc />
        public void StopNetwork()
        {
            if (_networkManager == null)
                return;

            _networkManager.StopClient();
            _networkManager.StopServer();
        }

        private PurrNet.NetworkManager ResolveNetworkManager()
        {
            if (PurrNet.NetworkManager.main != null)
                return PurrNet.NetworkManager.main;

            return Object.FindFirstObjectByType<PurrNet.NetworkManager>();
        }

        private bool EnsureNetworkManager()
        {
            if (_networkManager != null)
                return true;

            _networkManager = ResolveNetworkManager();

            if (_networkManager == null)
            {
                Log.Error("[NetworkModule] 操作失败：未找到 NetworkManager。请先挂载或调 BindNetworkManager 注入。");
                return false;
            }

            return true;
        }
    }
}
