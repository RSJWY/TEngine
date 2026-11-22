using PurrNet;
using PurrNet.Modules;
using TEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace GameLogic
{
    /// <summary>场景切换请求消息（客户端 → 服务器）。struct 天然满足 PurrNet 的 <c>T : new()</c> 约束。</summary>
    public struct SceneChangeRequest
    {
        /// <summary>SceneType 枚举整型传输；服务器端用强制转换还原。</summary>
        public int sceneTypeValue;
    }

    /// <summary>
    /// 场景加载协调器：服务器权威发起（PurrNet ScenesModule），客户端被动展示加载页（GameSceneModule 被动模式）。
    /// <para>非 Module：由 <see cref="NetworkModule"/> 持有生命周期，不参与 ModuleSystem 注册。</para>
    /// </summary>
    public sealed class NetworkSceneCoordinator
    {
        /// <summary>YooAsset 包名：与 AssetReference 默认包一致；传空串会被 PurrNet 直接拒绝。</summary>
        private const string PackageName = "DefaultPackage";

        static NetworkSceneCoordinator()
        {
            // PurrNet ILPP 只织入 PlayersManager/BroadcastModule/PlayersBroadcaster 三类 Subscribe 调用点的
            // 消息类型；本协调器走 NetworkManager.Subscribe/Send 转发层，不在织入名单内，
            // 自定义网络消息必须在首次使用前手动注册 Hasher，否则联机启动即抛
            // "Type is not registered"（2026-11-22 S6 联调发现）。
            PurrNet.Utils.Hasher.PrepareType<SceneChangeRequest>();

            // 仅注册 Hasher 不够：没有 ILPP 生成的序列化器时，Packer 走 Fallback 读写，
            // 其写端（PackUIntegers 变长）与读端（NativePacker 定长）格式不匹配，
            // 字段会被静默读成 0（2026-11-22 NetTest 联调实证）。手动注册精确字段读写。
            // 注意：ReadFunc<T> 带 ref 参数，lambda 不支持 ref 形参，必须用显式方法组。
            PurrNet.Packing.Packer<SceneChangeRequest>.RegisterWriter(WriteSceneChangeRequest);
            PurrNet.Packing.Packer<SceneChangeRequest>.RegisterReader(ReadSceneChangeRequest);
        }

        private static void WriteSceneChangeRequest(PurrNet.Packing.BitPacker packer, SceneChangeRequest v)
        {
            PurrNet.Packing.Packer<int>.Write(packer, v.sceneTypeValue);
        }

        private static void ReadSceneChangeRequest(PurrNet.Packing.BitPacker packer, ref SceneChangeRequest v)
        {
            int value = 0;
            PurrNet.Packing.Packer<int>.Read(packer, ref value);
            v.sceneTypeValue = value;
        }

        private PurrNet.NetworkManager _nm;
        private ScenesModule _serverScenes;
        private ScenesModule _clientScenes;
        private SceneType _pendingSceneType; // 服务器当前正在加载的目标场景（供完成事件派发）

        /// <summary>是否正在协调一次场景切换（服务器侧，从 LoadSceneServer 到 loaded/failed）。</summary>
        public bool IsTransitioning { get; private set; }

        /// <summary>
        /// 服务器当前已进入的游戏场景；null 表示仍在待机（未下发过目标场）。
        /// <para>业务可据此判断“已开局”而不重复发令。</para>
        /// </summary>
        public SceneType? ActiveServerScene { get; private set; }

        // ---------- 生命周期 ----------

        /// <summary>绑定 NetworkManager：订阅网络事件与场景事件，同步本地加载许可。</summary>
        /// <param name="nm">当前 NetworkManager 实例。</param>
        public void Bind(PurrNet.NetworkManager nm)
        {
            Unbind();
            _nm = nm;
            if (_nm == null)
            {
                Log.Error("[NetworkScene] Bind 失败：NetworkManager 为空。场景协调不可用。");
                return;
            }

            Log.Info($"[NetworkScene] Bind：isServer={_nm.isServer}, isClient={_nm.isClient}, isHost={_nm.isHost}");

            _nm.onNetworkStarted += OnNetworkStarted;
            _nm.onNetworkShutdown += OnNetworkShutdown;
            // 服务器订阅客户端的切场景请求（幂等：先退订再订阅）
            _nm.Unsubscribe<SceneChangeRequest>(OnSceneChangeRequest, true);
            _nm.Subscribe<SceneChangeRequest>(OnSceneChangeRequest, true);

            if (_nm.isServer || _nm.isClient)
            {
                // PurrNet 自动启动（StartFlags/ApplicationContext）早于热更模块注册，此时网络可能已就绪
                SubscribeSceneEvents();
            }
            else
            {
                Log.Info("[NetworkScene] 网络未启动，挂 onNetworkStarted 等待启动后补订阅。");
            }

            SyncLocalLoadAllowed();
        }

        /// <summary>解绑：退订全部事件、复位切换标志、恢复本地加载许可。</summary>
        public void Unbind()
        {
            if (_nm != null)
            {
                _nm.onNetworkStarted -= OnNetworkStarted;
                _nm.onNetworkShutdown -= OnNetworkShutdown;
                _nm.Unsubscribe<SceneChangeRequest>(OnSceneChangeRequest, true);
                UnsubscribeSceneEvents();
            }

            _nm = null;
            _serverScenes = null;
            _clientScenes = null;
            IsTransitioning = false;
            ActiveServerScene = null;
            GameModule.GameScene.SetLocalLoadAllowed(true); // 退出联机恢复单机流程
            Log.Info("[NetworkScene] Unbind：已退订全部事件并复位本地加载许可。");
        }

        private void OnNetworkStarted(PurrNet.NetworkManager nm, bool asServer)
        {
            Log.Info($"[NetworkScene] onNetworkStarted(asServer={asServer})：补订阅场景事件。");
            // 注意：本回调触发时 NM 的 isServer/isHost 标志可能尚未置位（2026-11-22 S6 联调发现），
            // 不能用它们判断角色；必须用回调自带的 asServer 参数路由：
            //   asServer=true  → 只补订阅服务器模块（Host 的 client 侧稍后由 asServer=false 回调覆盖）
            //   asServer=false → 纯客户端/Host 的 client 侧，补订阅 client 模块
            if (asServer)
            {
                SubscribeServerSceneEvents();
            }
            else
            {
                SubscribeClientSceneEvents();
            }

            SyncLocalLoadAllowed(); // 启动前后角色可能变化，重新同步
        }

        private void OnNetworkShutdown(PurrNet.NetworkManager nm, bool asServer)
        {
            // Host 下 server/client 先后停止：只在两端都停了才整体解绑，避免半路拆掉另一侧订阅
            if (_nm != null && (_nm.isServer || _nm.isClient))
            {
                Log.Info($"[NetworkScene] onNetworkShutdown(asServer={asServer})：另一侧仍在运行，暂不解绑。");
                return;
            }

            Log.Info("[NetworkScene] onNetworkShutdown：网络已完全停止，执行 Unbind。");
            Unbind();
        }

        private void SubscribeSceneEvents()
        {
            if (_nm == null)
            {
                return;
            }

            SubscribeServerSceneEvents();

            // Host 的 client 模块不开被动加载页（见 S2 Host 行为）；纯客户端才订阅 client 模块。
            // 此路径仅在 Bind 时网络已就绪（自动启动）才会进入；启动中场景由 OnNetworkStarted 按 asServer 路由。
            if (!_nm.isHost && !_nm.isServer)
            {
                SubscribeClientSceneEvents();
            }
        }

        private void SubscribeServerSceneEvents()
        {
            if (_nm == null)
            {
                return;
            }

            if (_serverScenes == null)
            {
                _serverScenes = _nm.GetModule<ScenesModule>(true);
            }

            if (_serverScenes != null)
            {
                _serverScenes.onYooAssetSceneStartLoading -= HandleStartLoading;
                _serverScenes.onYooAssetSceneLoaded -= HandleLoaded;
                _serverScenes.onYooAssetSceneStartLoading += HandleStartLoading;
                _serverScenes.onYooAssetSceneLoaded += HandleLoaded;
                Log.Info("[NetworkScene] 已订阅 server ScenesModule 场景事件。");
            }
            else
            {
                Log.Warning("[NetworkScene] server ScenesModule 未创建（服务器未启动？），稍后由事件补订阅。");
            }
        }

        private void SubscribeClientSceneEvents()
        {
            if (_nm == null)
            {
                return;
            }

            if (_clientScenes == null)
            {
                _clientScenes = _nm.GetModule<ScenesModule>(false);
            }

            if (_clientScenes != null)
            {
                _clientScenes.onYooAssetSceneStartLoading -= HandleStartLoading;
                _clientScenes.onYooAssetSceneLoaded -= HandleLoaded;
                _clientScenes.onYooAssetSceneStartLoading += HandleStartLoading;
                _clientScenes.onYooAssetSceneLoaded += HandleLoaded;
                Log.Info("[NetworkScene] 已订阅 client ScenesModule 场景事件。");
            }
            else
            {
                Log.Warning("[NetworkScene] client ScenesModule 未创建（客户端未启动？），稍后由事件补订阅。");
            }
        }

        private void UnsubscribeSceneEvents()
        {
            if (_serverScenes != null)
            {
                _serverScenes.onYooAssetSceneStartLoading -= HandleStartLoading;
                _serverScenes.onYooAssetSceneLoaded -= HandleLoaded;
            }

            if (_clientScenes != null)
            {
                _clientScenes.onYooAssetSceneStartLoading -= HandleStartLoading;
                _clientScenes.onYooAssetSceneLoaded -= HandleLoaded;
            }
        }

        private void SyncLocalLoadAllowed()
        {
            // 联机纯客户端禁止主动加载；服务器 / Host / 单机保持允许
            bool allowed = _nm == null || _nm.isServer || _nm.isHost || (!_nm.isServer && !_nm.isClient);
            GameModule.GameScene.SetLocalLoadAllowed(allowed);
            Log.Info($"[NetworkScene] 本地主动加载许可 = {allowed}");
        }

        // ---------- 服务器：权威加载 ----------

        /// <summary>服务器专用：权威加载场景（DS 与 Host 都走这里）。
        /// <para>
        /// <b>业务入口</b>：DS 启动后停在待机（不进任何业务场景），业务拿到目标场景后调本方法开局。
        /// 典型写法：<c>GameModule.Network.SceneCoordinator.LoadSceneServer(SceneType.xxx)</c>。
        /// </para>
        /// </summary>
        /// <param name="sceneType">目标场景类型。</param>
        /// <returns>是否成功发起加载。</returns>
        public bool LoadSceneServer(SceneType sceneType)
        {
            if (_nm == null || !_nm.isServer)
            {
                Log.Error("[NetworkScene] LoadSceneServer 拒绝：本端不是服务器（或 NM 未绑定）。客户端请走 RequestSceneChange。");
                return false;
            }

            if (IsTransitioning)
            {
                Log.Warning($"[NetworkScene] LoadSceneServer 拒绝：上一次切换（{_pendingSceneType}）尚未收口。");
                return false;
            }

            string location = GameModule.GameScene.GetSceneName(sceneType);
            if (string.IsNullOrEmpty(location))
            {
                Log.Error($"[NetworkScene] LoadSceneServer 拒绝：SceneType {sceneType} 无对应资源地址（检查 SceneEnumConfig 生成）。");
                return false;
            }

            if (_serverScenes == null)
            {
                _serverScenes = _nm.GetModule<ScenesModule>(true);
            }

            if (_serverScenes == null)
            {
                Log.Error("[NetworkScene] LoadSceneServer 失败：服务器 ScenesModule 尚未创建（网络未启动？）。");
                return false;
            }

            if (_serverScenes.IsYooAssetSceneLoading(PackageName, location))
            {
                Log.Warning($"[NetworkScene] LoadSceneServer 忽略：{location} 正在加载，拒绝重复请求。");
                return false;
            }

            _pendingSceneType = sceneType;
            IsTransitioning = true;

            // 1) 加载前写回场景状态（必须早于场景 Awake，否则 DynamicSceneSpawner.IsNormalBoot 判定失败）
            GameModule.GameScene.NotifyExternalSceneLoadStarting(sceneType);
            Log.Info($"[NetworkScene] LoadSceneServer：{sceneType}:{location} 开始加载（场景状态已写回）。");

            // 2) 派发请求事件
            GameEvent.Get<INetworkSceneEvent>().OnServerSceneLoadRequested(sceneType);

            var settings = new PurrSceneSettings
            {
                mode = LoadSceneMode.Single,          // UnityEngine.SceneManagement.LoadSceneMode
                physicsMode = LocalPhysicsMode.None, // UnityEngine.SceneManagement.LocalPhysicsMode
                isPublic = true
            };

            var handle = _serverScenes.LoadYooAssetSceneAsync(PackageName, location, settings);
            if (handle == null)
            {
                Log.Error($"[NetworkScene] LoadSceneServer 失败：LoadYooAssetSceneAsync 返回 null（location={location}），回滚场景状态。");
                GameModule.GameScene.NotifyExternalSceneLoadFailed();
                GameEvent.Get<INetworkSceneEvent>().OnSceneLoadFailed(sceneType, "LoadYooAssetSceneAsync 返回 null");
                IsTransitioning = false;
                return false;
            }

            // 3) 失败检测：PurrNet 失败只打日志、不派发 onYooAssetSceneLoaded
            handle.Completed += _ =>
            {
                if (handle.Status != EOperationStatus.Succeeded)
                {
                    Log.Error($"[NetworkScene] 服务器场景加载失败：{location}，Error={handle.Error}，回滚场景状态。");
                    GameModule.GameScene.NotifyExternalSceneLoadFailed();
                    GameEvent.Get<INetworkSceneEvent>().OnSceneLoadFailed(_pendingSceneType, handle.Error);
                    IsTransitioning = false;
                }
            };
            return true;
        }

        // ---------- 客户端：请求切换 ----------

        /// <summary>客户端专用：请求服务器切场景（本地不发起加载）。</summary>
        /// <param name="sceneType">想切换到的场景类型。</param>
        public void RequestSceneChange(SceneType sceneType)
        {
            if (_nm == null || !_nm.isClient || _nm.isHost)
            {
                Log.Error("[NetworkScene] RequestSceneChange 拒绝：只有联机纯客户端可请求切场景。");
                return;
            }

            Log.Info($"[NetworkScene] RequestSceneChange：向服务器请求切换到 {sceneType}。");
            _nm.SendToServer(new SceneChangeRequest { sceneTypeValue = (int)sceneType });
        }

        private void OnSceneChangeRequest(PlayerID player, SceneChangeRequest data, bool asServer)
        {
            if (!asServer)
            {
                return; // 只处理服务器侧收到的请求
            }

            var sceneType = (SceneType)data.sceneTypeValue;
            // 首版校验：枚举值合法即可（服务器是权威，后续可在此加白名单）
            if (!System.Enum.IsDefined(typeof(SceneType), sceneType))
            {
                Log.Warning($"[NetworkScene] 收到无效 SceneChangeRequest：{data.sceneTypeValue}（来自 {player}），忽略。");
                return;
            }

            Log.Info($"[NetworkScene] 收到切场景请求：{player} → {sceneType}，转 LoadSceneServer。");
            LoadSceneServer(sceneType);
        }

        // ---------- PurrNet 场景事件分流 ----------

        private void HandleStartLoading(SceneID sceneId, string packageName, string location, bool asServer)
        {
            // 只处理纯客户端侧：服务器侧无加载页；Host 的 client 模块按设计不开被动页
            if (asServer || _nm == null || _nm.isHost || _nm.isServer)
            {
                return;
            }

            var sceneType = GameModule.GameScene.GetSceneTypeFromName(location);
            if (sceneType == null)
            {
                Log.Warning($"[NetworkScene] 被动加载收到未知场景 location={location}，忽略。");
                return;
            }

            Log.Info($"[NetworkScene] 客户端被动加载开始：{location}（scene={sceneType}）。");
            // 必须同步调用（早于场景 Awake）：先开会话再通知伪进度启动，否则阶段 5 门闩失效
            GameModule.GameScene.BeginPassiveLoad(sceneType.Value);
            GameModule.GameScene.NotifyPassiveLoadStarted();
        }

        private void HandleLoaded(SceneID sceneId, string packageName, string location, bool asServer)
        {
            if (_nm == null)
            {
                return;
            }

            if (asServer)
            {
                // 服务器侧收口（DS 与 Host 的 server 模块都走这里）
                Log.Info($"[NetworkScene] 服务器场景加载完成：{location}。");
                ActiveServerScene = _pendingSceneType;
                GameModule.GameScene.NotifyExternalSceneLoaded(); // 内部防重，Host 下 client 侧再来一次不会重复派发
                GameEvent.Get<INetworkSceneEvent>().OnServerSceneLoadCompleted(_pendingSceneType);
                IsTransitioning = false;
                return;
            }

            if (_nm.isHost || _nm.isServer)
            {
                // Host：client 模块收到同一 handle 的 loaded，统一按服务器侧处理（不开始被动会话）
                Log.Info($"[NetworkScene] Host 侧场景加载完成：{location}（按服务器分支收口）。");
                GameModule.GameScene.NotifyExternalSceneLoaded();
                IsTransitioning = false;
                return;
            }

            // 纯客户端：通知被动会话完成（若没有活跃被动会话则忽略）
            Log.Info($"[NetworkScene] 客户端被动加载完成：{location}。");
            GameModule.GameScene.NotifyPassiveLoadCompleted();
        }
    }
}
