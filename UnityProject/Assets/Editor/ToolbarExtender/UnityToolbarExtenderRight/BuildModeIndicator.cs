#if !UNITY_6000_3_OR_NEWER

using UnityEditor;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 主工具栏构建模式指示：在 yooasset 资源模式切换的右侧实时显示 dev/release，点击弹出快捷切换菜单。
    /// 模式状态由 ENABLE_RELEASE 宏表达，切换会触发重编译与域重载，指示随之自动刷新，
    /// 因此模式状态在类加载时读取一次即可（静态字段随域重载重新初始化）。
    /// </summary>
    public partial class UnityToolbarExtenderRight
    {
        private const float ToolbarButtonHeight = 22f;

        private static readonly bool IsReleaseBuildMode = BuildDLLCommand.IsReleaseModeActive;
        private static readonly bool IsObfuzBuildMode = BuildDLLCommand.IsObfuzActiveSafe;

        // 与 BuildModeWindow 的状态配色保持一致：dev 绿色、release 橙色
        private static readonly Color ReleaseModeColor = new Color(0.95f, 0.7f, 0.25f);
        private static readonly Color DevModeColor = new Color(0.45f, 0.85f, 0.45f);

        private static GUIStyle _buildModeButtonStyle;

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
                : "当前构建模式：dev（开发：pdb 有则加载）";
            if (BuildDLLCommand.IsObfuzInstalled)
            {
                tooltip += $"\nObfuz 混淆：{(IsObfuzBuildMode ? "开" : "关")}";
            }

            return tooltip + "\n点击弹出快捷切换菜单";
        }

        private static void ShowBuildModeMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("dev 模式（开发，pdb 可用）"), !IsReleaseBuildMode,
                () => BuildDLLCommand.SetReleaseMode(false));
            menu.AddItem(new GUIContent("release 模式（发布，不含 pdb）"), IsReleaseBuildMode,
                () => BuildDLLCommand.SetReleaseMode(true));
            if (BuildDLLCommand.IsObfuzInstalled)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Obfuz 混淆/开启"), IsObfuzBuildMode,
                    () => BuildDLLCommand.SetObfuzSafe(true));
                menu.AddItem(new GUIContent("Obfuz 混淆/关闭"), !IsObfuzBuildMode,
                    () => BuildDLLCommand.SetObfuzSafe(false));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("打开构建模式窗口"), false,
                () => EditorApplication.ExecuteMenuItem("TEngine/Build/构建模式窗口"));
            menu.ShowAsContext();
        }
    }
}

#endif
