using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
#if ENABLE_OBFUZ
using Obfuz;
#endif
using RockeyArm;
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

        // 加密锁授权：主包 ProcedureLaunch 已完成验证并启动看门狗。
        // 若验证通过后、热更加载前看门狗已失效（狗被拔/被锁拒绝），在此拦截，不进入游戏场景。
        IRockeyArmModule rockeyArm = GameModule.RockeyArm;
        if (rockeyArm != null && !rockeyArm.IsLicensed)
        {
            ShowLicenseErrorUI(rockeyArm.LastError?.UserMessage ?? "加密锁授权已失效!");
            return;
        }

        // 运行期授权失败（看门狗复查失败）统一弹 ExitErrorUI
        GameEvent.AddEventListener<string>(
            RuntimeId.ToRuntimeId(RockeyArmModule.LICENSE_ERROR_EVENT_ID), OnLicenseError);

        //多屏显示配置，内部异步执行
        GameModule.Screen.ApplyAll();

        GameModule.GameScene.LoadScene(SceneType.MainScene);
    }

    private static void OnLicenseError(string message)
    {
        ShowLicenseErrorUI(message);
    }

    private static void ShowLicenseErrorUI(string message)
    {
        // ExitErrorUI 为零框架依赖弹窗（RockeyARM 包自带，prefab 位于其 Resources/UIWindow），不走 TEngine UI 框架。
        RockeyUIMgr.ShowUI<ExitErrorUI>(new ExitPanelData
        {
            title = "授权验证失败",
            contenet = message,
            buttonText = "退出"
        });
    }

    private static void Release()
    {
        GameEvent.RemoveEventListener<string>(
            RuntimeId.ToRuntimeId(RockeyArmModule.LICENSE_ERROR_EVENT_ID), OnLicenseError);
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
