using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 窗口布局配置模型（多屏支持）。
    /// 对应 StreamingAssets/Configs/ScreenConfig.json。
    /// </summary>
    [Serializable]
    public sealed class ScreenConfig
    {
        /// <summary>
        /// 模块初始化时是否自动应用配置。
        /// </summary>
        public bool ApplyOnInit = true;

        /// <summary>
        /// 多屏窗口配置列表。
        /// </summary>
        public List<ScreenSetting> Screens = new List<ScreenSetting>();
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
        public int DisplayIndex = 0;

        /// <summary>
        /// 是否激活该 Display（副屏必须激活才会创建窗口）。
        /// </summary>
        public bool Activate = true;

        /// <summary>
        /// 窗口 X 坐标（屏幕坐标系）。
        /// </summary>
        public int X = 0;

        /// <summary>
        /// 窗口 Y 坐标（屏幕坐标系）。
        /// </summary>
        public int Y = 0;

        /// <summary>
        /// 窗口宽度（像素）。
        /// </summary>
        public int Width = 1920;

        /// <summary>
        /// 窗口高度（像素）。
        /// </summary>
        public int Height = 1080;

        /// <summary>
        /// 强制置顶（HWND_TOPMOST）。
        /// </summary>
        public bool Topmost = false;

        /// <summary>
        /// 去除窗口边框与标题栏（WS_CAPTION | WS_THICKFRAME）。
        /// </summary>
        public bool Borderless = false;

        /// <summary>
        /// 诊断用文本。
        /// </summary>
        public override string ToString()
        {
            return $"Display={DisplayIndex}, Activate={Activate}, Rect=({X},{Y},{Width}x{Height}), Topmost={Topmost}, Borderless={Borderless}";
        }
    }
}
