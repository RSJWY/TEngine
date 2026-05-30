using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    [CreateAssetMenu(menuName = "TEngine/AOT Metadata Manifest", fileName = "AOTMetadataManifest")]
    public class AOTMetadataManifest : ScriptableObject
    {
        public const string ManifestAssetName = "AOTMetadataManifest";

        public List<string> AOTMetaAssemblies = new List<string>();
    }
}
