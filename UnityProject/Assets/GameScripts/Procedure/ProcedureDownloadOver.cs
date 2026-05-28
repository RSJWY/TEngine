using System.Collections.Generic;
using Launcher;
using TEngine;
using UnityEngine;
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

            if (procedureOwner.HasData(MainPackageVersionKey))
            {
                Utility.PlayerPrefs.SetString(GameVersionPlayerPrefsKey, procedureOwner.GetData<string>(MainPackageVersionKey));
                _resourceModule.PackageVersion = procedureOwner.GetData<string>(MainPackageVersionKey);
            }

            if (procedureOwner.HasData(CodePackageVersionKey))
            {
                Utility.PlayerPrefs.SetString(CodeVersionPlayerPrefsKey, procedureOwner.GetData<string>(CodePackageVersionKey));
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
