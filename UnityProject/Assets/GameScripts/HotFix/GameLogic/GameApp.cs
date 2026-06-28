using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
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
        // 部署配置已在主包 ProcedureLaunch（资源初始化前）加载，此处直接使用 GameModule.JsonConfig 即可

        // 启动窗口布局控制模块（多屏，仅 Windows Standalone 生效）。
        // 模块位于 AOT 层 TEngine.Runtime，首次访问触发创建并执行 OnInit；
        // OnInit 内读取 ScreenConfig.json 并按 ApplyOnInit 自动应用布局。这里再主动 ApplyAll 一次便于测试。
        if (GameModule.Screen.IsSupported)
        {
            Log.Info("[GameApp] 窗口布局模块已启动（Windows），主动应用一次布局。");
            GameModule.Screen.ApplyAll();
        }
        else
        {
            Log.Info("[GameApp] 当前平台不支持窗口布局模块，已跳过。");
        }

        GameModule.GameScene.LoadScene(SceneType.MainScene);
        UniTask.Create(async () =>
        {
            var assetHandle = YooAssets.LoadAssetAsync<GameObject>("UIHome");
            await assetHandle.ToUniTask();
        });
    }
    
    private static void Release()
    {
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
