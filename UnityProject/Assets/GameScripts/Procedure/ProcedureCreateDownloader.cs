using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Launcher;
using TEngine;
using UnityEngine;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    public class ProcedureCreateDownloader : ProcedureBase
    {
        private const float AutoStartDownloadDelaySeconds = 10f;

        public override bool UseNativeDialog { get; }

        private ProcedureOwner _procedureOwner;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            _procedureOwner = procedureOwner;

            Log.Info("创建补丁下载器");
            LauncherMgr.ShowUI<LoadUpdateUI>("创建补丁下载器...");
            CreateDownloader().Forget();
        }

        private async UniTaskVoid CreateDownloader()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f));

            var runtimePackages = GetRuntimePackages();
            Log.Info($"开始检查资源包下载需求，共 {runtimePackages.Count} 个包：{GetPackageNamesLogText(runtimePackages)}");

            var downloadPackageNames = new List<string>();
            var totalDownloadCount = 0;
            long totalDownloadBytes = 0;

            foreach (var runtimePackage in runtimePackages)
            {
                if (!runtimePackage.DownloadOnDemand)
                {
                    Log.Info($"跳过资源包下载检查：{runtimePackage.PackageName}");
                    continue;
                }

                var downloader = _resourceModule.CreateResourceDownloader(runtimePackage.PackageName);
                Log.Info($"资源包下载检查：{runtimePackage.PackageName} => count:{downloader.TotalDownloadCount}, bytes:{downloader.TotalDownloadBytes}");
                if (downloader.TotalDownloadCount <= 0)
                {
                    continue;
                }

                downloadPackageNames.Add(runtimePackage.PackageName);
                totalDownloadCount += downloader.TotalDownloadCount;
                totalDownloadBytes += downloader.TotalDownloadBytes;
            }

            _procedureOwner.SetData(DownloadPackageNamesKey, downloadPackageNames);
            _procedureOwner.RemoveData(CurrentDownloadPackageKey);
            _procedureOwner.RemoveData(SkipDownloadVersionSaveKey);

            if (downloadPackageNames.Count == 0)
            {
                Log.Info("Not found any download files !");
                ChangeState<ProcedureDownloadOver>(_procedureOwner);
                return;
            }

            Log.Info($"待下载资源包：{string.Join(", ", downloadPackageNames)}");

            float sizeMb = totalDownloadBytes / 1048576f;
            sizeMb = Mathf.Clamp(sizeMb, 0.1f, float.MaxValue);
            string totalSizeMb = sizeMb.ToString("f1");

            ShowDownloadConfirm(downloadPackageNames, totalDownloadCount, totalSizeMb);
        }

        private void ShowDownloadConfirm(List<string> downloadPackageNames, int totalDownloadCount, string totalSizeMb)
        {
            var message = $"Found update patch files, Total count {totalDownloadCount} Total size {totalSizeMb}MB";
            if (!CanSkipDownload(downloadPackageNames))
            {
                LauncherMgr.ShowMessageBox($@"{message}

没有找到本地版本记录，需要更新资源！", StartDownFile);
                return;
            }

            if (Settings.UpdateSetting.UpdateNotice != UpdateNotice.Notice)
            {
                SkipDownload(downloadPackageNames).Forget();
                return;
            }

            LauncherMgr.ShowMessageBox($@"{message}

检测到可选资源更新，推荐完成更新提升游戏体验。
确认开始更新，取消进入游戏。",
                StartDownFile, () => { SkipDownload(downloadPackageNames).Forget(); }, autoConfirmDelay: AutoStartDownloadDelaySeconds);
        }

        private bool CanSkipDownload(List<string> downloadPackageNames)
        {
            if (Settings.UpdateSetting.UpdateStyle != UpdateStyle.Optional || _resourceModule.UpdatableWhilePlaying)
            {
                return false;
            }

            foreach (var packageName in downloadPackageNames)
            {
                var runtimePackage = GetRuntimePackage(packageName);
                if (runtimePackage == null || !runtimePackage.SaveVersion)
                {
                    return false;
                }

                var versionKey = GetVersionPlayerPrefsKey(runtimePackage);
                if (string.IsNullOrEmpty(versionKey) || !Utility.PlayerPrefs.HasKey(versionKey))
                {
                    return false;
                }

                var localVersion = Utility.PlayerPrefs.GetString(versionKey, string.Empty);
                var remoteVersionKey = GetPackageVersionDataKey(packageName);
                var remoteVersion = _procedureOwner.HasData(remoteVersionKey)
                    ? _procedureOwner.GetData<string>(remoteVersionKey)
                    : string.Empty;
                if (string.IsNullOrEmpty(localVersion) || string.IsNullOrEmpty(remoteVersion) || string.Equals(localVersion, remoteVersion, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private async UniTaskVoid SkipDownload(List<string> downloadPackageNames)
        {
            _procedureOwner.SetData(SkipDownloadVersionSaveKey, true);
            foreach (var packageName in downloadPackageNames)
            {
                var runtimePackage = GetRuntimePackage(packageName);
                var localVersion = Utility.PlayerPrefs.GetString(GetVersionPlayerPrefsKey(runtimePackage), string.Empty);
                LauncherMgr.ShowUI<LoadUpdateUI>($"回退本地资源清单...({packageName})");
                var manifestOperation = _resourceModule.UpdatePackageManifestAsync(localVersion, customPackageName: packageName);
                await manifestOperation;
                if (manifestOperation.Status != EOperationStatus.Succeed)
                {
                    LauncherMgr.ShowMessageBox($@"回退本地资源清单失败！点击确认重试
包名：{packageName}
 <color=#FF0000>{manifestOperation.Error}</color>",
                        () => { SkipDownload(downloadPackageNames).Forget(); }, Application.Quit);
                    return;
                }
            }

            ChangeState<ProcedureDownloadOver>(_procedureOwner);
        }

        private void StartDownFile()
        {
            ChangeState<ProcedureDownloadFile>(_procedureOwner);
        }
    }
}
