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

        // 打包Player设置
        public bool BuildPlayer;
        public BuildTarget PlayerPlatform;
        public string PlayerOutputPath = "";

        // InnoSetup 安装包设置（仅 Windows）
        public bool BuildInstaller;
        public BuildTarget InstallerPlatform = BuildTarget.StandaloneWindows64;
        public string InstallerVersion = "";
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

        public static BuildConfig CreateDefault()
        {
            return new BuildConfig
            {
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PlayerPlatform = EditorUserBuildSettings.activeBuildTarget,
                PackageVersion = GetDefaultPackageVersion(),
                OutputRoot = "./Releases/Bundles/",
                PublishRoot = "./Releases/Publish/",
                CleanPublishPackageDirectory = true,
                PlayerOutputPath = GetDefaultPlayerOutputPath(EditorUserBuildSettings.activeBuildTarget),
                InstallerAppName = PlayerSettings.productName ?? string.Empty,
                InstallerPublisher = PlayerSettings.companyName ?? string.Empty,
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
}
