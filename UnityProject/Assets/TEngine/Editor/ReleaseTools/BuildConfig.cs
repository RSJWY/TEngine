using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TEngine
{
    /// <summary>
    /// 资源包版本号模式。
    /// </summary>
    public enum PackageVersionMode
    {
        /// <summary>所有包共用同一个版本号。</summary>
        Unified,
        /// <summary>每个包使用独立的版本号。</summary>
        PerPackage,
    }

    public class BuildConfig
    {
        // 基础设置
        public BuildTarget BuildTarget;
        public EBuildPipeline BuildPipeline = EBuildPipeline.ScriptableBuildPipeline;
        public ECompressOption CompressOption = ECompressOption.LZ4;
        public string PackageVersion = "";
        public PackageVersionMode PackageVersionMode = PackageVersionMode.Unified;
        /// <summary>PerPackage 模式下按包名存储独立版本号；Unified 模式下不使用。</summary>
        public Dictionary<string, string> PackageVersionMap = new Dictionary<string, string>();
        public string OutputRoot = "./Releases/Bundles/";

        // 发布整理设置
        public bool EnablePublishCopy;
        public string PublishRoot = "./Releases/Publish/";
        public bool CleanPublishPackageDirectory = true;

        // 最小包设置
        public bool MinimalPackage;
        public string RetainTags = "";

        // 高级设置
        public bool EnableSharePackRule = true;
        public bool EnableAssetPathValidation = true;
        public bool UseAssetDependencyDB = true;
        public bool ClearBuildCache;
        public bool VerifyBuildingResult = true;
        public EBundledCopyOption BuildinFileCopyOption = EBundledCopyOption.ClearAndCopyAll;
        public EFileNameStyle FileNameStyle = EFileNameStyle.BundleName_HashName;
        public bool GenerateCatalogInOutput = false;

        // 热更DLL设置
        public bool BuildHotFixDll = true;

        // 打包Player设置（Player 平台统一使用 BuildTarget，不再单独配置）
        public bool BuildPlayer;
        public string PlayerOutputPath = "";
        // 构建 Player 前清空输出目录（输出文件所在目录），避免上一版残留文件混入产物/安装包
        public bool CleanPlayerOutputDirectory = true;

        // InnoSetup 安装包设置（仅 Windows）
        public bool BuildInstaller;
        public BuildTarget InstallerPlatform = BuildTarget.StandaloneWindows64;
        public string IsccPath = "";
        // 应用显示名(中文软件名),回写 setup.iss MyAppName;默认取 PlayerSettings.productName
        public string InstallerAppName = "";
        // 软件英文名,回写 setup.iss MyAppEnglishName;仅用于安装目录,为空时回退用 InstallerAppName
        public string InstallerAppEnglishName = "";
        // 发布者,回写 MyAppPublisher;默认取 PlayerSettings.companyName
        public string InstallerPublisher = "";
        // 安装密码,空表示不加密,回写 MyAppPassword
        public string InstallerPassword = "";
        // 向导水印,回写 BrandWatermark;为空时回退用 Publisher
        public string InstallerWatermark = "";
        // 选中的 Inno Setup 脚本(Releases/Windows/<name>.iss);为空回退 setup.iss
        public string InstallerIssScript = "";

        // debkit 安装包设置（Linux；构建工具写入 Releases/Linux/appConfigs/<包名>.conf,打包仍在 WSL 里执行）
        // 应用显示名,回写 APP_TITLE;为空时取 PlayerSettings.productName
        public string DebkitAppTitle = "";
        // Debian 包名,回写 PKG_NAME;默认由 productName 归一化(全小写,仅 a-z0-9 . + -)
        public string DebkitPackageName = "";
        // 主可执行文件名,回写 EXECUTABLE;默认取 Linux Player 产物名
        public string DebkitExecutable = "";
        // 图标源图(相对 SRC_DIR),回写 LOGO
        public string DebkitLogo = "";
        // 安装目录,回写 INSTALL_DIR;为空时用 /opt/<包名>
        public string DebkitInstallDir = "";
        // 打包源目录,回写 SRC_DIR;为空时用 Releases/Linux/build
        public string DebkitSourceDir = "";
        // 默认画质索引(none 表示不生成),回写 QUALITY_DEFAULT
        public string DebkitQualityDefault = "none";
        // 默认图形接口 opengl/vulkan/none,回写 GRAPHICS_DEFAULT
        public string DebkitGraphicsDefault = "none";
        // 装完默认开机自启 yes/no,回写 AUTOSTART_DEFAULT
        public string DebkitAutostart = "yes";
        // 自启延迟秒数,回写 AUTOSTART_DELAY
        public string DebkitAutostartDelay = "3";
        // 装包时是否建桌面快捷方式 yes/no,回写 CREATE_DESKTOP_ICON
        public string DebkitCreateDesktopIcon = "yes";
        // 压缩方式 auto/pigz/gzip/xz/zstd/none,回写 COMPRESS
        public string DebkitCompress = "auto";
        // 压缩级别 1-9,回写 COMPRESS_LEVEL
        public string DebkitCompressLevel = "6";
        // 依赖库(逗号分隔,| 表示二选一),回写 DEPENDS;为空时用 debkit 默认列表
        public string DebkitDepends = "";
        // 默认启动参数,回写 EXTRA_ARGS
        public string DebkitExtraArgs = "";
        // 需要交给运行用户写的安装目录子目录,回写 WRITABLE_DIRS
        public string DebkitWritableDirs = "";
        // 要从包里剔除的通配符,回写 EXCLUDE
        public string DebkitExclude = "";
        // 执行打包用的 WSL 发行版(wsl.exe -d 参数);为空表示用默认发行版。只影响面板命令,不写进 conf
        public string DebkitWslDistro = "";
        // 菜单分类,回写 CATEGORIES
        public string DebkitCategories = "Utility;";
        // 选中的 debkit 构建配置(Releases/Linux/appConfigs/<name>.conf);为空回退 <包名>.conf
        public string DebkitConfFile = "";

        // CLI/batchmode 模式：跳过一切交互对话框（如 pdb 残留确认，自动清理继续），日志哨兵照常输出
        public bool HeadlessMode;

        /// <summary>构建记录：每个资源包的输出目录与体积，供 CLI 结构化结果回传。</summary>
        public readonly List<PackageBuildRecord> PackageRecords = new List<PackageBuildRecord>();

        public static BuildConfig CreateDefault()
        {
            return new BuildConfig
            {
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PackageVersion = GetDefaultPackageVersion(),
                OutputRoot = "./Releases/Bundles/",
                PublishRoot = "./Releases/Publish/",
                CleanPublishPackageDirectory = true,
                PlayerOutputPath = GetDefaultPlayerOutputPath(EditorUserBuildSettings.activeBuildTarget),
                InstallerAppName = PlayerSettings.productName ?? string.Empty,
                InstallerPublisher = PlayerSettings.companyName ?? string.Empty,
                // debkit 默认值全部由 productName 派生，与 Linux Player 产物名保持一致
                DebkitAppTitle = PlayerSettings.productName ?? string.Empty,
                DebkitPackageName = DebkitConfig.SanitizePackageName(PlayerSettings.productName),
                DebkitExecutable = System.IO.Path.GetFileName(GetDefaultPlayerOutputPath(BuildTarget.StandaloneLinux64)),
                DebkitLogo = "logo.jpg",
            };
        }

        public static List<string> GetDefaultPackageNames()
        {
            var packageNames = new List<string>();
            var runtimePackages = Settings.UpdateSetting != null ? Settings.UpdateSetting.GetEnabledRuntimePackages() : null;
            if (runtimePackages != null)
            {
                foreach (var runtimePackage in runtimePackages)
                {
                    if (runtimePackage == null || string.IsNullOrWhiteSpace(runtimePackage.PackageName))
                    {
                        continue;
                    }

                    var packageName = runtimePackage.PackageName.Trim();
                    if (!packageNames.Contains(packageName))
                    {
                        packageNames.Add(packageName);
                    }
                }
            }

            if (packageNames.Count <= 0)
            {
                packageNames.Add("DefaultPackage");
            }

            return packageNames;
        }

        public static string GetDefaultPackageVersion()
        {
            int totalMinutes = System.DateTime.Now.Hour * 60 + System.DateTime.Now.Minute;
            return System.DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
        }

        public static string GetDefaultPlayerOutputPath(BuildTarget target)
        {
            // 所有平台 Player 产物统一归到 Releases/{平台}/build/，与 InnoSetup 安装包目录平级；
            // 可执行文件名采用 PlayerSettings.productName，统一各平台输出名。
            // 统一使用项目根相对路径（./ 前缀），由构建链路在使用处转为绝对路径。
            string executableName = GetExecutableNameFromProductName();
            return target switch
            {
                BuildTarget.StandaloneWindows64 => "./Releases/Windows/build/" + executableName + ".exe",
                BuildTarget.StandaloneLinux64 => "./Releases/Linux/build/" + executableName,
                BuildTarget.Android => "./Releases/Android/build/" + GetDefaultPackageVersion() + "Android.apk",
                BuildTarget.iOS => "./Releases/IOS/build/XCode_Project",
                BuildTarget.StandaloneOSX => "./Releases/MacOS/build/" + executableName + ".app",
                BuildTarget.WebGL => "./Releases/WebGL/build",
                _ => "./Releases/" + target + "/build/" + executableName
            };
        }

        /// <summary>
        /// 以 PlayerSettings.productName 作为可执行文件名，过滤非法字符；为空时回退到 "Release"。
        /// </summary>
        private static string GetExecutableNameFromProductName()
        {
            const string fallback = "Release";
            var rawName = PlayerSettings.productName;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return fallback;
            }

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(rawName.Length);
            foreach (var c in rawName.Trim())
            {
                if (System.Array.IndexOf(invalid, c) < 0)
                {
                    builder.Append(c);
                }
            }

            var name = builder.ToString().Trim();
            return string.IsNullOrEmpty(name) ? fallback : name;
        }

        public static BuildTargetGroup GetBuildTargetGroup(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.StandaloneWindows64 => BuildTargetGroup.Standalone,
                BuildTarget.StandaloneOSX => BuildTargetGroup.Standalone,
                BuildTarget.StandaloneLinux64 => BuildTargetGroup.Standalone,
                BuildTarget.Android => BuildTargetGroup.Android,
                BuildTarget.iOS => BuildTargetGroup.iOS,
                BuildTarget.WebGL => BuildTargetGroup.WebGL,
                BuildTarget.Switch => BuildTargetGroup.Switch,
                BuildTarget.PS4 => BuildTargetGroup.PS4,
                BuildTarget.PS5 => BuildTargetGroup.PS5,
                _ => BuildTargetGroup.Standalone
            };
        }
    }

    /// <summary>单个资源包的构建结果记录。</summary>
    public sealed class PackageBuildRecord
    {
        public string PackageName;
        public string PackageVersion;
        public string OutputDirectory;
        /// <summary>输出目录字节数（含 OutputCache 清理后的最终产物）。</summary>
        public long OutputSizeBytes;
    }
}
