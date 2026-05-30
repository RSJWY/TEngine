using Launcher;
using TEngine;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    public class ProcedureDownloadOver : ProcedureBase
    {
        public override bool UseNativeDialog { get; }

        private bool _needClearCache;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            Log.Info("下载完成!!!");
            LauncherMgr.ShowUI<LoadUpdateUI>("下载完成...");

            var skipDownloadVersionSave = procedureOwner.HasData(SkipDownloadVersionSaveKey) && procedureOwner.GetData<bool>(SkipDownloadVersionSaveKey);
            if (skipDownloadVersionSave)
            {
                procedureOwner.RemoveData(SkipDownloadVersionSaveKey);
            }

            foreach (var runtimePackage in GetRuntimePackages())
            {
                if (!runtimePackage.SaveVersion || skipDownloadVersionSave)
                {
                    continue;
                }

                var versionDataKey = GetPackageVersionDataKey(runtimePackage.PackageName);
                if (!procedureOwner.HasData(versionDataKey))
                {
                    continue;
                }

                var packageVersion = procedureOwner.GetData<string>(versionDataKey);
                Utility.PlayerPrefs.SetString(GetVersionPlayerPrefsKey(runtimePackage), packageVersion);
                if (runtimePackage.PackageName == _resourceModule.DefaultPackageName)
                {
                    _resourceModule.PackageVersion = packageVersion;
                }

                Log.Info($"写入资源包版本记录：{runtimePackage.PackageName} => {packageVersion}");
            }

            procedureOwner.RemoveData(DownloadPackageNamesKey);
            procedureOwner.RemoveData(CurrentDownloadPackageKey);
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            if (_needClearCache)
            {
                ChangeState<ProcedureClearCache>(procedureOwner);
            }
            else
            {
                ChangeState<ProcedurePreload>(procedureOwner);
            }
        }
    }
}
