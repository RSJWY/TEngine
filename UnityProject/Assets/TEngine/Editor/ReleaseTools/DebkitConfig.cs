using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// debkit（Releases/Linux 下的 .deb 打包工具）配置读写。
    /// 面板只负责生成 appConfigs/*.conf；真正的打包仍在 WSL 里执行 debkit.sh。
    /// 生成的 conf 与 lib/wizard.sh 的输出格式保持一致（KEY="value"、LF 换行、按 bash 双引号规则转义）。
    /// </summary>
    public static class DebkitConfig
    {
        /// <summary>工具目录（相对 Unity 工程根）。</summary>
        public const string ToolDirRelative = "Releases/Linux";

        /// <summary>唯一入口脚本名。</summary>
        public const string ScriptFileName = "debkit.sh";

        /// <summary>应用配置目录名。</summary>
        public const string ConfDirName = "appConfigs";

        /// <summary>应用配置扩展名。</summary>
        public const string ConfExtension = ".conf";

        /// <summary>打包产物目录名（debkit.sh 里的 OUTDIR 默认值）。</summary>
        public const string OutputDirName = "output";

        /// <summary>Unity 工程根目录（Assets 的上一级）。</summary>
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>debkit 目录绝对路径。</summary>
        public static string ToolDir => Path.GetFullPath(Path.Combine(ProjectRoot, ToolDirRelative));

        /// <summary>debkit.sh 绝对路径。</summary>
        public static string ScriptPath => Path.Combine(ToolDir, ScriptFileName);

        /// <summary>appConfigs 目录绝对路径。</summary>
        public static string ConfDir => Path.Combine(ToolDir, ConfDirName);

        /// <summary>debkit.sh 是否存在（决定面板状态提示）。</summary>
        public static bool ScriptExists => File.Exists(ScriptPath);

        /// <summary>按包名取配置文件绝对路径。</summary>
        public static string GetConfPath(string packageName)
        {
            return Path.Combine(ConfDir, SanitizePackageName(packageName) + ConfExtension);
        }

        /// <summary>列出 appConfigs 下所有 conf 文件名，按名称排序。</summary>
        public static string[] ListConfFiles()
        {
            if (!Directory.Exists(ConfDir)) return Array.Empty<string>();

            var files = new List<string>();
            foreach (var path in Directory.GetFiles(ConfDir, "*" + ConfExtension, SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(name)) files.Add(name);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files.ToArray();
        }

        /// <summary>按文件名取 conf 绝对路径；只取文件名（拒绝目录穿越），无扩展名时补 .conf。</summary>
        public static string GetConfPathByFile(string confFileName)
        {
            if (string.IsNullOrWhiteSpace(confFileName)) return GetConfPath(string.Empty);

            var name = Path.GetFileName(confFileName.Trim());
            if (string.IsNullOrEmpty(name) || name == "." || name == "..") return GetConfPath(string.Empty);

            if (!name.EndsWith(ConfExtension, StringComparison.OrdinalIgnoreCase)) name += ConfExtension;
            return Path.Combine(ConfDir, name);
        }

        /// <summary>
        /// Debian 包名归一化：全小写，只保留 a-z 0-9 . + -，其余（含中文、空格、下划线）替换为 '-'。
        /// 为空时回退 "my-application"，以数字开头时补 "app-" 前缀。
        /// </summary>
        public static string SanitizePackageName(string raw)
        {
            const string fallback = "my-application";
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            var builder = new StringBuilder(raw.Length);
            foreach (var rawChar in raw.Trim().ToLowerInvariant())
            {
                var keep = (rawChar >= 'a' && rawChar <= 'z') ||
                           (rawChar >= '0' && rawChar <= '9') ||
                           rawChar == '-' || rawChar == '.' || rawChar == '+';
                builder.Append(keep ? rawChar : '-');
            }

            var name = builder.ToString().Trim('-', '.', '+');
            if (name.Length <= 0)
            {
                return fallback;
            }

            if (name[0] >= '0' && name[0] <= '9')
            {
                name = "app-" + name;
            }

            return name;
        }

        /// <summary>Windows 绝对路径 → WSL 路径（/mnt/&lt;盘符&gt;/...）；已是 Unix 路径则原样返回，相对路径按工程根补全。</summary>
        public static string ToWslPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var value = path.Trim();
            if (value.StartsWith("/", StringComparison.Ordinal))
            {
                return value;
            }

            if (!Path.IsPathRooted(value))
            {
                value = Path.Combine(ProjectRoot, value);
            }

            value = Path.GetFullPath(value).Replace('\\', '/');
            if (value.Length >= 2 && value[1] == ':')
            {
                return "/mnt/" + char.ToLowerInvariant(value[0]) + value.Substring(2);
            }

            return value;
        }

        /// <summary>默认打包源目录：Releases/Linux/build（Unity 的 Linux player 产物），已转成 WSL 路径。</summary>
        public static string DefaultSourceDirWsl()
        {
            return ToWslPath(Path.Combine(ProjectRoot, ToolDirRelative, "build"));
        }

        /// <summary>WSL 路径转回 Windows 路径（/mnt/e/xxx → E:\xxx）；非 /mnt 路径原样返回。</summary>
        public static string ToWindowsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var value = path.Trim();
            if (value.Length >= 6 &&
                value.StartsWith("/mnt/", StringComparison.OrdinalIgnoreCase) &&
                value[5] == '/')
            {
                var drive = char.ToUpperInvariant(value[4]);
                var rest = value.Substring(6).Replace('/', '\\');
                return drive + ":\\" + rest;
            }

            return value;
        }

        /// <summary>debkit 产物目录：Releases/Linux/output。</summary>
        public static string OutputDir => Path.Combine(ToolDir, OutputDirName);

        /// <summary>打包产物路径：output/&lt;包名&gt;_&lt;版本&gt;_&lt;架构&gt;.deb（与 debkit.sh 的 DEB_NAME 一致）。</summary>
        public static string GetDebPath(DebkitConfData data)
        {
            if (data == null)
            {
                return OutputDir;
            }

            var arch = string.IsNullOrWhiteSpace(data.Arch) ? "amd64" : data.Arch.Trim();
            var version = string.IsNullOrWhiteSpace(data.Version) ? "1.0.0" : data.Version.Trim();
            return Path.Combine(OutputDir, $"{data.PackageName}_{version}_{arch}.deb");
        }

        /// <summary>可直接粘贴到 WSL 的打包命令（配置已在 appConfigs 下时）。</summary>
        public static string BuildWslCommand(string packageName, string confFileName = null)
        {
            var confFile = string.IsNullOrWhiteSpace(confFileName)
                ? Path.GetFileName(GetConfPath(packageName))
                : Path.GetFileName(GetConfPathByFile(confFileName));
            return $"cd {ToWslPath(ToolDir)} && ./{ScriptFileName} -c {ConfDirName}/{confFile} -w ~/debkit-work";
        }

        /// <summary>
        /// 可直接粘贴到 Windows 终端（PowerShell / CMD）的命令：按需指定 WSL 发行版进入打包。
        /// distro 为空表示用 WSL 默认发行版。
        /// </summary>
        public static string BuildWindowsWslCommand(string packageName, string distro, string confFileName = null)
        {
            return "wsl.exe " + BuildWslProcessArguments(packageName, distro, confFileName);
        }

        /// <summary>wsl.exe 的参数部分（不含可执行文件名），供 Process.Start 直接使用。</summary>
        public static string BuildWslProcessArguments(string packageName, string distro, string confFileName = null)
        {
            var inner = BuildWslCommand(packageName, confFileName);
            var target = string.IsNullOrWhiteSpace(distro) ? string.Empty : "-d \"" + distro.Trim() + "\" ";
            return $"{target}-- bash -lc \"{inner}\"";
        }

        // ---- WSL 发行版（"容器"）探测 -------------------------------------------------

        private static readonly List<WslDistroInfo> CachedWslDistros = new List<WslDistroInfo>();
        private static DateTime _wslCacheTime = DateTime.MinValue;
        private static string _wslCacheError = string.Empty;
        private static bool _wslCacheFilled;

        /// <summary>最近一次探测 WSL 发行版的错误信息（为空表示正常）。</summary>
        public static string WslListError => _wslCacheError;

        /// <summary>清空 WSL 发行版缓存，下次读取时重新执行 wsl.exe -l -v。</summary>
        public static void InvalidateWslCache()
        {
            _wslCacheFilled = false;
            _wslCacheTime = DateTime.MinValue;
        }

        /// <summary>
        /// 列出本机 WSL 发行版；结果缓存 10 分钟（面板刷新/收起下拉都会调用，避免频繁起进程）。
        /// forceRefresh=true 时忽略缓存。
        /// </summary>
        public static IReadOnlyList<WslDistroInfo> ListWslDistros(bool forceRefresh = false)
        {
            var expired = (DateTime.UtcNow - _wslCacheTime).TotalMinutes > 10d;
            if (_wslCacheFilled && !forceRefresh && !expired)
            {
                return CachedWslDistros;
            }

            CachedWslDistros.Clear();
            _wslCacheError = string.Empty;
            _wslCacheTime = DateTime.UtcNow;
            _wslCacheFilled = true;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-l -v",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                // wsl.exe 的输出是 UTF-16LE（无 BOM），必须显式指定，否则中文发行版名会乱码
                StandardOutputEncoding = Encoding.Unicode,
                StandardErrorEncoding = Encoding.Unicode,
            };

            try
            {
                using (var process = new System.Diagnostics.Process { StartInfo = psi })
                {
                    process.Start();
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(5000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // 进程可能刚好退出，忽略
                        }

                        _wslCacheError = "wsl.exe 响应超时（5 秒），请确认 WSL 已安装且没有被其它任务卡住";
                        return CachedWslDistros;
                    }

                    var output = outputTask.GetAwaiter().GetResult() ?? string.Empty;
                    var error = errorTask.GetAwaiter().GetResult() ?? string.Empty;
                    ParseDistroTable(output, CachedWslDistros);
                    if (CachedWslDistros.Count == 0)
                    {
                        _wslCacheError = string.IsNullOrWhiteSpace(error)
                            ? "未检测到已安装的 WSL 发行版（wsl.exe -l -v 无输出）"
                            : error.Trim();
                    }
                }
            }
            catch (Exception e)
            {
                _wslCacheError = "无法执行 wsl.exe：" + e.Message;
            }

            return CachedWslDistros;
        }

        /// <summary>默认发行版名（wsl.exe -l -v 里带 * 的那一行），没有则返回空串。</summary>
        public static string GetDefaultWslDistro()
        {
            foreach (var distro in ListWslDistros())
            {
                if (distro.IsDefault)
                {
                    return distro.Name;
                }
            }

            return string.Empty;
        }

        /// <summary>解析 wsl.exe -l -v 的表格：NAME  STATE  VERSION，默认项行首带 *。</summary>
        private static void ParseDistroTable(string output, List<WslDistroInfo> result)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return;
            }

            var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.Trim();
                if (trimmed.Length == 0 ||
                    trimmed.StartsWith("NAME", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    // 表头 / “适用于 Linux 的 Windows 子系统没有已安装的分发版” 之类的提示行
                    continue;
                }

                var isDefault = trimmed.StartsWith("*", StringComparison.Ordinal);
                var parts = trimmed.Replace("*", " ")
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 0)
                {
                    continue;
                }

                var info = new WslDistroInfo
                {
                    Name = parts[0],
                    State = parts.Length > 1 ? parts[1] : string.Empty,
                    Version = parts.Length > 2 ? parts[2] : string.Empty,
                    IsDefault = isDefault,
                };
                result.Add(info);
            }
        }

        /// <summary>按面板字段渲染 conf 文本。</summary>
        public static string Render(DebkitConfData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var pkg = SanitizePackageName(data.PackageName);
            var appTitle = string.IsNullOrWhiteSpace(data.AppTitle) ? pkg : data.AppTitle.Trim();
            var installDir = string.IsNullOrWhiteSpace(data.InstallDir) ? "/opt/" + pkg : data.InstallDir.Trim();
            var srcDir = string.IsNullOrWhiteSpace(data.SourceDir) ? DefaultSourceDirWsl() : ToWslPath(data.SourceDir);
            var version = string.IsNullOrWhiteSpace(data.Version) ? "1.0.0" : data.Version.Trim();
            var revision = string.IsNullOrWhiteSpace(data.Revision) ? "1" : data.Revision.Trim();

            var builder = new StringBuilder(1024);
            builder.Append("# 由 TEngine 打包工具「安装包配置」生成: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');

            void Line(string key, string value)
            {
                builder.Append(key).Append("=\"").Append(Escape(value)).Append("\"\n");
            }

            Line("APP_TITLE", appTitle);
            Line("PKG_NAME", pkg);
            Line("VERSION", version);
            Line("REVISION", revision);
            Line("MAINTAINER", string.IsNullOrWhiteSpace(data.Maintainer) ? "TEngine <noreply@example.com>" : data.Maintainer);
            Line("HOMEPAGE", data.Homepage ?? string.Empty);
            Line("SECTION", string.IsNullOrWhiteSpace(data.Section) ? "misc" : data.Section);
            Line("PRIORITY", string.IsNullOrWhiteSpace(data.Priority) ? "optional" : data.Priority);
            Line("ARCH", string.IsNullOrWhiteSpace(data.Arch) ? "amd64" : data.Arch);
            Line("DESC_SHORT", string.IsNullOrWhiteSpace(data.DescShort) ? appTitle : data.DescShort);
            Line("DESC_LONG", string.IsNullOrWhiteSpace(data.DescLong) ? appTitle : data.DescLong);
            Line("SRC_DIR", srcDir);
            Line("EXECUTABLE", string.IsNullOrWhiteSpace(data.Executable) ? appTitle : data.Executable.Trim());
            Line("LOGO", string.IsNullOrWhiteSpace(data.Logo) ? "logo.jpg" : data.Logo.Trim());
            Line("INSTALL_DIR", installDir);
            Line("CATEGORIES", string.IsNullOrWhiteSpace(data.Categories) ? "Utility;" : data.Categories);
            Line("GENERIC_NAME", string.IsNullOrWhiteSpace(data.GenericName) ? appTitle : data.GenericName);
            Line("KEYWORDS", data.Keywords ?? string.Empty);
            Line("EXTRA_ARGS", data.ExtraArgs ?? string.Empty);
            // 画质只接受数字索引或 none：非数字一律回落到 none，避免把错误索引写进配置文件
            Line("QUALITY_DEFAULT", NormalizeQuality(data.QualityDefault));
            Line("GRAPHICS_DEFAULT", NormalizeGraphics(data.GraphicsDefault));
            Line("AUTOSTART_DEFAULT", NormalizeYesNo(data.AutostartDefault, "yes"));
            Line("AUTOSTART_DELAY", NormalizeDelay(data.AutostartDelay));
            Line("CREATE_DESKTOP_ICON", NormalizeYesNo(data.CreateDesktopIcon, "yes"));
            Line("DEPENDS", string.IsNullOrWhiteSpace(data.Depends) ? DefaultDepends : data.Depends);
            Line("COMPRESS", NormalizeCompress(data.Compress));
            Line("COMPRESS_LEVEL", NormalizeLevel(data.CompressLevel));
            Line("EXCLUDE", data.Exclude ?? string.Empty);
            Line("WRITABLE_DIRS", data.WritableDirs ?? string.Empty);

            return builder.ToString();
        }

        /// <summary>写入 appConfigs/&lt;confFileName&gt;；文件名为空时按包名写入，返回写入路径。</summary>
        public static string WriteConfig(DebkitConfData data, string confFileName = null)
        {
            var path = string.IsNullOrWhiteSpace(confFileName)
                ? GetConfPath(data.PackageName)
                : GetConfPathByFile(confFileName);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // UTF8 无 BOM + 文本内只用 \n，保证 Linux 侧读到的是 LF
            File.WriteAllText(path, Render(data), new UTF8Encoding(false));
            return path;
        }

        /// <summary>读取已存在的 conf；只解析 KEY="value" 形式的行，未识别的键忽略。</summary>
        public static bool TryReadConfig(string path, out DebkitConfData data)
        {
            data = new DebkitConfData();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length <= 0 || line[0] == '#')
                {
                    continue;
                }

                var index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, index).Trim();
                var value = line.Substring(index + 1).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                {
                    value = value.Substring(1, value.Length - 2);
                }

                value = Unescape(value);
                switch (key)
                {
                    case "APP_TITLE": data.AppTitle = value; break;
                    case "PKG_NAME": data.PackageName = value; break;
                    case "VERSION": data.Version = value; break;
                    case "REVISION": data.Revision = value; break;
                    case "MAINTAINER": data.Maintainer = value; break;
                    case "HOMEPAGE": data.Homepage = value; break;
                    case "SECTION": data.Section = value; break;
                    case "PRIORITY": data.Priority = value; break;
                    case "ARCH": data.Arch = value; break;
                    case "DESC_SHORT": data.DescShort = value; break;
                    case "DESC_LONG": data.DescLong = value; break;
                    case "SRC_DIR": data.SourceDir = value; break;
                    case "EXECUTABLE": data.Executable = value; break;
                    case "LOGO": data.Logo = value; break;
                    case "INSTALL_DIR": data.InstallDir = value; break;
                    case "CATEGORIES": data.Categories = value; break;
                    case "GENERIC_NAME": data.GenericName = value; break;
                    case "KEYWORDS": data.Keywords = value; break;
                    case "EXTRA_ARGS": data.ExtraArgs = value; break;
                    case "QUALITY_DEFAULT": data.QualityDefault = value; break;
                    case "GRAPHICS_DEFAULT": data.GraphicsDefault = value; break;
                    case "AUTOSTART_DEFAULT": data.AutostartDefault = value; break;
                    case "AUTOSTART_DELAY": data.AutostartDelay = value; break;
                    case "CREATE_DESKTOP_ICON": data.CreateDesktopIcon = value; break;
                    case "DEPENDS": data.Depends = value; break;
                    case "COMPRESS": data.Compress = value; break;
                    case "COMPRESS_LEVEL": data.CompressLevel = value; break;
                    case "EXCLUDE": data.Exclude = value; break;
                    case "WRITABLE_DIRS": data.WritableDirs = value; break;
                }
            }

            return true;
        }

        /// <summary>与 lib/wizard.sh 保持一致的默认依赖列表。</summary>
        public const string DefaultDepends =
            "libc6, libstdc++6, libgl1 | libgl1-mesa-glx, libglu1-mesa | libglu1, libxcursor1, libxrandr2, " +
            "libxinerama1, libxi6, libx11-6, libasound2 | libasound2t64, libpulse0, libgtk-3-0 | libgtk-3-0t64";

        /// <summary>bash 双引号内的转义，换行统一压成空格（保持一行一值，回读更可靠）。</summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '$': builder.Append("\\$"); break;
                    case '`': builder.Append("\\`"); break;
                    case '\r': break;
                    case '\n': builder.Append(' '); break;
                    default: builder.Append(c); break;
                }
            }

            return builder.ToString();
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    var next = value[i + 1];
                    switch (next)
                    {
                        case '\\':
                        case '"':
                        case '$':
                        case '`':
                            builder.Append(next);
                            i++;
                            continue;
                    }
                }

                builder.Append(value[i]);
            }

            return builder.ToString();
        }

        private static string NormalizeQuality(string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length <= 0 || string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
            {
                return "none";
            }

            foreach (var c in text)
            {
                if (c < '0' || c > '9')
                {
                    return "none";
                }
            }

            return text;
        }

        private static string NormalizeGraphics(string value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();
            return text switch
            {
                "opengl" => "opengl",
                "vulkan" => "vulkan",
                _ => "none",
            };
        }

        private static string NormalizeYesNo(string value, string fallback)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();
            return text switch
            {
                "yes" => "yes",
                "no" => "no",
                _ => fallback,
            };
        }

        private static string NormalizeDelay(string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length <= 0)
            {
                return "3";
            }

            foreach (var c in text)
            {
                if (c < '0' || c > '9')
                {
                    return "3";
                }
            }

            return text;
        }

        private static string NormalizeCompress(string value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();
            return text switch
            {
                "auto" => "auto",
                "pigz" => "pigz",
                "gzip" => "gzip",
                "xz" => "xz",
                "zstd" => "zstd",
                "none" => "none",
                _ => "auto",
            };
        }

        private static string NormalizeLevel(string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length != 1 || text[0] < '1' || text[0] > '9')
            {
                return "6";
            }

            return text;
        }
    }

    /// <summary>一个 WSL 发行版（面板里的"容器"）条目，来自 wsl.exe -l -v。</summary>
    public sealed class WslDistroInfo
    {
        /// <summary>发行版名，用作 wsl.exe -d 的参数。</summary>
        public string Name = string.Empty;

        /// <summary>运行状态（Running / Stopped）。</summary>
        public string State = string.Empty;

        /// <summary>WSL 版本（1 / 2）。</summary>
        public string Version = string.Empty;

        /// <summary>是否为默认发行版（wsl.exe 不加 -d 时使用的那个）。</summary>
        public bool IsDefault;

        /// <summary>形如 "ai-dev（默认，Running）" 的显示名。</summary>
        public string DisplayName
        {
            get
            {
                var builder = new StringBuilder(Name);
                var tags = new List<string>(2);
                if (IsDefault)
                {
                    tags.Add("默认");
                }

                if (!string.IsNullOrEmpty(State))
                {
                    tags.Add(State);
                }

                if (tags.Count > 0)
                {
                    builder.Append('（').Append(string.Join("，", tags)).Append('）');
                }

                return builder.ToString();
            }
        }
    }

    /// <summary>debkit 应用配置（appConfigs/*.conf）字段。</summary>
    public class DebkitConfData
    {
        public string AppTitle = "";
        public string PackageName = "";
        public string Version = "1.0.0";
        public string Revision = "1";
        public string Maintainer = "";
        public string Homepage = "";
        public string Section = "misc";
        public string Priority = "optional";
        public string Arch = "amd64";
        public string DescShort = "";
        public string DescLong = "";
        public string SourceDir = "";
        public string Executable = "";
        public string Logo = "logo.jpg";
        public string InstallDir = "";
        public string Categories = "Utility;";
        public string GenericName = "";
        public string Keywords = "";
        public string ExtraArgs = "";
        public string QualityDefault = "none";
        public string GraphicsDefault = "none";
        public string AutostartDefault = "yes";
        public string AutostartDelay = "3";
        public string CreateDesktopIcon = "yes";
        public string Depends = DebkitConfig.DefaultDepends;
        public string Compress = "auto";
        public string CompressLevel = "6";
        public string Exclude = "";
        public string WritableDirs = "";
    }
}
