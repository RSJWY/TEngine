namespace TEngine
{
    /// <summary>
    /// 窗口布局控制模块接口（多屏支持）。
    /// <para>位于 AOT 程序集 TEngine.Runtime，热更层通过 GameModule.Screen 访问。</para>
    /// <para>仅 Windows Standalone 打包后实际生效；Editor 及其他平台调用仅输出警告，不执行任何操作。</para>
    /// <para>配置 ScreenConfig 顶层 Enabled=false 时所有布局 API 均被禁用。</para>
    /// </summary>
    public interface IScreenModule
    {
    /// <summary>
    /// 是否运行在受支持的平台（仅 Windows Standalone 打包后生效；Editor 及其他平台返回 false）。
    /// </summary>
    bool IsSupported { get; }

        /// <summary>
        /// 设置配置（热更层从 RuntimeConfigModule 读出后注入）。传 null 时使用主显示器默认配置。
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

        /// <summary>
        /// 直接以参数设置指定 Display 窗口的布局（不依赖 ScreenConfig）。
        /// <para>用于运行时动态摆窗，内部仍走平台守卫与句柄查找。</para>
        /// </summary>
        /// <param name="displayIndex">Unity Display 索引。</param>
        /// <param name="x">窗口 X 坐标（桌面坐标系）。</param>
        /// <param name="y">窗口 Y 坐标（桌面坐标系）。</param>
        /// <param name="width">窗口宽度（像素）。</param>
        /// <param name="height">窗口高度（像素）。</param>
        /// <param name="topmost">是否置顶。</param>
        /// <param name="borderless">是否去除标题栏与边框。</param>
        void SetLayout(int displayIndex, int x, int y, int width, int height, bool topmost, bool borderless);

        /// <summary>
        /// 将指定 Display 窗口提到前台。
        /// </summary>
        /// <param name="displayIndex">Unity Display 索引。</param>
        void BringToFront(int displayIndex);

        /// <summary>
        /// 设置指定 Display 窗口的标题。
        /// </summary>
        /// <param name="displayIndex">Unity Display 索引。</param>
        /// <param name="title">窗口标题。</param>
        void SetTitle(int displayIndex, string title);
    }
}
