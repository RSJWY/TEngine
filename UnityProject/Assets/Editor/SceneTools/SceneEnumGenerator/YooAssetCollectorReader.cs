using System.Collections.Generic;
using UnityEditor;
using YooAsset.Editor;

namespace TEngine.SceneTools
{
    /// <summary>
    /// 读取 YooAsset <see cref="BundleCollectorSetting"/> 中 Scenes Group 的收集目录。
    /// 用于联动资源打包配置，避免 <see cref="SceneEnumConfig"/> 与 YooAsset 配置脱节。
    /// </summary>
    public static class YooAssetCollectorReader
    {
        public const string DefaultScenesGroupName = "Scenes";

        /// <summary>
        /// 加载 BundleCollectorSetting 资产（项目内应有且仅有一份）。
        /// </summary>
        public static BundleCollectorSetting LoadSetting()
        {
            string[] guids = AssetDatabase.FindAssets("t:BundleCollectorSetting");
            if (guids.Length == 0) return null;
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<BundleCollectorSetting>(path);
        }

        /// <summary>
        /// 读取指定 Group（默认 Scenes）的所有 Collector 收集路径。
        /// </summary>
        public static List<string> GetCollectPaths(string groupName = DefaultScenesGroupName)
        {
            List<string> result = new List<string>();
            BundleCollectorSetting setting = LoadSetting();
            if (setting == null) return result;

            foreach (BundleCollectorPackage package in setting.Packages)
            {
                foreach (BundleCollectorGroup group in package.Groups)
                {
                    if (group.GroupName != groupName) continue;
                    foreach (BundleCollector collector in group.Collectors)
                    {
                        if (!string.IsNullOrEmpty(collector.CollectPath))
                        {
                            result.Add(collector.CollectPath);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 判断场景资源路径是否在指定 Group 收集范围内（打包后可加载）。
        /// </summary>
        public static bool IsSceneCollected(string sceneAssetPath, string groupName = DefaultScenesGroupName)
        {
            if (string.IsNullOrEmpty(sceneAssetPath)) return false;
            List<string> collectPaths = GetCollectPaths(groupName);
            foreach (string collectPath in collectPaths)
            {
                if (AssetDatabase.IsValidFolder(collectPath))
                {
                    if (sceneAssetPath.StartsWith(collectPath + "/")) return true;
                }
                else if (sceneAssetPath == collectPath)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
