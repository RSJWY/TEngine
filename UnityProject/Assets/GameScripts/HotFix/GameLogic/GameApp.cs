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
    /// <para>按命令行参数决定加载哪个场景；NetworkManager 启动交给业务自行处理：</para>
    /// <para>  方式 A：场景预挂 NM（dedicated_server.unity），PurrNet <c>StartFlags.ServerBuild</c> 自动启动；</para>
    /// <para>  方式 B：<c>LoadGameObjectAsync</c> 出 NM 预制体 → <c>BindNetworkManager</c> → <c>StartServer</c>。</para>
    /// <para>框架侧不主动调用 <c>StartServer()</c>，最大化业务自由度。</para>
    /// </summary>
    private static void StartDedicatedServer()
    {
        Log.Warning("======= Dedicated Server 启动 =======");

        string sceneName = Launcher.DedicatedServerLauncher.ResolveStartupScene();
        string sceneTypeStr = Launcher.DedicatedServerLauncher.ResolveStartupSceneType();

        // 优先按 scene-type 加载（走 GameSceneModule.LoadScene(SceneType)）
        if (!string.IsNullOrEmpty(sceneTypeStr)
            && System.Enum.TryParse<SceneType>(sceneTypeStr, true, out var sceneType))
        {
            Log.Info($"[DS] 按命令行 scene-type 加载场景：{sceneType}");
            GameModule.GameScene.LoadScene(sceneType);
        }
        else if (!string.IsNullOrEmpty(sceneName))
        {
            // 其次按场景名加载（走 SceneModule.LoadSceneAsync(location)）
            Log.Info($"[DS] 按命令行 scene 加载场景：{sceneName}");
            GameModule.Scene.LoadSceneAsync(sceneName).Forget();
        }
        else
        {
            // 未指定启动场景时 fallback 到 MainScene
            Log.Warning("[DS] 未指定启动场景，加载默认 MainScene。");
            GameModule.GameScene.LoadScene(SceneType.MainScene);
        }

        // NetworkManager 启动由业务自行处理：
        //   方式 A：场景预挂 NM（dedicated_server.unity），PurrNet StartFlags.ServerBuild 自动启动
        //   方式 B：LoadGameObjectAsync 出 NM 预制体 → BindNetworkManager → StartServer
        // 框架侧不主动调用 StartServer()，最大化业务自由度。
        Log.Info("[DS] NetworkManager 启动由业务自行处理（场景预挂或代码动态注入）。");
    }
    
    private static void Release()
    {
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
