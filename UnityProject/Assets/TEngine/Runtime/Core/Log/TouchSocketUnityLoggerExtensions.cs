using TouchSocket.Core;

namespace TEngine
{
    /// <summary>
    /// TouchSocket Unity 日志扩展。
    /// </summary>
    public static class TouchSocketUnityLoggerExtensions
    {
        /// <summary>
        /// 添加输出到 Unity Console 的 TouchSocket 日志器。
        /// </summary>
        /// <param name="registrator">TouchSocket 注册器。</param>
        /// <param name="logLevel">日志输出级别。</param>
        /// <returns>TouchSocket 注册器。</returns>
        public static IRegistrator AddUnityDebugLogger(this IRegistrator registrator, LogLevel logLevel = LogLevel.Trace)
        {
            TouchSocketContainerUnityDebugLogger.Default.LogLevel = logLevel;
            return registrator.AddLogger(TouchSocketContainerUnityDebugLogger.Default);
        }

        /// <summary>
        /// 添加输出到 Unity Console 的 TouchSocket 日志器。
        /// </summary>
        /// <param name="loggerGroup">TouchSocket 日志组。</param>
        /// <param name="logLevel">日志输出级别。</param>
        public static void AddUnityDebugLogger(this LoggerGroup loggerGroup, LogLevel logLevel = LogLevel.Trace)
        {
            TouchSocketContainerUnityDebugLogger.Default.LogLevel = logLevel;
            loggerGroup.AddLogger(TouchSocketContainerUnityDebugLogger.Default);
        }
    }
}
