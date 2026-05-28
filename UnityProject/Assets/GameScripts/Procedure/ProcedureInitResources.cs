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

        public override bool UseNativeDialog => true;

        private ProcedureOwner _procedureOwner;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            _procedureOwner = procedureOwner;

            base.OnEnter(procedureOwner);

            _initResourcesComplete = false;
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
            Log.Info("更新资源清单！！！");

            foreach (var packageName in GetRuntimePackageNames())
            {
                LauncherMgr.ShowUI<LoadUpdateUI>($"更新清单文件...({packageName})");

                var versionOperation = _resourceModule.RequestPackageVersionAsync(customPackageName: packageName);
                yield return versionOperation;
                if (versionOperation.Status != EOperationStatus.Succeed)
                {
                    OnInitResourcesError(procedureOwner, packageName, versionOperation.Error);
                    yield break;
                }

                var packageVersion = versionOperation.PackageVersion;
                procedureOwner.SetData(GetPackageVersionKey(packageName), packageVersion);
                if (packageName == _resourceModule.DefaultPackageName)
                {
                    _resourceModule.PackageVersion = packageVersion;
                    if (Utility.PlayerPrefs.HasKey(GameVersionPlayerPrefsKey))
                    {
                        Utility.PlayerPrefs.SetString(GameVersionPlayerPrefsKey, _resourceModule.PackageVersion);
                    }
                }

                Log.Info($"Init resource package version : {packageName} => {packageVersion}");

                var manifestOperation = _resourceModule.UpdatePackageManifestAsync(packageVersion, customPackageName: packageName);
                yield return manifestOperation;
                if (manifestOperation.Status != EOperationStatus.Succeed)
                {
                    OnInitResourcesError(procedureOwner, packageName, manifestOperation.Error);
                    yield break;
                }
            }

            _initResourcesComplete = true;
        }

        private void ChangeToPreloadState(ProcedureOwner procedureOwner)
        {
            ChangeState<ProcedurePreload>(procedureOwner);
        }

        private void OnInitResourcesError(ProcedureOwner procedureOwner, string packageName, string message)
        {
            if (_resourceModule.PlayMode == EPlayMode.HostPlayMode)
            {
                if (!IsNeedUpdate())
                {
                    return;
                }

                Log.Error(message);
                LauncherMgr.ShowMessageBox($"获取远程版本失败！点击确认重试\n包名：{packageName}\n <color=#FF0000>{message}</color>",
                    () => { Utility.Unity.StartCoroutine(InitResources(procedureOwner)); },
                    Application.Quit);
                return;
            }

            Log.Error(message);
            LauncherMgr.ShowMessageBox($"初始化资源失败！点击确认重试 \n包名：{packageName}\n <color=#FF0000>{message}</color>",
                () => { Utility.Unity.StartCoroutine(InitResources(procedureOwner)); }, Application.Quit);
        }

        private bool IsNeedUpdate()
        {
            if (Settings.UpdateSetting.UpdateStyle == UpdateStyle.Optional && !_resourceModule.UpdatableWhilePlaying)
            {
                string packageVersion = Utility.PlayerPrefs.GetString(GameVersionPlayerPrefsKey, string.Empty);
                if (string.IsNullOrEmpty(packageVersion))
                {
                    LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Net_UnReachable);
                    LauncherMgr.ShowMessageBox("没有找到本地版本记录，需要更新资源！",
                        () => { Utility.Unity.StartCoroutine(InitResources(_procedureOwner)); },
                        Application.Quit);
                    return false;
                }

                _resourceModule.PackageVersion = packageVersion;

                if (Settings.UpdateSetting.UpdateNotice == UpdateNotice.Notice)
                {
                    LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_Notice);
                    LauncherMgr.ShowMessageBox("更新失败，检测到可选资源更新，推荐完成更新提升游戏体验！ \\n \\n 确定再试一次，取消进入游戏",
                        () => { Utility.Unity.StartCoroutine(InitResources(_procedureOwner)); },
                        () => { ChangeState<ProcedurePreload>(_procedureOwner); });
                }
                else
                {
                    ChangeState<ProcedurePreload>(_procedureOwner);
                }

                return false;
            }

            return true;
        }
    }
}
