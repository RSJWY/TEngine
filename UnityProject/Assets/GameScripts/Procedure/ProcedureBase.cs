using System.Collections.Generic;
using TEngine;

namespace Procedure
{
    public abstract class ProcedureBase : TEngine.ProcedureBase
    {
        protected const string DownloadPackageNamesKey = "Procedure.DownloadPackageNames";
        protected const string CurrentDownloadPackageKey = "Procedure.CurrentDownloadPackage";
        protected const string DownloadRetryCountKey = "Procedure.DownloadRetryCount";
        protected const string SkipDownloadVersionSaveKey = "Procedure.SkipDownloadVersionSave";
        protected const string ConfirmedVersionUpdateKey = "Procedure.ConfirmedVersionUpdate";

        public abstract bool UseNativeDialog { get; }

        protected readonly IResourceModule _resourceModule = ModuleSystem.GetModule<IResourceModule>();

        protected static List<RuntimePackageEntry> GetRuntimePackages()
            => Settings.UpdateSetting.GetEnabledRuntimePackages();

        protected static RuntimePackageEntry GetRuntimePackage(string packageName)
            => Settings.UpdateSetting.GetRuntimePackage(packageName);

        protected static string GetCodePackageName()
            => Settings.UpdateSetting.GetAssemblyPackageName();

        protected static string GetPackageVersionDataKey(string packageName)
            => $"Procedure.PackageVersion.{packageName}";

        protected static string GetVersionPlayerPrefsKey(RuntimePackageEntry runtimePackage)
            => runtimePackage == null ? string.Empty : runtimePackage.VersionKey;

        protected static string GetPackageNamesLogText(List<RuntimePackageEntry> runtimePackages)
        {
            if (runtimePackages == null || runtimePackages.Count == 0)
            {
                return "None";
            }

            return string.Join(", ", runtimePackages.ConvertAll(runtimePackage => runtimePackage.PackageName));
        }
    }
}
