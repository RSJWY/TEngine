#if !UNITY_6000_3_OR_NEWER

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityToolbarExtender;

namespace TEngine
{
    public partial class UnityToolbarExtenderLeft
    {
        private const string PreviousSceneKey = "TEngine_PreviousScenePath"; // 用于存储之前场景路径的键
        private const string IsLauncherBtn = "TEngine_IsLauncher"; // 用于存储之前是否按下launcher

        private static readonly string SceneMain = "main";

        private static readonly string ButtonStyleName = "Tab middle";
        private static GUIStyle _buttonGuiStyle;
        private const float ToolbarButtonHeight = 22f;

        private static void OnToolbarGUI_SceneLauncher()
        {
            _buttonGuiStyle ??= new GUIStyle(ButtonStyleName)
            {
                padding = new RectOffset(4, 4, 2, 2),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fixedHeight = ToolbarButtonHeight
            };

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent("Launcher", EditorGUIUtility.FindTexture("PlayButton"), "Start Scene Launcher"),
                    _buttonGuiStyle))
                SceneHelper.StartScene(SceneMain);

            GUILayout.Space(6);

            if (GUILayout.Button(
                    new GUIContent("前往主启动场景", EditorGUIUtility.FindTexture("SceneContent"), "切换到 Assets/Scenes/main.unity 主启动场景"),
                    _buttonGuiStyle))
                GoToMainScene();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                // 从 EditorPrefs 读取之前的场景路径
                var previousScenePath = EditorPrefs.GetString(PreviousSceneKey, string.Empty);
                if (!string.IsNullOrEmpty(previousScenePath) && EditorPrefs.GetBool(IsLauncherBtn))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (EditorSceneTransitionUtility.ConfirmSaveModifiedScenesBeforeSwitch())
                            EditorSceneManager.OpenScene(previousScenePath);
                    };
                }

                EditorPrefs.SetBool(IsLauncherBtn, false);
            }
        }

        private static void OnEditorQuit()
        {
            EditorPrefs.SetString(PreviousSceneKey, "");
            EditorPrefs.SetBool(IsLauncherBtn, false);
        }

        private static class SceneHelper
        {
            private static string _sceneToOpen;

            public static void StartScene(string sceneName)
            {
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;

                // 记录当前场景路径到 EditorPrefs
                var activeScene = SceneManager.GetActiveScene();
                if (activeScene.isLoaded && activeScene.name != SceneMain)
                {
                    EditorPrefs.SetString(PreviousSceneKey, activeScene.path);
                    EditorPrefs.SetBool(IsLauncherBtn, true);
                }

                _sceneToOpen = sceneName;
                EditorApplication.update += OnUpdate;
            }

            private static void OnUpdate()
            {
                if (_sceneToOpen == null ||
                    EditorApplication.isPlaying || EditorApplication.isPaused ||
                    EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                EditorApplication.update -= OnUpdate;

                if (EditorSceneTransitionUtility.ConfirmSaveModifiedScenesBeforeSwitch())
                {
                    string scenePath = EditorSceneTransitionUtility.FindScenePathInFolder(
                        _sceneToOpen,
                        EditorSceneTransitionUtility.InitialSceneFolder);

                    if (string.IsNullOrEmpty(scenePath))
                    {
                        Debug.LogWarning($"Couldn't find scene file '{_sceneToOpen}' in {EditorSceneTransitionUtility.InitialSceneFolder}");
                    }
                    else
                    {
                        EditorSceneManager.OpenScene(scenePath);
                        EditorApplication.isPlaying = true;
                    }
                }

                _sceneToOpen = null;
            }
        }
    }
}

#endif
