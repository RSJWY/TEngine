namespace TEngine
{
    /// <summary>
    /// 窗口布局控制模块接口（多屏支持）。
    /// <para>位于 AOT 程序集 TEngine.Runtime，热更层通过 GameModule.Screen 访问。</para>
    /// <para>仅 Windows Standalone 平台实际生效；其他平台调用仅输出警告，不执行任何操作。</para>
    /// </summary>
    public interface IScreenModule
    {
        /// <summary>
        /// 是否运行在受支持的平台（Windows Standalone / Editor）。
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// 设置配置（热更层从 JsonConfigModule 读出后注入）。传 null 时使用主显示器默认配置。
        /// </summary>
        /// <param name="config">窗口布局配置。</param>
        void SetConfig(ScreenConfig config);

        /// <summary>
        /// 按配置应用全部屏幕布局（异步：会先激活副屏并等待窗口创建）。
        /// </summary>
        void ApplyAll();

        /// <summary>
        /// 重新应用指定 Display 的窗口布局。
        /// </summary>
        /// <param name="displayIndex">Unity Display 索引。</param>
        void ApplyScreen(int displayIndex);

        /// <summary>
        /// 设置指定 Display 窗口的置顶状态。
        /// </summary>
        /// <param name="displayIndex">Unity Display 索引。</param>
        /// <param name="topmost">是否置顶。</param>
        void SetTopmost(int displayIndex, bool topmost);
    }
}
