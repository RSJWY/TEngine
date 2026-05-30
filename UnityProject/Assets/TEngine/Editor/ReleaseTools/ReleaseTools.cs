using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;
using BuildResult = UnityEditor.Build.Reporting.BuildResult;

namespace TEngine
{
    /// <summary>
    /// 打包工具类。
    /// <remarks>通过CommandLineReader可以不前台开启Unity实现静默打包以及CLI工作流，详见CommandLineReader.cs example1</remarks>
    /// </summary>
    public static class ReleaseTools
    {
        #region CLI 入口

        public static void BuildDll()
        {
            string platform = CommandLineReader.GetCustomArgument("platform");
            if (string.IsNullOrEmpty(platform))
            {
                Debug.LogError($"Build Asset Bundle Error！platform is null");
                return;
            }

            BuildTarget target = GetBuildTarget(platform);

            // BuildDLLCommand.BuildAndCopyDlls(target);
        }

        public static void BuildAssetBundle()
        {
            string outputRoot = CommandLineReader.GetCustomArgument("outputRoot");
            if (string.IsNullOrEmpty(outputRoot))
            {
                Debug.LogError($"Build Asset Bundle Error！outputRoot is null");
                return;
            }

            string packageVersion = CommandLineReader.GetCustomArgument("packageVersion");
            if (string.IsNullOrEmpty(packageVersion))
            {
                Debug.LogError($"Build Asset Bundle Error！packageVersion is null");
                return;
            }

            string platform = CommandLineReader.GetCustomArgument("platform");
            if (string.IsNullOrEmpty(platform))
            {
                Debug.LogError($"Build Asset Bundle Error！platform is null");
                return;
            }

            BuildTarget target = GetBuildTarget(platform);
            BuildInternal(target, outputRoot, packageVersion);
            Debug.LogWarning($"Start BuildPackage BuildTarget:{target} outputPath:{outputRoot}");
        }

        #endregion

        #region MenuItem 入口（兼容原有菜单）

        [MenuItem("TEngine/Build/一键打包AssetBundle _F8")]
        public static void BuildCurrentPlatformAB()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildHotFixDll = true;
            BuildWithConfig(config, buildPlayer: false);
        }

