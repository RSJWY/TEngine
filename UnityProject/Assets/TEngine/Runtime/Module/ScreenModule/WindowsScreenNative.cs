#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace TEngine
{
    /// <summary>
    /// Windows 窗口控制底层封装（user32 / kernel32）。
    /// <para>位于 AOT 程序集 TEngine.Runtime，所有 P/Invoke 在 IL2CPP/AOT 下直接编译，
    /// 不进入 HybridCLR 解释域，避免热更程序集中调用原生互操作不稳定的问题。</para>
    /// <para>仅 Windows Standalone 与 Editor 下走真实实现；其他平台所有方法返回安全默认值。</para>
    /// <para>窗口枚举使用 <c>FindWindowEx</c> 循环 + 进程过滤，不使用任何 native→managed 回调委托。</para>
    /// </summary>
    public static class WindowsScreenNative
    {
        /// <summary>
        /// 当前平台是否支持窗口控制。
        /// </summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR

        #region 常量

        /// <summary>窗口样式索引。</summary>
        private const int GWL_STYLE = -16;

        /// <summary>标题栏样式位。</summary>
        private const long WS_CAPTION = 0x00C00000L;

        /// <summary>可调整大小边框样式位。</summary>
        private const long WS_THICKFRAME = 0x00040000L;

        /// <summary>显示窗口标志。</summary>
        private const uint SWP_SHOWWINDOW = 0x0040;

        /// <summary>应用新窗口样式所需标志。</summary>
        private const uint SWP_FRAMECHANGED = 0x0020;

        /// <summary>不移动位置。</summary>
        private const uint SWP_NOMOVE = 0x0002;

        /// <summary>不改变大小。</summary>
        private const uint SWP_NOSIZE = 0x0001;

        /// <summary>置顶句柄。</summary>
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        /// <summary>取消置顶句柄。</summary>
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        /// <summary>Unity 窗口类名。</summary>
        private const string UNITY_WINDOW_CLASS = "UnityWndClass";

        #endregion

        #region P/Invoke 声明（全部正向调用）

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        #endregion

        /// <summary>
        /// 取窗口样式（兼容 32/64 位）。
        /// </summary>
        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        /// <summary>
        /// 设置窗口样式（兼容 32/64 位）。
        /// </summary>
        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        /// <summary>
        /// 枚举当前进程下全部 Unity 窗口句柄。
        /// <para>用 FindWindowEx(parent=Zero) 循环枚举顶层 UnityWndClass 窗口，按进程 ID 过滤，无回调。</para>
        /// </summary>
        /// <returns>Unity 窗口句柄列表（可能为空）。</returns>
        public static List<IntPtr> FindUnityWindows()
        {
            List<IntPtr> result = new List<IntPtr>();
            uint currentProcessId = GetCurrentProcessId();

            IntPtr hWnd = IntPtr.Zero;
            int guard = 0;
            while (guard < 4096)
            {
                guard++;
                hWnd = FindWindowEx(IntPtr.Zero, hWnd, UNITY_WINDOW_CLASS, null);
                if (hWnd == IntPtr.Zero)
                {
                    break;
                }

                GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == currentProcessId && !result.Contains(hWnd))
                {
                    result.Add(hWnd);
                }
            }

            return result;
        }

        /// <summary>
        /// 获取当前激活窗口句柄（主窗口）。
        /// </summary>
        public static IntPtr GetMainWindow()
        {
            return GetActiveWindow();
        }

        /// <summary>
        /// 设置窗口位置、大小、置顶与边框。
        /// </summary>
        /// <returns>是否成功。</returns>
        public static bool SetWindowLayout(IntPtr hWnd, int x, int y, int width, int height, bool topmost, bool borderless)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            if (borderless)
            {
                long style = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();
                style &= ~(WS_CAPTION | WS_THICKFRAME);
                SetWindowLongPtr(hWnd, GWL_STYLE, new IntPtr(style));
            }

            IntPtr insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
            bool ok = SetWindowPos(hWnd, insertAfter, x, y, width, height, SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                Log.Warning($"[WindowsScreenNative] SetWindowPos 失败，hWnd={hWnd}, Win32Error={err}");
            }

            return ok;
        }

        /// <summary>
        /// 单独设置窗口置顶状态（不改变位置与大小）。
        /// </summary>
        public static bool SetTopmost(IntPtr hWnd, bool topmost)
        {
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
            return SetWindowPos(hWnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }

        /// <summary>
        /// 将窗口提到前台。
        /// </summary>
        public static bool BringToFront(IntPtr hWnd)
        {
            return hWnd != IntPtr.Zero && SetForegroundWindow(hWnd);
        }

        /// <summary>
        /// 设置窗口标题。
        /// </summary>
        public static bool SetTitle(IntPtr hWnd, string title)
        {
            return hWnd != IntPtr.Zero && !string.IsNullOrEmpty(title) && SetWindowText(hWnd, title);
        }

#else

        /// <summary>非 Windows 平台：返回空列表。</summary>
        public static List<IntPtr> FindUnityWindows()
        {
            return new List<IntPtr>();
        }

        /// <summary>非 Windows 平台：返回 Zero。</summary>
        public static IntPtr GetMainWindow()
        {
            return IntPtr.Zero;
        }

        /// <summary>非 Windows 平台：空操作。</summary>
        public static bool SetWindowLayout(IntPtr hWnd, int x, int y, int width, int height, bool topmost, bool borderless)
        {
            return false;
        }

        /// <summary>非 Windows 平台：空操作。</summary>
        public static bool SetTopmost(IntPtr hWnd, bool topmost)
        {
            return false;
        }

        /// <summary>非 Windows 平台：空操作。</summary>
        public static bool BringToFront(IntPtr hWnd)
        {
            return false;
        }

        /// <summary>非 Windows 平台：空操作。</summary>
        public static bool SetTitle(IntPtr hWnd, string title)
        {
            return false;
        }

#endif
    }
}
