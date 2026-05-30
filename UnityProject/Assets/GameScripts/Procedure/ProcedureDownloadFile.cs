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
        private static readonly int[] RetryDelaysSeconds = { 2, 5, 10 };

        public override bool UseNativeDialog { get; }

        private ProcedureOwner _procedureOwner;
        private float _lastUpdateDownloadedSize;
        private float _totalSpeed;
        private int _speedSampleCount;
        private bool _downloadFailedHandled;

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
            _downloadFailedHandled = false;
            Log.Info("开始下载更新文件！");
            BeginDownload().Forget();
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            _downloadFailedHandled = false;
            base.OnLeave(procedureOwner, isShutdown);
        }

        private async UniTaskVoid RetryCurrentDownloadWithDelay(DownloadErrorData downloadErrorData)
        {
            if (_downloadFailedHandled)
            {
                return;
            }

            int downloadRetryCount = _procedureOwner.HasData(DownloadRetryCountKey)
                ? _procedureOwner.GetData<int>(DownloadRetryCountKey)
                : 0;
            if (downloadRetryCount >= RetryDelaysSeconds.Length)
            {
                _downloadFailedHandled = true;
                ShowDownloadFailedDialog(downloadErrorData, downloadRetryCount);
                return;
            }

            int retryAttempt = downloadRetryCount + 1;
            int delaySeconds = RetryDelaysSeconds[downloadRetryCount];
            _procedureOwner.SetData(DownloadRetryCountKey, retryAttempt);

            string packageName = string.IsNullOrEmpty(downloadErrorData.PackageName)
                ? (_procedureOwner.HasData(CurrentDownloadPackageKey)
                    ? _procedureOwner.GetData<string>(CurrentDownloadPackageKey)
                    : "UnknownPackage")
                : downloadErrorData.PackageName;
            string errorInfo = string.IsNullOrEmpty(downloadErrorData.ErrorInfo) ? "Unknown Error" : downloadErrorData.ErrorInfo;

            Log.Warning($"下载失败，{delaySeconds}秒后开始第{retryAttempt}次重试。包名：{packageName}，文件：{downloadErrorData.FileName}，原因：{errorInfo}");
            LauncherMgr.ShowUI<LoadUpdateUI>($"下载失败，{delaySeconds} 秒后自动重试...\n当前资源包 {packageName}\n失败文件 {downloadErrorData.FileName}\n原因：{errorInfo}\n正在进行第 {retryAttempt}/{RetryDelaysSeconds.Length} 次重试准备");
            await UniTask.Delay(TimeSpan.FromSeconds(delaySeconds));

            if (_downloadFailedHandled)
            {
                return;
            }

            ChangeState<ProcedureCreateDownloader>(_procedureOwner);
        }

        private void ShowDownloadFailedDialog(DownloadErrorData downloadErrorData, int downloadRetryCount)
        {
            string packageName = string.IsNullOrEmpty(downloadErrorData.PackageName)
                ? (_procedureOwner.HasData(CurrentDownloadPackageKey)
                    ? _procedureOwner.GetData<string>(CurrentDownloadPackageKey)
                    : "UnknownPackage")
                : downloadErrorData.PackageName;
            string errorInfo = string.IsNullOrEmpty(downloadErrorData.ErrorInfo) ? "Unknown Error" : downloadErrorData.ErrorInfo;

            LauncherMgr.ShowMessageBox($"下载文件失败：{downloadErrorData.FileName}\n资源包：{packageName}\n原因：{errorInfo}\n\n已自动重试 {downloadRetryCount} 次，点击确定重新检查更新，点击取消退出应用",
                () =>
                {
                    _procedureOwner.RemoveData(DownloadRetryCountKey);
                    ChangeState<ProcedureCreateDownloader>(_procedureOwner);
                }, Application.Quit);
        }

        private async UniTaskVoid BeginDownload()
        {
            var downloadPackageNames = _procedureOwner.HasData(DownloadPackageNamesKey)
                ? _procedureOwner.GetData<List<string>>(DownloadPackageNamesKey)
                : null;
            if (downloadPackageNames == null || downloadPackageNames.Count == 0)
            {
                _procedureOwner.RemoveData(DownloadRetryCountKey);
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

                _procedureOwner.RemoveData(DownloadRetryCountKey);
                downloadPackageNames.RemoveAt(0);
                _procedureOwner.SetData(DownloadPackageNamesKey, downloadPackageNames);
            }

            _procedureOwner.RemoveData(DownloadRetryCountKey);
            ChangeState<ProcedureDownloadOver>(_procedureOwner);
        }

        private void OnDownloadErrorCallback(DownloadErrorData downloadErrorData)
        {
            RetryCurrentDownloadWithDelay(downloadErrorData).Forget();
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
