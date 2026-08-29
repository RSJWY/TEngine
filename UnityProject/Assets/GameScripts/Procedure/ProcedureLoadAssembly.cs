using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if ENABLE_HYBRIDCLR
using HybridCLR;
#endif
using UnityEngine;
using TEngine;
using System.Reflection;
using YooAsset;
using Cysharp.Threading.Tasks;

namespace Procedure
{
    /// <summary>
    /// 流程加载器 - 代码初始化
    /// </summary>
    public class ProcedureLoadAssembly : ProcedureBase
    {
        private bool _enableAddressable = true;
        private string _assemblyPackageName;
        public override bool UseNativeDialog => true;
        private int _loadAssetCount;
        private int _loadMetadataAssetCount;
        private int _failureAssetCount;
        private int _failureMetadataAssetCount;
        private bool _loadAssemblyComplete;
        private bool _loadMetadataAssemblyComplete;
        private bool _loadAssemblyWait;
        private bool _loadMetadataAssemblyWait;
        private Assembly _mainLogicAssembly;
        private List<Assembly> _hotfixAssemblyList;
        private IFsm<IProcedureModule> _procedureOwner;
        private UpdateSetting _setting;
        private readonly Dictionary<string, byte[]> _pdbBytesCache = new Dictionary<string, byte[]>();

        protected override void OnInit(IFsm<IProcedureModule> procedureOwner)
        {
            base.OnInit(procedureOwner);
            _setting = Settings.UpdateSetting;
            _assemblyPackageName = _setting.GetAssemblyPackageName();
        }

        protected override void OnEnter(IFsm<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Debug($"HybridCLR ProcedureLoadAssembly OnEnter, package: {_assemblyPackageName}");
            _procedureOwner = procedureOwner;
            LoadAssembly().Forget();
        }

        private async UniTaskVoid LoadAssembly()
        {
            _loadAssemblyComplete = false;
            _hotfixAssemblyList = new List<Assembly>();

            if (_setting.Enable)
            {
#if !UNITY_EDITOR
                _loadMetadataAssemblyComplete = false;
                LoadMetadataForAOTAssembly();
#else
                _loadMetadataAssemblyComplete = true;
#endif
            }
            else
            {
                _loadMetadataAssemblyComplete = true;
            }

            if (!_setting.Enable || _resourceModule.PlayMode == EPlayMode.EditorSimulateMode)
            {
                _mainLogicAssembly = GetMainLogicAssembly();
            }
            else
            {
                if (_setting.Enable)
                {
                    // 先加载 pdb 并缓存（仅 dev 模式且 pdb 开关开启时；pdb 缺失时静默回退）。
                    // 必须先于 dll 循环：dll 在 LoadAssetSuccess 中立即 Assembly.Load，需要 pdb 字节已就位。
                    if (_setting.WillGeneratePdb)
                    {
                        foreach (string hotUpdateDllName in _setting.HotUpdateAssemblies)
                        {
                            string pdbAssetName = Path.GetFileNameWithoutExtension(hotUpdateDllName) + ".pdb";
                            Log.Debug($"LoadAsset (pdb): [ {pdbAssetName} ] from package [ {_assemblyPackageName} ]");
                            _loadAssetCount++;
                            if (IsArchivePackage)
                            {
                                var result = await _resourceModule.LoadAssetAsync<RawFileObject>(pdbAssetName, default, _assemblyPackageName);
                                LoadAssetSuccess(result, pdbAssetName);
                            }
                            else
                            {
                                var result = await _resourceModule.LoadAssetAsync<TextAsset>(pdbAssetName, default, _assemblyPackageName);
                                LoadAssetSuccess(result, pdbAssetName);
                            }
                        }
                    }

#if ENABLE_OBFUZ && !UNITY_EDITOR
                    // 动态密钥必须在 Assembly.Load 热更 DLL 前初始化！
                    // 此时 YooAsset 已初始化（ProcedureInitResources 已完成），可加载热更密钥资源。
                    // 密钥文件位于 Assets/AssetRaw/DLL/Obfuz/，与热更 DLL 同包。
                    if (!await ObfuzRuntimeInitializer.SetUpDynamicSecretKeyAsync(_assemblyPackageName))
                    {
                        // 动态密钥加载失败：UI 已在 ProcedureLaunch 初始化，直接弹窗报告并退出。
                        // 不继续 Assembly.Load——无密钥下加载混淆 DLL 会触发类型初始化异常。
                        ObfuzRuntimeInitializer.CheckFailureAndReport();
                        _loadAssemblyComplete = true;
                        _loadAssemblyWait = true;
                        return;
                    }
#endif

                    // 加载热更 dll
                    foreach (string hotUpdateDllName in _setting.HotUpdateAssemblies)
                    {
                        var assetLocation = hotUpdateDllName;
                        if (!_enableAddressable)
                        {
                            assetLocation = Utility.Path.GetRegularPath(
                                Path.Combine(
                                    "Assets",
                                    _setting.AssemblyTextAssetPath,
                                    $"{hotUpdateDllName}{_setting.AssemblyTextAssetExtension}"));
                        }

                        Log.Debug($"LoadAsset: [ {assetLocation} ] from package [ {_assemblyPackageName} ]");
                        _loadAssetCount++;
                        if (IsArchivePackage)
                        {
                            var result = await _resourceModule.LoadAssetAsync<RawFileObject>(assetLocation, default, _assemblyPackageName);
                            LoadAssetSuccess(result, assetLocation);
                        }
                        else
                        {
                            var result = await _resourceModule.LoadAssetAsync<TextAsset>(assetLocation, default, _assemblyPackageName);
                            LoadAssetSuccess(result, assetLocation);
                        }
                    }

                    _loadAssemblyWait = true;
                }
                else
                {
                    _mainLogicAssembly = GetMainLogicAssembly();
                }
            }

            if (_loadAssetCount == 0)
            {
                _loadAssemblyComplete = true;
            }
        }

