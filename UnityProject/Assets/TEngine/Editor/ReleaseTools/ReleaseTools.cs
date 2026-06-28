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
using Debug = UnityEngine.Debug;

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
        public static void BuildWithConfig(BuildConfig config, bool buildPlayer, string packageName = null)
        {
            var runtimePackages = GetBuildPackages(packageName);
            if (runtimePackages.Count <= 0)
            {
                Debug.LogError($"[BuildWithConfig] 未找到可构建的资源包: {packageName}");
                return;
            }

            AssetDatabase.Refresh();

            YooAsset.Editor.BuildResult firstBuildResult = null;
            var hotFixDllBuilt = false;
            foreach (var runtimePackage in runtimePackages)
            {
                if (config.BuildHotFixDll && !hotFixDllBuilt && IsAssemblyPackage(runtimePackage.PackageName))
                {
                    Debug.Log($"[BuildWithConfig] 构建 {runtimePackage.PackageName} 前同步AOT元数据清单并编译热更DLL...");
                    BuildDLLCommand.BuildAndCopyDlls();
                    AssetDatabase.Refresh();
                    hotFixDllBuilt = true;
                }

                var buildResult = BuildInternalWithConfig(config, runtimePackage, firstBuildResult != null);
                if (!buildResult.Success)
                {
                    Debug.LogError($"[BuildWithConfig] AssetBundle构建失败: {runtimePackage.PackageName} - {buildResult.ErrorInfo}");
                    return;
                }

                firstBuildResult ??= buildResult;
                Debug.Log($"[BuildWithConfig] AssetBundle构建成功: {runtimePackage.PackageName} => {buildResult.OutputPackageDirectory}");

                if (config.EnablePublishCopy)
                {
                    PublishBuiltPackage(config, runtimePackage.PackageName, buildResult.OutputPackageDirectory);
                }
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
            var buildPipeline = ResolveBuildPipeline(config, runtimePackage);
            Debug.Log($"开始构建 : {config.BuildTarget} - {runtimePackage.PackageName} - {buildPipeline}");

            IBuildPipeline pipeline;
            BuildParameters buildParameters;

            switch (buildPipeline)
            {
                case EBuildPipeline.RawFileBuildPipeline:
                {
                    pipeline = new RawFileBuildPipeline();
                    buildParameters = new RawFileBuildParameters();
                    break;
                }
                default:
                {
                    var scriptableBuildParameters = new ScriptableBuildParameters();
                    pipeline = new ScriptableBuildPipeline();
                    buildParameters = scriptableBuildParameters;
                    scriptableBuildParameters.CompressOption = config.CompressOption;
                    scriptableBuildParameters.BuiltinShadersBundleName = GetBuiltinShaderBundleName(runtimePackage.PackageName);
                    scriptableBuildParameters.ReplaceAssetPathWithAddress = Settings.UpdateSetting.GetReplaceAssetPathWithAddress();
                    break;
                }
            }

            string outputRoot = config.OutputRoot;
            if (!Path.IsPathRooted(outputRoot))
            {
                outputRoot = Path.Combine(Application.dataPath + "/../", outputRoot);
                outputRoot = Path.GetFullPath(outputRoot).Replace('\\', '/');
            }

            buildParameters.BuildOutputRoot = outputRoot;
            buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = buildPipeline.ToString();
            buildParameters.BuildTarget = config.BuildTarget;
            buildParameters.BuildBundleType = GetBuildBundleType(buildPipeline);
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

        #region 发布整理

        public static string GetResolvedOutputRoot(BuildConfig config)
        {
            var outputRoot = string.IsNullOrWhiteSpace(config.OutputRoot) ? "./Builds/" : config.OutputRoot;
            if (!Path.IsPathRooted(outputRoot))
            {
                outputRoot = Path.Combine(Application.dataPath + "/../", outputRoot);
            }

            return Path.GetFullPath(outputRoot).Replace('\\', '/');
        }

        public static string GetBuildPlatformOutputRoot(BuildConfig config)
        {
            var outputRoot = GetResolvedOutputRoot(config);
            return Path.Combine(outputRoot, config.BuildTarget.ToString()).Replace('\\', '/');
        }

        public static string GetPublishOutputRoot(BuildConfig config)
        {
            var publishRoot = string.IsNullOrWhiteSpace(config.PublishRoot) ? "./Publish/" : config.PublishRoot;
            if (!Path.IsPathRooted(publishRoot))
            {
                publishRoot = Path.Combine(Application.dataPath + "/../", publishRoot);
            }

            return Path.GetFullPath(publishRoot).Replace('\\', '/');
        }

        public static string GetRemotePlatformName(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.StandaloneWindows64 => "Windows64",
                BuildTarget.StandaloneOSX => "MacOS",
                BuildTarget.StandaloneLinux64 => "Linux",
                BuildTarget.Android => "Android",
                BuildTarget.iOS => "IOS",
                BuildTarget.WebGL => "WebGL",
                BuildTarget.PS5 => "PS5",
                _ => target.ToString()
            };
        }

        public static List<string> GetPublishableVersions(BuildConfig config)
        {
            var runtimePackages = GetBuildPackages();
            if (runtimePackages.Count <= 0)
            {
                return new List<string>();
            }

            var versionTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            var candidateVersions = new HashSet<string>(StringComparer.Ordinal);
            var isFirstPackage = true;

            foreach (var runtimePackage in runtimePackages)
            {
                var packageVersions = GetPackageVersionDirectories(config, runtimePackage.PackageName);
                if (isFirstPackage)
                {
                    foreach (var packageVersion in packageVersions)
                    {
                        candidateVersions.Add(packageVersion.Key);
                        versionTimes[packageVersion.Key] = packageVersion.Value;
                    }

                    isFirstPackage = false;
                    continue;
                }

                candidateVersions.IntersectWith(packageVersions.Keys);
                foreach (var version in candidateVersions.ToArray())
                {
                    if (packageVersions.TryGetValue(version, out var lastWriteTimeUtc) && versionTimes.TryGetValue(version, out var existingTime))
                    {
                        versionTimes[version] = existingTime > lastWriteTimeUtc ? existingTime : lastWriteTimeUtc;
                    }
                }
            }

            return candidateVersions
                .OrderByDescending(version => versionTimes.TryGetValue(version, out var lastWriteTimeUtc)
                    ? lastWriteTimeUtc
                    : DateTime.MinValue)
                .ThenByDescending(version => version, StringComparer.Ordinal)
                .ToList();
        }

        public static bool PublishFromExistingBuild(BuildConfig config, string packageVersion)
        {
            if (string.IsNullOrWhiteSpace(packageVersion))
            {
                Debug.LogError("[Publish] 发布整理失败：版本号为空。");
                return false;
            }

            var runtimePackages = GetBuildPackages();
            var packageDirectories = new List<(string PackageName, string SourceDirectory)>();
            foreach (var runtimePackage in runtimePackages)
            {
                var sourceDirectory = GetPackageVersionDirectory(config, runtimePackage.PackageName, packageVersion);
                if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                {
                    Debug.LogError($"[Publish] 发布整理失败：未找到版本目录 {runtimePackage.PackageName}/{packageVersion}");
                    return false;
                }

                packageDirectories.Add((runtimePackage.PackageName, sourceDirectory));
            }

            foreach (var packageDirectory in packageDirectories)
            {
                PublishBuiltPackage(config, packageDirectory.PackageName, packageDirectory.SourceDirectory, packageVersion);
            }

            Debug.Log($"[Publish] 已按版本整理完成：{packageVersion} => {GetPublishOutputRoot(config)}");
            return true;
        }

        private static void PublishBuiltPackage(BuildConfig config, string packageName, string outputPackageDirectory)
        {
            PublishBuiltPackage(config, packageName, outputPackageDirectory, config.PackageVersion);
        }

        private static void PublishBuiltPackage(BuildConfig config, string packageName, string outputPackageDirectory, string packageVersion)
        {
            if (string.IsNullOrWhiteSpace(outputPackageDirectory) || !Directory.Exists(outputPackageDirectory))
            {
                Debug.LogWarning($"[Publish] 构建输出目录不存在，跳过整理: {packageName} => {outputPackageDirectory}");
                return;
            }

            var projectName = Settings.UpdateSetting != null ? Settings.UpdateSetting.GetProjectName() : "Demo";
            var publishRoot = GetPublishOutputRoot(config);
            var remotePlatformName = GetRemotePlatformName(config.BuildTarget);
            var targetDirectory = Path.Combine(publishRoot, projectName, remotePlatformName, packageName);
            targetDirectory = Path.GetFullPath(targetDirectory).Replace('\\', '/');

            if (config.CleanPublishPackageDirectory && Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }

            Directory.CreateDirectory(targetDirectory);
            CopyDirectory(outputPackageDirectory, targetDirectory);

            var versionRecordPath = Path.Combine(targetDirectory, "_build_version.txt");
            File.WriteAllText(versionRecordPath, packageVersion ?? string.Empty);
            Debug.Log($"[Publish] 发布整理完成: {packageName} => {targetDirectory}");
        }

        private static Dictionary<string, DateTime> GetPackageVersionDirectories(BuildConfig config, string packageName)
        {
            var packageRoot = Path.Combine(GetBuildPlatformOutputRoot(config), packageName);
            if (!Directory.Exists(packageRoot))
            {
                return new Dictionary<string, DateTime>(StringComparer.Ordinal);
            }

            var packageVersions = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (var directory in Directory.GetDirectories(packageRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var directoryName = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(directoryName) || string.Equals(directoryName, "OutputCache", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                packageVersions[directoryName] = Directory.GetLastWriteTimeUtc(directory);
            }

            return packageVersions;
        }

        private static string GetPackageVersionDirectory(BuildConfig config, string packageName, string packageVersion)
        {
            var packageRoot = Path.Combine(GetBuildPlatformOutputRoot(config), packageName, packageVersion);
            return Path.GetFullPath(packageRoot).Replace('\\', '/');
        }

        private static void CopyDirectory(string sourceDirectory, string targetDirectory)
        {
            foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
            }

            foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, file);
                var targetFile = Path.Combine(targetDirectory, relativePath);
                var targetParent = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetParent))
                {
                    Directory.CreateDirectory(targetParent);
                }

                File.Copy(file, targetFile, true);
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

        private static EBuildPipeline ResolveBuildPipeline(BuildConfig config, RuntimePackageEntry runtimePackage)
        {
            return runtimePackage.BuildPipeline switch
            {
                RuntimePackageBuildPipeline.ScriptableBuildPipeline => EBuildPipeline.ScriptableBuildPipeline,
                RuntimePackageBuildPipeline.BuiltinBuildPipeline => EBuildPipeline.ScriptableBuildPipeline,
                RuntimePackageBuildPipeline.RawFileBuildPipeline => EBuildPipeline.RawFileBuildPipeline,
                _ => config.BuildPipeline,
            };
        }

        private static int GetBuildBundleType(EBuildPipeline buildPipeline)
        {
            return buildPipeline == EBuildPipeline.RawFileBuildPipeline
                ? (int)EBuildBundleType.RawBundle
                : (int)EBuildBundleType.AssetBundle;
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

        private static List<RuntimePackageEntry> GetBuildPackages(string packageName = null)
        {
            var runtimePackages = Settings.UpdateSetting != null
                ? Settings.UpdateSetting.GetEnabledRuntimePackages()
                : null;
            var buildPackages = runtimePackages != null && runtimePackages.Count > 0
                ? runtimePackages
                : new List<RuntimePackageEntry>
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
                        BuildPipeline = RuntimePackageBuildPipeline.UseGlobal,
                    }
                };

            if (string.IsNullOrWhiteSpace(packageName))
            {
                return buildPackages;
            }

            return buildPackages
                .Where(x => x != null && string.Equals(x.PackageName, packageName, StringComparison.Ordinal))
                .ToList();
        }

        private static bool IsAssemblyPackage(string packageName)
        {
            var assemblyPackageName = Settings.UpdateSetting != null
                ? Settings.UpdateSetting.GetAssemblyPackageName()
                : "CodePackage";
            return string.Equals(packageName, assemblyPackageName, StringComparison.Ordinal);
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
