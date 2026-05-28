using System;
using Cysharp.Threading.Tasks;
using Launcher;
using TEngine;
using UnityEngine;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    /// <summary>
    /// 流程 => 初始化Package。
    /// </summary>
    public class ProcedureInitPackage : ProcedureBase
    {
        public override bool UseNativeDialog { get; }

        private ProcedureOwner _procedureOwner;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);

            _procedureOwner = procedureOwner;
            procedureOwner.RemoveData(DownloadPackageNamesKey);
            procedureOwner.RemoveData(CurrentDownloadPackageKey);

            foreach (var runtimePackage in GetRuntimePackages())
            {
                procedureOwner.RemoveData(GetPackageVersionDataKey(runtimePackage.PackageName));
            }

            InitPackage(procedureOwner).Forget();
        }

        private async UniTaskVoid InitPackage(ProcedureOwner procedureOwner)
        {
            var runtimePackages = GetRuntimePackages();
            Log.Info($"开始初始化资源包，共 {runtimePackages.Count} 个：{GetPackageNamesLogText(runtimePackages)}");

            try
            {
                foreach (var runtimePackage in runtimePackages)
                {
                    if (!runtimePackage.InitOnStartup)
                    {
                        Log.Info($"跳过初始化资源包：{runtimePackage.PackageName}");
                        continue;
                    }

                    Log.Info($"初始化资源包：{runtimePackage.PackageName}");
                    var initializationOperation = await _resourceModule.InitPackage(runtimePackage.PackageName);
                    if (initializationOperation == null || initializationOperation.Status != EOperationStatus.Succeed)
                    {
                        var error = initializationOperation == null ? $"{runtimePackage.PackageName} 初始化返回空结果" : initializationOperation.Error;
                        OnInitPackageFailed(procedureOwner, runtimePackage.PackageName, error);
                        return;
                    }

                    Log.Info($"资源包初始化完成：{runtimePackage.PackageName}");
                }

                LoadText.Instance.InitConfigData(null);

                EPlayMode playMode = _resourceModule.PlayMode;
                if (playMode == EPlayMode.EditorSimulateMode)
                {
                    Log.Info("Editor resource mode detected.");
                    ChangeState<ProcedureInitResources>(procedureOwner);
                }
                else if (playMode == EPlayMode.OfflinePlayMode)
                {
                    Log.Info("Package resource mode detected.");
                    ChangeState<ProcedureInitResources>(procedureOwner);
                }
                else if (playMode == EPlayMode.HostPlayMode || playMode == EPlayMode.WebPlayMode)
                {
                    LauncherMgr.ShowUI<LoadUpdateUI>();
                    Log.Info("Updatable resource mode detected.");
                    ChangeState<ProcedureInitResources>(procedureOwner);
                }
                else
                {
                    Log.Error("UnKnow resource mode detected Please check???");
                }
            }
            catch (Exception e)
            {
                OnInitPackageFailed(procedureOwner, _resourceModule.DefaultPackageName, e.Message);
            }
        }

        private void OnInitPackageFailed(ProcedureOwner procedureOwner, string packageName, string message)
        {
            LauncherMgr.ShowUI<LoadUpdateUI>();

            Log.Error($"{packageName} init failed: {message}");
            LauncherMgr.ShowUI<LoadUpdateUI>("资源初始化失败！");

            if (message.Contains($"PackageManifest_{packageName}.version Error : HTTP/1.1 404 Not Found"))
            {
                message = $"请检查StreamingAssets/package/{packageName}/PackageManifest_{packageName}.version是否存在";
            }

            LauncherMgr.ShowMessageBox($"资源初始化失败！点击确认重试 \n \n包名：{packageName}\n<color=#FF0000>原因{message}</color>",
                () => { Retry(procedureOwner); },
                Application.Quit);
        }

        private void Retry(ProcedureOwner procedureOwner)
        {
            LauncherMgr.ShowUI<LoadUpdateUI>("重新初始化资源中...");
            InitPackage(procedureOwner).Forget();
        }
    }
}
