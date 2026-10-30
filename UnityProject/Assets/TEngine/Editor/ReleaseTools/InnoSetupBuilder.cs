using System;
using System.Collections.Generic;
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
        /// <summary>Inno Setup 模板脚本名（唯一，手工维护，构建流程永不回写）。</summary>
        public const string TemplateIssName = "setup.iss";

        /// <summary>默认配置名，对应编辑脚本 setup.generated.iss（与历史文件名一致，无需迁移）。</summary>
        public const string DefaultIssConfig = "generated";

        private const string IssNamePrefix = "setup.";
        private const string IssExtension = ".iss";

        /// <summary>当前选中的安装包配置名；编辑与编译的都是 setup.&lt;配置名&gt;.iss。</summary>
        public static string ActiveIssConfig { get; private set; } = DefaultIssConfig;

        /// <summary>模板脚本（唯一）：Releases/Windows/setup.iss。</summary>
        public static string IssPath => Path.Combine(WindowsDir, TemplateIssName);

        /// <summary>当前配置的编辑脚本：Releases/Windows/setup.&lt;配置名&gt;.iss。</summary>
        public static string GeneratedIssPath => Path.Combine(WindowsDir, GetIssFileName(ActiveIssConfig));

        /// <summary>切换安装包配置；接受配置名或 setup.&lt;名&gt;.iss 文件名，空/非法回退 generated。</summary>
        public static void SelectIssConfig(string configName)
        {
            ActiveIssConfig = NormalizeIssConfig(configName);
        }

        /// <summary>配置名 -> setup.&lt;配置名&gt;.iss。</summary>
        public static string GetIssFileName(string configName)
        {
            return IssNamePrefix + NormalizeIssConfig(configName) + IssExtension;
        }

        /// <summary>归一化配置名：去路径与 setup./.iss 前后缀、替换非法字符；空值回退 generated。</summary>
        public static string NormalizeIssConfig(string configName)
        {
            var name = string.IsNullOrWhiteSpace(configName) ? string.Empty : Path.GetFileName(configName.Trim());
            if (name.EndsWith(IssExtension, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - IssExtension.Length);
            }

            if (name.StartsWith(IssNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(IssNamePrefix.Length);
            }

            name = name.Trim();
            // 旧设置里存的是脚本文件名（setup.iss），归一后会剩 "setup"，统一回默认配置名
            if (string.Equals(name, "setup", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultIssConfig;
            }

            if (name.Length <= 0 || name == "." || name == "..")
            {
                return DefaultIssConfig;
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return name;
        }

        /// <summary>列出已有配置名（扫 setup.*.iss 并排除模板）；默认配置恒在首位。</summary>
        public static List<string> ListIssConfigs()
        {
            var configs = new List<string>();
            if (Directory.Exists(WindowsDir))
            {
                foreach (var path in Directory.GetFiles(WindowsDir, IssNamePrefix + "*" + IssExtension, SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(path);
                    if (string.Equals(fileName, TemplateIssName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var config = NormalizeIssConfig(fileName);
                    if (!configs.Contains(config))
                    {
                        configs.Add(config);
                    }
                }
            }

            configs.Sort(StringComparer.OrdinalIgnoreCase);
            if (!configs.Contains(DefaultIssConfig))
            {
                configs.Insert(0, DefaultIssConfig);
            }

            return configs;
        }
        public static string PlayerBuildDir => Path.Combine(WindowsDir, "build");
        public static string InstallerOutputDir => Path.Combine(WindowsDir, "setup");

        /// <summary>Unity 生成于 Player 输出目录、不可随包发布的目录名后缀。</summary>
        private static readonly string[] UnshippableDirectorySuffixes =
        {
            "_BackUpThisFolder_ButDontShipItWithYourGame",
            "_BurstDebugInformation_DoNotShip",
        };

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

        public static void BuildInstaller(IssInstallerConfig config, string isccPathOverride = null, string issConfig = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            // 指定安装包配置名时先切换：后续 GeneratedIssPath 即 setup.<配置名>.iss
            if (!string.IsNullOrWhiteSpace(issConfig)) SelectIssConfig(issConfig);

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
            ValidateUnshippableFoldersExcluded();
        }

        /// <summary>
        /// 校验构建目录中的 Unity 不可发布目录（IL2CPP 增量缓存备份 / Burst 调试信息）已被生成的 iss 脚本排除。
        /// 这些目录体积大且不可随包发布，未排除时会被 <c>Source: "build\*"</c> 原样打进安装包。
        /// </summary>
        private static void ValidateUnshippableFoldersExcluded()
        {
            foreach (var suffix in UnshippableDirectorySuffixes)
            {
                var unshippableDirs = Directory.GetDirectories(PlayerBuildDir, "*" + suffix, SearchOption.TopDirectoryOnly);
                if (unshippableDirs.Length <= 0)
                {
                    continue;
                }

                // 生成的 iss 脚本未提及该目录特征串即视为未配置 Excludes，拦截打包并给出修复指引。
                if (!File.Exists(GeneratedIssPath) || !File.ReadAllText(GeneratedIssPath).Contains(suffix))
                {
                    throw new Exception(
                        $"Windows Player 构建目录存在不可发布目录：{Path.GetFileName(unshippableDirs[0])}\n" +
                        "该目录是 IL2CPP 增量缓存备份 / Burst 调试信息，会被原样打进安装包，不应随包发布。\n" +
                        $"请在 setup.iss 的 build\\* 条目追加 Excludes: \"*{suffix}\" 后重试。");
                }
            }
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

        /// <summary>
        /// 尝试读取 iss 脚本中的 #define 值（供窗口「从脚本提取」使用）。
        /// 键不存在或出现多次时返回 false，不抛异常。
        /// </summary>
        public static bool TryGetIssDefine(string issPath, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(issPath) || !File.Exists(issPath)) return false;

            string content = File.ReadAllText(issPath);
            var matches = Regex.Matches(content, "(?m)^\\s*#define\\s+" + Regex.Escape(key) + "\\s+\"((?:[^\"]|\"\")*)\"\\s*$");
            if (matches.Count != 1) return false;
            value = matches[0].Groups[1].Value.Replace("\"\"", "\"");
            return true;
        }

        /// <summary>
        /// 从 iss 脚本提取安装包配置（<see cref="SyncIssDefines"/> 的反向操作）。
        /// 只提取有对应窗口字段的键；MyAppVersion/MyAppExeName 由构建流程按 PlayerSettings 自动推导，不参与提取；
        /// MyAppId 按设计以 iss 文件为准，既不回写也不提取。
        /// 缺失/重复的键不阻断提取：对应字段返回 null，并在 warnings 中记录（warnings 可为 null）。
        /// </summary>
        public static IssInstallerConfig ExtractIssConfig(string issPath, List<string> warnings = null)
        {
            if (!File.Exists(issPath))
            {
                throw new FileNotFoundException($"未找到 Inno Setup 脚本：{issPath}", issPath);
            }

            return new IssInstallerConfig
            {
                AppName = ReadExtractDefine(issPath, "MyAppName", warnings),
                AppEnglishName = ReadExtractDefine(issPath, "MyAppEnglishName", warnings),
                Publisher = ReadExtractDefine(issPath, "MyAppPublisher", warnings),
                Password = ReadExtractDefine(issPath, "MyAppPassword", warnings),
                Watermark = ReadExtractDefine(issPath, "BrandWatermark", warnings),
            };
        }

        private static string ReadExtractDefine(string issPath, string key, List<string> warnings)
        {
            if (TryGetIssDefine(issPath, key, out var value)) return value;

            warnings?.Add($"#define {key} 不存在或出现多次，已跳过该项");
            return null;
        }

        /// <summary>
        /// 解析安装包版本（回写 setup.iss 的 MyAppVersion）：
        /// 固定取 <see cref="UpdateSetting.GetVersion"/>（即 ProjectSettings 的 Version），
        /// UpdateSetting 缺失/为空时回退 PlayerSettings.bundleVersion。安装包版本不可单独覆盖。
        /// </summary>
        public static string ResolveInstallerVersion()
        {
            return ResolveInstallerVersion(Settings.UpdateSetting);
        }

        /// <summary>
        /// 从指定 UpdateSetting 解析安装包版本（供已缓存 UpdateSetting 的调用方避免重复查资产）。
        /// </summary>
        public static string ResolveInstallerVersion(UpdateSetting updateSetting)
        {
            var version = updateSetting != null ? updateSetting.GetVersion() : null;
            if (string.IsNullOrWhiteSpace(version))
            {
                version = PlayerSettings.bundleVersion;
            }

            return string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim();
        }

        private static void SyncIssDefines(IssInstallerConfig config)
        {
            string content = File.ReadAllText(GeneratedIssPath);
            content = ReplaceIssDefine(content, "MyAppName", config.AppName ?? string.Empty);
            content = ReplaceIssDefine(content, "MyAppEnglishName", !string.IsNullOrWhiteSpace(config.AppEnglishName) ? config.AppEnglishName : (config.AppName ?? string.Empty));
            // 安装包版本固定跟随 UpdateSetting.Version（同步自 ProjectSettings 的 Version），窗口/CLI 均不可覆盖
            var installerVersion = ResolveInstallerVersion();
            if (!string.IsNullOrWhiteSpace(installerVersion)) content = ReplaceIssDefine(content, "MyAppVersion", installerVersion);
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
        public string Publisher;
        public string ExeName;
        public string Password;
        public string Watermark;
    }
}
