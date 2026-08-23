using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TEngine
{
    /// <summary>
    /// 打包工具窗口的持久化配置。
    /// <remarks>存放在 Assets/TEngine/Settings/ 下,随工程入库共享;首次打开打包窗口时自动创建。</remarks>
    /// </summary>
    [CreateAssetMenu(menuName = "TEngine/BuildPipelineSetting", fileName = "BuildPipelineSetting")]
    public sealed class BuildPipelineSetting : ScriptableObject
    {
        private const string DefaultAssetPath = "Assets/TEngine/Settings/BuildPipelineSetting.asset";

        /// <summary>
        /// 是否已从 EditorPrefs 迁移过旧配置(历史版本的打包设置存在 EditorPrefs 里)。
        /// </summary>
        public bool EditorPrefsImported;

        // 基础设置
        public BuildTarget BuildTarget;
        public EBuildPipeline BuildPipeline = EBuildPipeline.ScriptableBuildPipeline;
        public ECompressOption CompressOption = ECompressOption.LZ4;
        public string PackageVersion = "";
        public string OutputRoot = "./Releases/Bundles/";

        // 发布整理设置
        public bool EnablePublishCopy;
        public string PublishRoot = "./Releases/Publish/";
        public bool CleanPublishPackageDirectory = true;

        // 最小包设置
        public bool MinimalPackage;
        public string RetainTags = "";

        // 高级设置
        public bool EnableSharePackRule = true;
        public bool UseAssetDependencyDB = true;
        public bool ClearBuildCache;
        public bool VerifyBuildingResult = true;
        public EBuildinFileCopyOption BuildinFileCopyOption = EBuildinFileCopyOption.ClearAndCopyAll;
        public EFileNameStyle FileNameStyle = EFileNameStyle.BundleName_HashName;

        // 热更DLL设置
        public bool BuildHotFixDll = true;

        // 打包Player设置
        public bool BuildPlayer;
        public BuildTarget PlayerPlatform;
        public string PlayerOutputPath = "";

        // InnoSetup 安装包设置（仅 Windows）
        public bool BuildInstaller;
        public string InstallerVersion = "";
        public string IsccPath = "";

        public void ApplyDefaults()
        {
            var config = BuildConfig.CreateDefault();
            BuildTarget = config.BuildTarget;
            BuildPipeline = config.BuildPipeline;
            CompressOption = config.CompressOption;
            PackageVersion = config.PackageVersion;
            OutputRoot = config.OutputRoot;
            EnablePublishCopy = config.EnablePublishCopy;
            PublishRoot = config.PublishRoot;
            CleanPublishPackageDirectory = config.CleanPublishPackageDirectory;
            MinimalPackage = config.MinimalPackage;
            RetainTags = config.RetainTags;
            EnableSharePackRule = config.EnableSharePackRule;
            UseAssetDependencyDB = config.UseAssetDependencyDB;
            ClearBuildCache = config.ClearBuildCache;
            VerifyBuildingResult = config.VerifyBuildingResult;
            BuildinFileCopyOption = config.BuildinFileCopyOption;
            FileNameStyle = config.FileNameStyle;
            BuildHotFixDll = config.BuildHotFixDll;
            BuildPlayer = config.BuildPlayer;
            PlayerPlatform = config.PlayerPlatform;
            PlayerOutputPath = config.PlayerOutputPath;
            BuildInstaller = config.BuildInstaller;
            InstallerVersion = config.InstallerVersion;
            IsccPath = config.IsccPath;
        }

        /// <summary>
        /// 加载配置资产,不存在时在 Assets/TEngine/Settings/ 下创建。
        /// </summary>
        public static BuildPipelineSetting LoadOrCreate()
        {
            var setting = AssetDatabase.LoadAssetAtPath<BuildPipelineSetting>(DefaultAssetPath);
            if (setting == null)
            {
                var guids = AssetDatabase.FindAssets("t:BuildPipelineSetting");
                if (guids.Length > 0)
                {
                    setting = AssetDatabase.LoadAssetAtPath<BuildPipelineSetting>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            if (setting == null)
            {
                setting = CreateInstance<BuildPipelineSetting>();
                setting.ApplyDefaults();
                AssetDatabase.CreateAsset(setting, DefaultAssetPath);
                AssetDatabase.SaveAssets();
            }

            return setting;
        }
    }
}
