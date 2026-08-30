using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
using WeChatWASM;
#endif

namespace TEngine
{
    /// <summary>
    /// 资源管理器。
    /// </summary>
    internal sealed partial class ResourceModule : Module, IResourceModule
    {
        /// <summary>
        /// 默认资源包名称。
        /// </summary>
        public string DefaultPackageName { get; set; } = "DefaultPackage";

        /// <summary>
        /// 资源系统运行模式。
        /// </summary>
        public EPlayMode PlayMode { get; set; } = EPlayMode.OfflinePlayMode;

        public EncryptionType EncryptionType { get; set; } = EncryptionType.None;

        /// <summary>
        /// 设置异步系统参数，每帧执行消耗的最大时间切片（单位：毫秒）
        /// </summary>
        public long Milliseconds { get; set; } = 30;

        /// <summary>
        /// 自动释放资源引用计数为0的资源包
        /// </summary>
        public bool AutoUnloadBundleWhenUnused { get; set; } = false;

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        /// <remarks>优先级较高的模块会优先轮询，并且关闭操作会后进行。</remarks>
        public override int Priority => 4;

        public override void OnInit()
        {
        }

        public override void Shutdown()
        {
        }

        /// <summary>
        /// 资源服务器地址。
        /// </summary>
        public string HostServerURL { get; set; }

        public string FallbackHostServerURL { get; set; }

        /// <summary>
        /// WebGL：加载资源方式
        /// </summary>
        public LoadResWayWebGL LoadResWayWebGL { get; set; }

        private string _applicableGameVersion;

        private int _internalResourceVersion;

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public string ApplicableGameVersion => _applicableGameVersion;

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public int InternalResourceVersion => _internalResourceVersion;

        /// <summary>
        /// 当前最新的包裹版本。
        /// </summary>
        public string PackageVersion { set; get; }

        public int DownloadingMaxNum { get; set; }

        public int FailedTryAgain { get; set; }

        /// <summary>
        /// 是否支持边玩边下载。
        /// </summary>
        public bool UpdatableWhilePlaying { get; set; }

        #region internal

        /// <summary>
        /// 默认资源包。
        /// </summary>
        internal ResourcePackage DefaultPackage { private set; get; }

        /// <summary>
        /// 资源包列表。
        /// </summary>
        private Dictionary<string, ResourcePackage> PackageMap { get; } = new Dictionary<string, ResourcePackage>();

        /// <summary>
        /// 资源信息列表。
        /// </summary>
        private readonly Dictionary<string, AssetInfo> _assetInfoMap = new Dictionary<string, AssetInfo>();

        /// <summary>
        /// 正在加载的资源列表。
        /// </summary>
        private readonly HashSet<string> _assetLoadingList = new HashSet<string>();

        private UnityWebRequestCreator _unityWebRequestCreator;

        #endregion

        public void Initialize()
        {
            // 初始化资源系统
            YooAssets.Initialize(new ResourceLogger());
            YooAssets.SetAsyncOperationMaxTimeSlice(Milliseconds);

            // 创建默认的资源包
            string packageName = DefaultPackageName;
            if (!YooAssets.TryGetPackage(packageName, out var defaultPackage))
                defaultPackage = YooAssets.CreatePackage(packageName);

            DefaultPackage = defaultPackage;

            IObjectPoolModule objectPoolManager = ModuleSystem.GetModule<IObjectPoolModule>();
            SetObjectPoolModule(objectPoolManager);
        }

