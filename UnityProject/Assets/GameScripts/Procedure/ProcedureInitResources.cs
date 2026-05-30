using System;
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
        private const float VersionConfirmAutoDelaySeconds = 5f;
        private const float LocalVersionFallbackAutoContinueDelaySeconds = 5f;

        private bool _initResourcesComplete;
        private bool _usedLocalPackageVersion;
        private bool _handledLocalPackageVersionNotice;
        private bool _needDownloadCheck;

        public override bool UseNativeDialog => true;

        private ProcedureOwner _procedureOwner;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            _procedureOwner = procedureOwner;

            base.OnEnter(procedureOwner);

            _initResourcesComplete = false;
            _usedLocalPackageVersion = false;
            _handledLocalPackageVersionNotice = false;
            _needDownloadCheck = false;
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

            if (_needDownloadCheck)
            {
                ChangeToCreateDownloaderState(procedureOwner);
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

                var localVersion = GetLocalPackageVersion(runtimePackage);
                var versionOperation = _resourceModule.RequestPackageVersionAsync(customPackageName: runtimePackage.PackageName);
                yield return versionOperation;
                if (versionOperation.Status != EOperationStatus.Succeed)
                {
                    if (!string.IsNullOrEmpty(localVersion))
                    {
                        Log.Warning($"请求资源包版本失败，回退使用本地版本记录：{runtimePackage.PackageName}，本地版本：{localVersion}，错误：{versionOperation.Error}");
                        var localManifestOperation = _resourceModule.UpdatePackageManifestAsync(localVersion,
                            customPackageName: runtimePackage.PackageName);
                        yield return localManifestOperation;
                        if (localManifestOperation.Status != EOperationStatus.Succeed)
                        {
                            OnInitResourcesError(procedureOwner, runtimePackage.PackageName,
                                $"本地版本清单恢复失败：{localManifestOperation.Error}");
                            yield break;
                        }

                        SavePackageVersionData(procedureOwner, runtimePackage, localVersion);
                        _usedLocalPackageVersion = true;
                        _needDownloadCheck = true;
                        Log.Warning($"资源包已回退到本地版本：{runtimePackage.PackageName} => {localVersion}");
                        continue;
                    }

                    OnInitResourcesError(procedureOwner, runtimePackage.PackageName, versionOperation.Error);
                    yield break;
                }

                var remoteVersion = versionOperation.PackageVersion;
                Log.Info($"资源包版本比对：{runtimePackage.PackageName}，上次版本：{GetVersionLogText(localVersion)}，本次版本：{GetVersionLogText(remoteVersion)}");

                var selectedVersion = remoteVersion;
                var versionChanged = !string.Equals(localVersion, remoteVersion, StringComparison.Ordinal);
                if (versionChanged)
                {
                    bool useRemoteVersion = true;
                    yield return ConfirmPackageVersion(runtimePackage, localVersion, remoteVersion, value => { useRemoteVersion = value; });
                    selectedVersion = useRemoteVersion ? remoteVersion : localVersion;
                    _needDownloadCheck = true;

                    if (useRemoteVersion)
                    {
                        procedureOwner.SetData(ConfirmedVersionUpdateKey, true);
                    }

                    Log.Info($"资源包版本选择：{runtimePackage.PackageName} => {(useRemoteVersion ? "使用远端版本" : "继续使用本地版本")} {selectedVersion}");
                }

                if (string.IsNullOrEmpty(selectedVersion))
                {
                    OnInitResourcesError(procedureOwner, runtimePackage.PackageName, "资源包版本为空，无法更新资源清单。");
                    yield break;
                }

                var manifestOperation = _resourceModule.UpdatePackageManifestAsync(selectedVersion, customPackageName: runtimePackage.PackageName);
                yield return manifestOperation;
                if (manifestOperation.Status != EOperationStatus.Succeed)
                {
                    OnInitResourcesError(procedureOwner, runtimePackage.PackageName, manifestOperation.Error);
                    yield break;
                }

                SavePackageVersionData(procedureOwner, runtimePackage, selectedVersion);
                Log.Info($"资源包清单更新完成：{runtimePackage.PackageName} => {selectedVersion}");
            }

            _initResourcesComplete = true;
        }


        private IEnumerator ConfirmPackageVersion(RuntimePackageEntry runtimePackage, string localVersion, string remoteVersion, Action<bool> onConfirm)
        {
            bool handled = false;
            bool useRemoteVersion = true;
            bool firstVersion = string.IsNullOrEmpty(localVersion);
            bool forceUpdate = Settings.UpdateSetting.UpdateStyle != UpdateStyle.Optional;
            string message = firstVersion
                ? $"首次获取资源包版本，需要更新资源。\n\n包名：{runtimePackage.PackageName}\n上次版本：无本地记录\n本次版本：{remoteVersion}\n\n点击确定开始检查并下载资源。"
                : $"检测到资源包版本更新。\n\n包名：{runtimePackage.PackageName}\n上次版本：{localVersion}\n本次版本：{remoteVersion}\n\n{(forceUpdate ? "本次为强制更新，点击确定开始检查并下载资源。" : "点击确定更新到新版本，点击取消继续使用本地版本。")}";

            if (firstVersion || forceUpdate)
            {
                LauncherMgr.ShowMessageBox(message,
                    () =>
                    {
                        useRemoteVersion = true;
                        handled = true;
                    },
                    autoConfirmDelay: VersionConfirmAutoDelaySeconds);
            }
            else
            {
                LauncherMgr.ShowMessageBox(message,
                    () =>
                    {
                        useRemoteVersion = true;
                        handled = true;
                    },
                    () =>
                    {
                        useRemoteVersion = false;
                        handled = true;
                    },
                    autoConfirmDelay: VersionConfirmAutoDelaySeconds);
            }

            yield return new WaitUntil(() => handled);
            onConfirm(useRemoteVersion);
        }

        private string GetLocalPackageVersion(RuntimePackageEntry runtimePackage)
        {
            if (runtimePackage == null || !runtimePackage.SaveVersion)
            {
                return string.Empty;
            }

            var versionKey = GetVersionPlayerPrefsKey(runtimePackage);
            return string.IsNullOrEmpty(versionKey) ? string.Empty : Utility.PlayerPrefs.GetString(versionKey, string.Empty);
        }

        private static string GetVersionLogText(string version)
        {
            return string.IsNullOrEmpty(version) ? "无本地记录" : version;
        }

        private void SavePackageVersionData(ProcedureOwner procedureOwner, RuntimePackageEntry runtimePackage, string packageVersion)
        {
            procedureOwner.SetData(GetPackageVersionDataKey(runtimePackage.PackageName), packageVersion);
            if (runtimePackage.PackageName == _resourceModule.DefaultPackageName)
            {
                _resourceModule.PackageVersion = packageVersion;
            }
        }

        private void ChangeToPreloadState(ProcedureOwner procedureOwner)
        {
            ChangeState<ProcedurePreload>(procedureOwner);
        }

        private void OnInitResourcesError(ProcedureOwner procedureOwner, string packageName, string message)
        {
            Log.Error(message);
            LauncherMgr.ShowMessageBox($"初始化资源失败！点击确定重试\n包名：{packageName}\n<color=#FF0000>{message}</color>",
                () => { RetryInitResources(procedureOwner); }, Application.Quit);
        }

        private void RetryInitResources(ProcedureOwner procedureOwner)
        {
            _initResourcesComplete = false;
            _usedLocalPackageVersion = false;
            _handledLocalPackageVersionNotice = false;
            _needDownloadCheck = false;
            procedureOwner.RemoveData(ConfirmedVersionUpdateKey);
            Utility.Unity.StartCoroutine(InitResources(procedureOwner));
        }

        private void HandleLocalPackageVersionFallback(ProcedureOwner procedureOwner)
        {
            if (_handledLocalPackageVersionNotice)
            {
                return;
            }

            _handledLocalPackageVersionNotice = true;
            Log.Warning("远程版本获取失败，已使用本地版本记录，接下来检查本地资源完整性。");
            if (Settings.UpdateSetting.UpdateNotice == UpdateNotice.Notice)
            {
                LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_Notice);
                LauncherMgr.ShowMessageBox("远程版本获取失败，已回退到本地资源清单。\n\n点击确定重新获取远程版本，点击取消检查本地资源完整性后继续进入游戏。",
                    () => { RetryInitResources(procedureOwner); },
                    () => { ChangeToCreateDownloaderState(procedureOwner); },
                    autoConfirmDelay: LocalVersionFallbackAutoContinueDelaySeconds,
                    autoConfirmUsesCancel: true);
                return;
            }

            ChangeToCreateDownloaderState(procedureOwner);
        }
    }
}
