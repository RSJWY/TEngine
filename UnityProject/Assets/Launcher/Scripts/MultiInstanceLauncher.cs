using System;

namespace Launcher
{
    /// <summary>
    /// 桌面多开启动器。解析命令行传入的实例标识，供资源模块按实例隔离缓存目录。
    /// 用法：Game.exe --yoo-instance client-1
    /// </summary>
    public static class MultiInstanceLauncher
    {
        /// <summary>
        /// 实例标识启动参数名。
        /// </summary>
        public const string InstanceIdArgumentName = "--yoo-instance";

        /// <summary>
        /// 解析命令行中的实例标识。未传参时返回 null（保持单开默认行为）。
        /// </summary>
        public static string ResolveInstanceId()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == InstanceIdArgumentName && !string.IsNullOrWhiteSpace(arguments[i + 1]))
                {
                    return arguments[i + 1];
                }
            }
#endif
            return null;
        }
    }
}