        public async UniTask<InitializePackageOperation> InitPackage(string packageName, bool needInitMainFest = false)
        {
#if UNITY_EDITOR
            //编辑器模式使用。
            EPlayMode playMode = (EPlayMode)UnityEditor.EditorPrefs.GetInt(
                "EditorPlayMode", (int)EPlayMode.EditorSimulateMode);
            Log.Warning($"Editor Module Used :{playMode}");
#else
            //运行时使用。
            EPlayMode playMode = (EPlayMode)PlayMode;
#endif

            if (PackageMap.TryGetValue(packageName, out var resourcePackage))
            {
                if (resourcePackage.InitializeStatus is EOperationStatus.Processing or EOperationStatus.Succeeded)
                {
                    Log.Error($"ResourceSystem has already init package : {packageName}");
                    return null;
                }
                else
                {
                    PackageMap.Remove(packageName);
                }
            }

            // 创建资源包裹类
            if (!YooAssets.TryGetPackage(packageName, out var package))
                package = YooAssets.CreatePackage(packageName);

            PackageMap[packageName] = package;

            // 编辑器下的模拟模式
            InitializePackageOperation initializationOperation = null;
            if (playMode == EPlayMode.EditorSimulateMode)
            {
                var buildResult = EditorSimulateBuildInvoker.Build(packageName, GetEditorSimulateBundleType(packageName));
                var packageRoot = buildResult.PackageRootDirectory;
                var createParameters = new EditorSimulateModeOptions();
                createParameters.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
                AddWebRequestCreator(createParameters.EditorFileSystemParameters);
                createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                initializationOperation = package.InitializePackageAsync(createParameters);
            }

            BundleCrypto bundleCrypto = BundleCrypto.Create(GetEncryptionType(packageName));
            bool manifestEncrypted = GetManifestEncrypted(packageName);

            // 单机运行模式
            if (playMode == EPlayMode.OfflinePlayMode)
            {
                var createParameters = new OfflinePlayModeOptions();
                createParameters.BuiltinFileSystemParameters = CreateBuiltinFileSystemParameters(bundleCrypto?.Local, bundleCrypto?.Archive, manifestEncrypted);
                createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                initializationOperation = package.InitializePackageAsync(createParameters);
            }

            // 联机运行模式
            if (playMode == EPlayMode.HostPlayMode)
            {
                string defaultHostServer = Settings.UpdateSetting.GetPackageHostServerURL(packageName);
                string fallbackHostServer = Settings.UpdateSetting.GetPackageFallbackHostServerURL(packageName);
                Log.Info($"HostPlay 资源包远端目录：{packageName} => {defaultHostServer}");
                IRemoteService remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
                var createParameters = new HostPlayModeOptions();
                createParameters.BuiltinFileSystemParameters = CreateBuiltinFileSystemParameters(bundleCrypto?.Local, bundleCrypto?.Archive, manifestEncrypted);
                createParameters.CacheFileSystemParameters = CreateSandboxFileSystemParameters(remoteServices, bundleCrypto?.Local, bundleCrypto?.Archive, manifestEncrypted);
                createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                initializationOperation = package.InitializePackageAsync(createParameters);
            }

            // WebGL运行模式
            if (playMode == EPlayMode.WebPlayMode)
            {
                var createParameters = new WebPlayModeOptions();
                string defaultHostServer = Settings.UpdateSetting.GetPackageHostServerURL(packageName);
                string fallbackHostServer = Settings.UpdateSetting.GetPackageFallbackHostServerURL(packageName);
                Log.Info($"WebPlay 资源包远端目录：{packageName} => {defaultHostServer}");
                IRemoteService remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
                Log.Info("=======================WEIXINMINIGAME=======================");
                string packageRoot = $"{WeChatWASM.WX.env.USER_DATA_PATH}/__GAME_FILE_CACHE";
                createParameters.WebServerFileSystemParameters = WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remoteServices, bundleCrypto?.Web);
                AddWebRequestCreator(createParameters.WebServerFileSystemParameters);
                AddManifestDecryptor(createParameters.WebServerFileSystemParameters, manifestEncrypted);
#else
                Log.Info("=======================UNITY_WEBGL=======================");
                if (LoadResWayWebGL == LoadResWayWebGL.Remote)
                {
                    createParameters.WebNetworkFileSystemParameters = CreateWebNetworkFileSystemParameters(remoteServices, bundleCrypto?.Web, bundleCrypto?.Archive, manifestEncrypted);
                }

                createParameters.WebServerFileSystemParameters = CreateWebServerFileSystemParameters(bundleCrypto?.Web, bundleCrypto?.Archive, manifestEncrypted);
#endif
                createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                initializationOperation = package.InitializePackageAsync(createParameters);
            }

