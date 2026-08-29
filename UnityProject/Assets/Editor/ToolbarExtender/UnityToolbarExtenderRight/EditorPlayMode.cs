#if !UNITY_6000_3_OR_NEWER

using UnityEditor;
using UnityEngine;
using UnityToolbarExtender;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// EditorPlayMode 控件（资源模式选择器）
    /// </summary>
    public partial class UnityToolbarExtenderRight
    {
        private const string BUTTON_STYLE_NAME = "Tab middle";
        private const string RESOURCE_MODE_PREFS_KEY = "EditorPlayMode";
        private const string RESOURCE_MODE_PREFS_VERSION_KEY = "TEngine.EditorPlayModePrefsVersion";
        private const int RESOURCE_MODE_PREFS_VERSION = 1;

        private static readonly string[] _resourceModeNames =
        {
            "EditorMode (编辑器下的模拟模式)",
            "OfflinePlayMode (单机模式)",
            "HostPlayMode (联机运行模式)",
            "WebPlayMode (WebGL运行模式)"
        };

        private static readonly EPlayMode[] _resourceModes =
        {
            EPlayMode.EditorSimulateMode,
            EPlayMode.OfflinePlayMode,
            EPlayMode.HostPlayMode,
            EPlayMode.WebPlayMode
        };

        private static int _resourceModeIndex = GetResourceModeIndex();
        public static int ResourceModeIndex => _resourceModeIndex;

        private static int GetResourceModeIndex()
        {
            MigrateLegacyResourceModePreference();
            int savedMode = EditorPrefs.GetInt(RESOURCE_MODE_PREFS_KEY, (int)EPlayMode.EditorSimulateMode);
            for (int i = 0; i < _resourceModes.Length; i++)
            {
                if ((int)_resourceModes[i] == savedMode)
                    return i;
            }

            return 0;
        }

        private static void MigrateLegacyResourceModePreference()
        {
            if (EditorPrefs.GetInt(RESOURCE_MODE_PREFS_VERSION_KEY, 0) >= RESOURCE_MODE_PREFS_VERSION)
                return;

            int legacyIndex = EditorPrefs.GetInt(RESOURCE_MODE_PREFS_KEY, 0);
            if (legacyIndex >= 0 && legacyIndex < _resourceModes.Length)
                EditorPrefs.SetInt(RESOURCE_MODE_PREFS_KEY, (int)_resourceModes[legacyIndex]);

            EditorPrefs.SetInt(RESOURCE_MODE_PREFS_VERSION_KEY, RESOURCE_MODE_PREFS_VERSION);
        }

        static class ToolbarStyles
        {
            public static readonly GUIStyle PopupStyle;

            static ToolbarStyles()
            {
                PopupStyle = new GUIStyle(EditorStyles.popup)
                {
                    //fontSize = 11,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(6, 6, 0, 0)
                };
            }
        }

        static void OnToolbarGUI_EditorPlayMode()
        {
            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            {
                GUILayout.Space(8);

                // 自动计算最长文本宽度
                float maxWidth = 0;
                foreach (string mode in _resourceModeNames)
                {
                    Vector2 size = ToolbarStyles.PopupStyle.CalcSize(new GUIContent(mode));
                    if (size.x > maxWidth)
                        maxWidth = size.x;
                }

                // 加点缓冲宽度（最多不超过220像素）
                float popupWidth = Mathf.Clamp(maxWidth + 20, 100, 220);

                GUILayout.BeginHorizontal();
                //GUILayout.Label("资源模式：", GUILayout.Width(65));

                int selectedIndex = EditorGUILayout.Popup(
                    _resourceModeIndex,
                    _resourceModeNames,
                    ToolbarStyles.PopupStyle,
                    GUILayout.Width(popupWidth)
                );

                if (selectedIndex != _resourceModeIndex)
                {
                    Debug.Log($"更改编辑器资源运行模式：{_resourceModeNames[selectedIndex]}");
                    _resourceModeIndex = selectedIndex;
                    EditorPrefs.SetInt(RESOURCE_MODE_PREFS_KEY, (int)_resourceModes[selectedIndex]);
                }
                GUILayout.EndHorizontal();
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}

#endif
