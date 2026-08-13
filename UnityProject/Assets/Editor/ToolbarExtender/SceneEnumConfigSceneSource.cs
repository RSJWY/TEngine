using System.Collections.Generic;
using System.IO;
using UnityEditor;
using TEngine.SceneTools;

namespace TEngine
{
    /// <summary>
    /// 从 <see cref="SceneEnumConfig"/> 读取场景列表，供工具栏「注册场景」分组使用。
    /// <para>配置资产缺失时返回空列表（优雅降级，不报错）。</para>
    /// </summary>
    internal static class SceneEnumConfigSceneSource
    {
        private const string ConfigAssetPath = "Assets/Resources/SceneEnumConfig.asset";

        /// <summary>
        /// 读取配置中 Active==true 且 SceneAsset 非空的场景条目。
        /// </summary>
        /// <returns>(sceneName, scenePath) 列表；sceneName 优先 EnumName，DisplayName 非空时显示 "EnumName (DisplayName)"。</returns>
        public static List<(string sceneName, string scenePath)> GetConfiguredScenes()
        {
            var result = new List<(string sceneName, string scenePath)>();

            var config = AssetDatabase.LoadAssetAtPath<SceneEnumConfig>(ConfigAssetPath);
            if (config == null || config.Scenes == null)
            {
                return result;
            }

            foreach (var entry in config.Scenes)
            {
                if (!entry.Active || entry.SceneAsset == null)
                {
                    continue;
                }

                string scenePath = AssetDatabase.GetAssetPath(entry.SceneAsset);
                if (string.IsNullOrEmpty(scenePath))
                {
                    continue;
                }

                string sceneName = string.IsNullOrEmpty(entry.EnumName)
                    ? Path.GetFileNameWithoutExtension(scenePath)
                    : entry.EnumName;

                if (!string.IsNullOrEmpty(entry.DisplayName))
                {
                    sceneName = $"{sceneName} ({entry.DisplayName})";
                }

                result.Add((sceneName, scenePath));
            }

            return result;
        }
    }
}
