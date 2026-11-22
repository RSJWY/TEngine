using Launcher;
using UnityEditor;

/// <summary>
/// DS 联调模拟开关（编辑器会话级，重启编辑器自动复位）。
/// <para><b>DS 模拟模式</b>：打开后 <see cref="DedicatedServerLauncher.IsDedicatedServerBuild"/> 在编辑器返回 true，
/// Play 含 GameEntry 的启动场景（main.unity）即走完整 DS 启动流程（Procedure 链跳过 UI、GameApp 进待机）。
/// 也可不设菜单、改用环境变量 <c>TENGINE_FORCE_DS=1</c>（batchmode/CI）。</para>
/// <para><b>S6 Scenario 形态</b>：决定 <see cref="MppmBootstrapProbe"/> 创建的 MPPM 双实例组合——
/// host+client（C-1）或 ds+client（C-2）。形态切换后需重新「启动 MPPM Scenario」。</para>
/// </summary>
public static class DedicatedServerSimulationMenu
{
    private const string MenuRoot = "TEngine/联调/";
    private const string ForceDsMenu = MenuRoot + "DS 模拟模式";
    private const string ModeDualMenu = MenuRoot + "S6 Scenario/host+client（C-1）";
    private const string ModeDsClientMenu = MenuRoot + "S6 Scenario/ds+client（C-2）";
    private const string LaunchMenu = MenuRoot + "启动 MPPM Scenario";

    public const string ScenarioModeSessionKey = "Launcher.MppmBootstrapProbe.ScenarioMode";
    public const string ScenarioModeDual = "dual";
    public const string ScenarioModeDsClient = "ds-client";

    // ===== DS 模拟模式（逃生门） =====

    [MenuItem(ForceDsMenu, false, 100)]
    private static void ToggleForceDs()
    {
        bool cur = SessionState.GetBool(DedicatedServerLauncher.ForceDsSessionKey, false);
        SessionState.SetBool(DedicatedServerLauncher.ForceDsSessionKey, !cur);
    }

    [MenuItem(ForceDsMenu, true)]
    private static bool ToggleForceDsValidate()
    {
        Menu.SetChecked(ForceDsMenu, SessionState.GetBool(DedicatedServerLauncher.ForceDsSessionKey, false));
        return true;
    }

    // ===== S6 Scenario 形态（radio） =====

    [MenuItem(ModeDualMenu, false, 200)]
    private static void SelectModeDual()
    {
        SessionState.SetString(ScenarioModeSessionKey, ScenarioModeDual);
    }

    [MenuItem(ModeDualMenu, true)]
    private static bool SelectModeDualValidate()
    {
        Menu.SetChecked(ModeDualMenu, CurrentMode() == ScenarioModeDual);
        return true;
    }

    [MenuItem(ModeDsClientMenu, false, 201)]
    private static void SelectModeDsClient()
    {
        SessionState.SetString(ScenarioModeSessionKey, ScenarioModeDsClient);
    }

    [MenuItem(ModeDsClientMenu, true)]
    private static bool SelectModeDsClientValidate()
    {
        Menu.SetChecked(ModeDsClientMenu, CurrentMode() == ScenarioModeDsClient);
        return true;
    }

    private static string CurrentMode()
    {
        return SessionState.GetString(ScenarioModeSessionKey, ScenarioModeDual);
    }

    // ===== 手动启动 Scenario =====

    [MenuItem(LaunchMenu, false, 300)]
    private static void LaunchScenario()
    {
        // 手动触发：强制执行（静态构造的自动跑一次已用掉防重标记）
        MppmBootstrapProbe.RunForce();
    }
}
