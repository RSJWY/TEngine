using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 窗口布局配置模型（多屏支持）。
    /// 对应 StreamingAssets/Configs/ScreenConfig.toml 或 .json。
    /// </summary>
    [Serializable]
    public sealed class ScreenConfig
    {
        /// <summary>
        /// 总开关：false 时模块所有布局 API 均不执行。
        /// 用于单屏或不需要窗口控制的场景整体禁用，避免侵入默认窗口行为。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 多屏窗口配置列表。
        /// <para>必须是属性：Tomlyn 反序列化不映射公有字段，字段会静默得到空列表。</para>
        /// </summary>
        public List<ScreenSetting> Screens { get; set; } = new List<ScreenSetting>();
    }

    /// <summary>
    /// 单个屏幕窗口配置。
    /// </summary>
    [Serializable]
    public sealed class ScreenSetting
    {
        /// <summary>
        /// Unity Display 索引（0=主屏，1/2/...=副屏）。
        /// </summary>
        public int DisplayIndex { get; set; } = 0;

        /// <summary>
        /// 是否激活该 Display（副屏必须激活才会创建窗口）。
        /// </summary>
        public bool Activate { get; set; } = true;

        /// <summary>
        /// 窗口 X 坐标（屏幕坐标系）。
        /// </summary>
        public int X { get; set; } = 0;

        /// <summary>
        /// 窗口 Y 坐标（屏幕坐标系）。
        /// </summary>
        public int Y { get; set; } = 0;

        /// <summary>
        /// 窗口宽度（像素）。
        /// </summary>
        public int Width { get; set; } = 1920;

        /// <summary>
        /// 窗口高度（像素）。
        /// </summary>
        public int Height { get; set; } = 1080;

        /// <summary>
        /// 强制置顶（HWND_TOPMOST）。
        /// </summary>
        public bool Topmost { get; set; } = false;

        /// <summary>
        /// 去除窗口边框与标题栏（WS_CAPTION | WS_THICKFRAME）。
        /// </summary>
        public bool Borderless { get; set; } = false;

        /// <summary>
        /// 诊断用文本。
        /// </summary>
        public override string ToString()
        {
            return $"Display={DisplayIndex}, Activate={Activate}, Rect=({X},{Y},{Width}x{Height}), Topmost={Topmost}, Borderless={Borderless}";
        }
    }
}
