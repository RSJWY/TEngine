using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 构建模式面板：dev/release 模式与 Obfuz 混淆的开关操作入口。
    /// 面板不持有状态，每次重绘实时读取宏/配置状态；切换宏会触发全量重编译，窗口随之重建，属正常现象。
    /// </summary>
    public class BuildModeWindow : OdinEditorWindow
    {
        [MenuItem("Build/构建模式窗口", false, 50)]
        private static void OpenWindow()
        {
            var window = GetWindow<BuildModeWindow>("构建模式");
            window.minSize = new Vector2(440, 340);
            window.Show();
        }

        private static bool ObfuzInstalled =>
#if OBFUZ_INSTALLED
            true;
#else
            false;
#endif

        private static bool IsRelease => BuildDLLCommand.IsReleaseModeActive;

        private static bool IsObfuzActiveSafe =>
#if OBFUZ_INSTALLED
            BuildDLLCommand.IsObfuzActive;
#else
            false;
#endif

        private static Color ActiveColor => new Color(0.45f, 0.85f, 0.45f);
        private static Color WarnColor => new Color(0.95f, 0.7f, 0.25f);
        private static Color MutedColor => new Color(0.65f, 0.65f, 0.65f);

        [Title("当前状态")]
        [DisplayAsString, LabelText("发布模式"), GUIColor(nameof(ModeStatusColor))]
        [ShowInInspector, PropertyOrder(0)]
        private string ModeStatus => IsRelease ? "release" : "dev";

        private Color ModeStatusColor => IsRelease ? WarnColor : ActiveColor;

        [DisplayAsString, LabelText("Obfuz 混淆"), GUIColor(nameof(ObfuzStatusColor))]
        [ShowInInspector, PropertyOrder(1)]
        private string ObfuzStatus =>
#if OBFUZ_INSTALLED
            BuildDLLCommand.IsObfuzActive ? "开" : "关";
#else
            "包未安装";
#endif

        private Color ObfuzStatusColor =>
#if OBFUZ_INSTALLED
            BuildDLLCommand.IsObfuzActive ? WarnColor : ActiveColor;
#else
            MutedColor;
#endif

        [DisplayAsString, LabelText("pdb 符号（仅 dev 生效）"), GUIColor(nameof(PdbStatusColor))]
        [InfoBox("release 模式下 pdb 开关不生效，打包时强制不含 pdb。", InfoMessageType.Info, VisibleIf = nameof(ReleaseWithPdbConfigured))]
        [ShowInInspector, PropertyOrder(2)]
        private string PdbStatus => IsRelease
            ? "禁用（release 模式）"
            : BuildDLLCommand.IsPdbEnabled ? "开" : "关";

        private Color PdbStatusColor => IsRelease || !BuildDLLCommand.IsPdbEnabled ? MutedColor : ActiveColor;

        private bool ReleaseWithPdbConfigured => IsRelease && BuildDLLCommand.IsPdbEnabled;

        [DisplayAsString, LabelText("当前组合"), GUIColor(nameof(ComboColor))]
        [ShowInInspector, PropertyOrder(3)]
        private string CurrentCombo
        {
            get
            {
                bool release = IsRelease;
                bool obfuz = ObfuzInstalled && IsObfuzActiveSafe;
                if (!release && !obfuz)
                {
                    return "真机调试（dev）";
                }
                if (release && obfuz)
                {
                    return "高防护发布（release + 混淆）";
                }
                if (release)
                {
                    return "低防护发布（release）";
                }
                return "dev + 混淆（非常规组合）";
            }
        }

        private Color ComboColor => IsRelease ? WarnColor : ActiveColor;

        [TitleGroup("开关")]
        [HorizontalGroup("开关/行")]
        [Button("$ReleaseToggleLabel", ButtonSizes.Large), GUIColor(nameof(ModeStatusColor))]
        [PropertyOrder(10)]
        private void ToggleReleaseMode()
        {
            BuildDLLCommand.SetReleaseModeConfirm(!IsRelease);
        }

        private string ReleaseToggleLabel => IsRelease ? "切回 dev 模式" : "切到 release 模式";

#if OBFUZ_INSTALLED
        [HorizontalGroup("开关/行")]
        [Button("$ObfuzToggleLabel", ButtonSizes.Large), GUIColor(nameof(ObfuzStatusColor))]
        [PropertyOrder(11)]
        private void ToggleObfuz()
        {
            BuildDLLCommand.SetObfuzSafeConfirm(!BuildDLLCommand.IsObfuzActive);
        }

        private string ObfuzToggleLabel => BuildDLLCommand.IsObfuzActive ? "关闭混淆" : "开启混淆";
#endif

        [HorizontalGroup("开关/行")]
        [Button("$PdbToggleLabel", ButtonSizes.Large), GUIColor(nameof(PdbStatusColor))]
        [EnableIf(nameof(EnablePdbToggle))]
        [PropertyOrder(12)]
        private void TogglePdb()
        {
            BuildDLLCommand.SetPdbEnabledConfirm(!BuildDLLCommand.IsPdbEnabled);
        }

        private string PdbToggleLabel => BuildDLLCommand.IsPdbEnabled ? "关闭 pdb" : "开启 pdb";

        private bool EnablePdbToggle => !IsRelease;

        [TitleGroup("一键预设")]
        [InfoBox("预设会切换发布模式与混淆；与 pdb 冲突时弹确认框，确认后自动关闭 pdb。\n打包 exe 与热更资源包时请保持宏状态一致，否则启动校验会拦截。", InfoMessageType.None)]
        [ShowInInspector, DisplayAsString, HideLabel, PropertyOrder(19)]
        private string PresetTip => string.Empty;

        [TitleGroup("一键预设")]
        [HorizontalGroup("一键预设/行")]
        [Button("真机调试", ButtonSizes.Large), GUIColor(nameof(ActiveColor))]
        [PropertyOrder(20)]
        private void 真机调试()
        {
            // dev + 关 Obfuz：与 pdb 不冲突，直接切换，保留 pdb 配置。
            BuildDLLCommand.SetReleaseMode(false);
#if OBFUZ_INSTALLED
            BuildDLLCommand.SetObfuz(false);
#endif
        }

        [HorizontalGroup("一键预设/行")]
        [Button("高防护发布", ButtonSizes.Large), GUIColor(nameof(WarnColor))]
        [PropertyOrder(21)]
        private void 高防护发布()
        {
            // release + 开 Obfuz：两者均与 pdb 互斥，存在 pdb 时一次性确认。
            if (!ConfirmPresetWithPdbOff("高防护发布（release + 混淆）"))
            {
                return;
            }
            BuildDLLCommand.SetReleaseMode(true);
#if OBFUZ_INSTALLED
            BuildDLLCommand.SetObfuz(true);
#endif
        }

        [HorizontalGroup("一键预设/行")]
        [Button("低防护发布", ButtonSizes.Large), GUIColor(nameof(WarnColor))]
        [PropertyOrder(22)]
        private void 低防护发布()
        {
            // release 与 pdb 互斥，存在 pdb 时一次性确认。
            if (!ConfirmPresetWithPdbOff("低防护发布（release）"))
            {
                return;
            }
            BuildDLLCommand.SetReleaseMode(true);
#if OBFUZ_INSTALLED
            BuildDLLCommand.SetObfuz(false);
#endif
        }

        /// <summary>预设执行前确认：仅当 pdb 开启时弹一次框，用户确认后自动关闭 pdb 并返回 true。</summary>
        private static bool ConfirmPresetWithPdbOff(string presetName)
        {
            if (!BuildDLLCommand.IsPdbEnabled)
            {
                return true;
            }

            bool ok = EditorUtility.DisplayDialog(
                $"应用预设：{presetName}",
                $"预设「{presetName}」与 pdb 符号互斥（release/Obfuz 均要求无 pdb）。\n\n" +
                "当前 pdb 处于开启状态，应用预设需要先关闭 pdb。\n\n" +
                "是否继续并自动关闭 pdb？",
                "继续（自动关闭 pdb）",
                "取消");
            if (!ok)
            {
                return false;
            }
            BuildDLLCommand.SetPdbEnabled(false);
            return true;
        }
    }
}
