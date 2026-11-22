using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>S6 联调探针：通过菜单加载持久化 MPPM 双实例 Scenario，结果落盘。</summary>
/// <remarks>
/// MPPM 2.x 的 Scenario API 是 UnityEditor.MultiplayerModule 内部类型，只能反射调用。
/// 形态由 <see cref="DedicatedServerSimulationMenu.ScenarioModeSessionKey"/> 决定：
/// dual=主编辑器(host)+克隆(client)（C-1）；ds-client=主编辑器(ds)+克隆(client)（C-2）。
/// 形态切换或手动重启走菜单「TEngine/联调/启动 MPPM Scenario」。
/// </remarks>
public static class MppmBootstrapProbe
{
    private const string ResultPath = "Library/mppm_probe_result.txt";

    /// <summary>加载并启动当前形态的持久化双实例 Scenario。</summary>
    public static void Run()
    {
        RunCore();
    }

    /// <summary>手动触发（菜单）：启动当前选择的配置。</summary>
    public static void RunForce()
    {
        RunCore();
    }

    private static void RunCore()
    {
        try
        {
            string mode = SessionState.GetString(DedicatedServerSimulationMenu.ScenarioModeSessionKey,
                DedicatedServerSimulationMenu.ScenarioModeDual);

            string assetName = mode == DedicatedServerSimulationMenu.ScenarioModeDsClient
                ? "NetworkTest-DsClient"
                : "NetworkTest-HostClient";
            string assetPath = $"Assets/Settings/PlayMode/{assetName}.asset";

            var asm = FindAssembly("UnityEditor.MultiplayerModule");
            var tConfig = asm.GetType("Unity.Multiplayer.PlayMode.Editor.ScenarioConfig");
            var tFactory = asm.GetType("Unity.Multiplayer.PlayMode.Editor.ScenarioFactory");
            var tRunner = asm.GetType("Unity.Multiplayer.PlayMode.Editor.ScenarioRunner");
            var config = AssetDatabase.LoadAssetAtPath(assetPath, tConfig);
            if (config == null)
            {
                throw new InvalidOperationException($"MPPM Scenario 配置不存在：{assetPath}");
            }

            var descriptions = tConfig.GetMethod("GetAllInstances").Invoke(config, null);
            var scenario = tFactory.GetMethod("CreateScenario").Invoke(null,
                new object[] { assetName, descriptions }) as UnityEngine.Object;
            if (scenario == null)
            {
                throw new InvalidOperationException($"MPPM Scenario 创建失败：{assetPath}");
            }

            UnityEngine.Object.DontDestroyOnLoad(scenario);
            tRunner.GetMethod("LoadScenario").Invoke(null, new object[] { scenario });
            tRunner.GetMethod("StartScenario").Invoke(null, null);

            File.WriteAllText(ResultPath, $"OK mode={mode} asset={assetPath}");
            Debug.Log($"[MppmProbe] OK: scenario '{assetName}' loaded & started");
        }
        catch (Exception e)
        {
            var inner = e is System.Reflection.TargetInvocationException && e.InnerException != null
                ? e.InnerException
                : e;
            var msg = "FAIL " + inner.GetType().Name + ": " + inner.Message + "\n" + inner.StackTrace;
            File.WriteAllText(ResultPath, msg);
            Debug.LogError("[MppmProbe] " + msg);
        }
    }

    private static System.Reflection.Assembly FindAssembly(string name)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.GetName().Name == name)
            {
                return a;
            }
        }

        throw new InvalidOperationException("程序集未找到: " + name);
    }
}
