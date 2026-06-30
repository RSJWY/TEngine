using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 运行时配置清单。声明 StreamingAssets/Configs 下需要加载的配置文件列表。
    /// </summary>
    [Serializable]
    public sealed class RuntimeConfigManifest
    {
        /// <summary>
        /// 需要加载的配置文件名列表（相对 StreamingAssets/Configs）。
        /// </summary>
        public List<string> files = new List<string>();
    }
}
