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
        private const float AutoStartDownloadDelaySeconds = 5f;

        public override bool UseNativeDialog { get; }

        private ProcedureOwner _procedureOwner;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            _procedureOwner = procedureOwner;

            Log.Info("检查资源包完整性");
            LauncherMgr.ShowUI<LoadUpdateUI>("检查资源包完整性...");
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
                Log.Info($"资源包完整性检查：{runtimePackage.PackageName} => count:{downloader.TotalDownloadCount}, bytes:{downloader.TotalDownloadBytes}");
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
                Log.Info("资源包完整性检查通过，无需下载文件。");
                ChangeState<ProcedureDownloadOver>(_procedureOwner);
                return;
            }

            Log.Info($"待下载资源包：{string.Join(", ", downloadPackageNames)}");

            float sizeMb = totalDownloadBytes / 1048576f;
            sizeMb = Mathf.Clamp(sizeMb, 0.1f, float.MaxValue);
            string totalSizeMb = sizeMb.ToString("f1");

            ShowDownloadConfirm(totalDownloadCount, totalSizeMb);
        }

        private void ShowDownloadConfirm(int totalDownloadCount, string totalSizeMb)
        {
            var confirmedVersionUpdate = _procedureOwner.HasData(ConfirmedVersionUpdateKey) &&
                                         _procedureOwner.GetData<bool>(ConfirmedVersionUpdateKey);
            if (confirmedVersionUpdate)
            {
                StartDownFile();
                return;
            }

            LauncherMgr.ShowMessageBox($"资源包不完整，需要下载缺失文件。\n\n文件数量：{totalDownloadCount}\n下载大小：{totalSizeMb}MB\n\n点击确定开始下载。",
                StartDownFile,
                autoConfirmDelay: AutoStartDownloadDelaySeconds);
        }

        private void StartDownFile()
        {
            ChangeState<ProcedureDownloadFile>(_procedureOwner);
        }
    }
}
