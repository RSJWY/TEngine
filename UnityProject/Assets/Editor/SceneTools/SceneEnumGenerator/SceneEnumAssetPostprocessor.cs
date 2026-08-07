using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TEngine.SceneTools
{
    /// <summary>
    /// 监听业务场景资源增删改，提示打开 <see cref="SceneEnumConfig"/> 同步。
    /// 不自动生成（避免偷偷改代码），仅 Console 提醒。
    /// </summary>
    public class SceneEnumAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // 优先用 YooAsset Scenes Group 收集目录判断；读不到则回退默认目录
            List<string> sceneFolders = YooAssetCollectorReader.GetCollectPaths();
            if (sceneFolders.Count == 0)
            {
                sceneFolders.Add(SceneEnumConfig.DefaultSceneFolder);
            }

            if (HasBusinessSceneChange(importedAssets, sceneFolders) ||
                HasBusinessSceneChange(deletedAssets, sceneFolders) ||
                HasBusinessSceneChange(movedAssets, sceneFolders) ||
                HasBusinessSceneChange(movedFromAssetPaths, sceneFolders))
            {
                Debug.Log("[场景枚举] 业务场景资源已变更，请打开 SceneEnumConfig（菜单 TEngine > 场景枚举配置）点击「同步场景资源」并「生成枚举代码」。");
            }
        }

        private static bool HasBusinessSceneChange(string[] paths, List<string> sceneFolders)
        {
            if (paths == null) return false;
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".unity")) continue;
                foreach (string folder in sceneFolders)
                {
                    if (path.StartsWith(folder + "/") || path == folder)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
