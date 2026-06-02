using System;
using System.Text;
using TouchSocket.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TEngine
{
    /// <summary>
    /// TouchSocket 日志转 Unity Console 的日志器。
    /// </summary>
    public sealed class TouchSocketContainerUnityDebugLogger : LoggerBase
    {
        private const string LOG_PREFIX = "[TouchSocket]";

        private static readonly StringBuilder s_StringBuilder = new StringBuilder(1024);

        private TouchSocketContainerUnityDebugLogger()
        {
        }

        /// <summary>
        /// 默认 TouchSocket Unity Console 日志器实例。
        /// </summary>
        public static TouchSocketContainerUnityDebugLogger Default { get; } = new TouchSocketContainerUnityDebugLogger();

        /// <summary>
        /// 写入 TouchSocket 日志到 Unity Console。
        /// </summary>
        /// <param name="logLevel">日志级别。</param>
        /// <param name="source">日志来源。</param>
        /// <param name="message">日志消息。</param>
        /// <param name="exception">异常信息。</param>
        protected override void WriteLog(LogLevel logLevel, object source, string message, Exception exception)
        {
            string logString = CreateUnityLogString(logLevel, source, message, exception);
            switch (logLevel)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(logString);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    Debug.LogError(logString);
                    break;
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                default:
                    Debug.Log(logString);
                    break;
            }
        }

        private static string CreateUnityLogString(LogLevel logLevel, object source, string message, Exception exception)
        {
            lock (s_StringBuilder)
            {
                s_StringBuilder.Clear();
                s_StringBuilder.Append(LOG_PREFIX)
                    .Append(' ')
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [")
                    .Append(logLevel)
                    .Append(']');

                if (source != null)
                {
                    s_StringBuilder.Append(" [")
                        .Append(source)
                        .Append(']');
                }

                if (!string.IsNullOrEmpty(message))
                {
                    s_StringBuilder.Append(' ')
                        .Append(message);
                }

                if (exception != null)
                {
                    s_StringBuilder.AppendLine()
                        .Append(exception.Message);

                    if (!string.IsNullOrEmpty(exception.StackTrace))
                    {
                        s_StringBuilder.AppendLine()
                            .Append(exception.StackTrace);
                    }
                }

                return s_StringBuilder.ToString();
            }
        }
    }
}
