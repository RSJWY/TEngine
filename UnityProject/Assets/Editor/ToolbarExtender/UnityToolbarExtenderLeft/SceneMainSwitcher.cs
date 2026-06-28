#if !UNITY_6000_3_OR_NEWER

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 快速切换到主启动场景（Assets/Scenes/main.unity）的工具方法。
    /// </summary>
    public partial class UnityToolbarExtenderLeft
    {
        private const string MainScenePath = "Assets/Scenes/main.unity";

        private static void GoToMainScene()
        {
            // 播放模式下先退出播放
            if (EditorApplication.isPlaying)
            {
                Debug.Log("正在退出播放模式，请再次点击以切换到主启动场景。");
                EditorApplication.isPlaying = false;
                return;
            }

            // 确认并保存当前场景修改
            if (!EditorSceneTransitionUtility.ConfirmSaveModifiedScenesBeforeSwitch())
            {
                return;
            }

            // 检查场景文件是否存在
            if (!File.Exists(MainScenePath))
            {
                Debug.LogWarning($"找不到主启动场景文件：{MainScenePath}");
                return;
            }

            Debug.Log($"切换到主启动场景：{MainScenePath}");
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }
    }
}

#endif