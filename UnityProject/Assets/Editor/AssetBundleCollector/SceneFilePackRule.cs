using System;
using System.IO;
using YooAsset.Editor;

namespace GameEditor
{
    /// <summary>
    /// 场景专用打包规则：以场景文件自身路径作为资源包名。
    /// 避免与同名父级文件夹下普通资源（PackDirectory 取父目录名）产生 bundle 命名冲突，
    /// 从而规避 "contains mixed Asset and Scene types" 报错。
    /// 例如："Assets/AssetRaw/Scenes/机库.unity" --> "assets_assetraw_scenes_机库_scene.bundle"
    /// </summary>
    [DisplayName("资源包名: 场景文件路径(防混包)")]
    public class PackSceneFile : IBundlePackRule
    {
        public BundlePackRuleResult GetPackRuleResult(BundlePackRuleData data)
        {
            string assetPath = data.AssetPath;
            
            if (!assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                throw new System.Exception(
                    $"PackSceneFile 只能用于场景资源：{assetPath}！请确保仅仅收集场景文件！");
            }
            string dir = Path.GetDirectoryName(assetPath);
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string bundleName = $"{dir}/{fileName}_scene";
            return new BundlePackRuleResult(bundleName, DefaultBundlePackRule.AssetBundleFileExtension);
        }
    }
    /// <summary>
    /// 资源过滤规则：排除所有 Unity 场景文件。
    /// </summary>
    [DisplayName("仅排除场景（可用收集场景烘焙）")]
    public class FilterExcludeScene : IAssetFilterRule
    {
        /// <summary>
        /// 搜索全部资源类型。
        /// </summary>
        public string FindAssetType => EAssetFilterType.All.ToString();

        /// <summary>
        /// 检查是否收集资源。
        /// </summary>
        /// <returns>
        /// 非场景资源返回 true；
        /// .unity 场景返回 false。
        /// </returns>
        public bool IsCollectAsset(AssetFilterRuleData data)
        {
            return !data.AssetPath.EndsWith(
                ".unity",
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
