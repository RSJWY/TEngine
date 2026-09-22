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
        /// <para>必须是属性：Tomlyn 反序列化不映射公有字段，字段会静默得到空列表。</para>
        /// </summary>
        public List<string> files { get; set; } = new List<string>();
    }
}