        [MenuItem("TEngine/Build/一键打包Window", false, 30)]
        public static void AutomationBuild()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildTarget = BuildTarget.StandaloneWindows64;
            config.OutputRoot = Application.dataPath + "/../Builds/Windows";
            config.BuildPlayer = true;
            config.PlayerPlatform = BuildTarget.StandaloneWindows64;
            config.PlayerOutputPath = $"{Application.dataPath}/../Build/Windows/Release_Windows.exe";
            BuildWithConfig(config, buildPlayer: true);
        }

        [MenuItem("TEngine/Build/一键打包Android", false, 30)]
        public static void AutomationBuildAndroid()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildTarget = BuildTarget.Android;
            config.OutputRoot = Application.dataPath + "/../Bundles";
            config.BuildPlayer = true;
            config.PlayerPlatform = BuildTarget.Android;
            config.PlayerOutputPath = $"{Application.dataPath}/../Build/Android/{BuildConfig.GetDefaultPackageVersion()}Android.apk";
            BuildWithConfig(config, buildPlayer: true);
        }

        [MenuItem("TEngine/Build/一键打包IOS", false, 30)]
        public static void AutomationBuildIOS()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildTarget = BuildTarget.iOS;
            config.OutputRoot = Application.dataPath + "/../Bundles";
            config.BuildPlayer = true;
            config.PlayerPlatform = BuildTarget.iOS;
            config.PlayerOutputPath = $"{Application.dataPath}/../Build/IOS/XCode_Project";
            BuildWithConfig(config, buildPlayer: true);
        }

        #endregion

        #region 参数化构建入口

        /// <summary>
        /// 通过 BuildConfig 执行完整构建流程
        /// </summary>
        public static void BuildWithConfig(BuildConfig config, bool buildPlayer)
        {
            if (config.BuildHotFixDll)
            {
                Debug.Log("[BuildWithConfig] 编译热更DLL...");
                BuildDLLCommand.BuildAndCopyDlls();
            }

            AssetDatabase.Refresh();

            var runtimePackages = GetBuildPackages();
            YooAsset.Editor.BuildResult firstBuildResult = null;
            foreach (var runtimePackage in runtimePackages)
            {
                var buildResult = BuildInternalWithConfig(config, runtimePackage, firstBuildResult != null);
                if (!buildResult.Success)
                {
                    Debug.LogError($"[BuildWithConfig] AssetBundle构建失败: {runtimePackage.PackageName} - {buildResult.ErrorInfo}");
                    return;
                }

                firstBuildResult ??= buildResult;
                Debug.Log($"[BuildWithConfig] AssetBundle构建成功: {runtimePackage.PackageName} => {buildResult.OutputPackageDirectory}");
            }

            if (config.MinimalPackage && firstBuildResult != null)
            {
                ProcessMinimalPackage(runtimePackages.Select(x => x.PackageName).ToList(), config.PackageVersion,
                    config.RetainTags, firstBuildResult.OutputPackageDirectory);
            }

            AssetDatabase.Refresh();

            if (buildPlayer || config.BuildPlayer)
            {
                BuildImp(
                    BuildConfig.GetBuildTargetGroup(config.PlayerPlatform),
                    config.PlayerPlatform,
                    config.PlayerOutputPath
                );
            }
        }

        #endregion

        #region AssetBundle 构建

        private static YooAsset.Editor.BuildResult BuildInternalWithConfig(BuildConfig config, RuntimePackageEntry runtimePackage, bool appendBuildinFiles)
        {
            Debug.Log($"开始构建 : {config.BuildTarget} - {runtimePackage.PackageName}");

            IBuildPipeline pipeline;
            BuildParameters buildParameters;

            if (config.BuildPipeline == EBuildPipeline.BuiltinBuildPipeline)
            {
                var builtinBuildParameters = new BuiltinBuildParameters();
                pipeline = new BuiltinBuildPipeline();
                buildParameters = builtinBuildParameters;
                builtinBuildParameters.CompressOption = config.CompressOption;
            }
            else
            {
                var scriptableBuildParameters = new ScriptableBuildParameters();
                pipeline = new ScriptableBuildPipeline();
                buildParameters = scriptableBuildParameters;
                scriptableBuildParameters.CompressOption = config.CompressOption;
                scriptableBuildParameters.BuiltinShadersBundleName = GetBuiltinShaderBundleName(runtimePackage.PackageName);
                scriptableBuildParameters.ReplaceAssetPathWithAddress = Settings.UpdateSetting.GetReplaceAssetPathWithAddress();
            }

            string outputRoot = config.OutputRoot;
            if (!Path.IsPathRooted(outputRoot))
            {
                outputRoot = Path.Combine(Application.dataPath + "/../", outputRoot);
                outputRoot = Path.GetFullPath(outputRoot).Replace('\\', '/');
            }

            buildParameters.BuildOutputRoot = outputRoot;
            buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = config.BuildPipeline.ToString();
            buildParameters.BuildTarget = config.BuildTarget;
            buildParameters.BuildBundleType = (int)EBuildBundleType.AssetBundle;
            buildParameters.PackageName = runtimePackage.PackageName;
            buildParameters.PackageVersion = config.PackageVersion;
            buildParameters.VerifyBuildingResult = config.VerifyBuildingResult;
            buildParameters.EnableSharePackRule = config.EnableSharePackRule;
            buildParameters.FileNameStyle = config.FileNameStyle;
            buildParameters.BuildinFileCopyOption = GetBuildinFileCopyOption(config.BuildinFileCopyOption, appendBuildinFiles);
            buildParameters.BuildinFileCopyParams = string.Empty;
            buildParameters.EncryptionServices = GetEncryptionFromType(runtimePackage.EncryptionType);
            buildParameters.ClearBuildCacheFiles = config.ClearBuildCache;
            buildParameters.UseAssetDependencyDB = config.UseAssetDependencyDB;

            return pipeline.Run(buildParameters, true);
        }

        /// <summary>
        /// 旧版 BuildInternal，供 CLI 入口兼容
        /// </summary>
        private static void BuildInternal(BuildTarget buildTarget, string outputRoot, string packageVersion = "1.0",
            EBuildPipeline buildPipeline = EBuildPipeline.ScriptableBuildPipeline)
        {
            var config = BuildConfig.CreateDefault();
            config.BuildTarget = buildTarget;
            config.OutputRoot = outputRoot;
            config.PackageVersion = packageVersion;
            config.BuildPipeline = buildPipeline;
            config.BuildPlayer = false;
            config.BuildHotFixDll = false;

            var runtimePackages = GetBuildPackages();
            for (var i = 0; i < runtimePackages.Count; i++)
            {
                var runtimePackage = runtimePackages[i];
                var buildResult = BuildInternalWithConfig(config, runtimePackage, i > 0);
                if (buildResult.Success)
                {
                    Debug.Log($"构建成功 : {runtimePackage.PackageName} => {buildResult.OutputPackageDirectory}");
                }
                else
                {
                    Debug.LogError($"构建失败 : {runtimePackage.PackageName} => {buildResult.ErrorInfo}");
                    break;
                }
            }
        }

        #endregion

        #region 最小包后处理

        /// <summary>
        /// 读取文件的文本数据
        /// </summary>
        public static string ReadAllText(string filePath)
        {
            if (File.Exists(filePath) == false)
            {
                return null;
            }
            return File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// 最小包模式：删除 StreamingAssets 中不带保留 tag 的 .bundle 文件
        /// 使用构建输出的 BuildReport（JSON）获取 bundle 的 tag 信息
        /// </summary>
        public static void ProcessMinimalPackage(IReadOnlyList<string> packageNames, string packageVersion, string retainTags, string outputPackageDirectory)
        {
            string streamingRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();

            HashSet<string> retainFileNames = new HashSet<string>();
            string[] retainTagArray = ParseRetainTags(retainTags);

            foreach (var packageName in packageNames)
            {
                string reportFileName = YooAssetSettingsData.GetBuildReportFileName(packageName, packageVersion);
                string reportPath = $"{outputPackageDirectory}/{reportFileName}";
                if (!File.Exists(reportPath))
                {
                    Debug.LogError($"[最小包] 未找到构建报告: {reportPath}，跳过 {packageName} 处理");
                    continue;
                }

                YooAsset.Editor.BuildReport buildReport;
                try
                {
                    string jsonData = ReadAllText(reportPath);
                    buildReport = YooAsset.Editor.BuildReport.Deserialize(jsonData);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[最小包] 反序列化构建报告失败: {packageName} - {e.Message}");
                    continue;
                }

                if (retainTagArray.Length <= 0)
                {
                    continue;
                }

                foreach (var bundleInfo in buildReport.BundleInfos)
                {
                    if (bundleInfo.Tags != null && HasTag(bundleInfo.Tags, retainTagArray))
                    {
                        retainFileNames.Add(bundleInfo.FileName);
                    }
                }
            }

            if (retainTagArray.Length > 0)
            {
                Debug.Log($"[最小包] 保留 Tag: [{string.Join(", ", retainTagArray)}]，匹配 {retainFileNames.Count} 个 bundle");
            }

            if (!Directory.Exists(streamingRoot))
            {
                Debug.LogWarning($"[最小包] StreamingAssets 目录不存在: {streamingRoot}");
                return;
            }

            string[] bundleFiles = Directory.GetFiles(streamingRoot, "*.bundle", SearchOption.AllDirectories);
            int deletedCount = 0;
            int retainedCount = 0;

            foreach (var file in bundleFiles)
            {
                string fileName = Path.GetFileName(file);
                if (retainFileNames.Contains(fileName))
                {
                    retainedCount++;
                    Debug.Log($"[最小包] 保留: {fileName}");
                }
                else
                {
                    File.Delete(file);
                    deletedCount++;
                    Debug.Log($"[最小包] 删除: {fileName}");
                }
            }

            Debug.Log($"[最小包] 处理完成 - 删除 {deletedCount} 个 .bundle，保留 {retainedCount} 个 .bundle");
            CleanEmptyDirectories(streamingRoot);
        }

        private static EBuildinFileCopyOption GetBuildinFileCopyOption(EBuildinFileCopyOption option, bool appendBuildinFiles)
        {
            if (!appendBuildinFiles)
            {
                return option;
            }

            return option switch
            {
                EBuildinFileCopyOption.ClearAndCopyAll => EBuildinFileCopyOption.OnlyCopyAll,
                EBuildinFileCopyOption.ClearAndCopyByTags => EBuildinFileCopyOption.OnlyCopyByTags,
                _ => option
            };
        }

        private static List<RuntimePackageEntry> GetBuildPackages()
        {
            var runtimePackages = Settings.UpdateSetting != null
                ? Settings.UpdateSetting.GetEnabledRuntimePackages()
                : null;
            if (runtimePackages != null && runtimePackages.Count > 0)
            {
                return runtimePackages;
            }

            return new List<RuntimePackageEntry>
            {
                new RuntimePackageEntry
                {
                    Enable = true,
                    PackageName = "DefaultPackage",
                    InitOnStartup = true,
                    UpdateManifestOnStartup = true,
                    DownloadOnDemand = true,
                    SaveVersion = true,
                    VersionKey = "GAME_VERSION",
                }
            };
        }

        private static bool HasTag(string[] bundleTags, string[] matchTags)
        {
            foreach (var matchTag in matchTags)
            {
                foreach (var bundleTag in bundleTags)
                {
                    if (bundleTag == matchTag)
                        return true;
                }
            }
            return false;
        }

        private static string[] ParseRetainTags(string retainTags)
        {
            if (string.IsNullOrWhiteSpace(retainTags))
                return Array.Empty<string>();

            return retainTags
                .Split(',', '，')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();
        }

        private static void CleanEmptyDirectories(string rootPath)
        {
            foreach (var dir in Directory.GetDirectories(rootPath))
            {
                CleanEmptyDirectories(dir);
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
        }

        #endregion

        #region Player 构建

        public static void BuildImp(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget, string locationPathName)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
            AssetDatabase.Refresh();

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Select(scene => scene.path).ToArray(),
                locationPathName = locationPathName,
                targetGroup = buildTargetGroup,
                target = buildTarget,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Build success: {summary.totalSize / 1024 / 1024} MB, {summary.outputPath}");
            }
            else
            {
                Debug.Log($"Build Failed" + summary.result);
            }
        }

        #endregion

        #region 工具方法

        private static BuildTarget GetBuildTarget(string platform)
        {
            BuildTarget target = BuildTarget.NoTarget;
            switch (platform)
            {
                case "Android":
                    target = BuildTarget.Android;
                    break;
                case "IOS":
                    target = BuildTarget.iOS;
                    break;
                case "Windows":
                    target = BuildTarget.StandaloneWindows64;
                    break;
                case "MacOS":
                    target = BuildTarget.StandaloneOSX;
                    break;
                case "Linux":
                    target = BuildTarget.StandaloneLinux64;
                    break;
                case "WebGL":
                    target = BuildTarget.WebGL;
                    break;
                case "Switch":
                    target = BuildTarget.Switch;
                    break;
                case "PS4":
                    target = BuildTarget.PS4;
                    break;
                case "PS5":
                    target = BuildTarget.PS5;
                    break;
            }

            return target;
        }

        private static string GetBuiltinShaderBundleName(string packageName)
        {
            var uniqueBundleName = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
            var packRuleResult = DefaultPackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
        }

        /// <summary>
        /// 根据 EncryptionType 枚举获取加密服务
        /// </summary>
        private static IEncryptionServices GetEncryptionFromType(EncryptionType encryptionType)
        {
            return encryptionType switch
            {
                EncryptionType.FileOffSet => new FileOffsetEncryption(),
                EncryptionType.FileStream => new FileStreamEncryption(),
                EncryptionType.XXTEA => new XXTEAEncryption(),
                _ => null
            };
        }

        /// <summary>
        /// 根据 ResourceModuleDriver 的 encryptionType 获取对应的加密服务（旧版兼容）
        /// </summary>
        private static IEncryptionServices GetEncryptionFromResourceModuleDriver()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab GameEntry");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[BuildInternal] Failed to find GameEntry.prefab");
                return null;
            }

            var gameEntryPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var gameEntryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gameEntryPath);
            if (gameEntryPrefab == null)
            {
                Debug.LogWarning("[BuildInternal] Failed to load GameEntry.prefab");
                return null;
            }

            var resourceModuleDriver = gameEntryPrefab.GetComponentInChildren<ResourceModuleDriver>();
            if (resourceModuleDriver == null)
            {
                Debug.LogWarning("[BuildInternal] ResourceModuleDriver not found in GameEntry.prefab");
                return null;
            }

            var encryptionType = resourceModuleDriver.EncryptionType;
            Debug.Log($"[BuildInternal] Use EncryptionType from ResourceModuleDriver: {encryptionType}");

            return GetEncryptionFromType(encryptionType);
        }

        private static string GetBuildPackageVersion()
        {
            int totalMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            return DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
        }

        #endregion
    }
}
