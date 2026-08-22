using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TEngine
{
    public class BuildConfig
    {
        // 基础设置
        public BuildTarget BuildTarget;
        public EBuildPipeline BuildPipeline = EBuildPipeline.ScriptableBuildPipeline;
        public ECompressOption CompressOption = ECompressOption.LZ4;
        public string PackageVersion = "";
        public string OutputRoot = "./Output/Bundles/";

        // 发布整理设置
        public bool EnablePublishCopy;
        public string PublishRoot = "./Output/Publish/";
        public bool CleanPublishPackageDirectory = true;

        // 最小包设置
        public bool MinimalPackage;
        public string RetainTags = "";

        // 高级设置
        public bool EnableSharePackRule = true;
        public bool UseAssetDependencyDB = true;
        public bool ClearBuildCache;
        public bool VerifyBuildingResult = true;
        public EBuildinFileCopyOption BuildinFileCopyOption = EBuildinFileCopyOption.ClearAndCopyAll;
        public EFileNameStyle FileNameStyle = EFileNameStyle.BundleName_HashName;

        // 热更DLL设置
        public bool BuildHotFixDll = true;

        // 打包Player设置
        public bool BuildPlayer;
        public BuildTarget PlayerPlatform;
        public string PlayerOutputPath = "";

        public static BuildConfig CreateDefault()
        {
            return new BuildConfig
            {
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PlayerPlatform = EditorUserBuildSettings.activeBuildTarget,
                PackageVersion = GetDefaultPackageVersion(),
                OutputRoot = "./Output/Bundles/",
                PublishRoot = "./Output/Publish/",
                CleanPublishPackageDirectory = true,
                PlayerOutputPath = GetDefaultPlayerOutputPath(EditorUserBuildSettings.activeBuildTarget),
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
            string basePath = Application.dataPath + "/../Output/Player/";
            // 可执行文件名采用 PlayerSettings.productName，统一各平台输出名
            string executableName = GetExecutableNameFromProductName();
            return target switch
            {
                BuildTarget.StandaloneWindows64 => basePath + "Windows/" + executableName + ".exe",
                BuildTarget.Android => basePath + $"Android/{GetDefaultPackageVersion()}Android.apk",
                BuildTarget.iOS => basePath + "IOS/XCode_Project",
                BuildTarget.StandaloneOSX => basePath + "MacOS/" + executableName + ".app",
                BuildTarget.StandaloneLinux64 => basePath + "Linux/" + executableName,
                BuildTarget.WebGL => basePath + "WebGL",
                _ => basePath + target + "/" + executableName
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
