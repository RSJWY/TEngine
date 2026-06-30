using System;

namespace TEngine
{
    /// <summary>
    /// 运行时部署配置。用于打包后现场覆盖资源服务器地址（StreamingAssets/Configs/DeployConfig.toml 或 .json）。
    /// 字段为空时回退 UpdateSetting 的 Inspector 默认值。
    /// </summary>
    [Serializable]
    public sealed class DeployConfig
    {
        /// <summary>
        /// 资源服务器地址（覆盖 UpdateSetting.ResDownLoadPath）。
        /// </summary>
        public string ResDownloadPath;

        /// <summary>
        /// 资源服务备用地址（覆盖 UpdateSetting.FallbackResDownLoadPath）。
        /// </summary>
        public string FallbackResDownloadPath;

        /// <summary>
        /// 调试器激活策略（覆盖 Debugger 组件 Inspector 的 activeWindow 字段）。
        /// 取值：AlwaysOpen / OnlyOpenWhenDevelopment / OnlyOpenInEditor / AlwaysClose。
        /// 留空或无法解析时回退 Inspector 配置。
        /// </summary>
        public string DebuggerActiveWindow;
    }
}
