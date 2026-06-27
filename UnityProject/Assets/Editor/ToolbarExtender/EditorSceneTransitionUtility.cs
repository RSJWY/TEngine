using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TEngine
{
    internal static class EditorSceneTransitionUtility
    {
        public const string InitialSceneFolder = "Assets/Scenes";

        public static bool ConfirmSaveModifiedScenesBeforeSwitch()
        {
            List<Scene> dirtyScenes = GetDirtyLoadedScenes();
            if (dirtyScenes.Count == 0)
            {
                return true;
            }

            string sceneMessage = dirtyScenes.Count == 1
                ? $"当前场景 \"{GetSceneDisplayName(dirtyScenes[0])}\" 有未保存的更改。"
                : $"当前打开的 {dirtyScenes.Count} 个场景有未保存的更改。";

            int choice = EditorUtility.DisplayDialogComplex(
                "切换场景",
                $"{sceneMessage}\n\n是否保存后再切换场景？选择“不保存”会放弃这些更改并继续跳转。",
                "保存",
                "不保存",
                "取消");

            switch (choice)
            {
                case 0:
                    return SaveDirtyScenes(dirtyScenes);
                case 1:
                    return true;
                default:
                    return false;
            }
        }

        public static string FindScenePathInFolder(string sceneName, string folderPath)
        {
            if (string.IsNullOrEmpty(sceneName) || string.IsNullOrEmpty(folderPath))
            {
                return null;
            }

            string normalizedFolderPath = folderPath.Replace('\\', '/').TrimEnd('/');
            string directScenePath = $"{normalizedFolderPath}/{sceneName}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(directScenePath) != null)
            {
                return directScenePath;
            }

            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { normalizedFolderPath });
            foreach (string sceneGuid in sceneGuids)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuid);
                string fileName = Path.GetFileNameWithoutExtension(scenePath);
                if (string.Equals(fileName, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return scenePath;
                }
            }

            return null;
        }

        private static List<Scene> GetDirtyLoadedScenes()
        {
            var dirtyScenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.isDirty)
                {
                    dirtyScenes.Add(scene);
                }
            }

            return dirtyScenes;
        }

        private static bool SaveDirtyScenes(List<Scene> dirtyScenes)
        {
            foreach (Scene scene in dirtyScenes)
            {
                if (!EditorSceneManager.SaveScene(scene))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetSceneDisplayName(Scene scene)
        {
            return string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
        }
    }
}