            if (initializationOperation == null)
                throw new GameFrameworkException($"Unsupported YooAsset play mode: {playMode}");

            await initializationOperation.ToUniTask();

            Log.Info($"Init resource package version : {initializationOperation?.Status}");

            if (needInitMainFest)
            {
                // 2. 请求资源清单的版本信息
                var requestPackageVersionOperation = package.RequestPackageVersionAsync(new RequestPackageVersionOptions(false, 60));
                await requestPackageVersionOperation;
                if (requestPackageVersionOperation.Status == EOperationStatus.Succeeded)
                {
                    // 3. 传入的版本信息更新资源清单
                    var updatePackageManifestAsync = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(requestPackageVersionOperation.PackageVersion, 60));
                    await updatePackageManifestAsync;
                    if (updatePackageManifestAsync.Status == EOperationStatus.Failed)
                    {
                        Log.Fatal($"Update package manifest failed : {updatePackageManifestAsync.Status}");
                    }
                }
                else
                {
                    Log.Fatal($"Request package version failed : {requestPackageVersionOperation.Status}");
                }
            }

            return initializationOperation;
        }

        private FileSystemParameters CreateBuiltinFileSystemParameters(IBundleDecryptor decryptor, IBundleDecryptor archiveDecryptor, bool manifestEncrypted)
        {
            var parameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            AddBundleDecryptor(parameters, decryptor, archiveDecryptor);
            AddManifestDecryptor(parameters, manifestEncrypted);
            AddWebRequestCreator(parameters);
            return parameters;
        }

        private FileSystemParameters CreateSandboxFileSystemParameters(IRemoteService remoteService, IBundleDecryptor decryptor, IBundleDecryptor archiveDecryptor, bool manifestEncrypted)
        {
            var parameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
            AddBundleDecryptor(parameters, decryptor, archiveDecryptor);
            AddManifestDecryptor(parameters, manifestEncrypted);
            AddWebRequestCreator(parameters);
            return parameters;
        }

        private FileSystemParameters CreateWebServerFileSystemParameters(IBundleDecryptor decryptor, IBundleDecryptor archiveDecryptor, bool manifestEncrypted)
        {
            var parameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
            AddBundleDecryptor(parameters, decryptor, archiveDecryptor);
            AddManifestDecryptor(parameters, manifestEncrypted);
            AddWebRequestCreator(parameters);
            return parameters;
        }

        private FileSystemParameters CreateWebNetworkFileSystemParameters(IRemoteService remoteService, IBundleDecryptor decryptor, IBundleDecryptor archiveDecryptor, bool manifestEncrypted)
        {
            var parameters = FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(remoteService);
            AddBundleDecryptor(parameters, decryptor, archiveDecryptor);
            AddManifestDecryptor(parameters, manifestEncrypted);
            AddWebRequestCreator(parameters);
            return parameters;
        }

        private static void AddBundleDecryptor(FileSystemParameters parameters, IBundleDecryptor decryptor, IBundleDecryptor archiveDecryptor)
        {
            if (decryptor != null)
            {
                parameters.AddParameter(EFileSystemParameter.AssetBundleDecryptor, decryptor);
                parameters.AddParameter(EFileSystemParameter.RawBundleDecryptor, decryptor);
            }
            if (archiveDecryptor != null)
                parameters.AddParameter(EFileSystemParameter.ArchiveBundleDecryptor, archiveDecryptor);
        }

        /// <summary>
        /// 按包配置注入清单解密器。清单加密固定为 ChaCha20，密钥与 Bundle 独立。
        /// </summary>
        private static void AddManifestDecryptor(FileSystemParameters parameters, bool manifestEncrypted)
        {
            if (manifestEncrypted)
                parameters.AddParameter(EFileSystemParameter.ManifestDecryptor, new ManifestChaCha20Decryptor());
        }

        private int GetEditorSimulateBundleType(string packageName)
        {
            var pipeline = Settings.UpdateSetting?.GetRuntimePackage(packageName)?.BuildPipeline
                           ?? RuntimePackageBuildPipeline.UseGlobal;
            return pipeline switch
            {
                RuntimePackageBuildPipeline.ArchiveFileBuildPipeline => (int)EBundleType.VirtualArchiveBundle,
                RuntimePackageBuildPipeline.RawFileBuildPipeline => (int)EBundleType.VirtualRawBundle,
                _ => (int)EBundleType.VirtualAssetBundle,
            };
        }

        private void AddWebRequestCreator(FileSystemParameters parameters)
        {
            if (_unityWebRequestCreator != null)
                parameters.AddParameter(EFileSystemParameter.UnityWebRequestCreator, _unityWebRequestCreator);
        }

        private EncryptionType GetEncryptionType(string packageName)
        {
            var runtimePackage = Settings.UpdateSetting != null ? Settings.UpdateSetting.GetRuntimePackage(packageName) : null;
            return runtimePackage != null ? runtimePackage.EncryptionType : EncryptionType;
        }

        /// <summary>
        /// 获取资源包是否启用清单加密。
        /// </summary>
        private bool GetManifestEncrypted(string packageName)
        {
            var runtimePackage = Settings.UpdateSetting != null ? Settings.UpdateSetting.GetRuntimePackage(packageName) : null;
            return runtimePackage != null && runtimePackage.ManifestEncrypted;
        }

        /// <summary>
        /// 获取当前资源包版本。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源包版本。</returns>
        public string GetPackageVersion(string customPackageName = "")
        {
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            if (package == null)
            {
                return string.Empty;
            }

            return package.GetPackageVersion();
        }

        /// <summary>
        /// 异步更新最新包的版本。
        /// </summary>
        /// <param name="appendTimeTicks">请求URL是否需要带时间戳。</param>
        /// <param name="timeout">超时时间。</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>请求远端包裹的最新版本操作句柄。</returns>
        public RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60,
            string customPackageName = "")
        {
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            return package.RequestPackageVersionAsync(new RequestPackageVersionOptions(appendTimeTicks, timeout));
        }

        public void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer)
        {
            HostServerURL = defaultHostServer;
            FallbackHostServerURL = fallbackHostServer;
        }

        /// <summary>
        /// 向网络端请求并更新清单
        /// </summary>
        /// <param name="packageVersion">更新的包裹版本</param>
        /// <param name="timeout">超时时间（默认值：60秒）</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public LoadPackageManifestOperation UpdatePackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
        {
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(this.DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            return package.LoadPackageManifestAsync(new LoadPackageManifestOptions(packageVersion, timeout));
        }

        /// <summary>
        /// 资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        public ResourceDownloaderOperation Downloader { get; set; }

        /// <summary>
        /// 创建资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "")
        {
            ResourcePackage package;
            if (string.IsNullOrEmpty(customPackageName))
            {
                package = YooAssets.GetPackage(this.DefaultPackageName);
            }
            else
            {
                package = YooAssets.GetPackage(customPackageName);
            }

            Downloader = package.CreateResourceDownloader(new ResourceDownloaderOptions(DownloadingMaxNum, FailedTryAgain));
            return Downloader;
        }

        /// <summary>
        /// 清理包裹未使用的缓存文件。
        /// </summary>
        /// <param name="clearMode">文件清理方式。</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public ClearCacheOperation ClearCacheFilesAsync(
            string clearMode = ClearCacheMethods.ClearUnusedBundleFiles,
            string customPackageName = "")
        {
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            return package.ClearCacheAsync(new ClearCacheOptions(clearMode));
        }

        /// <summary>
        /// 清理沙盒路径。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public void ClearAllBundleFiles(string customPackageName = "")
            => ClearCacheFilesAsync(ClearCacheMethods.ClearAllBundleFiles, customPackageName);

        #region 资源回收

        public void OnLowMemory()
        {
            Log.Warning("Low memory reported...");
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        private Action<bool> _forceUnloadUnusedAssetsAction;

        /// <summary>
        /// 低内存回调保护。
        /// </summary>
        /// <param name="action">低内存行为。</param>
        public void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
            _forceUnloadUnusedAssetsAction = action;
        }

        /// <summary>
        /// 资源回收（卸载引用计数为零的资源）。
        /// </summary>
        public void UnloadUnusedAssets()
        {
            _assetPool.ReleaseAllUnused();
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeeded })
                {
                    package.UnloadUnusedAssetsAsync();
                }
            }
        }

        /// <summary>
        /// 强制回收所有资源。
        /// </summary>
        public void ForceUnloadAllAssets()
        {
#if UNITY_WEBGL
            Log.Warning($"WebGL not support invoke {nameof(ForceUnloadAllAssets)}");
			return;
#else

            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeeded })
                {
                    package.UnloadAllAssetsAsync();
                }
            }
