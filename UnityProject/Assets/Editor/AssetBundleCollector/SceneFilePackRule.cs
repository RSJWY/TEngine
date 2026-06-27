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
    public class PackSceneFile : IPackRule
    {
        public PackRuleResult GetPackRuleResult(PackRuleData data)
        {
            string assetPath = data.AssetPath;
            string dir = Path.GetDirectoryName(assetPath);
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string bundleName = $"{dir}/{fileName}_scene";
            return new PackRuleResult(bundleName, DefaultPackRule.AssetBundleFileExtension);
        }
    }
}
