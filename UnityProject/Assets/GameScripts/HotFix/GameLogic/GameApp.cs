using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
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

        //多屏显示配置，内部异步执行
        GameModule.Screen.ApplyAll();

        GameModule.GameScene.LoadScene(SceneType.MainScene);
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
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
