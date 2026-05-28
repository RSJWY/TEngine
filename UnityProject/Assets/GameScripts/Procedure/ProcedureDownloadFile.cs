using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Launcher;
using TEngine;
using UnityEngine;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;
using Utility = TEngine.Utility;

namespace Procedure
{
    public class ProcedureDownloadFile : ProcedureBase
    {
        public override bool UseNativeDialog { get; }

        private ProcedureOwner _procedureOwner;
        private float _lastUpdateDownloadedSize;
        private float _totalSpeed;
        private int _speedSampleCount;

        private float CurrentSpeed(ResourceDownloaderOperation downloader)
        {
            float interval = Math.Max(Time.deltaTime, 0.01f);
            var sizeDiff = downloader.CurrentDownloadBytes - _lastUpdateDownloadedSize;
            _lastUpdateDownloadedSize = downloader.CurrentDownloadBytes;
            var speed = sizeDiff / interval;
            _totalSpeed += speed;
            _speedSampleCount++;
            return _totalSpeed / _speedSampleCount;
        }

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            _procedureOwner = procedureOwner;
            Log.Info("开始下载更新文件！");
            BeginDownload().Forget();
        }

        private async UniTaskVoid BeginDownload()
        {
            var downloadPackageNames = _procedureOwner.HasData(DownloadPackageNamesKey)
                ? _procedureOwner.GetData<List<string>>(DownloadPackageNamesKey)
                : null;
            if (downloadPackageNames == null || downloadPackageNames.Count == 0)
            {
                ChangeState<ProcedureDownloadOver>(_procedureOwner);
                return;
            }

            while (downloadPackageNames.Count > 0)
            {
                _lastUpdateDownloadedSize = 0f;
                _totalSpeed = 0f;
                _speedSampleCount = 0;

                var packageName = downloadPackageNames[0];
                _procedureOwner.SetData(CurrentDownloadPackageKey, packageName);
                var downloader = _resourceModule.CreateResourceDownloader(packageName);

                LauncherMgr.ShowUI<LoadUpdateUI>($"开始下载更新文件...({packageName})");

                downloader.DownloadErrorCallback = OnDownloadErrorCallback;
                downloader.DownloadUpdateCallback = OnDownloadProgressCallback;
                downloader.BeginDownload();
                await downloader;

                if (downloader.Status != EOperationStatus.Succeed)
                {
                    return;
                }

                downloadPackageNames.RemoveAt(0);
                _procedureOwner.SetData(DownloadPackageNamesKey, downloadPackageNames);
            }

            ChangeState<ProcedureDownloadOver>(_procedureOwner);
        }

        private void OnDownloadErrorCallback(DownloadErrorData downloadErrorData)
        {
            var packageName = _procedureOwner.HasData(CurrentDownloadPackageKey)
                ? _procedureOwner.GetData<string>(CurrentDownloadPackageKey)
                : "UnknownPackage";
            LauncherMgr.ShowMessageBox($"Failed to download file : {downloadErrorData.FileName}\nPackage: {packageName}",
                () => { ChangeState<ProcedureCreateDownloader>(_procedureOwner); }, Application.Quit);
        }

        private void OnDownloadProgressCallback(DownloadUpdateData downloadUpdateData)
        {
            var packageName = _procedureOwner.HasData(CurrentDownloadPackageKey)
                ? _procedureOwner.GetData<string>(CurrentDownloadPackageKey)
                : "UnknownPackage";
            var downloader = _resourceModule.Downloader;
            string currentSizeMb = (downloadUpdateData.CurrentDownloadBytes / 1048576f).ToString("f1");
            string totalSizeMb = (downloadUpdateData.TotalDownloadBytes / 1048576f).ToString("f1");
            float progressPercentage = downloader.Progress * 100;
            float speedValue = CurrentSpeed(downloader);
            string speed = Utility.File.GetLengthString((int)speedValue);

            string line0 = Utility.Text.Format("当前资源包 {0}", packageName);
            string line1 = Utility.Text.Format("正在更新，已更新 {0}/{1} ({2:F2}%)", downloadUpdateData.CurrentDownloadCount,
                downloadUpdateData.TotalDownloadCount, progressPercentage);
            string line2 = Utility.Text.Format("已更新大小 {0}MB/{1}MB", currentSizeMb, totalSizeMb);
            string line3 = Utility.Text.Format("当前网速 {0}/s，剩余时间 {1}", speed,
                GetRemainingTime(downloadUpdateData.TotalDownloadBytes, downloadUpdateData.CurrentDownloadBytes, speedValue));

            LauncherMgr.RefreshProgress(downloader.Progress);
            LauncherMgr.ShowUI<LoadUpdateUI>($"{line0}\n{line1}\n{line2}\n{line3}");
            Log.Info($"{line0} {line1} {line2} {line3}");
        }

        private string GetRemainingTime(long totalBytes, long currentBytes, float speed)
        {
            int needTime = 0;
            if (speed > 0)
            {
                needTime = (int)((totalBytes - currentBytes) / speed);
            }

            TimeSpan ts = new TimeSpan(0, 0, needTime);
            return ts.ToString(@"mm\:ss");
        }
    }
}
