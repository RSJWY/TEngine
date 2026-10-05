using System;

namespace Launcher
{
    /// <summary>
    /// 专用服务器启动器。解析命令行参数，提供 DS 模式判断和启动场景/端口/地址指定。
    /// <para>用法示例：GameServer -batchmode -scene dedicated_server --port 7777 --address 0.0.0.0</para>
    /// <para>
    /// <b>口径说明</b>：<see cref="IsDedicatedServerBuild"/> 走 <c>#if UNITY_SERVER &amp;&amp; !UNITY_EDITOR</c>，
    /// 与 PurrNet <c>ApplicationContext.isServerBuild</c> 同口径但独立——
    /// 框架流程（Procedure 链/GameApp）只管"跳不跳 UI/语言/声音"，PurrNet 侧管 NM 是否 AutoStart。
    /// </para>
    /// </summary>
    public static class DedicatedServerLauncher
    {
        /// <summary>启动场景名参数（值如 <c>dedicated_server</c>）。</summary>
        public const string SceneArgumentName = "--scene";

        /// <summary>启动场景类型参数（值如 <c>MainScene</c>，对应 <see cref="GameLogic.SceneType"/> 枚举名）。</summary>
        public const string SceneTypeArgumentName = "--scene-type";

        /// <summary>服务器监听端口参数。</summary>
        public const string PortArgumentName = "--port";

        /// <summary>服务器绑定地址参数。</summary>
        public const string AddressArgumentName = "--address";

        /// <summary>
        /// 是否为专用服务器构建（UNITY_SERVER define 且非 Editor）。
        /// <para>严格口径：仅在真 DS 包返回 true。普通客户端即使 batchmode 也返回 false，
        /// 走完整客户端流程（普通客户端支持 host 模式，与 DS 代码路径不同）。</para>
        /// </summary>
        public static bool IsDedicatedServerBuild
        {
            get
            {
#if UNITY_SERVER && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 解析命令行指定的启动场景名。未传参时返回 null。
        /// <para>业务可据此选择走 <c>GameModule.Scene.LoadSceneAsync(location)</c>。</para>
        /// </summary>
        public static string ResolveStartupScene()
        {
            return ResolveArgument(SceneArgumentName);
        }

        /// <summary>
        /// 解析命令行指定的启动场景类型字符串。未传参时返回 null。
        /// <para>业务可按 <see cref="GameLogic.SceneType"/> TryParse 后走 <c>GameModule.GameScene.LoadScene(SceneType)</c>。</para>
        /// </summary>
        public static string ResolveStartupSceneType()
        {
            return ResolveArgument(SceneTypeArgumentName);
        }

        /// <summary>
        /// 解析命令行指定的端口号。未传参或解析失败时返回默认 7777。
        /// <para>仅作为参考值，实际端口由 NetworkManager 上挂载的 Transport Inspector 配置决定，
        /// 业务可读取此值在运行时覆盖 Transport 端口。</para>
        /// </summary>
        public static int ResolvePort()
        {
            string portStr = ResolveArgument(PortArgumentName);
            return int.TryParse(portStr, out int port) ? port : 7777;
        }

        /// <summary>
        /// 解析命令行指定的服务器地址。未传参时返回 "localhost"。
        /// <para>仅作为参考值，实际连接地址由 Transport Inspector 配置决定。</para>
        /// </summary>
        public static string ResolveAddress()
        {
            return ResolveArgument(AddressArgumentName) ?? "localhost";
        }

        /// <summary>
        /// 内部工具：按参数名查找下一个 token。未找到或无可用的下一个 token 时返回 null。
        /// <para>仅在受支持平台（Standalone/Editor）解析命令行，其他平台直接返回 null。</para>
        /// </summary>
        private static string ResolveArgument(string argName)
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == argName && !string.IsNullOrWhiteSpace(arguments[i + 1]))
                {
                    return arguments[i + 1];
                }
            }
#endif
            return null;
        }
    }
}
