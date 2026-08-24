using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TEngine
{
    /// <summary>Inno Setup 安装包编译器。模板与实际编译脚本分离，模板不会被构建流程回写。</summary>
    public static class InnoSetupBuilder
    {
        private const int CompileTimeoutMilliseconds = 10 * 60 * 1000;

        public static string ReleasesDir => Path.GetFullPath(Application.dataPath + "/../Releases");
        public static string WindowsDir => Path.Combine(ReleasesDir, "Windows");
        public static string IssPath => Path.Combine(WindowsDir, "setup.iss");
        public static string GeneratedIssPath => Path.Combine(WindowsDir, "setup.generated.iss");
        public static string PlayerBuildDir => Path.Combine(WindowsDir, "build");
        public static string InstallerOutputDir => Path.Combine(WindowsDir, "setup");

        public static bool EnsureGeneratedIss(IssInstallerConfig config = null)
        {
            if (!File.Exists(IssPath))
            {
                throw new FileNotFoundException($"未找到 Inno Setup 模板：{IssPath}\n请将 setup.iss 放入 Releases/Windows/ 后重试。", IssPath);
            }

            var created = !File.Exists(GeneratedIssPath);
            if (created)
            {
                Directory.CreateDirectory(WindowsDir);
                File.Copy(IssPath, GeneratedIssPath, false);
            }

            if (created && config != null)
            {
                SyncIssDefines(config);
            }

            return true;
        }

        public static void RegenerateGeneratedIss(IssInstallerConfig config = null)
        {
            if (!File.Exists(IssPath))
            {
                throw new FileNotFoundException($"未找到 Inno Setup 模板：{IssPath}", IssPath);
            }

            Directory.CreateDirectory(WindowsDir);
            File.Copy(IssPath, GeneratedIssPath, true);
            if (config != null)
            {
                SyncIssDefines(config);
            }
        }

        public static void BuildInstaller(IssInstallerConfig config, string isccPathOverride = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var sw = Stopwatch.StartNew();
            try
            {
                EnsureGeneratedIss();
                SyncIssDefines(config);
                ValidatePlayerBuild(config.ExeName);
                Directory.CreateDirectory(InstallerOutputDir);
                Debug.Log("[InnoSetup] 开始编译安装包...");
                EditorUtility.DisplayProgressBar("一键出安装包", "Inno Setup 安装包编译中...", 0.9f);
                CompileSetup(isccPathOverride);
                Debug.Log($"[InnoSetup] 编译完成，耗时 {sw.Elapsed.TotalSeconds:F1} 秒，安装包位于：{InstallerOutputDir}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static void ValidatePlayerBuild(string exeName)
        {
            if (!Directory.Exists(PlayerBuildDir))
                throw new DirectoryNotFoundException($"未找到 Windows Player 构建目录：{PlayerBuildDir}\n请先构建 Windows Player。");
            if (Directory.GetFileSystemEntries(PlayerBuildDir).Length == 0)
                throw new Exception($"Windows Player 构建目录为空：{PlayerBuildDir}\n请先构建 Windows Player。");
            if (string.IsNullOrWhiteSpace(exeName))
                throw new Exception("未配置主程序 EXE 文件名，无法校验安装包输入。");
            var exePath = Path.Combine(PlayerBuildDir, exeName);
            if (!File.Exists(exePath))
                throw new FileNotFoundException($"未找到主程序 EXE：{exePath}\n请确认 Player 输出目录与安装包配置一致。", exePath);
        }

        public static string ResolveIscc(string isccPathOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(isccPathOverride) && File.Exists(isccPathOverride)) return isccPathOverride;
            return FindIscc();
        }

        public static string GetIssDefine(string key) => GetIssDefine(GeneratedIssPath, key);

        public static string GetIssDefine(string issPath, string key)
        {
            string content = File.ReadAllText(issPath);
            var matches = Regex.Matches(content, "(?m)^\\s*#define\\s+" + Regex.Escape(key) + "\\s+\"((?:[^\"]|\"\")*)\"\\s*$");
            if (matches.Count != 1) throw new Exception($"{Path.GetFileName(issPath)} 中 #define {key} 应存在且只能存在一次，实际为 {matches.Count} 次。");
            return matches[0].Groups[1].Value.Replace("\"\"", "\"");
        }

        private static void SyncIssDefines(IssInstallerConfig config)
        {
            string content = File.ReadAllText(GeneratedIssPath);
            content = ReplaceIssDefine(content, "MyAppName", config.AppName ?? string.Empty);
            content = ReplaceIssDefine(content, "MyAppEnglishName", !string.IsNullOrWhiteSpace(config.AppEnglishName) ? config.AppEnglishName : (config.AppName ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(config.InstallerVersion)) content = ReplaceIssDefine(content, "MyAppVersion", config.InstallerVersion);
            content = ReplaceIssDefine(content, "MyAppPublisher", config.Publisher ?? string.Empty);
            content = ReplaceIssDefine(content, "MyAppExeName", config.ExeName ?? string.Empty);
            content = ReplaceIssDefine(content, "MyAppPassword", config.Password ?? string.Empty);
            content = ReplaceIssDefine(content, "BrandWatermark", config.Watermark ?? string.Empty);
            File.WriteAllText(GeneratedIssPath, content, new UTF8Encoding(false));
        }

        private static string ReplaceIssDefine(string content, string key, string value)
        {
            var pattern = "(?m)^(\\s*#define\\s+" + Regex.Escape(key) + "\\s+\")((?:[^\"]|\"\")*)(\"\\s*)$";
            var matches = Regex.Matches(content, pattern);
            if (matches.Count != 1) throw new Exception($"{Path.GetFileName(GeneratedIssPath)} 中 #define {key} 应存在且只能存在一次，实际为 {matches.Count} 次。");
            var safeValue = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\"", "\"\"");
            return new Regex(pattern).Replace(content, m => m.Groups[1].Value + safeValue + m.Groups[3].Value, 1);
        }

        private static void CompileSetup(string isccPathOverride)
        {
            string iscc = ResolveIscc(isccPathOverride);
            if (iscc == null)
            {
                throw new Exception("未找到 ISCC.exe，请安装 Inno Setup，或在打包窗口手动指定 ISCC.exe 路径。");
            }

            var psi = new ProcessStartInfo(iscc, $"\"{GeneratedIssPath}\"")
            {
                WorkingDirectory = WindowsDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                if (!process.Start()) throw new Exception("ISCC.exe 启动失败。");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(CompileTimeoutMilliseconds))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException($"ISCC 编译超过 {CompileTimeoutMilliseconds / 60000} 分钟，已终止进程。\n{stderr}");
                }

                process.WaitForExit();
                var output = stdout.ToString().Trim();
                var error = stderr.ToString().Trim();
                if (!string.IsNullOrEmpty(output)) Debug.Log("[InnoSetup][stdout]\n" + output);
                if (!string.IsNullOrEmpty(error)) Debug.LogWarning("[InnoSetup][stderr]\n" + error);
                if (process.ExitCode != 0)
                {
                    throw new Exception($"ISCC 编译失败，ExitCode={process.ExitCode}\nstdout:\n{output}\nstderr:\n{error}");
                }
            }
        }

        private static string FindIscc()
        {
            foreach (var keyPath in new[] { @"SOFTWARE\WOW6432Node\Inno Setup 7", @"SOFTWARE\WOW6432Node\Inno Setup 6", @"SOFTWARE\Inno Setup 7", @"SOFTWARE\Inno Setup 6" })
            {
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null) continue;
                    foreach (var valueName in new[] { "InstallPath", "InstallDir" })
                    {
                        var installPath = key.GetValue(valueName) as string;
                        var iscc = string.IsNullOrEmpty(installPath) ? null : Path.Combine(installPath, "ISCC.exe");
                        if (!string.IsNullOrEmpty(iscc) && File.Exists(iscc)) return iscc;
                    }
                }
            }

            var path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                foreach (var dir in path.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    var iscc = Path.Combine(dir.Trim(), "ISCC.exe");
                    if (File.Exists(iscc)) return Path.GetFullPath(iscc);
                }
            }

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                foreach (var programFiles in new[] { "Program Files", "Program Files (x86)" })
                foreach (var version in new[] { "7", "6" })
                {
                    var iscc = Path.Combine(drive.RootDirectory.FullName, programFiles, $"Inno Setup {version}", "ISCC.exe");
                    if (File.Exists(iscc)) return iscc;
                }
            }
            return null;
        }
    }

    public sealed class IssInstallerConfig
    {
        public string AppName;
        public string AppEnglishName;
        public string InstallerVersion;
        public string Publisher;
        public string ExeName;
        public string Password;
        public string Watermark;
    }
}
