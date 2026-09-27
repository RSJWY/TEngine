namespace GameLogic
{
    /// <summary>
    /// 网络模块接口。封装 PurrNet NetworkManager 生命周期。
    /// </summary>
    public interface INetworkModule
    {
        /// <summary>
        /// PurrNet NetworkManager 实例（场景中挂载的）。
        /// <para>业务可通过此属性直接访问 NM——挂 onPlayerJoined 事件、调 SpawnYooAsset、读 localPlayer 等。</para>
        /// </summary>
        PurrNet.NetworkManager NetworkManager { get; }

        /// <summary>当前是否为服务器角色。</summary>
        bool IsServer { get; }

        /// <summary>当前是否为客户端角色。</summary>
        bool IsClient { get; }

        /// <summary>当前是否为 Host 模式（server+client 同机）。</summary>
        bool IsHost { get; }

        /// <summary>
        /// 是否为专用服务器构建（UNITY_SERVER define 或 batchmode）。
        /// <para>编译期判断，走 <c>PurrNet.Utils.ApplicationContext.isServerBuild</c>。</para>
        /// </summary>
        bool IsDedicatedServerBuild { get; }

        /// <summary>
        /// PurrNet AutoStart 是否已触发。
        /// <para>DS/客户端构建时 PurrNet 在 Start() 自动启动，早于热更模块注册。
        /// 业务据此判断"网络已经是活的"还是"需要手动 Start"。</para>
        /// </summary>
        bool IsAutoStarted { get; }

        /// <summary>
        /// 注入 NetworkManager 实例（跳过懒查找）。
        /// <para>业务动态实例化 NetworkManager 预制体后调用此方法注入。
        /// 传 null 清空绑定。</para>
        /// </summary>
        /// <param name="networkManager">NetworkManager 实例。</param>
        void BindNetworkManager(PurrNet.NetworkManager networkManager);

        /// <summary>启动服务器。</summary>
        void StartServer();

        /// <summary>启动客户端。地址配置在 transport Inspector 上。</summary>
        void StartClient();

        /// <summary>启动 Host（同机 server+client）。</summary>
        void StartHost();

        /// <summary>停止网络（断开连接/关闭服务器）。</summary>
        void StopNetwork();
    }
}
