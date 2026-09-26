using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TEngine
{
    /// <summary>
    /// TEngine 构建工具 CLI 桥。
    /// <remarks>
    /// 供 BuildCLI（Python GUI）以 batchmode 方式调用，是唯一对外 CLI 入口。
    /// 用法：Unity.exe -projectPath &lt;项目&gt; -batchmode -quit -executeMethod TEngine.CLIBridge.Run
    ///       -tengineConfig=&lt;JSON 文件绝对路径&gt; [-tengineResult=&lt;结果 JSON 输出路径&gt;]
    /// 退出码：0 成功；1 失败（Python 端据此判定构建结果）。
    /// </remarks>
    /// </summary>
    public static class CLIBridge
    {
        private const string ConfigArgPrefix = "-tengineConfig=";
        private const string ResultArgPrefix = "-tengineResult=";

        /// <summary>
        /// JSON 反序列化 DTO。字段名与 Python 端 BuildCLI 的 build_config JSON 保持一致。
        /// </summary>
        [Serializable]
        private sealed class BuildRequestDTO
        {
            // 动作：build(AB+可选Player) / buildAb(仅AB) / buildPlayer(仅Player) / publish(仅发布整理)
            // hotfixDll(编译并拷贝热更DLL) / generateAll / syncAotManifest / copyAotDll / switchPlatform(仅切平台)
            // buildInstaller(仅编译 InnoSetup 安装包，需已有 Windows Player 产物)
            public string action = "build";

            // 基础设置（对应 BuildConfig）
            public string buildTarget = "StandaloneWindows64";
            public string buildPipeline = "ScriptableBuildPipeline";
            public string compressOption = "LZ4";
            public string packageVersion = "";
            public string packageVersionMode = "Unified";
            public List<PackageVersionEntryDTO> packageVersions = new List<PackageVersionEntryDTO>();
            public string outputRoot = "./Releases/Bundles/";
            // 指定资源包名：为空构建全部启用的包
            public string packageName = "";

            // 发布整理
            public bool enablePublishCopy;
            public string publishRoot = "./Releases/Publish/";
            public bool cleanPublishPackageDirectory = true;

            // 最小包
            public bool minimalPackage;
            public string retainTags = "";

            // 高级
            public bool enableSharePackRule = true;
            public bool enableAssetPathValidation = true;
            public bool useAssetDependencyDB = true;
            public bool clearBuildCache;
            public bool verifyBuildingResult = true;
            public string buildinFileCopyOption = "ClearAndCopyAll";
            public string fileNameStyle = "BundleName_HashName";
            public bool generateCatalogInOutput;

            // 热更 DLL
            public bool buildHotFixDll = true;

            // Player
            public bool buildPlayer;
            public string playerPlatform = "StandaloneWindows64";
            public string playerOutputPath = "";

            // InnoSetup 安装包（action=buildInstaller 或 build+buildInstaller 时使用）
            public bool buildInstaller;
            public string installerVersion = "";
            public string isccPath = "";
            public string installerAppName = "";
            public string installerAppEnglishName = "";
            public string installerPublisher = "";
            public string installerPassword = "";
            public string installerWatermark = "";
        }

        [Serializable]
        private sealed class PackageVersionEntryDTO
        {
            public string packageName = "";
            public string version = "";
        }

        // ============ 结构化结果（与 Python 端 unity_runner 合并读取） ============

        [Serializable]
        private sealed class BuildResultDTO
        {
            public string action = "";
            public bool success;
            public string error = "";
            public long durationSeconds;
            public string playerOutputPath = "";
            public long playerSizeBytes;
            public string installerOutputPath = "";
            public List<PackageRecordDTO> packages = new List<PackageRecordDTO>();
        }

        [Serializable]
        private sealed class PackageRecordDTO
        {
            public string packageName = "";
            public string packageVersion = "";
            public string outputDirectory = "";
            public long sizeBytes;
        }

        public static void Run()
        {
            int exitCode;
            var result = new BuildResultDTO();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var request = LoadRequest();
                if (request == null)
                {
                    result.error = "配置文件不存在或未传入 -tengineConfig 参数";
                    exitCode = 1;
                }
                else
                {
                    result.action = request.action;
                    bool ok = Execute(request, result);
                    result.success = ok;
                    if (!ok && string.IsNullOrEmpty(result.error))
                    {
                        result.error = "详见 Unity 日志";
                    }

                    exitCode = ok ? 0 : 1;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[TEngineCLI] 构建异常终止：{e.Message}");
                result.success = false;
                result.error = e.Message;
                exitCode = 1;
            }

            stopwatch.Stop();
            result.durationSeconds = (long)stopwatch.Elapsed.TotalSeconds;
            WriteResultFile(result);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static void WriteResultFile(BuildResultDTO result)
        {
            try
            {
                string resultPath = Environment.GetCommandLineArgs()
                    .FirstOrDefault(arg => arg.StartsWith(ResultArgPrefix, StringComparison.OrdinalIgnoreCase))
                    ?.Substring(ResultArgPrefix.Length);
                if (string.IsNullOrWhiteSpace(resultPath))
                {
                    return;
                }

                string directory = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(resultPath, JsonUtility.ToJson(result, true), new UTF8Encoding(false));
                Debug.Log($"[TEngineCLI] 结构化结果已写入：{resultPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TEngineCLI] 结果文件写入失败（忽略）：{e.Message}");
            }
        }

        private static BuildRequestDTO LoadRequest()
        {
            string configPath = Environment.GetCommandLineArgs()
                .FirstOrDefault(arg => arg.StartsWith(ConfigArgPrefix, StringComparison.OrdinalIgnoreCase))
                ?.Substring(ConfigArgPrefix.Length);

            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                Debug.LogError($"[TEngineCLI] 配置文件不存在或未传入 -tengineConfig 参数：{configPath ?? "<null>"}");
                return null;
            }

            string json = File.ReadAllText(configPath);
            var request = JsonUtility.FromJson<BuildRequestDTO>(json);
            if (request == null)
            {
                Debug.LogError($"[TEngineCLI] 配置 JSON 解析失败：{configPath}");
                return null;
            }

            Debug.Log($"[TEngineCLI] 已加载构建配置：{configPath}\n{json}");
            return request;
        }

        private static bool Execute(BuildRequestDTO request, BuildResultDTO result)
        {
            Debug.Log($"[TEngineCLI] ========== 开始执行 action={request.action} ==========");
            switch (request.action)
            {
                case "build":
                case "buildAb":
                {
                    var config = ToBuildConfig(request);
                    bool withPlayer = request.action == "build" && request.buildPlayer;
                    if (string.IsNullOrWhiteSpace(request.packageVersion) &&
                        request.packageVersionMode == "Unified")
                    {
                        config.PackageVersion = BuildConfig.GetDefaultPackageVersion();
                        Debug.Log($"[TEngineCLI] 统一版本号为空，自动生成：{config.PackageVersion}");
                    }

                    bool ok = ReleaseTools.BuildWithConfig(config, withPlayer, NullIfEmpty(request.packageName));
                    FillPackageRecords(config, result);
                    if (ok && withPlayer)
                    {
                        FillPlayerRecord(config, result);
                    }

                    // AB 成功后串联安装包（与 GUI 窗口“串联构建”语义一致）
                    if (ok && request.action == "build" && request.buildInstaller)
                    {
                        ok = ExecuteInstaller(request, result);
                    }

                    Debug.Log(ok
                        ? "[TEngineCLI] ========== 构建完成 =========="
                        : "[TEngineCLI] ========== 构建失败 ==========");
                    return ok;
                }
                case "buildPlayer":
                {
                    var config = ToBuildConfig(request);
                    var playerTarget = ParseBuildTarget(request.playerPlatform);
                    if (!ReleaseTools.BuildImp(
                            BuildConfig.GetBuildTargetGroup(playerTarget),
                            playerTarget,
                            request.playerOutputPath))
                    {
                        Debug.LogError("[TEngineCLI] Player 构建失败。");
                        return false;
                    }

                    FillPlayerRecord(config, result);
                    Debug.Log("[TEngineCLI] ========== Player 构建完成 ==========");
                    return true;
                }
                case "buildInstaller":
                    return ExecuteInstaller(request, result);
                case "publish":
                {
                    var config = ToBuildConfig(request);
                    if (!ReleaseTools.PublishFromExistingBuild(config, request.packageVersion))
                    {
                        Debug.LogError("[TEngineCLI] 发布整理失败。");
                        return false;
                    }

                    Debug.Log("[TEngineCLI] ========== 发布整理完成 ==========");
                    return true;
                }
                case "hotfixDll":
                {
                    BuildDLLCommand.BuildAndCopyDlls(ParseBuildTarget(request.buildTarget));
                    Debug.Log("[TEngineCLI] ========== 热更 DLL 编译拷贝完成 ==========");
                    return true;
                }
                case "generateAll":
                {
                    BuildDLLCommand.GenerateAllForTarget(ParseBuildTarget(request.buildTarget));
                    Debug.Log("[TEngineCLI] ========== GenerateAll 完成 ==========");
                    return true;
                }
                case "syncAotManifest":
                {
                    BuildDLLCommand.SyncAOTMetadataManifest();
                    Debug.Log("[TEngineCLI] ========== AOT 元数据清单同步完成 ==========");
                    return true;
                }
                case "copyAotDll":
                {
                    BuildDLLCommand.CopyAOTAssembliesToAssetPath(ParseBuildTarget(request.buildTarget));
                    Debug.Log("[TEngineCLI] ========== AOT 元数据 DLL 拷贝完成 ==========");
                    return true;
                }
                case "switchPlatform":
                {
                    var target = ParseBuildTarget(request.buildTarget);
                    BuildDLLCommand.ActivateBuildTarget(target);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[TEngineCLI] ========== 平台已切换到 {target} ==========");
                    return true;
                }
                default:
                    Debug.LogError($"[TEngineCLI] 未知 action：{request.action}");
                    result.error = $"未知 action：{request.action}";
                    return false;
            }
        }

        private static bool ExecuteInstaller(BuildRequestDTO request, BuildResultDTO result)
        {
            try
            {
                var exeName = Path.GetFileName(BuildConfig.GetDefaultPlayerOutputPath(BuildTarget.StandaloneWindows64));
                var watermark = string.IsNullOrWhiteSpace(request.installerWatermark)
                    ? request.installerPublisher
                    : request.installerWatermark;
                var issConfig = new IssInstallerConfig
                {
                    AppName = request.installerAppName,
                    AppEnglishName = request.installerAppEnglishName,
                    InstallerVersion = request.installerVersion,
                    Publisher = request.installerPublisher,
                    ExeName = exeName,
                    Password = request.installerPassword,
                    Watermark = watermark,
                };

                Debug.Log("[TEngineCLI] ========== 开始编译 InnoSetup 安装包 ==========");
                InnoSetupBuilder.BuildInstaller(issConfig, NullIfEmpty(request.isccPath));
                result.installerOutputPath = InnoSetupBuilder.InstallerOutputDir;
                Debug.Log($"[TEngineCLI] ========== 安装包构建完成：{result.installerOutputPath} ==========");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TEngineCLI] 安装包构建失败：{e.Message}");
                result.error = $"安装包构建失败：{e.Message}";
                return false;
            }
        }

        private static void FillPackageRecords(BuildConfig config, BuildResultDTO result)
        {
            foreach (var record in config.PackageRecords)
            {
                result.packages.Add(new PackageRecordDTO
                {
                    packageName = record.PackageName,
                    packageVersion = record.PackageVersion,
                    outputDirectory = record.OutputDirectory,
                    sizeBytes = record.OutputSizeBytes,
                });
            }
        }

        private static void FillPlayerRecord(BuildConfig config, BuildResultDTO result)
        {
            var outputPath = string.IsNullOrWhiteSpace(config.PlayerOutputPath)
                ? BuildConfig.GetDefaultPlayerOutputPath(config.PlayerPlatform)
                : config.PlayerOutputPath;
            if (!Path.IsPathRooted(outputPath))
            {
                outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputPath));
            }

            result.playerOutputPath = outputPath;
            try
            {
                if (Directory.Exists(outputPath))
                {
                    result.playerSizeBytes = Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories)
                        .Sum(file => new FileInfo(file).Length);
                }
                else if (File.Exists(outputPath))
                {
                    result.playerSizeBytes = new FileInfo(outputPath).Length;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TEngineCLI] 统计 Player 体积失败（忽略）：{e.Message}");
            }
        }

        private static BuildConfig ToBuildConfig(BuildRequestDTO request)
        {
            var config = new BuildConfig
            {
                BuildTarget = ParseBuildTarget(request.buildTarget),
                BuildPipeline = ParseEnum(request.buildPipeline, EBuildPipeline.ScriptableBuildPipeline),
                CompressOption = ParseEnum(request.compressOption, ECompressOption.LZ4),
                PackageVersion = request.packageVersion ?? string.Empty,
                PackageVersionMode = request.packageVersionMode == "PerPackage"
                    ? PackageVersionMode.PerPackage
                    : PackageVersionMode.Unified,
                OutputRoot = string.IsNullOrWhiteSpace(request.outputRoot) ? "./Releases/Bundles/" : request.outputRoot,
                EnablePublishCopy = request.enablePublishCopy,
                PublishRoot = string.IsNullOrWhiteSpace(request.publishRoot) ? "./Releases/Publish/" : request.publishRoot,
                CleanPublishPackageDirectory = request.cleanPublishPackageDirectory,
                MinimalPackage = request.minimalPackage,
                RetainTags = request.retainTags ?? string.Empty,
                EnableSharePackRule = request.enableSharePackRule,
                EnableAssetPathValidation = request.enableAssetPathValidation,
                UseAssetDependencyDB = request.useAssetDependencyDB,
                ClearBuildCache = request.clearBuildCache,
                VerifyBuildingResult = request.verifyBuildingResult,
                BuildinFileCopyOption = ParseEnum(request.buildinFileCopyOption, EBundledCopyOption.ClearAndCopyAll),
                FileNameStyle = ParseEnum(request.fileNameStyle, EFileNameStyle.BundleName_HashName),
                GenerateCatalogInOutput = request.generateCatalogInOutput,
                BuildHotFixDll = request.buildHotFixDll,
                BuildPlayer = request.buildPlayer,
                PlayerPlatform = ParseBuildTarget(request.playerPlatform),
                PlayerOutputPath = request.playerOutputPath ?? string.Empty,
                HeadlessMode = Application.isBatchMode,
            };

            if (config.PackageVersionMode == PackageVersionMode.PerPackage && request.packageVersions != null)
            {
                foreach (var entry in request.packageVersions)
                {
                    if (string.IsNullOrWhiteSpace(entry.packageName))
                    {
                        continue;
                    }

                    config.PackageVersionMap[entry.packageName] = entry.version ?? string.Empty;
                }
            }

            return config;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static BuildTarget ParseBuildTarget(string value)
        {
            return Enum.TryParse(value, out BuildTarget target) && target != BuildTarget.NoTarget
                ? target
                : BuildTarget.StandaloneWindows64;
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return Enum.TryParse(value, out T result) ? result : fallback;
        }
    }
}