#endif
        }

        public void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            _forceUnloadUnusedAssetsAction?.Invoke(performGCCollect);
        }

        #region Public Methods

        #region 获取资源信息

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        public bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return DefaultPackage.GetDownloadSize(location) > 0;
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.GetDownloadSize(location) > 0;
            }
        }

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="assetInfo">资源信息。</param>
        /// <param name="packageName">资源包名称。</param>
        public bool IsNeedDownloadFromRemote(AssetInfo assetInfo, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return DefaultPackage.GetDownloadSize(assetInfo) > 0;
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.GetDownloadSize(assetInfo) > 0;
            }
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tag">资源标签。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string tag, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return DefaultPackage.GetAssetInfos(tag);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.GetAssetInfos(tag);
            }
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tags">资源标签列表。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return DefaultPackage.GetAssetInfos(tags);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.GetAssetInfos(tags);
            }
        }

        /// <summary>
        /// 获取资源信息。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息。</returns>
        public AssetInfo GetAssetInfo(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (string.IsNullOrEmpty(packageName))
            {
                if (_assetInfoMap.TryGetValue(location, out AssetInfo assetInfo))
                {
                    return assetInfo;
                }

                assetInfo = DefaultPackage.GetAssetInfo(location);
                _assetInfoMap[location] = assetInfo;
                return assetInfo;
            }
            else
            {
                string key = $"{packageName}/{location}";
                if (_assetInfoMap.TryGetValue(key, out AssetInfo assetInfo))
                {
                    return assetInfo;
                }

                var package = YooAssets.GetPackage(packageName);
                if (package == null)
                {
                    throw new GameFrameworkException($"The package does not exist. Package Name :{packageName}");
                }

                assetInfo = package.GetAssetInfo(location);
                _assetInfoMap[key] = assetInfo;
                return assetInfo;
            }
        }

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>检查资源是否存在的结果。</returns>
        public HasAssetResult HasAsset(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            AssetInfo assetInfo = GetAssetInfo(location, packageName);

            if (!CheckLocationValid(location, packageName))
            {
                return HasAssetResult.Valid;
            }

            if (assetInfo == null)
            {
                return HasAssetResult.NotExist;
            }

            if (IsNeedDownloadFromRemote(assetInfo))
            {
                return HasAssetResult.AssetOnline;
            }

            return HasAssetResult.AssetOnDisk;
        }

        /// <summary>
        /// 检查资源定位地址是否有效。
        /// </summary>
        /// <param name="location">资源的定位地址</param>
        /// <param name="packageName">资源包名称。</param>
        public bool CheckLocationValid(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return DefaultPackage.IsLocationValid(location);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.IsLocationValid(location);
            }
        }

        #endregion

        #region 资源加载

        #region 获取资源句柄

        /// <summary>
        /// 获取同步资源句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源句柄。</returns>
        private AssetHandle GetHandleSync<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return GetHandleSync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleSync(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return DefaultPackage.LoadAssetSync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetSync(location, assetType);
        }

        /// <summary>
        /// 获取异步资源句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源句柄。</returns>
        private AssetHandle GetHandleAsync<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return GetHandleAsync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleAsync(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return DefaultPackage.LoadAssetAsync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetAsync(location, assetType);
        }

        #endregion

        /// <summary>
        /// 获取资源定位地址的缓存Key。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源定位地址的缓存Key。</returns>
        private string GetCacheKey(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName) || packageName.Equals(DefaultPackageName))
            {
                return location;
            }

            return $"{packageName}/{location}";
        }

        public T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAsset(location, typeof(T), packageName) as T;
        }

        public UnityEngine.Object LoadAsset(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error($"Could not found location [{location}].");
                return null;
            }

            string assetObjectKey = GetCacheKey(location, packageName);
            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject != null)
            {
                return assetObject.Target as UnityEngine.Object;
            }

            AssetHandle handle = GetHandleSync(location, assetType, packageName);

            var ret = handle.AssetObject;

            assetObject = AssetObject.Create(assetObjectKey, handle.AssetObject, handle, this);
            _assetPool.Register(assetObject, true);

            return ret;
        }

        public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error($"Could not found location [{location}].");
                return null;
            }

            string assetObjectKey = GetCacheKey(location, packageName);
            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject != null)
            {
                return AssetsReference.Instantiate(assetObject.Target as GameObject, parent, this).gameObject;
            }

            AssetHandle handle = GetHandleSync<GameObject>(location, packageName: packageName);

            GameObject gameObject = AssetsReference.Instantiate(handle.AssetObject as GameObject, parent, this).gameObject;

            assetObject = AssetObject.Create(assetObjectKey, handle.AssetObject, handle, this);
            _assetPool.Register(assetObject, true);
