using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.NetworkTest;
using Launcher;
#if ENABLE_OBFUZ
using Obfuz;
#endif
using TEngine;
using UnityEngine;
using YooAsset;

#pragma warning disable CS0436


/// <summary>
/// 游戏App。
/// </summary>
#if ENABLE_OBFUZ
[ObfuzIgnore(ObfuzScope.TypeName | ObfuzScope.MethodName)]
#endif
public partial class GameApp
{
    private static List<Assembly> _hotfixAssembly;

    /// <summary>
    /// 热更域App主入口。
    /// </summary>
    /// <param name="objects"></param>
    public static void Entrance(object[] objects)
    {
        GameEventHelper.Init();
        _hotfixAssembly = (List<Assembly>)objects[0];

        // NM 初始化早于热更程序集加载；在热更入口补扫，注册其中的 RPC Manifest 与 Packer。
        PurrNet.NetworkManager.CallAllRegisters();

        Log.Warning("======= 看到此条日志代表你成功运行了热更新代码 =======");
        Log.Warning("======= Entrance GameApp =======");
        Utility.Unity.AddDestroyListener(Release);
        Log.Warning("======= StartGameLogic =======");
        StartGameLogic();
        //Log.Warning("======= 我是热更代码 =======");
    }
    
    private static void StartGameLogic()
    {
        ModuleSystem.RegisterModule<IUIJumpControl>(new UIJumpControl());
        ModuleSystem.RegisterModule<IGameSceneModule>(new GameSceneModule());
        ModuleSystem.RegisterModule<INetworkModule>(new NetworkModule());

        // DS 模式：跳过多屏布局，按命令行参数加载场景，NM 启动交给业务
        if (Launcher.DedicatedServerLauncher.IsDedicatedServerBuild)
        {
            StartDedicatedServer();
            return;
        }

        // MPPM 联调（tag=ds 实例，见 MppmBootstrapProbe）：编辑器内 NM 的 StartFlags.ServerBuild 不触发
        // （isServerBuild 编译期口径），这里手动补 StartServer，让主编辑器承担 DS 角色（C-2）。
        if (ResolveMppmTag() == "ds")
        {
            StartDedicatedServer();
            if (GameModule.Network.NetworkManager != null && !GameModule.Network.IsAutoStarted)
            {
                Log.Warning("[MppmAutoStart] 主编辑器（ds）：DS 待机，自动 StartServer。");
                GameModule.Network.StartServer();
            }
            NetTestRunner.RunAllWhenReady(GameModule.Network.NetworkManager);
            return;
        }

        //多屏显示配置，内部异步执行
        GameModule.Screen.ApplyAll();

        GameModule.GameScene.LoadScene(SceneType.MainScene);

        string mppmTag = ResolveMppmTag();
        if (mppmTag == "host" || mppmTag == "client" || NetTestRunner.ResolvePlayerTestRole().HasValue)
        {
            ArmMppmAutoStart();
        }
    }

    // ===== S6 联调临时分流（MPPM Tag / 真包 -role host|client）=====
    // MPPM 主编辑器自动 StartHost，tag=client 的 Editor Instance 自动 StartClient；
    // 真包由 -role 决定角色。联调验收完成后整体移除（含 Release 里的退订）。
    private static bool _mppmAutoStartArmed;

    private static void ArmMppmAutoStart()
    {
        if (_mppmAutoStartArmed)
        {
            return;
        }

        _mppmAutoStartArmed = true;
        GameEvent.AddEventListener<SceneType>(IGameSceneEvent_Event.OnSceneReady, OnMppmSceneReady);
    }

