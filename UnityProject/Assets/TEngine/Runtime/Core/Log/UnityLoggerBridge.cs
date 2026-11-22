using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using TouchSocket.Core;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// Unity 日志到 TouchSocket 文件日志的桥接器。
    /// <remarks>
    /// 输出路径是示范：logs\default\[2024-09-08]\0000.log；
    /// 传 <c>--log-instance</c> 时隔离到 logs\instance-{实例标识}\[2024-09-08]\0000.log（日志多实例，独立于资源多开的 <c>--yoo-instance</c>）。
    /// 默认进程与各实例进程各管各的目录，互不统计、互不清理。
    /// 清理策略：保留天数清理 + 日志总量上限自清理（从最旧日期目录开始删除），启动时执行一次，运行期按间隔定时重复。
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
        /// 未传 <c>--log-instance</c> 时的默认实例目录名：默认日志与 instance-{id} 实例目录分家，互不混放。
        /// </summary>
        private const string DEFAULT_INSTANCE_FOLDER_NAME = "default";
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
        /// <summary>
        /// 日志总量上限（字节）：所有日期目录累计超过此值时，从最旧的日期目录开始整目录删除，直至低于上限。
        /// </summary>
        private const long TOTAL_LOG_SIZE_LIMIT = 200L * 1024 * 1024;
        /// <summary>
        /// 运行期定时清理间隔（秒）：启动清理一次后，运行期间按此间隔由写日志路径触发后台重复清理。
        /// </summary>
        private const double CLEANUP_INTERVAL_SECONDS = 600;
        /// <summary>
        /// 日志多实例实例标识启动参数名（独立于资源多开的 <c>--yoo-instance</c>，二者可单独使用）。
        /// </summary>
        private const string INSTANCE_ARGUMENT_NAME = "--log-instance";

        private static readonly object s_Lock = new object();

        private static bool s_Initialized;
        private static string s_LogRootPath;
        private static FileLogger s_FileLogger;
        private static long s_LastCleanupUtcTicks;   // 上次（或本次启动）执行清理的 UTC 时间（Ticks），用于运行期定时触发
        private static int s_CleanupScheduled;       // 后台清理防重入标志（Interlocked 0/1）

        [ThreadStatic]
        private static bool s_IsWriting;

        /// <summary>
        /// 当前日志多实例实例标识（命令行 <c>--log-instance</c> 传入；未传参或非桌面平台为空串）。
        /// </summary>
        /// <remarks>仅诊断用途；日志目录在 <see cref="Init"/> 时一次性确定，运行期修改不会重定向输出。</remarks>
        public static string InstanceId { get; private set; } = string.Empty;

        /// <summary>
        /// 重置静态状态，兼容关闭 Domain Reload 的编辑器播放模式。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Shutdown();
            s_LogRootPath = null;
            s_IsWriting = false;
            InstanceId = string.Empty;
            s_LastCleanupUtcTicks = 0L;
            s_CleanupScheduled = 0;
        }

        /// <summary>
        /// 初始化 Unity 日志桥接器。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        public static void Init()
        {
            // 标记日志桥接开始初始化。此时 FileLogger 尚未就绪，走 Early 缓冲，待下方 Flush 时补写。
            Log.EarlyInfo("[Log] UnityLoggerBridge initializing (BeforeSplashScreen).");

            lock (s_Lock)
            {
                if (s_Initialized)
                {
                    return;
                }

                try
                {
                    s_LogRootPath = Path.Combine(Application.persistentDataPath, LOG_DIRECTORY_NAME);

                    // 日志多实例：传 --log-instance 时隔离到 instance-{id}（独立于资源多开的 --yoo-instance）；
                    // 未传时也隔离到 default 子目录，避免默认日志与各实例目录混在 Logs 根下。
                    // 必须在 BeforeSplashScreen 解析命令行，保证整个会话（含最早日志）落进同一实例目录。
                    string instanceId = ResolveInstanceId();
                    if (!string.IsNullOrEmpty(instanceId))
                    {
                        InstanceId = instanceId;
                        s_LogRootPath = Path.Combine(s_LogRootPath, $"instance-{instanceId}");
                    }
                    else
                    {
                        s_LogRootPath = Path.Combine(s_LogRootPath, DEFAULT_INSTANCE_FOLDER_NAME);
                    }

                    RunCleanup();
                    s_LastCleanupUtcTicks = DateTime.UtcNow.Ticks;

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

                    // 将 Init 之前经 Log.EarlyXxx 缓冲的早期日志补写到文件日志。
                    EarlyLogBuffer.Flush(s_FileLogger);

                    // 标记日志桥接初始化完成。此时链路已通，直接落盘。
                    Log.Info("[Log] UnityLoggerBridge initialized, file logger ready.");
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

                // 清空早期日志缓冲，兼容关闭 Domain Reload 的编辑器播放模式，防止上次会话残留。
                EarlyLogBuffer.Reset();
            }
        }

        private static string CreateLogFolder(LogLevel logLevel)
        {
            string dateFolderName = DateTime.Now.ToString(LOG_DATE_FORMAT, CultureInfo.InvariantCulture);
            return Path.Combine(s_LogRootPath, dateFolderName);
        }

        /// <summary>
        /// 解析命令行中的日志多实例实例标识（<c>--log-instance</c>）。未传参返回空串。
        /// </summary>
        /// <remarks>日志桥接在 BeforeSplashScreen 初始化（早于主包启动流程），故在桥接内部自带解析；
        /// 实例标识独立于资源多开的 <c>--yoo-instance</c>，仅隔离日志目录。</remarks>
        private static string ResolveInstanceId()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == INSTANCE_ARGUMENT_NAME && !string.IsNullOrWhiteSpace(arguments[i + 1]))
                {
                    return arguments[i + 1].Trim();
                }
            }
