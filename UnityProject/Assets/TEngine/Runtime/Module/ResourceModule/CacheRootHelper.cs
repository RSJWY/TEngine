using System.IO;
using UnityEngine;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// YooAsset 缓存根目录桥接类。
    /// YooAssetConfiguration.GetDefaultCacheRoot() 为 internal，此处用公开接口按平台复刻同等逻辑。
    /// </summary>
    public static class CacheRootHelper
    {
        /// <summary>
        /// 沙盒清单目录名（对应 YooAsset 内部 SandboxFileSystemConsts.ManifestFilesFolderName）。
        /// </summary>
        public const string ManifestFolderName = "ManifestFiles";

        /// <summary>
        /// 获取平台默认缓存根目录（与 YooAsset 内部 GetDefaultCacheRoot 逻辑一致）。
        /// </summary>
        public static string GetDefaultCacheRoot()
        {
            string yooFolderName = YooAssetConfiguration.GetYooFolderName();

#if UNITY_EDITOR
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectPath))
                throw new System.InvalidOperationException("Could not determine project root path from Application.dataPath.");
            string libraryPath = Path.Combine(projectPath, "Library").Replace('\\', '/');
            return string.IsNullOrEmpty(yooFolderName)
                ? libraryPath
                : $"{libraryPath}/{yooFolderName}";
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            return string.IsNullOrEmpty(yooFolderName)
                ? Application.dataPath
                : $"{Application.dataPath}/{yooFolderName}";
#else
            return string.IsNullOrEmpty(yooFolderName)
                ? Application.persistentDataPath
                : $"{Application.persistentDataPath}/{yooFolderName}";
#endif
        }
    }
}
