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

            var downloadPackageNames = new List<string>();
            var totalDownloadCount = 0;
            long totalDownloadBytes = 0;

            foreach (var packageName in GetRuntimePackageNames())
            {
                var downloader = _resourceModule.CreateResourceDownloader(packageName);
                if (downloader.TotalDownloadCount <= 0)
                {
                    continue;
                }

                downloadPackageNames.Add(packageName);
                totalDownloadCount += downloader.TotalDownloadCount;
                totalDownloadBytes += downloader.TotalDownloadBytes;
            }

            _procedureOwner.SetData(DownloadPackageNamesKey, downloadPackageNames);
            _procedureOwner.RemoveData(CurrentDownloadPackageKey);

            if (downloadPackageNames.Count == 0)
            {
                Log.Info("Not found any download files !");
                ChangeState<ProcedureDownloadOver>(_procedureOwner);
                return;
            }

            float sizeMb = totalDownloadBytes / 1048576f;
            sizeMb = Mathf.Clamp(sizeMb, 0.1f, float.MaxValue);
            string totalSizeMb = sizeMb.ToString("f1");

            LauncherMgr.ShowMessageBox($"Found update patch files, Total count {totalDownloadCount} Total size {totalSizeMb}MB",
                StartDownFile, Application.Quit);
        }

        private void StartDownFile()
        {
            ChangeState<ProcedureDownloadFile>(_procedureOwner);
        }
    }
}
