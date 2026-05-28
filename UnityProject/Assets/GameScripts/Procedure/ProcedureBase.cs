using System.Collections.Generic;
using TEngine;
using YooAsset;

namespace Procedure
{
    public abstract class ProcedureBase : TEngine.ProcedureBase
    {
        protected const string MainPackageVersionKey = "Procedure.MainPackageVersion";
        protected const string CodePackageVersionKey = "Procedure.CodePackageVersion";
        protected const string DownloadPackageNamesKey = "Procedure.DownloadPackageNames";
        protected const string CurrentDownloadPackageKey = "Procedure.CurrentDownloadPackage";
        protected const string CodeVersionPlayerPrefsKey = "CODE_VERSION";
        protected const string GameVersionPlayerPrefsKey = "GAME_VERSION";

        /// <summary>
        /// 获取流程是否使用原生对话框
        /// 在一些特殊的流程（如游戏逻辑对话框资源更新完成前的流程）中，可以考虑调用原生对话框进行消息提示行为
        /// </summary>
        public abstract bool UseNativeDialog { get; }

        protected readonly IResourceModule _resourceModule = ModuleSystem.GetModule<IResourceModule>();

        protected static string GetCodePackageName()
            => string.IsNullOrWhiteSpace(Settings.UpdateSetting.AssemblyPackageName)
                ? "CodePackage"
                : Settings.UpdateSetting.AssemblyPackageName;

        protected static List<string> GetRuntimePackageNames()
        {
            var packageNames = new List<string> { "DefaultPackage" };
            var codePackageName = GetCodePackageName();
            if (!packageNames.Contains(codePackageName))
            {
                packageNames.Add(codePackageName);
            }

            return packageNames;
        }

        protected static string GetVersionPlayerPrefsKey(string packageName)
            => packageName == "DefaultPackage" ? GameVersionPlayerPrefsKey : CodeVersionPlayerPrefsKey;

        protected static bool IsCodePackage(string packageName)
            => packageName == GetCodePackageName();

        protected static string GetPackageVersionKey(string packageName)
            => packageName == "DefaultPackage" ? MainPackageVersionKey : CodePackageVersionKey;
    }
}
