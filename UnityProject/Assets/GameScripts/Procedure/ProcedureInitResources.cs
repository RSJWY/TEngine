using System.Collections;
using Launcher;
using TEngine;
using UnityEngine;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    public class ProcedureInitResources : ProcedureBase
    {
        private bool _initResourcesComplete;
        private bool _usedLocalPackageVersion;
        private bool _handledLocalPackageVersionNotice;

        public override bool UseNativeDialog => true;

        private ProcedureOwner _procedureOwner;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            _procedureOwner = procedureOwner;

            base.OnEnter(procedureOwner);

            _initResourcesComplete = false;
            _usedLocalPackageVersion = false;
            _handledLocalPackageVersionNotice = false;
            LauncherMgr.ShowUI<LoadUpdateUI>("初始化资源中...");
            Utility.Unity.StartCoroutine(InitResources(procedureOwner));
        }

        private void ChangeToCreateDownloaderState(ProcedureOwner procedureOwner)
        {
            ChangeState<ProcedureCreateDownloader>(procedureOwner);
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (!_initResourcesComplete)
            {
                return;
            }

            if (_usedLocalPackageVersion)
            {
                HandleLocalPackageVersionFallback(procedureOwner);
                return;
            }

            if (_resourceModule.PlayMode == EPlayMode.HostPlayMode || _resourceModule.PlayMode == EPlayMode.WebPlayMode)
            {
                Log.Debug($"Updated package Version : from {_resourceModule.GetPackageVersion()} to {_resourceModule.PackageVersion}");
                if (_resourceModule.PlayMode == EPlayMode.WebPlayMode || _resourceModule.UpdatableWhilePlaying)
                {
                    ChangeToPreloadState(procedureOwner);
                    return;
                }

                ChangeToCreateDownloaderState(procedureOwner);
                return;
            }

            ChangeToPreloadState(procedureOwner);
        }

        private IEnumerator InitResources(ProcedureOwner procedureOwner)
        {
            var runtimePackages = GetRuntimePackages();
            Log.Info($"开始更新资源清单，共 {runtimePackages.Count} 个包：{GetPackageNamesLogText(runtimePackages)}");

            foreach (var runtimePackage in runtimePackages)
            {
                if (!runtimePackage.UpdateManifestOnStartup)
                {
                    Log.Info($"跳过资源包清单更新：{runtimePackage.PackageName}");
                    continue;
                }

                LauncherMgr.ShowUI<LoadUpdateUI>($"更新清单文件...({runtimePackage.PackageName})");
                Log.Info($"请求资源包版本：{runtimePackage.PackageName}");

                var versionOperation = _resourceModule.RequestPackageVersionAsync(customPackageName: runtimePackage.PackageName);
                yield return versionOperation;
                if (versionOperation.Status != EOperationStatus.Succeed)
                {
                    if (TryGetLocalPackageVersion(runtimePackage, out var localPackageVersion))
                    {
                        Log.Warning($"请求资源包版本失败，尝试使用本地版本记录继续：{runtimePackage.PackageName} => {localPackageVersion}");
                        var localManifestOperation = _resourceModule.UpdatePackageManifestAsync(localPackageVersion,
                            customPackageName: runtimePackage.PackageName);
                        yield return localManifestOperation;
                        if (localManifestOperation.Status == EOperationStatus.Succeed)
                        {
                            procedureOwner.SetData(GetPackageVersionDataKey(runtimePackage.PackageName), localPackageVersion);
                            if (runtimePackage.PackageName == _resourceModule.DefaultPackageName)
                            {
                                _resourceModule.PackageVersion = localPackageVersion;
                            }

                            _usedLocalPackageVersion = true;
                            Log.Warning($"资源包已回退到本地版本：{runtimePackage.PackageName} => {localPackageVersion}");
                            continue;
                        }

                        OnInitResourcesError(procedureOwner, runtimePackage.PackageName,
                            $"{versionOperation.Error}\n本地版本清单恢复失败：{localManifestOperation.Error}");
                        yield break;
                    }

                    OnInitResourcesError(procedureOwner, runtimePackage.PackageName, versionOperation.Error);
                    yield break;
                }

                var packageVersion = versionOperation.PackageVersion;
                procedureOwner.SetData(GetPackageVersionDataKey(runtimePackage.PackageName), packageVersion);
                if (runtimePackage.PackageName == _resourceModule.DefaultPackageName)
                {
                    _resourceModule.PackageVersion = packageVersion;
                    var versionKey = GetVersionPlayerPrefsKey(runtimePackage);
                    if (!string.IsNullOrEmpty(versionKey) && Utility.PlayerPrefs.HasKey(versionKey))
                    {
                        Utility.PlayerPrefs.SetString(versionKey, _resourceModule.PackageVersion);
                    }
                }

                Log.Info($"资源包版本获取完成：{runtimePackage.PackageName} => {packageVersion}");

                var manifestOperation = _resourceModule.UpdatePackageManifestAsync(packageVersion, customPackageName: runtimePackage.PackageName);
                yield return manifestOperation;
                if (manifestOperation.Status != EOperationStatus.Succeed)
                {
                    OnInitResourcesError(procedureOwner, runtimePackage.PackageName, manifestOperation.Error);
                    yield break;
                }

                Log.Info($"资源包清单更新完成：{runtimePackage.PackageName} => {packageVersion}");
            }

            _initResourcesComplete = true;
        }

        private void ChangeToPreloadState(ProcedureOwner procedureOwner)
        {
            ChangeState<ProcedurePreload>(procedureOwner);
        }

        private void OnInitResourcesError(ProcedureOwner procedureOwner, string packageName, string message)
        {
            Log.Error(message);
            LauncherMgr.ShowMessageBox($"初始化资源失败！点击确认重试 \n包名：{packageName}\n <color=#FF0000>{message}</color>",
                () => { RetryInitResources(procedureOwner); }, Application.Quit);
        }

        private void RetryInitResources(ProcedureOwner procedureOwner)
        {
            _initResourcesComplete = false;
            _usedLocalPackageVersion = false;
            _handledLocalPackageVersionNotice = false;
            Utility.Unity.StartCoroutine(InitResources(procedureOwner));
        }

        private bool TryGetLocalPackageVersion(RuntimePackageEntry runtimePackage, out string packageVersion)
        {
            packageVersion = string.Empty;
            if (_resourceModule.PlayMode != EPlayMode.HostPlayMode)
            {
                return false;
            }

            if (Settings.UpdateSetting.UpdateStyle != UpdateStyle.Optional || _resourceModule.UpdatableWhilePlaying)
            {
                return false;
            }

            if (runtimePackage == null || !runtimePackage.SaveVersion)
            {
                return false;
            }

            var versionKey = GetVersionPlayerPrefsKey(runtimePackage);
            if (string.IsNullOrEmpty(versionKey))
            {
                return false;
            }

            packageVersion = Utility.PlayerPrefs.GetString(versionKey, string.Empty);
            return !string.IsNullOrEmpty(packageVersion);
        }

        private void HandleLocalPackageVersionFallback(ProcedureOwner procedureOwner)
        {
            if (_handledLocalPackageVersionNotice)
            {
                return;
            }

            _handledLocalPackageVersionNotice = true;
            Log.Warning("远程版本获取失败，已使用本地版本记录完成资源初始化。\n");
            if (Settings.UpdateSetting.UpdateNotice == UpdateNotice.Notice)
            {
                LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_Notice);
                LauncherMgr.ShowMessageBox("更新失败，已使用本地版本继续初始化资源！\n\n确定再试一次，取消继续进入游戏",
                    () => { RetryInitResources(procedureOwner); },
                    () => { ChangeState<ProcedurePreload>(procedureOwner); });
                return;
            }

            ChangeToPreloadState(procedureOwner);
        }
    }
}