        protected override void OnUpdate(IFsm<IProcedureModule> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
            if (!_loadAssemblyComplete)
            {
                return;
            }
            if (!_loadMetadataAssemblyComplete)
            {
                return;
            }
            AllAssemblyLoadComplete();
        }

        private void AllAssemblyLoadComplete()
        {
            ChangeState<ProcedureStartGame>(_procedureOwner);
#if UNITY_EDITOR
            _mainLogicAssembly = GetMainLogicAssembly();
#endif
            if (_mainLogicAssembly == null)
            {
                Log.Fatal($"Main logic assembly missing. Please check 'ENABLE_HYBRIDCLR' is defined in Player Settings And check the file of {_setting.LogicMainDllName}.bytes is exits.");
                return;
            }

            var appType = _mainLogicAssembly.GetType("GameApp");
            if (appType == null)
            {
                Log.Fatal("Main logic type 'GameMain' missing.");
                return;
            }
            var entryMethod = appType.GetMethod("Entrance");
            if (entryMethod == null)
            {
                Log.Fatal("Main logic entry method 'Entrance' missing.");
                return;
            }
            object[] objects = new object[] { new object[] { _hotfixAssemblyList } };
            entryMethod.Invoke(appType, objects);
        }

        private Assembly GetMainLogicAssembly()
        {
            _hotfixAssemblyList.Clear();
            Assembly mainLogicAssembly = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Compare(_setting.LogicMainDllName, $"{assembly.GetName().Name}.dll",
                        StringComparison.Ordinal) == 0)
                {
                    mainLogicAssembly = assembly;
                }

                foreach (var hotUpdateDllName in _setting.HotUpdateAssemblies)
                {
                    if (hotUpdateDllName == $"{assembly.GetName().Name}.dll")
                    {
                        _hotfixAssemblyList.Add(assembly);
                    }
                }

                if (mainLogicAssembly != null && _hotfixAssemblyList.Count == _setting.HotUpdateAssemblies.Count)
                {
                    break;
                }
            }

