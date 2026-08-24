using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TEngine
{
    /// <summary>
    /// Inno Setup 安装包编译器：回写 setup.iss 的 #define 后调用 ISCC.exe 编译安装包。
    /// 仅负责 InnoSetup 相关步骤；YooAsset 资源构建与 Player 打包由 ReleaseTools.BuildWithConfig 完成，
    /// 产物已落在 Releases/Windows/build/，本类在此之上编译出 Releases/Windows/setup/ 下的安装包。
    /// 注意：setup.iss 需用户放入 Releases/Windows/setup.iss 后本流程才可用。
    /// </summary>
    public static class InnoSetupBuilder
    {
        /// <summary>Releases 根目录（UnityProject/Releases）。</summary>
        public static string ReleasesDir => Path.GetFullPath(Application.dataPath + "/../Releases");

        /// <summary>Windows 程序包目录（Releases/Windows）。</summary>
        public static string WindowsDir => Path.Combine(ReleasesDir, "Windows");

        /// <summary>InnoSetup 脚本路径（Releases/Windows/setup.iss），由用户放入。</summary>
        public static string IssPath => Path.Combine(WindowsDir, "setup.iss");

        /// <summary>Unity Player 产物目录（Releases/Windows/build），对应 setup.iss 的 Source 根。</summary>
        public static string PlayerBuildDir => Path.Combine(WindowsDir, "build");

        /// <summary>InnoSetup 安装包输出目录（Releases/Windows/setup），对应 setup.iss 的 OutputDir。</summary>
        public static string InstallerOutputDir => Path.Combine(WindowsDir, "setup");

        /// <summary>
        /// 编译 Windows 安装包：回写 setup.iss 的 #define 后调用 ISCC 编译。
        /// 回写范围(均保持双引号格式,iss 随时可手动编译):
        ///   MyAppName / MyAppEnglishName / MyAppVersion / MyAppPublisher / MyAppExeName / MyAppPassword / BrandWatermark
        /// 注意 MyAppId 不参与回写,始终以 setup.iss 文件内手填值为准(决定升级覆盖/并存的安装包身份)。
        /// </summary>
        /// <param name="config">安装包回写配置；各字段为空时写入空字符串,保持 iss 可直接编译。</param>
        /// <param name="isccPathOverride">用户手动指定的 ISCC.exe 路径；为空则自动查找。</param>
        public static void BuildInstaller(IssInstallerConfig config, string isccPathOverride = null)
        {
            if (!File.Exists(IssPath))
            {
                throw new FileNotFoundException(
                    $"未找到 InnoSetup 脚本：{IssPath}\n请将 setup.iss 放入 Releases/Windows/ 后重试。", IssPath);
            }

            var sw = Stopwatch.StartNew();
            Debug.Log("[InnoSetup] 开始编译安装包...");

            EditorUtility.DisplayProgressBar("一键出安装包", "Inno Setup 安装包编译中...", 0.9f);

            // 回写 setup.iss，保证手动编译 iss 也是最新配置
            SyncIssDefines(config);

            // 确保安装包输出目录存在
            Directory.CreateDirectory(InstallerOutputDir);

            CompileSetup(isccPathOverride);

            EditorUtility.ClearProgressBar();
            Debug.Log($"[InnoSetup] 编译完成，耗时 {sw.Elapsed.TotalSeconds:F1} 秒，安装包位于: {InstallerOutputDir}");
        }

        /// <summary>
        /// 解析实际使用的 ISCC.exe 路径：优先用户手动指定，其次自动查找。返回 null 表示未找到。
        /// </summary>
        public static string ResolveIscc(string isccPathOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(isccPathOverride) && File.Exists(isccPathOverride))
            {
                return isccPathOverride;
            }
            return FindIscc();
        }

        /// <summary>
        /// 从 setup.iss 读取 #define 值（如 MyAppExeName、MyAppVersion）。
        /// </summary>
        public static string GetIssDefine(string key)
        {
            return GetIssDefine(IssPath, key);
        }

        /// <summary>
        /// 从指定 setup.iss 读取 #define 值（如 MyAppExeName、MyAppVersion）。
        /// </summary>
        public static string GetIssDefine(string issPath, string key)
        {
            string content = File.ReadAllText(issPath);
            var m = Regex.Match(content, $"#define\\s+{key}\\s+\"([^\"]+)\"");
            if (!m.Success)
            {
                throw new Exception($"setup.iss 中未找到 #define {key}");
            }
            return m.Groups[1].Value;
        }

        /// <summary>
        /// 将窗口配置回写进 setup.iss 的 #define，保证 iss 随时可直接手动编译。
        /// MyAppVersion 为空时沿用 iss 现有值,不回写(避免清空版本号);
        /// 其余字段即便为空也写入空字符串,触发加密开关与默认值正确。
        /// </summary>
        private static void SyncIssDefines(IssInstallerConfig config)
        {
            WriteIssDefine("MyAppName", config.AppName ?? string.Empty);
            // 英文名为空时回退用中文名,保证安装目录始终有值
            WriteIssDefine("MyAppEnglishName", !string.IsNullOrEmpty(config.AppEnglishName) ? config.AppEnglishName : (config.AppName ?? string.Empty));
            if (!string.IsNullOrEmpty(config.InstallerVersion))
            {
                WriteIssDefine("MyAppVersion", config.InstallerVersion);
            }
            WriteIssDefine("MyAppPublisher", config.Publisher ?? string.Empty);
            WriteIssDefine("MyAppExeName", config.ExeName ?? string.Empty);
            WriteIssDefine("MyAppPassword", config.Password ?? string.Empty);
            WriteIssDefine("BrandWatermark", config.Watermark ?? string.Empty);
        }

        private static void WriteIssDefine(string key, string value)
        {
            string content = File.ReadAllText(IssPath);
            string replaced = Regex.Replace(content, $"(#define\\s+{key}\\s+\")[^\"]*(\")", $"${{1}}{value}${{2}}");
            if (replaced != content)
            {
                // 保持 UTF-8 无 BOM 写回，与原文件编码一致
                File.WriteAllText(IssPath, replaced, new System.Text.UTF8Encoding(false));
            }
        }

        private static void CompileSetup(string isccPathOverride = null)
        {
            string iscc = ResolveIscc(isccPathOverride);
            if (iscc == null)
            {
                throw new Exception(
                    "未找到 ISCC.exe，请安装 Inno Setup（https://jrsoftware.org/isdl.php），\n" +
                    "或在打包窗口的「ISCC 路径」手动指定 ISCC.exe 位置。");
            }

            var psi = new ProcessStartInfo(iscc, $"\"{IssPath}\"")
            {
                WorkingDirectory = WindowsDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                Debug.Log(output);
                if (p.ExitCode != 0)
                {
                    throw new Exception($"ISCC 编译失败，ExitCode={p.ExitCode}");
                }
            }
        }

        /// <summary>
        /// 查找 ISCC.exe：注册表 → PATH → 所有固定驱动器的 ProgramFiles 目录扫描。
        /// 注意：非系统盘安装（如装在 D:\Program Files\Inno Setup 7）注册表通常无记录，
        ///       SpecialFolder 也只指向 C 盘，故需扫描所有固定驱动器兜底。
        /// </summary>
        private static string FindIscc()
        {
            // 1. 注册表：HKLM\SOFTWARE\WOW6432Node\Inno Setup <ver> 的 InstallPath/InstallDir
            foreach (var keyPath in new[]
            {
                @"SOFTWARE\WOW6432Node\Inno Setup 7",
                @"SOFTWARE\WOW6432Node\Inno Setup 6",
                @"SOFTWARE\Inno Setup 7",
                @"SOFTWARE\Inno Setup 6",
            })
            {
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null) continue;
                    foreach (var valueName in new[] { "InstallPath", "InstallDir" })
                    {
                        var installPath = key.GetValue(valueName) as string;
                        if (!string.IsNullOrEmpty(installPath))
                        {
                            var iscc = Path.Combine(installPath, "ISCC.exe");
                            if (File.Exists(iscc)) return iscc;
                        }
                    }
                }
            }

            // 2. PATH 环境变量
            var pathIscc = FindInPath("ISCC.exe");
            if (pathIscc != null) return pathIscc;

            // 3. 扫描所有固定驱动器的 Program Files / Program Files (x86) 下的 Inno Setup 目录
            //    覆盖装在非系统盘（如 D:\Program Files\Inno Setup 7）的情况
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                var root = drive.RootDirectory.FullName;
                foreach (var programFilesDir in new[] { "Program Files", "Program Files (x86)" })
                {
                    foreach (var version in new[] { "7", "6" })
                    {
                        var iscc = Path.Combine(root, programFilesDir, $"Inno Setup {version}", "ISCC.exe");
                        if (File.Exists(iscc)) return iscc;
                    }
                }
            }
            return null;
        }

        private static string FindInPath(string fileName)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var full = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(full)) return Path.GetFullPath(full);
            }
            return null;
        }
    }

    /// <summary>
    /// setup.iss 回写配置:承载窗口侧输入的应用名/版本/发布者/exe名/密码/水印。
    /// MyAppId 不在其中,始终以 iss 文件内手填值为准。
    /// </summary>
    public sealed class IssInstallerConfig
    {
        /// <summary>应用显示名(中文软件名),回写 MyAppName。</summary>
        public string AppName;
        /// <summary>软件英文名,回写 MyAppEnglishName;仅用于安装目录,为空时回退用 AppName。</summary>
        public string AppEnglishName;
        /// <summary>安装包版本号;为空则沿用 iss 现有 MyAppVersion。</summary>
        public string InstallerVersion;
        /// <summary>发布者,回写 MyAppPublisher。</summary>
        public string Publisher;
        /// <summary>主程序 exe 文件名,回写 MyAppExeName。</summary>
        public string ExeName;
        /// <summary>安装密码;空字符串表示不加密,非空则启用 Password+Encryption。</summary>
        public string Password;
        /// <summary>向导左下角水印文字,回写 BrandWatermark。</summary>
        public string Watermark;
    }
}
