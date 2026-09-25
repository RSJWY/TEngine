#if !UNITY_6000_3_OR_NEWER

using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 主工具栏构建模式指示：在 yooasset 资源模式切换的右侧实时显示 dev/release、Obfuz 与 pdb 状态，点击弹出快捷切换菜单。
    /// 模式状态由 ENABLE_RELEASE 宏表达，切换会触发重编译与域重载，指示随之自动刷新，
    /// 因此模式状态在类加载时读取一次即可（静态字段随域重载重新初始化）。
    /// pdb 开关是 UpdateSetting 的序列化配置，切换不触发域重载，因此实时读取，并在值变化时主动重绘工具栏。
    /// </summary>
    public partial class UnityToolbarExtenderRight
    {
        private const float ToolbarButtonHeight = 22f;

        private static readonly bool IsReleaseBuildMode = BuildDLLCommand.IsReleaseModeActive;
        private static readonly bool IsObfuzBuildMode = BuildDLLCommand.IsObfuzActiveSafe;

        // 缓存配置资产引用：工具栏重绘频繁，避免每次都经 Settings.UpdateSetting 查找场景对象与资产
        private static UpdateSetting _updateSetting;
        private static bool _lastPdbEnabled;

        // 与 BuildModeWindow 的状态配色保持一致：dev 绿色、release 橙色
        private static readonly Color ReleaseModeColor = new Color(0.95f, 0.7f, 0.25f);
        private static readonly Color DevModeColor = new Color(0.45f, 0.85f, 0.45f);

        private static GUIStyle _buildModeButtonStyle;

        private static bool IsPdbEnabled
        {
            get
            {
                if (_updateSetting == null)
                {
                    _updateSetting = Settings.UpdateSetting;
                }

                return _updateSetting != null && _updateSetting.GeneratePdb;
            }
        }

        // release 模式下 pdb 开关不生效，与构建模式窗口一致显示为"禁用"
        private static string PdbStatusText => IsReleaseBuildMode ? "禁用" : IsPdbEnabled ? "开" : "关";

        /// <summary>
        /// 编辑器每次 update 比对 pdb 开关，变化时重绘工具栏（覆盖构建模式窗口、Inspector 中的修改），
        /// 否则工具栏要等下次鼠标悬停重绘才会更新显示。
        /// </summary>
        private static void RefreshToolbarOnPdbChanged()
        {
            bool pdbEnabled = IsPdbEnabled;
            if (pdbEnabled == _lastPdbEnabled)
            {
                return;
            }

            _lastPdbEnabled = pdbEnabled;
            InternalEditorUtility.RepaintAllViews();
        }

        private static void OnToolbarGUI_BuildMode()
        {
            _buildModeButtonStyle ??= new GUIStyle(BUTTON_STYLE_NAME)
            {
                padding = new RectOffset(4, 4, 2, 2),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fixedHeight = ToolbarButtonHeight
            };

            GUILayout.Space(8);

            var prevColor = GUI.color;
            GUI.color = IsReleaseBuildMode ? ReleaseModeColor : DevModeColor;
            string label = IsReleaseBuildMode ? "模式: release" : "模式: dev";
            if (BuildDLLCommand.IsObfuzInstalled)
            {
                label += $" | Obfuz: {(IsObfuzBuildMode ? "开" : "关")}";
            }
            label += $" | pdb: {PdbStatusText}";
            if (GUILayout.Button(
                    new GUIContent(label, BuildModeTooltip()),
                    _buildModeButtonStyle))
            {
                ShowBuildModeMenu();
            }

            GUI.color = prevColor;
        }

        private static string BuildModeTooltip()
        {
            var tooltip = IsReleaseBuildMode
                ? "当前构建模式：release（发布：不生成/不加载 pdb）"
                : "当前构建模式：dev（开发：按 pdb 开关生成/加载 pdb）";
            if (BuildDLLCommand.IsObfuzInstalled)
            {
                tooltip += $"\nObfuz 混淆：{(IsObfuzBuildMode ? "开" : "关")}";
            }

            tooltip += $"\npdb 符号：{PdbStatusText}";
            return tooltip + "\n点击弹出快捷切换菜单";
        }

        private static void ShowBuildModeMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("dev 模式（开发，pdb 可用）"), !IsReleaseBuildMode,
                () => BuildDLLCommand.SetReleaseModeConfirm(false));
            menu.AddItem(new GUIContent("release 模式（发布，不含 pdb）"), IsReleaseBuildMode,
                () => BuildDLLCommand.SetReleaseModeConfirm(true));
            if (BuildDLLCommand.IsObfuzInstalled)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Obfuz 混淆/开启"), IsObfuzBuildMode,
                    () => BuildDLLCommand.SetObfuzSafeConfirm(true));
                menu.AddItem(new GUIContent("Obfuz 混淆/关闭"), !IsObfuzBuildMode,
                    () => BuildDLLCommand.SetObfuzSafeConfirm(false));
            }

            menu.AddSeparator(string.Empty);
            if (IsReleaseBuildMode)
            {
                // 与构建模式窗口一致：release 模式下 pdb 开关不生效，不提供切换
                menu.AddDisabledItem(new GUIContent("pdb 符号（release 模式不生效）"));
            }
            else
            {
                bool pdbEnabled = IsPdbEnabled;
                menu.AddItem(new GUIContent("pdb 符号/开启"), pdbEnabled,
                    () => BuildDLLCommand.SetPdbEnabledConfirm(true));
                menu.AddItem(new GUIContent("pdb 符号/关闭"), !pdbEnabled,
                    () => BuildDLLCommand.SetPdbEnabledConfirm(false));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("打开构建模式窗口"), false,
                () => EditorApplication.ExecuteMenuItem("Build/构建模式窗口"));
            menu.ShowAsContext();
        }
    }
}

#endif
