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
            config.OutputRoot = Application.dataPath + "/../Releases/Bundles";
            config.BuildPlayer = true;
            config.PlayerPlatform = BuildTarget.StandaloneWindows64;
            config.PlayerOutputPath = BuildConfig.GetDefaultPlayerOutputPath(BuildTarget.StandaloneWindows64);
            BuildWithConfig(config, buildPlayer: true);
        }

        [MenuItem("TEngine/Build/一键打包Android", false, 30)]
        public static void AutomationBuildAndroid()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildTarget = BuildTarget.Android;
            config.OutputRoot = Application.dataPath + "/../Releases/Bundles";
            config.BuildPlayer = true;
            config.PlayerPlatform = BuildTarget.Android;
            config.PlayerOutputPath = BuildConfig.GetDefaultPlayerOutputPath(BuildTarget.Android);
            BuildWithConfig(config, buildPlayer: true);
        }

        [MenuItem("TEngine/Build/一键打包IOS", false, 30)]
        public static void AutomationBuildIOS()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildTarget = BuildTarget.iOS;
            config.OutputRoot = Application.dataPath + "/../Releases/Bundles";
            config.BuildPlayer = true;
            config.PlayerPlatform = BuildTarget.iOS;
            config.PlayerOutputPath = BuildConfig.GetDefaultPlayerOutputPath(BuildTarget.iOS);
            BuildWithConfig(config, buildPlayer: true);
        }

        #endregion

        #region 参数化构建入口

        /// <summary>
        /// 通过 BuildConfig 执行完整构建流程
        /// </summary>
        public static bool BuildWithConfig(BuildConfig config, bool buildPlayer, string packageName = null)
        {
            var runtimePackages = GetBuildPackages(packageName);
            if (runtimePackages.Count <= 0)
            {
                Debug.LogError($"[BuildWithConfig] 未找到可构建的资源包: {packageName}");
                return false;
            }

            if (config.BuildHotFixDll && runtimePackages.Any(runtimePackage => IsAssemblyPackage(runtimePackage.PackageName)))
            {
                if ((buildPlayer || config.BuildPlayer) && config.PlayerPlatform != config.BuildTarget)
                {
                    Debug.LogError($"[BuildWithConfig] Player 平台 {config.PlayerPlatform} 与代码资源包平台 {config.BuildTarget} 不一致。");
                    return false;
                }

                BuildDLLCommand.ActivateBuildTarget(config.BuildTarget);
            }

            AssetDatabase.Refresh();

            YooAsset.Editor.BuildResult firstBuildResult = null;
            var hotFixDllBuilt = false;
            foreach (var runtimePackage in runtimePackages)
            {
                if (config.BuildHotFixDll && !hotFixDllBuilt && IsAssemblyPackage(runtimePackage.PackageName))
                {
                    Debug.Log($"[BuildWithConfig] 构建 {runtimePackage.PackageName} 前同步AOT元数据清单并编译热更DLL...");
                    BuildDLLCommand.BuildAndCopyDlls(config.BuildTarget);
                    AssetDatabase.Refresh();
                    hotFixDllBuilt = true;
                }

                var buildResult = BuildInternalWithConfig(config, runtimePackage, firstBuildResult != null);
                if (!buildResult.Success)
                {
                    Debug.LogError($"[BuildWithConfig] AssetBundle构建失败: {runtimePackage.PackageName} - {buildResult.ErrorInfo}");
                    return false;
                }

                firstBuildResult ??= buildResult;
                Debug.Log($"[BuildWithConfig] AssetBundle构建成功: {runtimePackage.PackageName} => {buildResult.OutputPackageDirectory}");

                if (config.EnablePublishCopy)
                {
                    var publishVersion = ResolvePackageVersion(config, runtimePackage.PackageName);
                    PublishBuiltPackage(config, runtimePackage.PackageName, buildResult.OutputPackageDirectory, publishVersion);
                }
            }

            if (config.MinimalPackage && firstBuildResult != null)
            {
                var minimalVersion = config.PackageVersionMode == PackageVersionMode.PerPackage
                    ? ResolvePackageVersion(config, runtimePackages[0].PackageName)
                    : config.PackageVersion;
                ProcessMinimalPackage(runtimePackages.Select(x => x.PackageName).ToList(), minimalVersion,
                    config.RetainTags, firstBuildResult.OutputPackageDirectory);
            }

            AssetDatabase.Refresh();

            if (buildPlayer || config.BuildPlayer)
            {
                return BuildImp(
                    BuildConfig.GetBuildTargetGroup(config.PlayerPlatform),
                    config.PlayerPlatform,
                    config.PlayerOutputPath
                );
            }

            return true;
        }

        #endregion

        #region AssetBundle 构建

        private static YooAsset.Editor.BuildResult BuildInternalWithConfig(BuildConfig config, RuntimePackageEntry runtimePackage, bool appendBuildinFiles)
        {
            // pdb 残留检测（当前配置不生成 pdb 且构建 CodePackage 时检查：release 模式，或 dev 但 pdb 开关关闭）
            bool pdbDisabled = !Settings.UpdateSetting.WillGeneratePdb;
            bool isCodePackage = IsAssemblyPackage(runtimePackage.PackageName);
            if (pdbDisabled && isCodePackage)
            {
                string pdbDir = Settings.UpdateSetting.GetPdbAssemblyAssetPath();
                if (Directory.Exists(pdbDir))
                {
                    var pdbFiles = Directory.GetFiles(pdbDir, "*.pdb.bytes", SearchOption.TopDirectoryOnly);
                    if (pdbFiles.Length > 0)
                    {
                        string pdbList = string.Join("\n", pdbFiles.Select(Path.GetFileName));
                        bool shouldContinue = EditorUtility.DisplayDialog(
                            "检测到 pdb 调试符号文件",
                            $"当前构建配置不会生成 pdb（release 模式或 pdb 开关已关闭），但在 PDB 目录检测到以下 pdb 残留文件：\n\n{pdbList}\n\npdb 文件会增大包体并泄露符号信息，不应打入此包。\n\n是否清理这些文件并继续打包？",
                            "清理并继续",
                            "取消打包");

                        if (!shouldContinue)
                        {
                            Debug.LogWarning("[打包中止] 用户取消打包以手动处理 pdb 文件。");
                            return new YooAsset.Editor.BuildResult { Success = false };
                        }

                        // 清理 pdb
                        foreach (var pdbFile in pdbFiles)
                        {
                            File.Delete(pdbFile);
                            string metaFile = pdbFile + ".meta";
                            if (File.Exists(metaFile))
                            {
                                File.Delete(metaFile);
                            }
                            Debug.Log($"[pdb清理] 已删除：{pdbFile}");
                        }
                        AssetDatabase.Refresh();
                    }
                }
            }

            var buildPipeline = ResolveBuildPipeline(config, runtimePackage);
            string packageVersion = ResolvePackageVersion(config, runtimePackage.PackageName);
            Debug.Log($"开始构建 : {config.BuildTarget} - {runtimePackage.PackageName} - {buildPipeline} - 版本:{packageVersion}");

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
                case EBuildPipeline.ArchiveFileBuildPipeline:
                {
                    pipeline = new ArchiveFileBuildPipeline();
                    buildParameters = new ArchiveFileBuildParameters
                    {
                        FileAlignment = 4,
                    };
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
            buildParameters.BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = buildPipeline.ToString();
            buildParameters.BuildTarget = config.BuildTarget;
            buildParameters.BuildBundleType = GetBuildBundleType(buildPipeline);
            buildParameters.PackageName = runtimePackage.PackageName;
            buildParameters.PackageVersion = packageVersion;
            buildParameters.PackageNote = JsonUtility.ToJson(new PackageMetadata { mode = Settings.UpdateSetting.BuildMode });
            buildParameters.VerifyBuildingResult = config.VerifyBuildingResult;
            buildParameters.EnableSharePackRule = config.EnableSharePackRule;
            buildParameters.EnableAssetPathValidation = config.EnableAssetPathValidation;
            buildParameters.FileNameStyle = config.FileNameStyle;
            buildParameters.BundledCopyOption = GetBundledFileCopyOption(config.BuildinFileCopyOption, appendBuildinFiles);
            buildParameters.BundledCopyParams = string.Empty;
            buildParameters.BundleEncryptor = GetEncryptionFromType(runtimePackage.EncryptionType);
            if (runtimePackage.ManifestEncrypted)
            {
                buildParameters.ManifestEncryptor = new ManifestChaCha20Encryptor();
                // 构建期 TaskCreateCatalog 会反序列化刚加密的清单生成 Catalog，
                // 必须同时提供解密器，否则 Catalog 生成时 FileMagic 校验失败。
                buildParameters.ManifestDecryptor = new ManifestChaCha20Decryptor();
            }
            buildParameters.ClearBuildCacheFiles = config.ClearBuildCache;
            buildParameters.UseAssetDependencyDB = config.UseAssetDependencyDB;

            var buildResult = pipeline.Run(buildParameters, true);

            if (buildResult.Success && config.GenerateCatalogInOutput)
            {
                var decryptor = runtimePackage.ManifestEncrypted ? new ManifestChaCha20Decryptor() : null;
                CatalogOutputHelper.GenerateCatalog(decryptor, runtimePackage.PackageName, buildResult.OutputPackageDirectory);
            }

            return buildResult;
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
            var outputRoot = string.IsNullOrWhiteSpace(config.OutputRoot) ? "./Releases/Bundles/" : config.OutputRoot;
            if (!Path.IsPathRooted(outputRoot))
            {
                outputRoot = Path.Combine(Application.dataPath + "/../", outputRoot);
            }

            return Path.GetFullPath(outputRoot).Replace('\\', '/');
        }

        public static string GetBuildPlatformOutputRoot(BuildConfig config)
        {
            var outputRoot = GetResolvedOutputRoot(config);
            return Path.Combine(outputRoot, GetRemotePlatformName(config.BuildTarget)).Replace('\\', '/');
        }

        public static string GetPublishOutputRoot(BuildConfig config)
        {
            var publishRoot = string.IsNullOrWhiteSpace(config.PublishRoot) ? "./Releases/Publish/" : config.PublishRoot;
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

            // PerPackage 模式：返回每包各自版本的并集，不要求公共版本。
            if (config.PackageVersionMode == PackageVersionMode.PerPackage)
            {
                var versionTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
                foreach (var runtimePackage in runtimePackages)
                {
                    var packageVersions = GetPackageVersionDirectories(config, runtimePackage.PackageName);
                    foreach (var pair in packageVersions)
                    {
                        if (versionTimes.TryGetValue(pair.Key, out var existing))
                        {
                            versionTimes[pair.Key] = existing > pair.Value ? existing : pair.Value;
                        }
                        else
                        {
                            versionTimes[pair.Key] = pair.Value;
                        }
                    }
                }

                return versionTimes
                    .OrderByDescending(kv => kv.Value)
                    .ThenByDescending(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => kv.Key)
                    .ToList();
            }

            // Unified 模式：返回所有包公共版本（交集）。
            var candidateVersions = new HashSet<string>(StringComparer.Ordinal);
            var sharedVersionTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            var isFirstPackage = true;

            foreach (var runtimePackage in runtimePackages)
            {
                var packageVersions = GetPackageVersionDirectories(config, runtimePackage.PackageName);
                if (isFirstPackage)
                {
                    foreach (var packageVersion in packageVersions)
                    {
                        candidateVersions.Add(packageVersion.Key);
                        sharedVersionTimes[packageVersion.Key] = packageVersion.Value;
                    }

                    isFirstPackage = false;
                    continue;
                }

                candidateVersions.IntersectWith(packageVersions.Keys);
                foreach (var version in candidateVersions.ToArray())
                {
                    if (packageVersions.TryGetValue(version, out var lastWriteTimeUtc) && sharedVersionTimes.TryGetValue(version, out var existingTime))
                    {
                        sharedVersionTimes[version] = existingTime > lastWriteTimeUtc ? existingTime : lastWriteTimeUtc;
                    }
                }
            }

            return candidateVersions
                .OrderByDescending(version => sharedVersionTimes.TryGetValue(version, out var lastWriteTimeUtc)
                    ? lastWriteTimeUtc
                    : DateTime.MinValue)
                .ThenByDescending(version => version, StringComparer.Ordinal)
                .ToList();
        }

        public static bool PublishFromExistingBuild(BuildConfig config, string packageVersion)
        {
            var isPerPackage = config.PackageVersionMode == PackageVersionMode.PerPackage;
            if (!isPerPackage && string.IsNullOrWhiteSpace(packageVersion))
            {
                Debug.LogError("[Publish] 发布整理失败：版本号为空。");
                return false;
            }

            var runtimePackages = GetBuildPackages();
            var packageDirectories = new List<(string PackageName, string SourceDirectory, string Version)>();
            var missingPackages = new List<string>();

            foreach (var runtimePackage in runtimePackages)
            {
                var effectiveVersion = isPerPackage
                    ? ResolvePackageVersion(config, runtimePackage.PackageName)
                    : packageVersion;

                var sourceDirectory = GetPackageVersionDirectory(config, runtimePackage.PackageName, effectiveVersion);
                if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                {
                    missingPackages.Add($"{runtimePackage.PackageName}/{effectiveVersion}");
                    continue;
                }

                packageDirectories.Add((runtimePackage.PackageName, sourceDirectory, effectiveVersion));
            }

            if (packageDirectories.Count <= 0)
            {
                Debug.LogError($"[Publish] 发布整理失败：未找到任何版本目录。\n缺失：{string.Join("\n  ", missingPackages)}");
                return false;
            }

            foreach (var missing in missingPackages)
            {
                Debug.LogWarning($"[Publish] 跳过缺失版本目录：{missing}");
            }

            foreach (var entry in packageDirectories)
            {
                PublishBuiltPackage(config, entry.PackageName, entry.SourceDirectory, entry.Version);
            }

            if (isPerPackage)
            {
                var versionSummary = string.Join(", ", packageDirectories.Select(x => $"{x.PackageName}={x.Version}"));
                Debug.Log($"[Publish] 已按包版本整理完成：{versionSummary} => {GetPublishOutputRoot(config)}");
            }
            else
            {
                Debug.Log($"[Publish] 已按版本整理完成：{packageVersion} => {GetPublishOutputRoot(config)}");
            }

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

            var publishRoot = GetPublishOutputRoot(config);
            var remotePlatformName = GetRemotePlatformName(config.BuildTarget);
            // 扁平化：Releases/Publish/{平台}/{包名}/，不再按项目名分子目录。
            var targetDirectory = Path.Combine(publishRoot, remotePlatformName, packageName);
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
            string streamingRoot = BundleBuilderHelper.GetStreamingAssetsRoot();

            HashSet<string> retainFileNames = new HashSet<string>();
            string[] retainTagArray = ParseRetainTags(retainTags);

            foreach (var packageName in packageNames)
            {
                string reportFileName = YooAssetConfiguration.GetBuildReportFileName(packageName, packageVersion);
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
                RuntimePackageBuildPipeline.ArchiveFileBuildPipeline => EBuildPipeline.ArchiveFileBuildPipeline,
                _ => config.BuildPipeline,
            };
        }

        private static int GetBuildBundleType(EBuildPipeline buildPipeline)
        {
            return buildPipeline switch
            {
                EBuildPipeline.RawFileBuildPipeline => (int)EBundleType.RawBundle,
                EBuildPipeline.ArchiveFileBuildPipeline => (int)EBundleType.ArchiveBundle,
                _ => (int)EBundleType.AssetBundle,
            };
        }

        private static EBundledCopyOption GetBundledFileCopyOption(EBundledCopyOption option, bool appendBuildinFiles)
        {
            if (!appendBuildinFiles)
            {
                return option;
            }

            return option switch
            {
                EBundledCopyOption.ClearAndCopyAll => EBundledCopyOption.OnlyCopyAll,
                EBundledCopyOption.ClearAndCopyByTags => EBundledCopyOption.OnlyCopyByTags,
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

        public static bool BuildImp(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget, string locationPathName)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
            AssetDatabase.Refresh();

            // 统一将项目根相对路径（./ 前缀）或非根路径转为绝对路径，BuildPipeline.BuildPlayer 需要绝对路径
            if (!string.IsNullOrWhiteSpace(locationPathName) && !Path.IsPathRooted(locationPathName))
            {
                locationPathName = Path.GetFullPath(Path.Combine(Application.dataPath, "..", locationPathName));
            }

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
                return true;
            }
            else
            {
                Debug.LogError($"Build Failed: {summary.result}");
                return false;
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
            var uniqueBundleName = BundleCollectorSettingData.Setting.UniqueBundleName;
            var packRuleResult = DefaultBundlePackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
        }

        /// <summary>
        /// 根据 EncryptionType 枚举获取加密服务
        /// </summary>
        private static IBundleEncryptor GetEncryptionFromType(EncryptionType encryptionType)
        {
            return BundleCrypto.Create(encryptionType)?.Encryptor;
        }

        /// <summary>
        /// 根据版本模式解析当前包的版本号。
        /// PerPackage 模式下优先取 PackageVersionMap，其次取 _build_version.txt，最后自动生成。
        /// </summary>
        private static string ResolvePackageVersion(BuildConfig config, string packageName)
        {
            if (config.PackageVersionMode != PackageVersionMode.PerPackage)
            {
                return config.PackageVersion;
            }

            if (config.PackageVersionMap.TryGetValue(packageName, out var version) && !string.IsNullOrWhiteSpace(version))
            {
                return version;
            }

            // 尝试从已有构建目录的 _build_version.txt 读取
            var existingDir = GetBuildPlatformOutputRoot(config) + "/" + packageName;
            if (Directory.Exists(existingDir))
            {
                var versionFile = Path.Combine(existingDir, "_build_version.txt");
                if (File.Exists(versionFile))
                {
                    var fileVersion = File.ReadAllText(versionFile).Trim();
                    if (!string.IsNullOrWhiteSpace(fileVersion))
                    {
                        config.PackageVersionMap[packageName] = fileVersion;
                        return fileVersion;
                    }
                }
            }

            // 自动生成
            var autoVersion = BuildConfig.GetDefaultPackageVersion();
            config.PackageVersionMap[packageName] = autoVersion;
            return autoVersion;
        }

        #endregion
    }
}
