using System.IO;
using YooAsset.Editor;

namespace TEngine.Editor.ReleaseTools
{
    /// <summary>
    /// 仅收集 .bytes 文件的过滤规则（收集器级 FilterRule）。
    /// 用于 CodePackage 的 AOT/HotDll/PDB/Obfuz 等收集器：
    /// 只放行 .bytes（dll.bytes / pdb.bytes / json.bytes / 密钥.bytes），
    /// 排除 .asset（ScriptableObject 编辑表面）、.meta、.dll 原文件等。
    /// </summary>
    [DisplayName("收集 .bytes 文件")]
    public class CollectBytesOnly : IAssetFilterRule
    {
        /// <inheritdoc/>
        public string FindAssetType
        {
            get { return EAssetFilterType.All.ToString(); }
        }

        /// <inheritdoc/>
        public bool IsCollectAsset(AssetFilterRuleData data)
        {
            return Path.GetExtension(data.AssetPath) == ".bytes";
        }
    }
}