#endif
            return string.Empty;
        }

        /// <summary>
        /// 清理套件：保留天数清理 + 日志总量上限自清理。启动时同步执行一次；运行期由 <see cref="ScheduleCleanup"/> 定时后台执行。
        /// </summary>
        private static void RunCleanup()
        {
            CleanUpOldLogs();
            EnforceTotalSizeLimit();
        }

        /// <summary>
        /// 运行期定时清理：距上次清理超过间隔时调度一次后台清理（不阻塞日志写入，Interlocked 防重入）。
        /// </summary>
        private static void ScheduleCleanup()
        {
            long lastTicks = Interlocked.Read(ref s_LastCleanupUtcTicks);
            if ((DateTime.UtcNow.Ticks - lastTicks) < TimeSpan.FromSeconds(CLEANUP_INTERVAL_SECONDS).Ticks)
            {
                return;
            }

            // 先推进时间戳再调度：即使并发多触发一次，也只会成功调度一个后台任务
            Interlocked.Exchange(ref s_LastCleanupUtcTicks, DateTime.UtcNow.Ticks);
            if (Interlocked.CompareExchange(ref s_CleanupScheduled, 1, 0) != 0)
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    RunCleanup();
                }
                catch
                {
                    // 清理失败不落日志，避免日志系统自身递归。
                }
                finally
                {
                    Interlocked.Exchange(ref s_CleanupScheduled, 0);
                }
            });
        }

        /// <summary>
        /// 保留天数清理：删除超过保留天数的日期目录。
        /// </summary>
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

        /// <summary>
        /// 日志总量上限自清理：统计根目录下所有日期目录的总大小，超过 <see cref="TOTAL_LOG_SIZE_LIMIT"/> 时
        /// 从最旧的日期目录开始整目录删除，直至低于上限；今天的活动目录正在写入，永不删除。
        /// </summary>
        private static void EnforceTotalSizeLimit()
        {
            if (string.IsNullOrEmpty(s_LogRootPath) || !Directory.Exists(s_LogRootPath))
            {
                return;
            }

            DateTime today = DateTime.Now.Date;
            var folders = new List<(string Path, DateTime Date, long Size)>();
            long totalSize = 0;
            foreach (string directory in Directory.EnumerateDirectories(s_LogRootPath))
            {
                string directoryName = Path.GetFileName(directory);
                if (!DateTime.TryParseExact(directoryName, LOG_DATE_FORMAT, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime directoryDate))
                {
                    continue;
                }

                long size = GetDirectorySize(directory);
                folders.Add((directory, directoryDate, size));
                totalSize += size;
            }

            if (totalSize <= TOTAL_LOG_SIZE_LIMIT)
            {
                return;
            }

            folders.Sort((a, b) => a.Date.CompareTo(b.Date)); // 从最旧的日期开始删
            foreach (var folder in folders)
            {
                if (folder.Date >= today)
                {
                    continue; // 今天正在写入的目录不删
                }

                TryDeleteDirectory(folder.Path);
                totalSize -= folder.Size;
                if (totalSize <= TOTAL_LOG_SIZE_LIMIT)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 递归统计目录大小（字节）。个别文件/子目录访问失败按 0 计，不中断统计。
        /// </summary>
        private static long GetDirectorySize(string directory)
        {
            long size = 0;
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        size += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // 单个文件访问失败（被占用/已删除）不计入，继续统计。
                    }
                }
            }
            catch
            {
                // 目录访问失败时返回已统计到的部分。
            }

            return size;
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
            // 经 Unity Debug 输出：既显示到 Console，也会被 logMessageReceivedThreaded 捕获写入文件。
            Debug.LogError(string.Concat("TaskScheduler.UnobservedTaskException", Environment.NewLine, e.Exception));
            e.SetObserved();
        }

        private static void OnUniTaskSchedulerUnobservedTaskException(Exception exception)
        {
            // 经 Unity Debug 输出：既显示到 Console，也会被 logMessageReceivedThreaded 捕获写入文件。
            Debug.LogError(string.Concat("UniTaskScheduler.UnobservedTaskException", Environment.NewLine, exception));
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

                // 定时清理触发点：写日志路径上做低成本到期检查（时间戳比较），到期后台执行，不阻塞写入。
                ScheduleCleanup();
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