#if UNITY_EDITOR&&EditorFixedMaterialShader
            if (PlayMode!=EPlayMode.EditorSimulateMode)
            {
                Utility.MaterialHelper.FixedMaterialShader_All(gameObject.transform);
            }
#endif
            return gameObject;
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="callback">回调函数。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        public async UniTaskVoid LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(location))
            {
                Log.Error("Asset name is invalid.");
                return;
            }

            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error($"Could not found location [{location}].");
                callback?.Invoke(null);
                return;
            }

            string assetObjectKey = GetCacheKey(location, packageName);

            await TryWaitingLoading(assetObjectKey);

            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject != null)
            {
                await UniTask.Yield();
                callback?.Invoke(assetObject.Target as T);
                return;
            }

            _assetLoadingList.Add(assetObjectKey);

            AssetHandle handle = GetHandleAsync<T>(location, packageName: packageName);

            handle.Completed += assetHandle =>
            {
                _assetLoadingList.Remove(assetObjectKey);

                if (assetHandle.AssetObject != null)
                {
                    assetObject = AssetObject.Create(assetObjectKey, handle.AssetObject, handle, this);
                    _assetPool.Register(assetObject, true);

                    callback?.Invoke(assetObject.Target as T);
                }
                else
                {
                    callback?.Invoke(null);
                }
            };
        }

        public async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object
        {
            return await LoadAssetAsync(location, typeof(T), cancellationToken, packageName) as T;
        }

        public async UniTask<UnityEngine.Object> LoadAssetAsync(string location, Type assetType, CancellationToken cancellationToken = default, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error($"Could not found location [{location}].");
                return null;
            }

            string assetObjectKey = GetCacheKey(location, packageName);

            await TryWaitingLoading(assetObjectKey);

            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject != null)
            {
                await UniTask.Yield();
                return assetObject.Target as UnityEngine.Object;
            }

            _assetLoadingList.Add(assetObjectKey);

            AssetHandle handle = GetHandleAsync(location, assetType, packageName);
            bool cancelOrFailed = await handle.ToUniTask(cancellationToken: cancellationToken).AttachExternalCancellation(cancellationToken).SuppressCancellationThrow();

            if (cancelOrFailed)
            {
                _assetLoadingList.Remove(assetObjectKey);
                handle.Dispose();
                return null;
            }

            assetObject = AssetObject.Create(assetObjectKey, handle.AssetObject, handle, this);
            _assetPool.Register(assetObject, true);

            _assetLoadingList.Remove(assetObjectKey);

            return handle.AssetObject;
        }

        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error($"Could not found location [{location}].");
                return null;
            }

            string assetObjectKey = GetCacheKey(location, packageName);

            await TryWaitingLoading(assetObjectKey);

            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject != null)
            {
                await UniTask.Yield();
                return AssetsReference.Instantiate(assetObject.Target as GameObject, parent, this).gameObject;
            }

            _assetLoadingList.Add(assetObjectKey);

            AssetHandle handle = GetHandleAsync<GameObject>(location, packageName: packageName);

            bool cancelOrFailed = await handle.ToUniTask().AttachExternalCancellation(cancellationToken).SuppressCancellationThrow();

            if (cancelOrFailed)
            {
                _assetLoadingList.Remove(assetObjectKey);
                handle.Dispose();
                return null;
            }

            GameObject gameObject = AssetsReference.Instantiate(handle.AssetObject as GameObject, parent, this).gameObject;

            assetObject = AssetObject.Create(assetObjectKey, handle.AssetObject, handle, this);
            _assetPool.Register(assetObject, true);

            _assetLoadingList.Remove(assetObjectKey);
