using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// JSON 配置清单。声明 StreamingAssets/Configs 下需要加载的 JSON 文件列表。
    /// </summary>
    [Serializable]
    public sealed class JsonConfigManifest
    {
        public List<string> files = new List<string>();
    }
}
