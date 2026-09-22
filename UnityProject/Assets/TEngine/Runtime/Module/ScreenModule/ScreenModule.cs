using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 窗口布局控制模块（多屏支持）。
    /// <para>位于 AOT 程序集 TEngine.Runtime，所有原生互操作在 IL2CPP/AOT 下编译，不进入 HybridCLR 解释域。</para>
    /// <para>基于 TEngine <see cref="Module"/> 生命周期，通过 <c>GameModule.Screen</c> 访问。</para>
    /// <para>仅 Windows Standalone 打包后实际生效；Editor 及其他平台调用仅输出警告，不执行任何窗口操作。</para>
    /// <para>配置顶层 <c>Enabled=false</c> 可整体禁用（见 <see cref="ScreenConfig.Enabled"/>）。</para>
    /// </summary>
    public sealed class ScreenModule : Module, IScreenModule
    {
        /// <summary>
        /// 配置文件名（不含扩展名），对应 StreamingAssets/Configs/ScreenConfig.toml 或 .json。
        /// </summary>
        private const string CONFIG_NAME = "ScreenConfig";

        /// <summary>
        /// 等待副屏窗口创建的帧数。
        /// </summary>
        private const int WAIT_FRAMES = 3;

        /// <summary>
        /// 当前生效配置。
        /// </summary>
        private ScreenConfig _config;

        /// <summary>
        /// DisplayIndex -> 窗口句柄缓存。
        /// </summary>
        private readonly Dictionary<int, IntPtr> _displayHandles = new Dictionary<int, IntPtr>();

        /// <summary>
        /// 是否运行在受支持的平台。
        /// </summary>
        public bool IsSupported => WindowsScreenNative.IsSupported;

        /// <summary>
        /// 模块初始化：保持空实现，窗口布局仅由显式 API 调用触发。
        /// </summary>
        public override void OnInit()
        {
        }

        /// <summary>
        /// 模块关闭，清理缓存。
        /// </summary>
        public override void Shutdown()
        {
            _config = null;
            _displayHandles.Clear();
        }

        /// <summary>
        /// 注入配置（热更层可显式传入；传 null 时构造主显示器默认配置）。
        /// </summary>
        public void SetConfig(ScreenConfig config)
        {
            if (config != null && config.Screens != null && config.Screens.Count > 0)
            {
                _config = config;
                Log.Info($"[ScreenModule] 已注入外部配置，屏幕数={_config.Screens.Count}，Enabled={_config.Enabled}。");
            }
            else
            {
                _config = BuildDefaultConfig();
                Log.Warning("[ScreenModule] 注入的配置为空，已回退主显示器默认配置。");
            }
        }

        /// <summary>
        /// 按配置应用全部屏幕布局。
        /// </summary>
        public void ApplyAll()
        {
            if (!EnsureEnabled())
            {
                return;
            }

            ApplyAllAsync().Forget();
        }

        /// <summary>
        /// 重新应用指定 Display 的窗口布局。
        /// </summary>
        public void ApplyScreen(int displayIndex)
        {
            if (!EnsureEnabled())
            {
                return;
            }

            ScreenSetting setting = FindSetting(displayIndex);
            if (setting == null)
            {
                Log.Warning($"[ScreenModule] 未找到 DisplayIndex={displayIndex} 的配置，跳过。");
                return;
            }

            if (!TryGetHandle(displayIndex, out IntPtr hWnd))
            {
                RefreshHandles();
                _displayHandles.TryGetValue(displayIndex, out hWnd);
            }

            ApplySetting(setting, hWnd);
        }

        /// <summary>
        /// 设置指定 Display 窗口的置顶状态。
        /// </summary>
        public void SetTopmost(int displayIndex, bool topmost)
        {
            if (!EnsureEnabled())
            {
                return;
            }

            if (_config == null)
            {
                LoadConfig();
            }

            if (!TryGetHandle(displayIndex, out IntPtr hWnd))
            {
                RefreshHandles();
                _displayHandles.TryGetValue(displayIndex, out hWnd);
            }

            if (hWnd == IntPtr.Zero)
            {
                Log.Warning($"[ScreenModule] 未找到 DisplayIndex={displayIndex} 的窗口句柄，无法设置置顶。");
                return;
            }

            bool ok = WindowsScreenNative.SetTopmost(hWnd, topmost);
            Log.Info($"[ScreenModule] SetTopmost：Display={displayIndex}, hWnd={hWnd}, topmost={topmost}, 结果={ok}。");
        }

        /// <summary>
        /// 读取配置；未配置或为空时构造主显示器默认配置并输出警告。
        /// </summary>
        private void LoadConfig()
        {
            IRuntimeConfigModule runtimeConfig = ModuleSystem.GetModule<IRuntimeConfigModule>();
            if (runtimeConfig != null
                && runtimeConfig.TryGet(out ScreenConfig config, CONFIG_NAME)
                && config != null
                && config.Screens != null
                && config.Screens.Count > 0)
            {
                _config = config;
                Log.Info($"[ScreenModule] 已从 ScreenConfig 读取配置，屏幕数={_config.Screens.Count}。");
                for (int i = 0; i < _config.Screens.Count; i++)
                {
                    Log.Info($"[ScreenModule]   配置[{i}] {_config.Screens[i]}");
                }
                return;
            }

            Log.Warning("[ScreenModule] 未找到有效的 ScreenConfig 配置，使用主显示器默认分辨率（铺满主屏、保留边框、不置顶）。");
            _config = BuildDefaultConfig();
        }

        /// <summary>
        /// 构造主显示器默认配置：使用当前主显示器分辨率铺满主屏。
        /// </summary>
        private static ScreenConfig BuildDefaultConfig()
        {
            Resolution res = Screen.currentResolution;
            Log.Info($"[ScreenModule] 主显示器默认分辨率 {res.width}x{res.height}。");
            return new ScreenConfig
            {
                Screens = new List<ScreenSetting>
                {
                    new ScreenSetting
                    {
                        DisplayIndex = 0,
                        Activate = true,
                        X = 0,
                        Y = 0,
                        Width = res.width,
                        Height = res.height,
                        Topmost = false,
                        Borderless = false,
                    },
                },
            };
        }

        /// <summary>
        /// 异步应用全部布局：先切窗口化并激活副屏 Display，等待窗口创建/模式切换生效后发现句柄并逐个应用。
        /// </summary>
        private async UniTaskVoid ApplyAllAsync()
        {
            if (_config == null)
            {
                LoadConfig();
            }

            Log.Info("[ScreenModule] ApplyAll：开始应用窗口布局。");

            // 全屏模式下 SetWindowPos 会被 Unity/OS 覆盖而看不到效果，主窗口需先切到窗口化。
            // 这是打包后“位置/大小不生效”的常见根因（默认 fullscreenMode 多为 FullScreenWindow）。
            EnsureWindowedMode();

            ActivateDisplays();

            // 等待若干帧，确保 OS 完成副屏窗口创建、且窗口化模式切换生效（SetResolution 帧末生效）
            await UniTask.DelayFrame(WAIT_FRAMES);

            RefreshHandles();

            int applied = 0;
            foreach (ScreenSetting setting in _config.Screens)
            {
                _displayHandles.TryGetValue(setting.DisplayIndex, out IntPtr hWnd);
                if (ApplySetting(setting, hWnd))
                {
                    applied++;
                }
            }

            Log.Info($"[ScreenModule] ApplyAll 完成：成功应用 {applied}/{_config.Screens.Count} 个屏幕布局。");
        }

        /// <summary>
        /// 若当前为全屏模式，切换为窗口化，以便窗口位置/大小生效。
        /// 取主屏（DisplayIndex=0）配置尺寸；无主屏配置则用当前分辨率。
        /// </summary>
        private void EnsureWindowedMode()
        {
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                return;
            }

            ScreenSetting main = FindSetting(0);
            int w = main != null ? main.Width : Screen.currentResolution.width;
            int h = main != null ? main.Height : Screen.currentResolution.height;

            Log.Info($"[ScreenModule] 当前为 {Screen.fullScreenMode}，切换为 Windowed（{w}x{h}）以便应用窗口布局。");
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
        }

        /// <summary>
        /// 按配置激活需要的副屏 Display（主屏无需激活）。
        /// </summary>
        private void ActivateDisplays()
        {
            Log.Info($"[ScreenModule] 当前可用显示器数量 Display.displays.Length={Display.displays.Length}。");

            foreach (ScreenSetting setting in _config.Screens)
            {
                if (setting.DisplayIndex <= 0 || !setting.Activate)
                {
                    continue;
                }

                if (setting.DisplayIndex >= Display.displays.Length)
                {
                    Log.Warning($"[ScreenModule] DisplayIndex={setting.DisplayIndex} 超出可用显示器数量（{Display.displays.Length}），跳过激活。");
                    continue;
                }

                Display display = Display.displays[setting.DisplayIndex];
                if (!display.active)
                {
                    display.Activate(setting.Width, setting.Height, new RefreshRate { numerator = 60, denominator = 1 });
                    Log.Info($"[ScreenModule] 已激活 Display={setting.DisplayIndex}（{setting.Width}x{setting.Height}@60）。");
                }
                else
                {
                    Log.Info($"[ScreenModule] Display={setting.DisplayIndex} 已处于激活状态。");
                }
            }
        }

        /// <summary>
        /// 发现 Unity 窗口句柄并建立 DisplayIndex 映射。
        /// <para>主窗口（DisplayIndex=0）= 当前激活窗口；副屏窗口优先按显示器几何配对（见 <see cref="AssignSecondaryHandles"/>），
        /// 几何信息不可用时回退为枚举顺序配对。</para>
        /// </summary>
        private void RefreshHandles()
        {
            _displayHandles.Clear();

            List<IntPtr> windows = WindowsScreenNative.FindUnityWindows();
            int totalFound = windows.Count;
            IntPtr mainWindow = WindowsScreenNative.GetMainWindow();
            Log.Info($"[ScreenModule] 窗口发现：FindUnityWindows 命中 {totalFound} 个，GetActiveWindow={mainWindow}。");

            if (mainWindow != IntPtr.Zero && windows.Contains(mainWindow))
            {
                _displayHandles[0] = mainWindow;
                windows.Remove(mainWindow);
            }
            else if (windows.Count > 0)
            {
                // GetActiveWindow 不在枚举结果内（少见），退化为取第一个枚举窗口作为主窗口
                _displayHandles[0] = windows[0];
                Log.Warning($"[ScreenModule] GetActiveWindow 未在枚举结果中，退化取首个枚举窗口 {windows[0]} 作为主窗口。");
                windows.RemoveAt(0);
            }
            else
            {
                Log.Warning("[ScreenModule] 未发现任何 Unity 窗口，布局将无法应用。");
            }

            // 收集已激活的副屏配置（DisplayIndex>0），按索引升序，与剩余窗口依次配对
            List<int> secondaryIndices = new List<int>();
            foreach (ScreenSetting setting in _config.Screens)
            {
                if (setting.DisplayIndex > 0 && setting.Activate && !secondaryIndices.Contains(setting.DisplayIndex))
                {
                    secondaryIndices.Add(setting.DisplayIndex);
                }
            }
            secondaryIndices.Sort();

            AssignSecondaryHandles(secondaryIndices, windows);

            // 输出最终映射
            foreach (KeyValuePair<int, IntPtr> kv in _displayHandles)
            {
                Log.Info($"[ScreenModule]   映射 Display={kv.Key} -> hWnd={kv.Value}。");
            }

            int unassigned = 0;
            foreach (int displayIndex in secondaryIndices)
            {
                if (!_displayHandles.ContainsKey(displayIndex))
                {
                    unassigned++;
                }
            }

            if (unassigned > 0)
            {
                Log.Warning($"[ScreenModule] {unassigned} 个副屏未分配到窗口（候选窗口 {windows.Count} 个）。");
            }
        }

        /// <summary>
        /// 为副屏 DisplayIndex 分配窗口句柄。
        /// <para>优先按显示器几何配对：查询每个候选窗口所在显示器矩形，与配置的 X/Y 所在显示器匹配则配对；
        /// 几何查询失败或无匹配时回退为按枚举顺序配对。</para>
        /// </summary>
        /// <param name="secondaryIndices">已激活的副屏 DisplayIndex（升序）。</param>
        /// <param name="windows">主窗口之外剩余的候选窗口句柄。</param>
        private void AssignSecondaryHandles(List<int> secondaryIndices, List<IntPtr> windows)
        {
            if (secondaryIndices.Count == 0 || windows.Count == 0)
            {
                return;
            }

            // 计算每个副屏配置的目标显示器原点（配置 X/Y 即窗口摆放位置，其所在显示器以包含该点的矩形判定）
            // 对每个候选窗口查询其当前所在显示器原点，若与某副屏配置的目标显示器原点一致则配对。
            List<IntPtr> remaining = new List<IntPtr>(windows);
            bool anyGeometryMatch = false;

            foreach (int displayIndex in secondaryIndices)
            {
                if (_displayHandles.ContainsKey(displayIndex))
                {
                    continue;
                }

                ScreenSetting setting = FindSetting(displayIndex);
                if (setting == null)
                {
                    continue;
                }

                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    IntPtr hWnd = remaining[i];
                    if (!WindowsScreenNative.TryGetMonitorRect(hWnd, out int mx, out int my, out int mw, out int mh))
                    {
                        continue;
                    }

                    // 配置的目标点（X,Y）落在该窗口当前所在显示器矩形内 => 几何匹配
                    bool pointInside = setting.X >= mx && setting.X < mx + mw && setting.Y >= my && setting.Y < my + mh;
                    if (!pointInside)
                    {
                        continue;
                    }

                    _displayHandles[displayIndex] = hWnd;
                    remaining.RemoveAt(i);
                    anyGeometryMatch = true;
                    Log.Info($"[ScreenModule] 几何配对：Display={displayIndex} -> hWnd={hWnd}（目标点({setting.X},{setting.Y}) 位于显示器({mx},{my},{mw}x{mh})）。");
                    break;
                }
            }

            if (anyGeometryMatch)
            {
                // 几何已配对部分；剩余副屏若无几何匹配窗口，用剩余窗口按顺序补齐
                foreach (int displayIndex in secondaryIndices)
                {
                    if (_displayHandles.ContainsKey(displayIndex) || remaining.Count == 0)
                    {
                        continue;
                    }

                    _displayHandles[displayIndex] = remaining[0];
                    remaining.RemoveAt(0);
                    Log.Warning($"[ScreenModule] Display={displayIndex} 无几何匹配窗口，按剩余顺序配对。");
                }

                return;
            }

            // 完全无几何信息（查询全部失败）：回退为按枚举顺序配对
            Log.Warning("[ScreenModule] 显示器几何查询无有效结果，回退为按枚举顺序配对副屏窗口。");
            for (int i = 0; i < secondaryIndices.Count && i < windows.Count; i++)
            {
                _displayHandles[secondaryIndices[i]] = windows[i];
            }
        }

        /// <summary>
        /// 对单个窗口应用一条配置。
        /// </summary>
        /// <returns>是否成功应用。</returns>
        private bool ApplySetting(ScreenSetting setting, IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                Log.Warning($"[ScreenModule] DisplayIndex={setting.DisplayIndex} 未找到窗口句柄，跳过布局应用。");
                return false;
            }

            bool ok = WindowsScreenNative.SetWindowLayout(hWnd, setting.X, setting.Y, setting.Width, setting.Height, setting.Topmost, setting.Borderless);
            if (ok)
            {
                Log.Info($"[ScreenModule] 已应用：Display={setting.DisplayIndex}, hWnd={hWnd}, Rect=({setting.X},{setting.Y},{setting.Width}x{setting.Height}), Topmost={setting.Topmost}, Borderless={setting.Borderless}。");
            }
            else
            {
                Log.Warning($"[ScreenModule] DisplayIndex={setting.DisplayIndex} 窗口布局应用失败（hWnd={hWnd}）。");
            }

            return ok;
        }

        /// <summary>
        /// 查找指定 DisplayIndex 的配置。
        /// </summary>
        private ScreenSetting FindSetting(int displayIndex)
        {
            if (_config == null || _config.Screens == null)
            {
                return null;
            }

            foreach (ScreenSetting setting in _config.Screens)
            {
                if (setting.DisplayIndex == displayIndex)
                {
                    return setting;
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试取已缓存的有效句柄。
        /// </summary>
        private bool TryGetHandle(int displayIndex, out IntPtr hWnd)
        {
            return _displayHandles.TryGetValue(displayIndex, out hWnd) && hWnd != IntPtr.Zero;
        }

        /// <summary>
        /// 统一前置检查：平台支持且配置开关打开才允许执行布局操作。
        /// <para>配置未加载时先按需加载（Enabled=false 时直接短路）。</para>
        /// </summary>
        private bool EnsureEnabled()
        {
            if (!IsSupported)
            {
                WarnUnsupported();
                return false;
            }

            if (_config == null)
            {
                LoadConfig();
            }

            if (!_config.Enabled)
            {
                Log.Info("[ScreenModule] 配置 Enabled=false，窗口布局已被禁用，调用被忽略。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 非支持平台统一警告。
        /// </summary>
        private static void WarnUnsupported()
        {
            Log.Warning("[ScreenModule] 当前平台不支持窗口布局控制，调用被忽略（仅 Windows Standalone 打包后生效）。");
        }
    }
}
