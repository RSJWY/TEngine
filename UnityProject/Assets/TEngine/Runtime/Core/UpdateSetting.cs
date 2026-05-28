using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 强制更新类型。
    /// </summary>
    public enum UpdateStyle
    {
        /// <summary>
        /// 强制更新(不更新无法进入游戏。)
        /// </summary>
        Force = 1,

        /// <summary>
        /// 非强制(不更新可以进入游戏。)
        /// </summary>
        Optional = 2,
    }

    /// <summary>
    /// 是否提示更新。
    /// </summary>
    public enum UpdateNotice
    {
        /// <summary>
        /// 更新存在提示。
        /// </summary>
        Notice = 1,

        /// <summary>
        /// 更新非提示。
        /// </summary>
        NoNotice = 2,
    }

    /// <summary>
    /// WebGL平台下，
    /// StreamingAssets：跳过远程下载资源直接访问StreamingAssets
    /// Remote：访问远程资源
    /// </summary>
    public enum LoadResWayWebGL
    {
        Remote,
        StreamingAssets,
    }

    [Serializable]
    /// <summary>
    /// 运行时资源包配置。
    /// </summary>
    public class RuntimePackageEntry
    {
        /// <summary>
        /// 资源包名称。
        /// </summary>
        public string PackageName = "DefaultPackage";

        /// <summary>
        /// 是否启用该资源包。
        /// </summary>
        public bool Enable = true;

        /// <summary>
        /// 启动时是否初始化该资源包。
        /// </summary>
        public bool InitOnStartup = true;

        /// <summary>
        /// 启动时是否更新该资源包清单。
        /// </summary>
        public bool UpdateManifestOnStartup = true;

        /// <summary>
        /// 是否在更新流程中检查并下载该资源包。
        /// </summary>
        public bool DownloadOnDemand = true;

        /// <summary>
        /// 是否保存该资源包的版本记录。
        /// </summary>
        public bool SaveVersion = true;

        /// <summary>
        /// 资源包版本记录键。
        /// </summary>
        public string VersionKey = "GAME_VERSION";

        /// <summary>
        /// 是否为程序集资源包。
        /// </summary>
        public bool IsAssemblyPackage;
    }

    [CreateAssetMenu(menuName = "TEngine/UpdateSetting", fileName = "UpdateSetting")]
    public class UpdateSetting : ScriptableObject
    {
        private const string DefaultPackageName = "DefaultPackage";
        private const string DefaultGameVersionKey = "GAME_VERSION";
        private const string DefaultCodeVersionKey = "CODE_VERSION";

        /// <summary>
        /// 项目名称。
        /// </summary>
        [SerializeField]
        private string projectName = "Demo";

        public bool Enable
        {
            get
            {
#if ENABLE_HYBRIDCLR
                return true;
#else
                return false;
#endif
            }
        }

        [Header("Auto sync with [HybridCLRGlobalSettings]")]
        public List<string> HotUpdateAssemblies = new List<string>() {"GameProto.dll", "GameLogic.dll" };

        [Header("Need manual setting!")]
        public List<string> AOTMetaAssemblies = new List<string>() { "mscorlib.dll", "System.dll", "System.Core.dll", "TEngine.Runtime.dll" ,"UniTask.dll", "YooAsset.dll"};

        /// <summary>
        /// Dll of main business logic assembly
        /// </summary>
        public string LogicMainDllName = "GameLogic.dll";

        /// <summary>
        /// 程序集文本资产打包Asset后缀名
        /// </summary>
        public string AssemblyTextAssetExtension = ".bytes";

        /// <summary>
        /// 程序集文本资产资源目录
        /// </summary>
        public string AssemblyTextAssetPath = "AssetRaw/DLL";

        /// <summary>
        /// 程序集文本资产资源包名
        /// </summary>
        public string AssemblyPackageName = "CodePackage";

        [Header("运行时资源包")]
        public List<RuntimePackageEntry> RuntimePackages = new List<RuntimePackageEntry>();

        [Header("更新设置")]
        public UpdateStyle UpdateStyle = UpdateStyle.Force;

        public UpdateNotice UpdateNotice = UpdateNotice.Notice;

        /// <summary>
        /// 资源服务器地址。
        /// </summary>
        [SerializeField]
        private string ResDownLoadPath = "http://127.0.0.1:8081";

        /// <summary>
        /// 资源服务备用地址。
        /// </summary>
        [SerializeField]
        private string FallbackResDownLoadPath = "http://127.0.0.1:8082";

        /// <summary>
        /// WebGL平台加载本地资源/加载远程资源。
        /// </summary>
        [Header("WebGL设置")]
        [SerializeField]
        private LoadResWayWebGL LoadResWayWebGL = LoadResWayWebGL.Remote;
        /// <summary>
        /// 是否自动你讲打包资源复制到打包后的StreamingAssets地址
        /// </summary>
        [Header("构建资源设置")]
        [SerializeField]
        private bool isAutoAssetCopeToBuildAddress = false;
        /// <summary>
        /// 打包程序资源地址
        /// </summary>
        [SerializeField]
        private string BuildAddress = "../../Builds/Unity_Data/StreamingAssets";
        /// <summary>
        /// 是否使用可寻址资源代替资源路径
        /// 说明：开启此项可以节省运行时清单占用的内存！
        /// </summary>
        [SerializeField, Tooltip("是否使用可寻址资源代替资源路径 说明：开启此项可以节省运行时清单占用的内存！")]
        private bool ReplaceAssetPathWithAddress = false;

        public List<RuntimePackageEntry> GetEnabledRuntimePackages()
        {
            var sourcePackages = RuntimePackages != null && RuntimePackages.Count > 0
                ? RuntimePackages
                : GetDefaultRuntimePackages();
            var runtimePackages = new List<RuntimePackageEntry>(sourcePackages.Count);
            var packageNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sourcePackage in sourcePackages)
            {
                if (sourcePackage == null || !sourcePackage.Enable || string.IsNullOrWhiteSpace(sourcePackage.PackageName))
                {
                    continue;
                }

                var runtimePackage = NormalizeRuntimePackageEntry(sourcePackage);
                if (!packageNames.Add(runtimePackage.PackageName))
                {
                    continue;
                }

                runtimePackages.Add(runtimePackage);
            }

            EnsureRequiredRuntimePackages(runtimePackages, packageNames);
            return runtimePackages;
        }

        public RuntimePackageEntry GetRuntimePackage(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return null;
            }

            foreach (var runtimePackage in GetEnabledRuntimePackages())
            {
                if (string.Equals(runtimePackage.PackageName, packageName, StringComparison.Ordinal))
                {
                    return runtimePackage;
                }
            }

            return null;
        }

        public RuntimePackageEntry GetAssemblyPackage()
        {
            foreach (var runtimePackage in GetEnabledRuntimePackages())
            {
                if (runtimePackage.IsAssemblyPackage)
                {
                    return runtimePackage;
                }
            }

            return null;
        }

        public string GetAssemblyPackageName()
        {
            var assemblyPackage = GetAssemblyPackage();
            if (assemblyPackage != null)
            {
                return assemblyPackage.PackageName;
            }

            return GetConfiguredAssemblyPackageName();
        }

        public string GetVersionKey(string packageName)
        {
            var runtimePackage = GetRuntimePackage(packageName);
            if (runtimePackage != null)
            {
                return runtimePackage.VersionKey;
            }

            var isAssemblyPackage = string.Equals(packageName, GetConfiguredAssemblyPackageName(), StringComparison.Ordinal);
            return GetDefaultVersionKey(packageName, isAssemblyPackage);
        }

        public string GetRemotePackageSubPath(string packageName)
        {
            return NormalizePathSegment(packageName);
        }

        public string GetPackageHostServerURL(string packageName)
            => CombineUrl(GetResDownLoadPath(), GetRemotePackageSubPath(packageName));

        public string GetPackageFallbackHostServerURL(string packageName)
            => CombineUrl(GetFallbackResDownLoadPath(), GetRemotePackageSubPath(packageName));

        private List<RuntimePackageEntry> GetDefaultRuntimePackages()
        {
            var runtimePackages = new List<RuntimePackageEntry>
            {
                CreateDefaultPackageEntry()
            };
            var assemblyPackage = CreateAssemblyPackageEntry();
            if (!string.Equals(assemblyPackage.PackageName, DefaultPackageName, StringComparison.Ordinal))
            {
                runtimePackages.Add(assemblyPackage);
            }

            return runtimePackages;
        }

        private void EnsureRequiredRuntimePackages(List<RuntimePackageEntry> runtimePackages, HashSet<string> packageNames)
        {
            if (!packageNames.Contains(DefaultPackageName))
            {
                var defaultPackage = CreateDefaultPackageEntry();
                runtimePackages.Insert(0, defaultPackage);
                packageNames.Add(defaultPackage.PackageName);
            }

            if (!Enable)
            {
                return;
            }

            foreach (var runtimePackage in runtimePackages)
            {
                if (runtimePackage.IsAssemblyPackage)
                {
                    return;
                }
            }

            var assemblyPackage = CreateAssemblyPackageEntry();
            if (packageNames.Contains(assemblyPackage.PackageName))
            {
                return;
            }

            runtimePackages.Add(assemblyPackage);
            packageNames.Add(assemblyPackage.PackageName);
        }

        private RuntimePackageEntry NormalizeRuntimePackageEntry(RuntimePackageEntry sourcePackage)
        {
            var packageName = sourcePackage.PackageName.Trim();
            var isAssemblyPackage = sourcePackage.IsAssemblyPackage || string.Equals(packageName, GetConfiguredAssemblyPackageName(), StringComparison.Ordinal);
            return new RuntimePackageEntry
            {
                Enable = true,
                PackageName = packageName,
                InitOnStartup = sourcePackage.InitOnStartup,
                UpdateManifestOnStartup = sourcePackage.UpdateManifestOnStartup,
                DownloadOnDemand = sourcePackage.DownloadOnDemand,
                SaveVersion = sourcePackage.SaveVersion,
                VersionKey = string.IsNullOrWhiteSpace(sourcePackage.VersionKey)
                    ? GetDefaultVersionKey(packageName, isAssemblyPackage)
                    : sourcePackage.VersionKey.Trim(),
                IsAssemblyPackage = isAssemblyPackage,
            };
        }

        private RuntimePackageEntry CreateDefaultPackageEntry()
        {
            return new RuntimePackageEntry
            {
                Enable = true,
                PackageName = DefaultPackageName,
                InitOnStartup = true,
                UpdateManifestOnStartup = true,
                DownloadOnDemand = true,
                SaveVersion = true,
                VersionKey = DefaultGameVersionKey,
                IsAssemblyPackage = false,
            };
        }

        private RuntimePackageEntry CreateAssemblyPackageEntry()
        {
            var packageName = GetConfiguredAssemblyPackageName();
            return new RuntimePackageEntry
            {
                Enable = true,
                PackageName = packageName,
                InitOnStartup = true,
                UpdateManifestOnStartup = true,
                DownloadOnDemand = true,
                SaveVersion = true,
                VersionKey = GetDefaultVersionKey(packageName, true),
                IsAssemblyPackage = true,
            };
        }

        private string GetConfiguredAssemblyPackageName()
            => string.IsNullOrWhiteSpace(AssemblyPackageName) ? "CodePackage" : AssemblyPackageName.Trim();

        private string GetDefaultVersionKey(string packageName, bool isAssemblyPackage)
        {
            if (string.Equals(packageName, DefaultPackageName, StringComparison.Ordinal))
            {
                return DefaultGameVersionKey;
            }

            if (isAssemblyPackage)
            {
                return DefaultCodeVersionKey;
            }

            return $"PACKAGE_VERSION_{packageName}";
        }

        private static string NormalizePathSegment(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Trim().Replace('\\', '/').Trim('/');
        }

        private static string CombineUrl(string root, string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return root.TrimEnd('/');
            }

            return $"{root.TrimEnd('/')}/{segment}";
        }

        /// <summary>
        /// 是否自动你讲打包资源复制到打包后的StreamingAssets地址
        /// </summary>
        /// <returns></returns>
        public bool IsAutoAssetCopeToBuildAddress()
        {
            return isAutoAssetCopeToBuildAddress;
        }
        /// <summary>
        /// 获取打包程序资源地址
        /// </summary>
        /// <returns></returns>
        public string GetBuildAddress()
        {
            return BuildAddress;
        }

        /// <summary>
        /// 获取是否使用可寻址资源代替资源路径
        /// </summary>
        /// <returns></returns>
        public bool GetReplaceAssetPathWithAddress()
            => ReplaceAssetPathWithAddress;

        /// <summary>
        /// 是否加载远程资源
        /// </summary>
        /// <returns></returns>
        public LoadResWayWebGL GetLoadResWayWebGL()
        {
            return LoadResWayWebGL;
        }
        /// <summary>
        /// 获取资源下载路径。
        /// </summary>
        public string GetResDownLoadPath()
        {
            return Path.Combine(ResDownLoadPath, projectName, GetPlatformName()).Replace("\\", "/");
        }

        /// <summary>
        /// 获取备用资源下载路径。
        /// </summary>
        public string GetFallbackResDownLoadPath()
        {
            return Path.Combine(FallbackResDownLoadPath, projectName, GetPlatformName()).Replace("\\", "/");
        }

        /// <summary>
        /// 获取当前的平台名称。
        /// </summary>
        /// <returns>平台名称。</returns>
        public static string GetPlatformName()
        {
#if UNITY_ANDROID
        return "Android";
#elif UNITY_IOS
        return "IOS";
#elif UNITY_WEBGL
        return "WebGL";
#else
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    return "Windows64";
                case RuntimePlatform.WindowsPlayer:
                    return "Windows64";

                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                    return "MacOS";

                case RuntimePlatform.IPhonePlayer:
                    return "IOS";

                case RuntimePlatform.Android:
                    return "Android";
                case RuntimePlatform.WebGLPlayer:
                    return "WebGL";

                case RuntimePlatform.PS5:
                    return "PS5";
                default:
                    throw new NotSupportedException($"Platform '{Application.platform.ToString()}' is not supported.");
            }
#endif
        }
    }
}