            return mainLogicAssembly;
        }

        private bool IsArchivePackage => _setting.GetRuntimePackage(_assemblyPackageName)?.BuildPipeline == RuntimePackageBuildPipeline.ArchiveFileBuildPipeline;

        private void LoadAssetSuccess(UnityEngine.Object asset, string assetLocation)
        {
            _loadAssetCount--;
            if (asset == null)
            {
                Log.Warning("Load Assembly failed.");
                return;
            }

            var assetName = Path.GetFileName(assetLocation);
            var rawFile = asset as RawFileObject;
            var textAsset = asset as TextAsset;
            var assetBytes = rawFile != null ? rawFile.GetBytes() : textAsset?.bytes;
            Log.Debug($"LoadAssetSuccess, assetName: [ {assetName} ], package: [ {_assemblyPackageName} ]");

            try
            {
                // 判断是 pdb 还是 dll
                if (assetName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                {
                    // 缓存 pdb 字节流（key = assembly 名，如 "GameLogic"）
                    string assemblyName = Path.GetFileNameWithoutExtension(assetName);
                    if (!_pdbBytesCache.ContainsKey(assemblyName))
                    {
                        _pdbBytesCache[assemblyName] = assetBytes;
                        Log.Debug($"PDB cached: [ {assemblyName} ]");
                    }
                    _resourceModule.UnloadAsset(asset);
                    return;
                }

                // 加载 dll（尝试带 pdb）
                string dllName = Path.GetFileNameWithoutExtension(assetName);
                byte[] dllBytes = assetBytes;
                byte[] pdbBytes = _pdbBytesCache.ContainsKey(dllName) ? _pdbBytesCache[dllName] : null;

                Assembly assembly = pdbBytes != null
                    ? Assembly.Load(dllBytes, pdbBytes)  // 带 pdb 加载
                    : Assembly.Load(dllBytes);           // 无 pdb 时回退单参数

                if (string.Compare(_setting.LogicMainDllName, assetName, StringComparison.Ordinal) == 0)
                {
                    _mainLogicAssembly = assembly;
                }
                _hotfixAssemblyList.Add(assembly);
                Log.Debug($"Assembly [ {assembly.GetName().Name} ] loaded{(pdbBytes != null ? " with PDB" : "")}");
            }
            catch (Exception e)
            {
                _failureAssetCount++;
                Log.Fatal(e);
                throw;
            }
            finally
            {
                _loadAssemblyComplete = _loadAssemblyWait && 0 == _loadAssetCount;
            }
            _resourceModule.UnloadAsset(asset);
        }

        private async UniTaskVoid LoadMetadataForAOTAssembly()
        {
            Log.Debug($"[AOTMetadata] 开始准备加载AOT补充元数据。Package:{_assemblyPackageName}, Addressable:{_enableAddressable}, AssetPath:{_setting.AssemblyTextAssetPath}, Extension:{_setting.AssemblyTextAssetExtension}");
            var aotMetaAssemblies = await GetAOTMetaAssembliesAsync();
            if (aotMetaAssemblies.Count == 0)
            {
                Log.Warning("[AOTMetadata] AOT补充元数据列表为空，跳过加载。");
                _loadMetadataAssemblyComplete = true;
                return;
            }

            Log.Debug($"[AOTMetadata] 最终运行时AOT补充元数据加载列表，Count:{aotMetaAssemblies.Count}, List:{string.Join(", ", aotMetaAssemblies)}");
            _loadMetadataAssemblyWait = true;
            foreach (string aotDllName in aotMetaAssemblies)
            {
                var assetLocation = aotDllName;
                if (!_enableAddressable)
                {
                    assetLocation = Utility.Path.GetRegularPath(
                        Path.Combine(
                            "Assets",
                            _setting.AssemblyTextAssetPath,
                            $"{aotDllName}{_setting.AssemblyTextAssetExtension}"));
                }

                Log.Debug($"[AOTMetadata] 请求加载AOT元数据资源。Dll:{aotDllName}, Location:{assetLocation}, Package:{_assemblyPackageName}");
                _loadMetadataAssetCount++;
                if (IsArchivePackage)
                {
                    _resourceModule.LoadAsset<RawFileObject>(assetLocation,
                        asset => LoadMetadataAssetSuccess(asset, assetLocation), _assemblyPackageName);
                }
                else
                {
                    _resourceModule.LoadAsset<TextAsset>(assetLocation,
                        asset => LoadMetadataAssetSuccess(asset, assetLocation), _assemblyPackageName);
                }
            }
        }

        private async UniTask<List<string>> GetAOTMetaAssembliesAsync()
        {
            if (_setting.GetRuntimePackage(_assemblyPackageName)?.BuildPipeline == RuntimePackageBuildPipeline.ArchiveFileBuildPipeline)
            {
                return _setting.AOTMetaAssemblies
                    .Where(assembly => !string.IsNullOrWhiteSpace(assembly))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }

            var manifestLocation = AOTMetadataManifest.ManifestAssetName;
            if (!_enableAddressable)
            {
                manifestLocation = Utility.Path.GetRegularPath(
                    Path.Combine("Assets", _setting.AssemblyTextAssetPath, $"{AOTMetadataManifest.ManifestAssetName}.asset"));
            }

            Log.Debug($"[AOTMetadata] 查找运行时AOTMetadataManifest。Location:{manifestLocation}, Package:{_assemblyPackageName}");
            if (_resourceModule.CheckLocationValid(manifestLocation, _assemblyPackageName))
            {
                Log.Debug($"[AOTMetadata] AOTMetadataManifest location有效，开始加载。Location:{manifestLocation}, Package:{_assemblyPackageName}");
                var manifest = await _resourceModule.LoadAssetAsync<AOTMetadataManifest>(manifestLocation, default, _assemblyPackageName);
                if (manifest != null && manifest.AOTMetaAssemblies != null && manifest.AOTMetaAssemblies.Count > 0)
                {
                    var assemblies = manifest.AOTMetaAssemblies
                        .Where(assembly => !string.IsNullOrWhiteSpace(assembly))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    _resourceModule.UnloadAsset(manifest);
                    Log.Debug($"[AOTMetadata] 使用热更AOTMetadataManifest列表。Location:{manifestLocation}, Count:{assemblies.Count}, List:{string.Join(", ", assemblies)}");
                    return assemblies;
                }

                if (manifest != null)
                {
                    Log.Warning($"[AOTMetadata] AOTMetadataManifest已加载但列表为空，回退 UpdateSetting.AOTMetaAssemblies。Location:{manifestLocation}");
                    _resourceModule.UnloadAsset(manifest);
                }
                else
                {
                    Log.Warning($"[AOTMetadata] AOTMetadataManifest加载结果为空，回退 UpdateSetting.AOTMetaAssemblies。Location:{manifestLocation}");
                }
            }
            else
            {
                Log.Warning($"[AOTMetadata] AOTMetadataManifest location无效，回退 UpdateSetting.AOTMetaAssemblies。Location:{manifestLocation}, Package:{_assemblyPackageName}");
            }

            var fallbackAssemblies = _setting.AOTMetaAssemblies
                .Where(assembly => !string.IsNullOrWhiteSpace(assembly))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Log.Debug($"[AOTMetadata] 使用 UpdateSetting.AOTMetaAssemblies 回退列表，Count:{fallbackAssemblies.Count}, List:{string.Join(", ", fallbackAssemblies)}");
            return fallbackAssemblies;
        }

        private void LoadMetadataAssetSuccess(UnityEngine.Object asset, string assetLocation)
        {
            _loadMetadataAssetCount--;
            if (asset == null)
            {
                Log.Warning("[AOTMetadata] AOT元数据资源加载失败，TextAsset为空。");
                _loadMetadataAssemblyComplete = _loadMetadataAssemblyWait && 0 == _loadMetadataAssetCount;
                return;
            }

            string assetName = Path.GetFileName(assetLocation);
            var rawFile = asset as RawFileObject;
            var textAsset = asset as TextAsset;
            byte[] assetBytes = rawFile != null ? rawFile.GetBytes() : textAsset?.bytes;
            Log.Debug($"[AOTMetadata] AOT元数据资源加载成功。Asset:{assetName}, Package:{_assemblyPackageName}, Size:{assetBytes?.Length ?? 0} bytes, Remaining:{_loadMetadataAssetCount}");
            try
            {
                byte[] dllBytes = assetBytes;
#if ENABLE_HYBRIDCLR
                HomologousImageMode mode = HomologousImageMode.SuperSet;
                LoadImageErrorCode err = (LoadImageErrorCode)HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, mode);
                Log.Warning($"[AOTMetadata] HybridCLR LoadMetadataForAOTAssembly 完成。Asset:{assetName}, Mode:{mode}, Ret:{err}");
#endif
            }
            catch (Exception e)
            {
                _failureMetadataAssetCount++;
                Log.Fatal(e.Message);
                throw;
            }
            finally
            {
                _loadMetadataAssemblyComplete = _loadMetadataAssemblyWait && 0 == _loadMetadataAssetCount;
                _resourceModule.UnloadAsset(asset);
            }
        }
    }
}