#if UNITY_EDITOR&&EditorFixedMaterialShader
            if (PlayMode!=EPlayMode.EditorSimulateMode)
            {
                Utility.MaterialHelper.FixedMaterialShader_All(gameObject.transform);
            }
#endif
            return gameObject;
        }

        #endregion

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="assetType">要加载资源的类型。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        public async void LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                string errorMessage = Utility.Text.Format("Could not found location [{0}].", location);
                Log.Error(errorMessage);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotExist, errorMessage, userData);
                }

                return;
            }

            string assetObjectKey = GetCacheKey(location, packageName);

            await TryWaitingLoading(assetObjectKey);

            float duration = Time.time;

            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject != null)
            {
                await UniTask.Yield();
                loadAssetCallbacks.LoadAssetSuccessCallback(location, assetObject.Target, Time.time - duration, userData);
                return;
            }

            _assetLoadingList.Add(assetObjectKey);

            AssetInfo assetInfo = GetAssetInfo(location, packageName);

            if (!string.IsNullOrEmpty(assetInfo.Error))
            {
                _assetLoadingList.Remove(assetObjectKey);

                string errorMessage = Utility.Text.Format("Can not load asset '{0}' because :'{1}'.", location, assetInfo.Error);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotExist, errorMessage, userData);
                    return;
                }

                throw new GameFrameworkException(errorMessage);
            }

            AssetHandle handle = GetHandleAsync(location, assetType, packageName: packageName);

            if (loadAssetCallbacks.LoadAssetUpdateCallback != null)
            {
                InvokeProgress(location, handle, loadAssetCallbacks.LoadAssetUpdateCallback, userData).Forget();
            }

            await handle.ToUniTask();

            if (handle.AssetObject == null || handle.Status == EOperationStatus.Failed)
            {
                _assetLoadingList.Remove(assetObjectKey);

                string errorMessage = Utility.Text.Format("Can not load asset '{0}'.", location);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotReady, errorMessage, userData);
                    return;
                }

                throw new GameFrameworkException(errorMessage);
            }
            else
            {
                assetObject = AssetObject.Create(assetObjectKey, handle.AssetObject, handle, this);
                _assetPool.Register(assetObject, true);

                _assetLoadingList.Remove(assetObjectKey);

                if (loadAssetCallbacks.LoadAssetSuccessCallback != null)
                {
                    duration = Time.time - duration;

                    loadAssetCallbacks.LoadAssetSuccessCallback(location, handle.AssetObject, duration, userData);
                }
            }
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        public async void LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                string errorMessage = Utility.Text.Format("Could not found location [{0}].", location);
                Log.Error(errorMessage);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotExist, errorMessage, userData);
                }

                return;
            }

            string assetObjectKey = GetCacheKey(location, packageName);

            await TryWaitingLoading(assetObjectKey);

            float duration = Time.time;

            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject != null)
            {
                await UniTask.Yield();
                loadAssetCallbacks.LoadAssetSuccessCallback(location, assetObject.Target, Time.time - duration, userData);
                return;
            }

            _assetLoadingList.Add(assetObjectKey);

            AssetInfo assetInfo = GetAssetInfo(location, packageName);

            if (!string.IsNullOrEmpty(assetInfo.Error))
            {
                _assetLoadingList.Remove(assetObjectKey);

                string errorMessage = Utility.Text.Format("Can not load asset '{0}' because :'{1}'.", location, assetInfo.Error);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotExist, errorMessage, userData);
                    return;
                }

                throw new GameFrameworkException(errorMessage);
            }

            AssetHandle handle = GetHandleAsync(location, assetInfo.AssetType, packageName: packageName);

            if (loadAssetCallbacks.LoadAssetUpdateCallback != null)
            {
                InvokeProgress(location, handle, loadAssetCallbacks.LoadAssetUpdateCallback, userData).Forget();
            }

            await handle.ToUniTask();

            if (handle.AssetObject == null || handle.Status == EOperationStatus.Failed)
            {
                _assetLoadingList.Remove(assetObjectKey);

                string errorMessage = Utility.Text.Format("Can not load asset '{0}'.", location);
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(location, LoadResourceStatus.NotReady, errorMessage, userData);
                    return;
                }

                throw new GameFrameworkException(errorMessage);
            }
            else
            {
                assetObject = AssetObject.Create(assetObjectKey, handle.AssetObject, handle, this);
                _assetPool.Register(assetObject, true);

                _assetLoadingList.Remove(assetObjectKey);

                if (loadAssetCallbacks.LoadAssetSuccessCallback != null)
                {
                    duration = Time.time - duration;

                    loadAssetCallbacks.LoadAssetSuccessCallback(location, handle.AssetObject, duration, userData);
                }
            }
        }

        private async UniTaskVoid InvokeProgress(string location, AssetHandle assetHandle, LoadAssetUpdateCallback loadAssetUpdateCallback, object userData)
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetUpdateCallback != null)
            {
                while (assetHandle is { IsValid: true, IsDone: false })
                {
                    await UniTask.Yield();

                    loadAssetUpdateCallback.Invoke(location, assetHandle.Progress, userData);
                }
            }
        }

        /// <summary>
        /// 获取同步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源操作句柄。</returns>
        public AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetSyncHandle(location, typeof(T), packageName);
        }

        public AssetHandle LoadAssetSyncHandle(string location, System.Type type, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return DefaultPackage.LoadAssetSync(location, type);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetSync(location, type);
        }

        /// <summary>
        /// 获取异步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源操作句柄。</returns>
        public AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetAsyncHandle(location, typeof(T), packageName);
        }

        public AssetHandle LoadAssetAsyncHandle(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return DefaultPackage.LoadAssetAsync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetAsync(location, assetType);
        }

        #endregion

        private readonly TimeoutController _timeoutController = new TimeoutController();

        private async UniTask TryWaitingLoading(string assetObjectKey)
        {
            if (_assetLoadingList.Contains(assetObjectKey))
            {
                try
                {
                    await UniTask.WaitUntil(() => !_assetLoadingList.Contains(assetObjectKey))
#if UNITY_EDITOR
                        .AttachExternalCancellation(_timeoutController.Timeout(TimeSpan.FromSeconds(60)));
                    _timeoutController.Reset();
#else
                    ;
#endif
                }
                catch (OperationCanceledException ex)
                {
                    if (_timeoutController.IsTimeout())
                    {
                        Log.Error($"LoadAssetAsync Waiting {assetObjectKey} timeout. reason:{ex.Message}");
                    }
                }
            }
        }

        #endregion

        #region 设置下载系统参数，自定义下载请求

        /// <summary>
        /// 设置下载系统参数，自定义下载请求。
        /// </summary>
        /// <param name="unityWebRequestCreator">自定义 UnityWebRequest 创建委托。</param>
        public void SetDownloadSystemUnityWebRequest(UnityWebRequestCreator unityWebRequestCreator)
        {
            _unityWebRequestCreator = unityWebRequestCreator;
        }

        public UnityEngine.Networking.UnityWebRequest CustomWebRequester(string url, string method)
        {
            var request = new UnityEngine.Networking.UnityWebRequest(url, method);
            var authorization = GetAuthorization("Admin", "12345");
            request.SetRequestHeader("AUTHORIZATION", authorization);
            return request;
        }

        private string GetAuthorization(string userName, string password)
        {
            string auth = $"{userName}:{password}";
            var bytes = System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(auth);
            return $"Basic {Convert.ToBase64String(bytes)}";
        }

        #endregion
    }
}