    private static void OnMppmSceneReady(SceneType sceneType)
    {
        if (sceneType != SceneType.MainScene)
        {
            return;
        }

        string tag = ResolveMppmTag();
        NetTestRole? playerRole = NetTestRunner.ResolvePlayerTestRole();
        bool isHost = tag == "host" || playerRole == NetTestRole.Host;
        bool isClient = tag == "client" || playerRole == NetTestRole.Client;
        if (!isHost && !isClient)
        {
            return;
        }

        GameEvent.RemoveEventListener<SceneType>(IGameSceneEvent_Event.OnSceneReady, OnMppmSceneReady);
        _mppmAutoStartArmed = false;
        NetTestRunner.EnsurePrefabProvider(GameModule.Network.NetworkManager);

        if (isHost)
        {
            string source = tag == "host" ? "MPPM 主编辑器" : "Player";
            Log.Warning($"[NetTestAutoStart] {source}（host）：MainScene 就绪，自动 StartHost。");
            GameModule.Network.StartHost();
        }
        else
        {
            string source = tag == "client" ? "MPPM 实例" : "Player";
            Log.Warning($"[NetTestAutoStart] {source}（client）：MainScene 就绪，自动 StartClient。");
            GameModule.Network.StartClient();
        }

        NetTestRunner.RunAllWhenReady(GameModule.Network.NetworkManager);
    }

    /// <summary>反射读取持久化 MPPM Scenario 为主实例或克隆分配的 Tag；非联调环境返回 null。</summary>
    private static string ResolveMppmTag()
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("Unity.Multiplayer.PlayMode.CurrentPlayer");
            if (t == null)
            {
                continue;
            }

            var tagsProp = t.GetProperty("Tags");
            var tags = tagsProp?.GetValue(null) as System.Collections.IEnumerable;
            if (tags == null)
            {
                return null;
            }

            foreach (var tag in tags)
            {
                var s = tag?.ToString();
                if (s == "client" || s == "host" || s == "ds")
                {
                    return s;
                }
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Dedicated Server 启动入口。
    /// <para><b>只进待机，不决定场景</b>：DS 启动后停在当前启动场景（预挂 <c>NetworkManager</c>，
    /// PurrNet <c>StartFlags.ServerBuild</c> 自动起服），等待业务侧下发目标场景后再
    /// 调 <c>NetworkSceneCoordinator.LoadSceneServer</c> 推进游戏场景。</para>
    /// <para>
    /// 为何不在命令行决定场景：目标房间/关卡属于业务决策（例如由中控分配），框架不应硬编码。
    /// 因此已移除 <c>--scene</c> / <c>--scene-type</c> 两个参数。
    /// </para>
    /// </summary>
    private static void StartDedicatedServer()
    {
        Log.Warning("======= Dedicated Server 启动 ======");

        // 不做任何场景加载：就停在当前启动场景（NetworkManager 所在，已 DontDestroyOnLoad）。
        // 目标场景由业务在运行时决定，取得后调用：
        //     GameModule.Network.SceneCoordinator.LoadSceneServer(sceneType);
        //
        // NetworkManager 启动方式：
        //   方式 A（当前）：启动场景预挂 NM，PurrNet StartFlags.ServerBuild 自动 StartServer
        //   方式 B：业务异步加载 NM 预制体 → GameModule.Network.BindNetworkManager(nm) → StartServer
        Log.Warning("[DS] 已进入待机，等待业务下发目标场景（收到后调 SceneCoordinator.LoadSceneServer）。");

        if (GameModule.Network.NetworkManager == null)
        {
            Log.Error("[DS] 待机时未找到 NetworkManager：服务器未起服，中控/客户端都无法连接。" +
                      "请确认 DS 启动场景已预挂 NetworkManager（dedicated_server_boot.unity）且已加入 Build Settings。");
        }
        else
        {
            Log.Info($"[DS] NetworkManager 就绪：IsServer={GameModule.Network.IsServer}, IsClient={GameModule.Network.IsClient}");
        }
    }
    
    private static void Release()
    {
        if (_mppmAutoStartArmed)
        {
            GameEvent.RemoveEventListener<SceneType>(IGameSceneEvent_Event.OnSceneReady, OnMppmSceneReady);
            _mppmAutoStartArmed = false;
        }

        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
