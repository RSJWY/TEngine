using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using TouchSocket.Core;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// Unity 日志到 TouchSocket 文件日志的桥接器。
    /// <remarks>
    /// 输出路径是示范：logs\[2024-09-08]\0000.log
    /// </remarks>
    /// </summary>
    public static class UnityLoggerBridge
    {
        /// <summary>
        /// 保留天数
        /// </summary>
        private const int LOG_RETENTION_DAYS = 3;
        /// <summary>
        /// 单文件大小 KB
        /// </summary>
        private const int MAX_LOG_FILE_SIZE = 1024 * 1024 * 5;
        /// <summary>
        /// 日志存放目录
        /// </summary>
        private const string LOG_DIRECTORY_NAME = "Logs";
        /// <summary>
        /// 输出天格式（Log下的子文件夹）
        /// </summary>
        private const string LOG_DATE_FORMAT = "yyyy-MM-dd";
        /// <summary>
        /// 输出时间格式化
        /// </summary>
        private const string LOG_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss.ffff";
        /// <summary>
        /// 文件名，滚动增加
        /// </summary>
        private const string LOG_FILE_NAME_FORMAT = "0000";

        private static readonly object s_Lock = new object();

        private static bool s_Initialized;
        private static string s_LogRootPath;
        private static FileLogger s_FileLogger;

        [ThreadStatic]
        private static bool s_IsWriting;

        /// <summary>
        /// 重置静态状态，兼容关闭 Domain Reload 的编辑器播放模式。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Shutdown();
            s_LogRootPath = null;
            s_IsWriting = false;
        }

        /// <summary>
        /// 初始化 Unity 日志桥接器。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        public static void Init()
        {
            lock (s_Lock)
            {
                if (s_Initialized)
                {
                    return;
                }

                try
                {
                    s_LogRootPath = Path.Combine(Application.persistentDataPath, LOG_DIRECTORY_NAME);
                    CleanUpOldLogs();

                    s_FileLogger = new FileLogger
                    {
                        LogLevel = LogLevel.Trace,
                        DateTimeFormat = LOG_TIME_FORMAT,
                        MaxSize = MAX_LOG_FILE_SIZE,
                        FileNameFormat = LOG_FILE_NAME_FORMAT,
                        CreateLogFolder = CreateLogFolder,
                    };

                    Application.logMessageReceivedThreaded += OnLogMessageReceivedThreaded;
                    Application.quitting += Shutdown;
                    TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
                    UniTaskScheduler.UnobservedTaskException += OnUniTaskSchedulerUnobservedTaskException;
                    s_Initialized = true;
                }
                catch
                {
                    DisposeFileLogger();
                    s_Initialized = false;
                }
            }
        }

        /// <summary>
        /// 关闭 Unity 日志桥接器。
        /// </summary>
        public static void Shutdown()
        {
            lock (s_Lock)
            {
                if (!s_Initialized)
                {
                    return;
                }

                Application.logMessageReceivedThreaded -= OnLogMessageReceivedThreaded;
                Application.quitting -= Shutdown;
                TaskScheduler.UnobservedTaskException -= OnTaskSchedulerUnobservedTaskException;
                UniTaskScheduler.UnobservedTaskException -= OnUniTaskSchedulerUnobservedTaskException;
                DisposeFileLogger();
                s_Initialized = false;
            }
        }

        private static string CreateLogFolder(LogLevel logLevel)
        {
            string dateFolderName = DateTime.Now.ToString(LOG_DATE_FORMAT, CultureInfo.InvariantCulture);
            return Path.Combine(s_LogRootPath, dateFolderName);
        }

        private static void CleanUpOldLogs()
        {
            if (string.IsNullOrEmpty(s_LogRootPath) || !Directory.Exists(s_LogRootPath))
            {
                return;
            }

            DateTime expireDate = DateTime.Now.Date.AddDays(-LOG_RETENTION_DAYS);
            foreach (string directory in Directory.EnumerateDirectories(s_LogRootPath))
            {
                string directoryName = Path.GetFileName(directory);
                if (!DateTime.TryParseExact(directoryName, LOG_DATE_FORMAT, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime directoryDate))
                {
                    continue;
                }

                if (directoryDate < expireDate)
                {
                    TryDeleteDirectory(directory);
                }
            }
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // 日志清理失败时不再写日志，避免日志系统自身递归。
            }
        }

        private static void OnLogMessageReceivedThreaded(string condition, string stackTrace, LogType type)
        {
            LogLevel logLevel = ConvertLogLevel(type);
            string message = string.IsNullOrEmpty(stackTrace)
                ? condition
                : string.Concat(condition, Environment.NewLine, stackTrace);

            WriteLog(logLevel, "Unity", message, null);
        }

        private static void OnTaskSchedulerUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            WriteLog(LogLevel.Error, sender, "TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private static void OnUniTaskSchedulerUnobservedTaskException(Exception exception)
        {
            WriteLog(LogLevel.Error, "UniTaskScheduler", "UniTaskScheduler.UnobservedTaskException", exception);
        }

        private static LogLevel ConvertLogLevel(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return LogLevel.Warning;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return LogLevel.Error;
                case LogType.Log:
                default:
                    return LogLevel.Debug;
            }
        }

        private static void WriteLog(LogLevel logLevel, object source, string message, Exception exception)
        {
            if (s_IsWriting)
            {
                return;
            }

            s_IsWriting = true;
            try
            {
                lock (s_Lock)
                {
                    s_FileLogger?.Log(logLevel, source, message, exception);
                }
            }
            catch
            {
                // 写文件失败时不能回写 Unity Console 或 TEngine Log，避免递归。
            }
            finally
            {
                s_IsWriting = false;
            }
        }

        private static void DisposeFileLogger()
        {
            try
            {
                s_FileLogger?.Dispose();
            }
            catch
            {
                // 释放日志器失败时不再写日志，避免递归。
            }
            finally
            {
                s_FileLogger = null;
            }
        }
    }
}
