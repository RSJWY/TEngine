using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TEngine
{
    public class BuildPipelineWindow : OdinEditorWindow
    {
        private const string MenuPath = "Build/打包工具窗口";
        private const string AllBuildPackagesDisplayName = "全部资源包";
        private const string DefaultOutputRoot = "./Releases/Bundles/";
        private const string DefaultPublishRoot = "./Releases/Publish/";
        private const string LegacyOutputRoot = "./Builds/";
        private const string LegacyPublishRoot = "./Publish/";
        // 上一版默认值（Output/），统一迁移到 Releases/。
        private const string LegacyOutputRootV2 = "./Output/Bundles/";
        private const string LegacyPublishRootV2 = "./Output/Publish/";

        private static readonly BuildTarget[] PlatformTargets =
        {
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneOSX,
            BuildTarget.StandaloneLinux64,
            BuildTarget.Android,
            BuildTarget.iOS,
            BuildTarget.WebGL,
        };

        private bool _isLoadingSettings;
        private bool _isSavingRuntimePackages;
        private bool _runtimePackagesDirty;
        private bool _runtimePackageSaveQueued;
        private double _nextRuntimePackageSaveTime;
        private BuildPipelineSetting _setting;
        private bool _settingDirty;
        private bool _settingSaveQueued;
        private double _nextSettingSaveTime;
        private double _nextLogRepaintTime;
        private UpdateSetting _cachedUpdateSetting;
        private string _cachedPackageSummary = "DefaultPackage(ScriptableBuildPipeline)";
        private string _cachedToolbarStatus = string.Empty;
        private string _cachedPublishPackagePreviewText = "DefaultPackage";

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/基础设置")]
        [LabelText("目标平台")]
        [Tooltip("资源包与 Player 的统一目标平台（Player 不再单独配置）")]
        [ValueDropdown(nameof(BuildTargetOptions))]
        [OnValueChanged(nameof(OnBuildTargetChanged))]
        [SerializeField]
        private BuildTarget _buildTarget;

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/基础设置")]
        [LabelText("默认构建管线")]
        [ValueDropdown(nameof(BuildPipelineOptions))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private EBuildPipeline _buildPipeline = EBuildPipeline.ScriptableBuildPipeline;

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/基础设置")]
        [LabelText("压缩方式")]
        [ValueDropdown(nameof(CompressOptions))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private ECompressOption _compressOption = ECompressOption.LZ4;

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/基础设置")]
        [HorizontalGroup("Pages/快速构建/基础设置/VersionMode")]
        [LabelText("版本号模式")]
        [ValueDropdown(nameof(PackageVersionModeOptions))]
        [OnValueChanged(nameof(OnPackageVersionModeChanged))]
        [SerializeField]
        private PackageVersionMode _packageVersionMode = PackageVersionMode.Unified;

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/基础设置")]
        [HorizontalGroup("Pages/快速构建/基础设置/Version")]
        [LabelText("资源版本号")]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [ShowIf(nameof(IsUnifiedVersionMode))]
        [SerializeField]
        private string _packageVersion = string.Empty;

        [TabGroup("Pages", "快速构建")]
        [HorizontalGroup("Pages/快速构建/基础设置/Version", Width = 70)]
        [Button("自动", ButtonSizes.Small)]
        [ShowIf(nameof(IsUnifiedVersionMode))]
        private void GeneratePackageVersion()
        {
            _packageVersion = BuildConfig.GetDefaultPackageVersion();
            OnSettingsChanged();
        }

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/基础设置")]
        [ShowIf(nameof(IsPerPackageVersionMode))]
        [ListDrawerSettings(Expanded = true, DraggableItems = false, HideAddButton = true, HideRemoveButton = true, NumberOfItemsPerPage = 20)]
        [SerializeField]
        private List<PerPackageVersionEntry> _perPackageVersions = new List<PerPackageVersionEntry>();

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/基础设置")]
        [LabelText("AB输出目录")]
        [InlineButton(nameof(ChooseOutputRoot), "浏览")]
        [InlineButton(nameof(OpenOutputRoot), "打开")]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _outputRoot = DefaultOutputRoot;

        [TabGroup("Pages", "资源包")]
        [BoxGroup("Pages/资源包/资源包列表")]
        [ShowInInspector]
        [ReadOnly]
        [HideLabel]
        [MultiLineProperty(2)]
        [ShowIf(nameof(IsUpdateSettingMissing))]
        private string UpdateSettingMissingMessage => "未找到 UpdateSetting 资源。窗口仍可按默认包构建，但不能在这里编辑运行时资源包列表。";

        [TabGroup("Pages", "资源包")]
        [BoxGroup("Pages/资源包/资源包列表")]
        [TableList(ShowIndexLabels = true, AlwaysExpanded = true, IsReadOnly = false)]
        [ListDrawerSettings(Expanded = true, DraggableItems = true, HideAddButton = true)]
        [OnValueChanged(nameof(MarkRuntimePackagesDirty), true)]
        [ShowIf(nameof(HasUpdateSetting))]
        [SerializeField]
        private List<RuntimePackageView> _runtimePackages = new List<RuntimePackageView>();

        [TabGroup("Pages", "资源包")]
        [BoxGroup("Pages/资源包/资源包列表")]
        [HorizontalGroup("Pages/资源包/资源包列表/Actions")]
        [Button("添加资源包", ButtonSizes.Medium)]
        [EnableIf(nameof(HasUpdateSetting))]
        private void AddRuntimePackage()
        {
            var updateSetting = _cachedUpdateSetting;
            if (updateSetting == null)
            {
                return;
            }

            EnsureRuntimePackages(updateSetting);
            _runtimePackages.Add(RuntimePackageView.FromEntry(CreateRuntimePackageEntry(GetNextPackageName(updateSetting), GetAssemblyPackageName())));
            MarkRuntimePackagesDirty();
        }

        [TabGroup("Pages", "资源包")]
        [HorizontalGroup("Pages/资源包/资源包列表/Actions")]
        [Button("重新读取", ButtonSizes.Medium)]
        [EnableIf(nameof(HasUpdateSetting))]
        private void ReloadRuntimePackageViewsButton()
        {
            ReloadRuntimePackageViews();
        }

        [TabGroup("Pages", "资源包")]
        [HorizontalGroup("Pages/资源包/资源包列表/Actions")]
        [Button("定位 UpdateSetting", ButtonSizes.Medium)]
        [EnableIf(nameof(HasUpdateSetting))]
        private void PingUpdateSetting()
        {
            Selection.activeObject = _cachedUpdateSetting;
            EditorGUIUtility.PingObject(_cachedUpdateSetting);
        }

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/发布整理")]
        [LabelText("启用发布整理")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _enablePublishCopy;

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/发布整理")]
        [LabelText("发布根目录")]
        [InlineButton(nameof(ChoosePublishRoot), "浏览")]
        [InlineButton(nameof(OpenPublishRoot), "打开")]
        [ShowIf(nameof(IsPublishCopyEnabled))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _publishRoot = DefaultPublishRoot;

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/发布整理")]
        [LabelText("清空目标包目录后再拷贝")]
        [ToggleLeft]
        [ShowIf(nameof(IsPublishCopyEnabled))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _cleanPublishPackageDirectory = true;

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/发布整理")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("平台目录名")]
        [ShowIf(nameof(IsPublishCopyEnabled))]
        private string PublishPlatformName => ReleaseTools.GetRemotePlatformName(_buildTarget);

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/发布整理")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("输出规则")]
        [ShowIf(nameof(IsPublishCopyEnabled))]
        private string PublishRuleText => $"{_publishRoot}/{PublishPlatformName}/{{资源包名}}";

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/发布整理")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("当前包示例")]
        [MultiLineProperty(3)]
        [ShowIf(nameof(IsPublishCopyEnabled))]
        private string PublishPackagePreviewText => _cachedPublishPackagePreviewText;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/最小包设置")]
        [LabelText("启用最小包模式")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _minimalPackage;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/最小包设置")]
        [LabelText("保留Tag(逗号分隔)")]
        [ShowIf(nameof(_minimalPackage))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _retainTags = string.Empty;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/最小包设置")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("处理说明")]
        [MultiLineProperty(3)]
        [ShowIf(nameof(_minimalPackage))]
        private string MinimalPackageInfo => string.IsNullOrWhiteSpace(_retainTags)
            ? "构建后删除 StreamingAssets 中所有 .bundle 文件，仅保留清单文件，适合 HostPlayMode 在线下载资源。"
            : $"构建后仅保留带 [{_retainTags}] Tag 的 bundle，其余 .bundle 文件会从 StreamingAssets 删除。";

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/高级设置")]
        [LabelText("启用共享资源打包")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _enableSharePackRule = true;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/高级设置")]
        [LabelText("资源路径校验")]
        [ToggleLeft]
        [Tooltip("构建时检测资源及依赖路径中的 Unicode 格式控制字符（如零宽空格、双向控制符），命中即终止构建并输出问题路径。")]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _enableAssetPathValidation = true;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/高级设置")]
        [LabelText("使用资源依赖数据库")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _useAssetDependencyDB = true;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/高级设置")]
        [LabelText("清理构建缓存")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _clearBuildCache;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/高级设置")]
        [LabelText("验证构建结果")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _verifyBuildingResult = true;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/高级设置")]
        [LabelText("内置文件拷贝")]
        [ValueDropdown(nameof(BuildinFileCopyOptions))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private EBundledCopyOption _buildinFileCopyOption = EBundledCopyOption.ClearAndCopyAll;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/高级设置")]
        [LabelText("在构建输出目录生成 Catalog")]
        [Tooltip("开启后，构建完成时在 AB 输出目录额外生成 BuiltinCatalog.bytes/json，直接复制该目录即可用于 OfflinePlayMode 加载。")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _generateCatalogInOutput = false;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/高级设置")]
        [LabelText("文件名风格")]
        [ValueDropdown(nameof(FileNameStyleOptions))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private EFileNameStyle _fileNameStyle = EFileNameStyle.BundleName_HashName;

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/热更 DLL")]
        [LabelText("构建前编译热更DLL")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _buildHotFixDll = true;

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/Player 设置")]
        [LabelText("构建 Player")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _buildPlayer;

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/Player 设置")]
        [LabelText("构建前清空输出目录")]
        [Tooltip("构建 Player（含\"仅构建 Player\"）前清空输出目录（如 Releases/Windows/build），避免上一版残留文件混入产物或安装包。iOS/WebGL 等输出为目录的平台清目录本身，其余清输出文件所在目录。")]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _cleanPlayerOutputDirectory = true;

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/Player 设置")]
        [LabelText("输出路径")]
        [InlineButton(nameof(ChoosePlayerOutputPath), "浏览")]
        [InlineButton(nameof(SyncPlayerOutputName), "同步名字")]
        [InlineButton(nameof(NormalizePlayerOutputPath), "规范化路径")]
        [ShowIf(nameof(_buildPlayer))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _playerOutputPath = string.Empty;

        // ============ 安装包配置（独立 Tab，为后续 Linux 安装包留位） ============
        // 平台选择：决定下方显示哪类安装包配置；与 Player/资源包的目标平台（快速构建页）解耦，
        // 便于在任意目标平台下独立配置 InnoSetup。
        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/平台选择")]
        [LabelText("安装包平台")]
        [ValueDropdown(nameof(InstallerPlatformOptions))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private BuildTarget _installerPlatform = BuildTarget.StandaloneWindows64;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/平台选择")]
        [LabelText("构建安装包")]
        [Tooltip("勾选后参与「构建 AssetBundle + Player + 安装包」一键流程；Windows 走 InnoSetup，Linux 走 debkit（打包需在 WSL 里执行，见下方命令）")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _buildInstaller;

        // 安装包版本不可在此覆盖：固定取 UpdateSetting.Version（已同步到 ProjectSettings 的 Version），
        // 构建时写入 setup.iss 的 MyAppVersion。改版本请改 UpdateSetting。
        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("安装包版本")]
        [Tooltip("固定取 UpdateSetting 的 Version（同步自 ProjectSettings 的 Version），构建时写入 setup.iss 的 MyAppVersion；要改版本请改 UpdateSetting")]
        [ShowIf(nameof(IsInstallerEnabled))]
        private string InstallerVersionText => InnoSetupBuilder.ResolveInstallerVersion(_cachedUpdateSetting);

        /// <summary>「从脚本提取」的读取来源。</summary>
        private enum IssExtractSource
        {
            /// <summary>手工维护的模板 Releases/Windows/setup.iss。</summary>
            Template,

            /// <summary>实际编译脚本 Releases/Windows/setup.generated.iss，可能含手工微调。</summary>
            Generated,
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [HorizontalGroup("Pages/安装包配置/InnoSetup 安装包/Extract")]
        [LabelText("提取来源")]
        [Tooltip("「从脚本提取」读取哪个 iss 脚本的 #define 值")]
        [ValueDropdown(nameof(IssExtractSourceOptions))]
        [ShowIf(nameof(IsInstallerEnabled))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private IssExtractSource _issExtractSource = IssExtractSource.Template;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [HorizontalGroup("Pages/安装包配置/InnoSetup 安装包/Extract")]
        [Button("从脚本提取", ButtonSizes.Small)]
        [Tooltip("读取 iss 脚本中填写的 #define 值,回填下方配置字段(应用名称/软件英文名/发布者/安装密码/发布者水印)")]
        [ShowIf(nameof(IsInstallerEnabled))]
        private void ExtractInstallerConfigFromIss()
        {
            var issPath = _issExtractSource == IssExtractSource.Generated
                ? InnoSetupBuilder.GeneratedIssPath
                : InnoSetupBuilder.IssPath;

            if (!File.Exists(issPath))
            {
                Debug.LogWarning($"[InnoSetup] 脚本不存在，无法提取：{issPath}");
                return;
            }

            IssInstallerConfig extracted;
            var warnings = new List<string>();
            try
            {
                extracted = InnoSetupBuilder.ExtractIssConfig(issPath, warnings);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }

            foreach (var warning in warnings)
            {
                Debug.LogWarning($"[InnoSetup] 提取 {Path.GetFileName(issPath)}：{warning}");
            }

            // 只覆盖脚本中确实读到的键；未读到的字段保持现有配置不变
            var preview = new List<string>();
            AppendExtractChange(preview, "应用名称", _installerAppName, extracted.AppName);
            AppendExtractChange(preview, "软件英文名", _installerAppEnglishName, extracted.AppEnglishName);
            AppendExtractChange(preview, "发布者", _installerPublisher, extracted.Publisher);
            AppendExtractChange(preview, "安装密码", _installerPassword, extracted.Password, true);
            AppendExtractChange(preview, "发布者水印", _installerWatermark, extracted.Watermark);

            if (preview.Count == 0)
            {
                Debug.Log($"[InnoSetup] 已解析 {Path.GetFileName(issPath)}，配置字段无变化。");
                return;
            }

            if (!EditorUtility.DisplayDialog("从脚本提取安装包配置",
                    $"来源：{issPath}\n\n将更新：\n{string.Join("\n", preview)}\n\n是否填充到配置？", "填充", "取消"))
            {
                return;
            }

            if (extracted.AppName != null) _installerAppName = extracted.AppName;
            if (extracted.AppEnglishName != null) _installerAppEnglishName = extracted.AppEnglishName;
            if (extracted.Publisher != null) _installerPublisher = extracted.Publisher;
            if (extracted.Password != null) _installerPassword = extracted.Password;
            if (extracted.Watermark != null) _installerWatermark = extracted.Watermark;

            OnSettingsChanged();
            Repaint();
            Debug.Log($"[InnoSetup] 已从 {Path.GetFileName(issPath)} 提取安装包配置：{string.Join("；", preview)}");
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [LabelText("应用名称")]
        [Tooltip("中文软件名,回写 setup.iss 的 MyAppName;影响安装目录/开始菜单/桌面图标名/安装包文件名")]
        [ShowIf(nameof(IsInstallerEnabled))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _installerAppName = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [LabelText("软件英文名")]
        [Tooltip("仅用于安装目录,回写 setup.iss 的 MyAppEnglishName;为空时回退用「应用名称」")]
        [ShowIf(nameof(IsInstallerEnabled))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _installerAppEnglishName = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [LabelText("发布者")]
        [Tooltip("回写 setup.iss 的 MyAppPublisher,仅用于安装向导显示")]
        [ShowIf(nameof(IsInstallerEnabled))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _installerPublisher = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [LabelText("安装密码")]
        [Tooltip("留空表示不加密;填入后自动启用 Password+Encryption")]
        [ShowIf(nameof(IsInstallerEnabled))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _installerPassword = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [LabelText("发布者水印")]
        [Tooltip("安装向导左下角灰色水印文字,回写 BrandWatermark;为空时回退用「发布者」值")]
        [ShowIf(nameof(IsInstallerEnabled))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _installerWatermark = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [LabelText("ISCC 路径")]
        [Tooltip("手动指定 ISCC.exe 路径兜底；为空则自动按注册表/PATH/ProgramFiles 查找")]
        [InlineButton(nameof(ChooseIsccPath), "浏览")]
        [InlineButton(nameof(OpenIsccPath), "打开")]
        [ShowIf(nameof(IsInstallerEnabled))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _isccPath = string.Empty;

        private string _cachedIsccResolved;
        private string _cachedIsccResolvedForPath = "\0";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("ISCC 状态")]
        [MultiLineProperty(2)]
        [ShowIf(nameof(IsInstallerEnabled))]
        private string IsccStatusText
        {
            get
            {
                var isccPath = _isccPath ?? string.Empty;
                if (!ReferenceEquals(isccPath, _cachedIsccResolvedForPath) &&
                    !string.Equals(isccPath, _cachedIsccResolvedForPath, StringComparison.Ordinal))
                {
                    _cachedIsccResolvedForPath = isccPath;
                    _cachedIsccResolved = InnoSetupBuilder.ResolveIscc(isccPath);
                }

                var resolved = _cachedIsccResolved;
                return string.IsNullOrWhiteSpace(resolved)
                    ? "未找到 ISCC.exe（请安装 Inno Setup 或在上方手动指定路径）"
                    : $"已就绪：{resolved}";
            }
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("模板脚本")]
        [ShowIf(nameof(IsInstallerEnabled))]
        private string IssTemplatePath => InnoSetupBuilder.IssPath;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("实际编译脚本")]
        [ShowIf(nameof(IsInstallerEnabled))]
        private string GeneratedIssScriptPath => InnoSetupBuilder.GeneratedIssPath;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("脚本状态")]
        [MultiLineProperty(2)]
        [ShowIf(nameof(IsInstallerEnabled))]
        private string IssScriptStatusText
        {
            get
            {
                if (!File.Exists(InnoSetupBuilder.IssPath)) return "模板缺失：请放入 setup.iss";
                if (!File.Exists(InnoSetupBuilder.GeneratedIssPath)) return "生成脚本不存在：打开面板时会自动创建";
                var time = File.GetLastWriteTime(InnoSetupBuilder.GeneratedIssPath).ToString("yyyy-MM-dd HH:mm:ss");
                return $"已生成\n最后修改：{time}";
            }
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [HorizontalGroup("Pages/安装包配置/ISCC 编译/ScriptActions")]
        [Button("打开模板", ButtonSizes.Small)]
        [ShowIf(nameof(IsInstallerEnabled))]
        private void OpenIssTemplate()
        {
            OpenFileInEditor(InnoSetupBuilder.IssPath);
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [HorizontalGroup("Pages/安装包配置/ISCC 编译/ScriptActions")]
        [Button("打开生成脚本", ButtonSizes.Small)]
        [ShowIf(nameof(IsInstallerEnabled))]
        private void OpenGeneratedIss()
        {
            OpenFileInEditor(InnoSetupBuilder.GeneratedIssPath);
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [HorizontalGroup("Pages/安装包配置/ISCC 编译/ScriptActions")]
        [Button("从模板重新生成", ButtonSizes.Small)]
        [ShowIf(nameof(IsInstallerEnabled))]
        private void RegenerateGeneratedIss()
        {
            if (File.Exists(InnoSetupBuilder.GeneratedIssPath) &&
                !EditorUtility.DisplayDialog("重新生成 setup.generated.iss", "这会覆盖生成脚本中的手工修改，是否继续？", "覆盖生成", "取消"))
            {
                return;
            }

            try
            {
                InnoSetupBuilder.RegenerateGeneratedIss(CreateInstallerIssConfig());
                Debug.Log($"[InnoSetup] 已从模板重新生成：{InnoSetupBuilder.GeneratedIssPath}");
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("安装包输出")]
        [MultiLineProperty(2)]
        [ShowIf(nameof(IsInstallerEnabled))]
        private string InstallerOutputPreview =>
            $"主程序产物：{InnoSetupBuilder.PlayerBuildDir}\n安装包输出：{InnoSetupBuilder.InstallerOutputDir}";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/ISCC 编译")]
        [Button("构建安装包", ButtonSizes.Medium)]
        [GUIColor(0.35f, 0.95f, 0.55f)]
        [ShowIf(nameof(IsInstallerEnabled))]
        [EnableIf(nameof(IsInstallerEnabled))]
        private void BuildInstallerButton()
        {
            SaveSettings();
            ExecuteBuildInstallerOnly();
        }

        // ============ 安装包配置 / debkit（Linux） ============
        // debkit 位于 Releases/Linux，本面板只负责把字段写成 appConfigs/<包名>.conf；
        // 真正的 .deb 打包仍在 WSL 里执行（面板给出可直接粘贴的命令）。
        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("工具状态")]
        [MultiLineProperty(4)]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        private string DebkitStatusText
        {
            get
            {
                var scriptState = DebkitConfig.ScriptExists
                    ? "已找到 debkit.sh"
                    : "缺少 debkit.sh（请确认 Releases/Linux 下的工具目录完整）";
                var confPath = DebkitConfig.GetConfPath(_debkitPackageName);
                var confState = File.Exists(confPath) ? "配置已存在" : "配置尚未生成";
                return $"{scriptState}\n工具目录：{DebkitConfig.ToolDir}\n配置文件：{confPath}（{confState}）";
            }
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/WSL")]
        [LabelText("WSL 发行版")]
        [Tooltip("打包在哪个 WSL 发行版里执行（即 wsl.exe -d 的参数）；默认发行版 = wsl.exe 不加 -d 时用的那个")]
        [ValueDropdown(nameof(DebkitWslDistroOptions))]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitWslDistro = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/WSL")]
        [Button("刷新列表", ButtonSizes.Small)]
        [Tooltip("重新执行 wsl.exe -l -v（新增 / 卸载发行版之后用）")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        private void RefreshWslDistros()
        {
            DebkitConfig.InvalidateWslCache();
            var distros = DebkitConfig.ListWslDistros(true);
            if (distros.Count > 0)
            {
                AddLog($"[debkit] 检测到 {distros.Count} 个 WSL 发行版：{string.Join("、", distros.Select(item => item.DisplayName))}");
            }
            else
            {
                AddLog($"[debkit] 未检测到 WSL 发行版：{DebkitConfig.WslListError}");
            }

            Repaint();
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("WSL 环境")]
        [MultiLineProperty(2)]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        private string DebkitWslText
        {
            get
            {
                var distros = DebkitConfig.ListWslDistros();
                if (distros.Count <= 0)
                {
                    var reason = string.IsNullOrEmpty(DebkitConfig.WslListError)
                        ? "未安装 WSL 或还没有创建发行版"
                        : DebkitConfig.WslListError;
                    return "WSL：" + reason;
                }

                var names = string.Join("、", distros.Select(item => item.DisplayName));
                var current = string.IsNullOrWhiteSpace(_debkitWslDistro) ? "默认发行版" : _debkitWslDistro;
                return $"WSL 发行版：{names}\n当前选择：{current}";
            }
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [LabelText("应用名称")]
        [Tooltip("回写 APP_TITLE；显示在桌面图标与开始菜单里，可中文")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitAppTitle = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [LabelText("包名")]
        [Tooltip("回写 PKG_NAME；Debian 包名规范：全小写，仅 a-z 0-9 . + -，同时是启动命令名")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitPackageName = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [LabelText("主可执行文件")]
        [Tooltip("回写 EXECUTABLE；源目录里的主程序文件名，默认取 Linux Player 产物名")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitExecutable = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [LabelText("图标源图")]
        [Tooltip("回写 LOGO；相对打包源目录的图片名（方形、四角纯白底效果最好，会自动去白底并生成 8 种尺寸）")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitLogo = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [LabelText("安装目录")]
        [Tooltip("回写 INSTALL_DIR；留空则用 /opt/<包名>")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitInstallDir = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/运行时开关")]
        [LabelText("默认画质")]
        [Tooltip("回写 QUALITY_DEFAULT；数字索引，顺序同 Project Settings > Quality；none 表示不写明由程序决定")]
        [ValueDropdown(nameof(DebkitQualityOptions))]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitQualityDefault = "none";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/运行时开关")]
        [LabelText("默认图形接口")]
        [Tooltip("回写 GRAPHICS_DEFAULT；opengl 传 -force-glcore，vulkan 传 -force-vulkan，none 由 Unity 自己挑")]
        [ValueDropdown(nameof(DebkitGraphicsOptions))]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitGraphicsDefault = "none";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/自启")]
        [LabelText("默认开机自启")]
        [Tooltip("回写 AUTOSTART_DEFAULT；装完默认给每个本地用户开启自启，之后可用 <包名>-ctl autostart 切换")]
        [ValueDropdown(nameof(DebkitYesNoOptions))]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitAutostart = "yes";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/自启")]
        [LabelText("自启延迟(秒)")]
        [Tooltip("回写 AUTOSTART_DELAY；登录后等桌面就绪再拉起，Unity 程序一般 3 秒起")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitAutostartDelay = "3";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/压缩")]
        [LabelText("压缩方式")]
        [Tooltip("回写 COMPRESS；auto 会按 pigz → zstd → gzip → xz 挑第一个可用的，大包建议 pigz")]
        [ValueDropdown(nameof(DebkitCompressOptions))]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitCompress = "auto";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/压缩")]
        [LabelText("压缩级别")]
        [Tooltip("回写 COMPRESS_LEVEL；1-9，默认 6")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitCompressLevel = "6";

        [TabGroup("Pages", "安装包配置")]
        [FoldoutGroup("Pages/安装包配置/debkit 高级", Expanded = false)]
        [LabelText("打包源目录")]
        [Tooltip("回写 SRC_DIR；留空用 Releases/Linux/build。填 Windows 路径会自动换算成 /mnt/<盘符>/...")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitSourceDir = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [FoldoutGroup("Pages/安装包配置/debkit 高级", Expanded = false)]
        [LabelText("依赖库")]
        [Tooltip("回写 DEPENDS；逗号分隔，| 表示二选一（如 libasound2 | libasound2t64）。留空用 debkit 默认列表")]
        [MultiLineProperty(3)]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitDepends = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [FoldoutGroup("Pages/安装包配置/debkit 高级", Expanded = false)]
        [LabelText("默认启动参数")]
        [Tooltip("回写 EXTRA_ARGS；四个入口（桌面/菜单/自启/终端）共用，例：-screen-fullscreen 1")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitExtraArgs = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [FoldoutGroup("Pages/安装包配置/debkit 高级", Expanded = false)]
        [LabelText("可写目录")]
        [Tooltip("回写 WRITABLE_DIRS；相对安装目录、空格分隔，如 \"configs StreamingAssets/logs\"，装包后属主交给运行用户")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitWritableDirs = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [FoldoutGroup("Pages/安装包配置/debkit 高级", Expanded = false)]
        [LabelText("剔除文件")]
        [Tooltip("回写 EXCLUDE；要从包里剔除的通配符，空格分隔，例：\"*.pdb *.log\"")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitExclude = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [FoldoutGroup("Pages/安装包配置/debkit 高级", Expanded = false)]
        [LabelText("菜单分类")]
        [Tooltip("回写 CATEGORIES；desktop 文件的分类，例：Utility;Game;")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitCategories = "Utility;";

        [TabGroup("Pages", "安装包配置")]
        [FoldoutGroup("Pages/安装包配置/debkit 高级", Expanded = false)]
        [LabelText("装包时建桌面图标")]
        [Tooltip("回写 CREATE_DESKTOP_ICON；no 表示装包不建快捷方式，用户之后可自己执行 <包名>-ctl icon create")]
        [ValueDropdown(nameof(DebkitYesNoOptions))]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _debkitCreateDesktopIcon = "yes";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("安装包输出")]
        [MultiLineProperty(2)]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        private string DebkitOutputPreview =>
            $"程序产物：{DebkitConfig.ToWindowsPath(DebkitConfig.DefaultSourceDirWsl())}\n安装包输出：{DebkitConfig.OutputDir}";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("打包命令")]
        [Tooltip("第 1 行可直接粘到 PowerShell / CMD；第 2 行是进入 WSL 后执行的原始命令")]
        [MultiLineProperty(3)]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        private string DebkitWslCommandText =>
            DebkitConfig.BuildWindowsWslCommand(_debkitPackageName, _debkitWslDistro) + "\n" +
            DebkitConfig.BuildWslCommand(_debkitPackageName);

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/操作")]
        [Button("写入配置", ButtonSizes.Small)]
        [Tooltip("把上方字段写成 Releases/Linux/appConfigs/<包名>.conf（已存在会直接覆盖）")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        private void WriteDebkitConf()
        {
            try
            {
                SaveSettings();
                var path = DebkitConfig.WriteConfig(CreateDebkitConfData());
                Debug.Log($"[debkit] 已写入安装包配置：{path}");
                AddLog($"[debkit] 已写入安装包配置：{path}");
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                AddLog($"[错误] 写入 debkit 配置失败：{e.Message}");
                Repaint();
            }
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/操作")]
        [Button("从配置回读", ButtonSizes.Small)]
        [Tooltip("读取 appConfigs/<包名>.conf 的现有值回填上方字段（手工改过 conf 时用）")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        private void ReadDebkitConf()
        {
            var path = DebkitConfig.GetConfPath(_debkitPackageName);
            if (!DebkitConfig.TryReadConfig(path, out var data))
            {
                Debug.LogWarning($"[debkit] 配置不存在，无法回读：{path}");
                return;
            }

            _debkitAppTitle = data.AppTitle;
            _debkitPackageName = string.IsNullOrWhiteSpace(data.PackageName) ? _debkitPackageName : data.PackageName;
            _debkitExecutable = data.Executable;
            _debkitLogo = data.Logo;
            _debkitInstallDir = data.InstallDir;
            _debkitSourceDir = data.SourceDir;
            _debkitQualityDefault = data.QualityDefault;
            _debkitGraphicsDefault = data.GraphicsDefault;
            _debkitAutostart = data.AutostartDefault;
            _debkitAutostartDelay = data.AutostartDelay;
            _debkitCompress = data.Compress;
            _debkitCompressLevel = data.CompressLevel;
            _debkitDepends = data.Depends;
            _debkitExtraArgs = data.ExtraArgs;
            _debkitWritableDirs = data.WritableDirs;
            _debkitExclude = data.Exclude;
            _debkitCategories = data.Categories;
            _debkitCreateDesktopIcon = data.CreateDesktopIcon;

            OnSettingsChanged();
            Debug.Log($"[debkit] 已从配置回读：{path}");
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/操作")]
        [Button("打开工具目录", ButtonSizes.Small)]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        private void OpenDebkitToolDir()
        {
            var dir = DebkitConfig.ToolDir;
            if (Directory.Exists(dir))
            {
                EditorUtility.RevealInFinder(dir);
            }
            else
            {
                Debug.LogWarning($"[debkit] 目录不存在：{dir}");
            }
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [HorizontalGroup("Pages/安装包配置/debkit 安装包/操作")]
        [Button("复制 WSL 命令", ButtonSizes.Small)]
        [Tooltip("复制「cd 工具目录 && ./debkit.sh -c appConfigs/<包名>.conf -w ~/debkit-work」到剪贴板")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        private void CopyDebkitWslCommand()
        {
            var command = DebkitConfig.BuildWindowsWslCommand(_debkitPackageName, _debkitWslDistro);
            EditorGUIUtility.systemCopyBuffer = command;
            AddLog($"[debkit] 已复制打包命令：{command}");
            Debug.Log($"[debkit] 已复制打包命令：{command}");
            Repaint();
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/debkit 安装包")]
        [Button("构建安装包", ButtonSizes.Medium)]
        [GUIColor(0.35f, 0.95f, 0.55f)]
        [Tooltip("写入 conf 后在 WSL 里执行 debkit.sh，产物输出到 Releases/Linux/output")]
        [ShowIf(nameof(IsDebkitPanelVisible))]
        [EnableIf(nameof(CanBuildDebkit))]
        private void BuildDebkitInstaller()
        {
            SaveSettings();
            try
            {
                var confPath = DebkitConfig.WriteConfig(CreateDebkitConfData());
                _buildLogs.Clear();
                AddLog("========== 构建 Linux 安装包（debkit）==========");
                AddLog($"[debkit] 已写入配置：{confPath}");
                StartDebkitProcess();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                AddLog($"[错误] 构建 Linux 安装包失败：{e.Message}");
                Repaint();
            }
        }

        /// <summary>打包进行中时禁用按钮，避免叠加多个 debkit 进程。</summary>
        private bool CanBuildDebkit
        {
            get
            {
                if (_debkitBuildRunning && _debkitProcess == null)
                {
                    // 进程对象丢了（脚本重载 / 窗口重开），别让按钮永久禁用
                    _debkitBuildRunning = false;
                }

                return !_debkitBuildRunning;
            }
        }

        /// <summary>起 wsl.exe 执行 debkit；输出在编辑器主线程分帧回显到构建日志。</summary>
        private void StartDebkitProcess()
        {
            if (_debkitBuildRunning)
            {
                AddLog("[跳过] 上一次 debkit 打包还没结束。");
                Repaint();
                return;
            }

            if (!DebkitConfig.ScriptExists)
            {
                AddLog($"[错误] 找不到打包脚本：{DebkitConfig.ScriptPath}");
                Repaint();
                return;
            }

            var config = CreateDebkitConfData();
            var sourceDir = DebkitConfig.ToWindowsPath(config.SourceDir);
            if (!Directory.Exists(sourceDir))
            {
                AddLog($"[错误] 找不到 Linux Player 产物：{sourceDir}");
                AddLog("[提示] 先到「快速构建」把平台切到 Linux 64-bit 构建一次 Player，再回来打 .deb。");
                Repaint();
                return;
            }

            var distro = string.IsNullOrWhiteSpace(_debkitWslDistro) ? "默认发行版" : _debkitWslDistro;
            var arguments = DebkitConfig.BuildWslProcessArguments(config.PackageName, _debkitWslDistro);
            AddLog($"[debkit] WSL 发行版：{distro}");
            AddLog($"[debkit] 源目录：{sourceDir}");
            AddLog($"[debkit] 命令：wsl.exe {arguments}");

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = arguments,
                    WorkingDirectory = DebkitConfig.ProjectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    // WSL 里程序的输出是 UTF-8；这与 wsl.exe -l 的 UTF-16LE 表格不同
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                };

                var process = new System.Diagnostics.Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true,
                };
                process.OutputDataReceived += (sender, args) => EnqueueDebkitLine(args.Data);
                process.ErrorDataReceived += (sender, args) => EnqueueDebkitLine(args.Data);
                process.Exited += (sender, args) =>
                {
                    try
                    {
                        // 等异步读取把剩余输出刷完，再收尾
                        process.WaitForExit();
                        EnqueueDebkitFinish(process.ExitCode);
                    }
                    catch (Exception)
                    {
                        EnqueueDebkitFinish(-1);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                _debkitProcess = process;
                _debkitBuildRunning = true;
                EditorApplication.update -= PumpDebkitOutput;
                EditorApplication.update += PumpDebkitOutput;
                AddLog("[debkit] 已启动，日志会在打包过程中陆续输出……");
            }
            catch (Exception e)
            {
                _debkitBuildRunning = false;
                AddLog($"[错误] 启动 wsl.exe 失败：{e.Message}");
                AddLog("[提示] 确认本机已安装 WSL（命令行执行 wsl.exe -l -v 能看到发行版）。");
            }

            Repaint();
        }

        /// <summary>后台线程写队列，避免直接触碰编辑器 UI。</summary>
        private void EnqueueDebkitLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            lock (_debkitOutputLock)
            {
                _debkitOutputLines.Add(line);
            }
        }

        private void EnqueueDebkitFinish(int exitCode)
        {
            lock (_debkitOutputLock)
            {
                _debkitExitCode = exitCode;
                _debkitOutputFinished = true;
            }
        }

        /// <summary>主线程泵：把队列里的输出搬进构建日志，结束后汇总产物。</summary>
        private void PumpDebkitOutput()
        {
            if (this == null)
            {
                // 窗口已关闭，停止泵
                EditorApplication.update -= PumpDebkitOutput;
                return;
            }

            List<string> lines;
            bool finished;
            lock (_debkitOutputLock)
            {
                lines = new List<string>(_debkitOutputLines);
                _debkitOutputLines.Clear();
                finished = _debkitOutputFinished;
            }

            foreach (var line in lines)
            {
                AddLog("  " + line);
            }

            if (!finished)
            {
                return;
            }

            EditorApplication.update -= PumpDebkitOutput;

            var exitCode = _debkitExitCode;
            _debkitOutputFinished = false;
            _debkitBuildRunning = false;

            try
            {
                _debkitProcess?.Dispose();
            }
            catch (Exception)
            {
                // 进程对象可能已被回收，忽略
            }

            _debkitProcess = null;

            if (exitCode == 0)
            {
                var debPath = DebkitConfig.GetDebPath(CreateDebkitConfData());
                if (File.Exists(debPath))
                {
                    var sizeMb = new FileInfo(debPath).Length / 1024d / 1024d;
                    AddLog($"[完成] 已生成安装包：{debPath}（{sizeMb:F1} MB）");
                    Debug.Log($"[debkit] 已生成安装包：{debPath}");
                }
                else
                {
                    AddLog($"[完成] debkit 执行成功，但没在 {DebkitConfig.OutputDir} 找到预期的 .deb（{Path.GetFileName(debPath)}）。");
                }
            }
            else
            {
                AddLog($"[失败] debkit 退出码 {exitCode}，请查看上方输出定位原因。");
            }

            Repaint();
        }

        /// <summary>debkit 进程与输出泵的运行时状态（不参与序列化）。</summary>
        private System.Diagnostics.Process _debkitProcess;
        private readonly List<string> _debkitOutputLines = new List<string>();
        private readonly object _debkitOutputLock = new object();
        private bool _debkitOutputFinished;
        private int _debkitExitCode;
        private bool _debkitBuildRunning;

        /// <summary>把面板字段组装成 debkit 配置数据；版本取自 UpdateSetting（与 Windows 安装包口径一致）。</summary>
        private DebkitConfData CreateDebkitConfData()
        {
            var versionText = InnoSetupBuilder.ResolveInstallerVersion(_cachedUpdateSetting);
            var version = string.IsNullOrWhiteSpace(versionText) ? "1.0.0" : versionText.Trim();
            var revision = "1";
            var dashIndex = version.LastIndexOf('-');
            if (dashIndex > 0 && dashIndex < version.Length - 1)
            {
                revision = version.Substring(dashIndex + 1);
                version = version.Substring(0, dashIndex);
            }

            return new DebkitConfData
            {
                AppTitle = _debkitAppTitle,
                PackageName = _debkitPackageName,
                Version = version,
                Revision = revision,
                SourceDir = _debkitSourceDir,
                Executable = _debkitExecutable,
                Logo = _debkitLogo,
                InstallDir = _debkitInstallDir,
                Categories = _debkitCategories,
                QualityDefault = _debkitQualityDefault,
                GraphicsDefault = _debkitGraphicsDefault,
                AutostartDefault = _debkitAutostart,
                AutostartDelay = _debkitAutostartDelay,
                CreateDesktopIcon = _debkitCreateDesktopIcon,
                Compress = _debkitCompress,
                CompressLevel = _debkitCompressLevel,
                Depends = _debkitDepends,
                ExtraArgs = _debkitExtraArgs,
                WritableDirs = _debkitWritableDirs,
                Exclude = _debkitExclude,
            };
        }

        /// <summary>收集「脚本值 → 配置字段」的差异行；脚本未读到该键或值相同则不记录。</summary>
        private static void AppendExtractChange(List<string> preview, string label, string current, string extracted, bool maskValue = false)
        {
            if (extracted == null) return;
            if (string.Equals(current ?? string.Empty, extracted, StringComparison.Ordinal)) return;

            var oldText = string.IsNullOrEmpty(current) ? "(空)" : (maskValue ? "******" : current);
            var newText = string.IsNullOrEmpty(extracted) ? "(空)" : (maskValue ? "******" : extracted);
            preview.Add($"{label}：{oldText} → {newText}");
        }

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/构建流程预览")]
        [TableList(ShowIndexLabels = false, AlwaysExpanded = true, IsReadOnly = true)]
        [ListDrawerSettings(Expanded = true, DraggableItems = false, HideAddButton = true, HideRemoveButton = true)]
        [ReadOnly]
        [SerializeField]
        private List<FlowStepView> _flowSteps = new List<FlowStepView>();

        [PropertySpace(8)]
        [TitleGroup("构建")]
        [LabelText("构建资源包")]
        [ValueDropdown(nameof(GetBuildPackageSelectionOptions))]
        [OnValueChanged(nameof(OnBuildPackageSelectionChanged))]
        [SerializeField]
        private string _selectedBuildPackageName = AllBuildPackagesDisplayName;

        [TitleGroup("构建")]
        [ButtonGroup("构建/MainBuild")]
        [Button("构建 AssetBundle", ButtonSizes.Large)]
        [GUIColor(0.45f, 0.75f, 1f)]
        private void BuildAssetBundleButton()
        {
            SaveSettings();
            ExecuteBuild(false, GetSelectedBuildPackageName());
        }

        [ButtonGroup("构建/MainBuild")]
        [Button("构建 AssetBundle + Player", ButtonSizes.Large)]
        [GUIColor(0.35f, 0.95f, 0.55f)]
        private void FullBuildButton()
        {
            _buildPlayer = true;
            SaveSettings();
            ExecuteBuild(true, GetSelectedBuildPackageName());
        }

        // 安装包构建已与 Player 解耦,这里单独触发;仅 Windows 且勾选「构建安装包」时可用
        [ButtonGroup("构建/MainBuild")]
        [Button("构建 AssetBundle + Player + 安装包", ButtonSizes.Medium)]
        [GUIColor(0.35f, 0.95f, 0.55f)]
        [EnableIf(nameof(IsInstallerEnabled))]
        private void MainBuildInstallerButton()
        {
            SaveSettings();
            var config = CreateConfig();
            var expectedPlayerPath = Path.GetFullPath(BuildConfig.GetDefaultPlayerOutputPath(BuildTarget.StandaloneWindows64));
            var actualPlayerPath = Path.GetFullPath(ToAbsolutePath(config.PlayerOutputPath));
            if (config.BuildTarget != BuildTarget.StandaloneWindows64 ||
                !actualPlayerPath.Equals(expectedPlayerPath, StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"[中断] 一键构建安装包要求 Windows Player 输出到：{expectedPlayerPath}\n当前输出：{actualPlayerPath}\n可点击 Player 输出路径旁的\"规范化路径\"后再试。");
                Repaint();
                return;
            }
            // MainBuild 区的一键构建安装包:AssetBundle + Player + InnoSetup 安装包 一条龙
            ExecuteBuild(true, GetSelectedBuildPackageName());
            if (_lastBuildFailed)
            {
                AddLog("[中断] 前置构建失败,已跳过安装包构建。请修复后重试。");
                Repaint();
                return;
            }
            ExecuteInstallerBuild(clearLogs: false);
        }

        [TitleGroup("构建")]
        [ButtonGroup("构建/SubActions")]
        [Button("仅构建 Player", ButtonSizes.Large)]
        private void BuildPlayerButton()
        {
            SaveSettings();
            ExecuteBuildPlayerOnly();
        }

        [ButtonGroup("构建/SubActions")]
        [Button("仅执行发布整理", ButtonSizes.Large)]
        [EnableIf(nameof(IsPublishCopyEnabled))]
        private void PublishOnlyButton()
        {
            SaveSettings();
            ExecutePublishOnly();
        }

        [PropertySpace(8)]
        [TitleGroup("打开目录")]
        [ButtonGroup("打开目录/Dirs")]
        [Button("打开AB输出目录", ButtonSizes.Large)]
        private void OpenOutputRootButton()
        {
            OpenOutputRoot();
        }

        [ButtonGroup("打开目录/Dirs")]
        [Button("打开Player输出目录", ButtonSizes.Large)]
        [ShowIf(nameof(_buildPlayer))]
        private void OpenPlayerOutputPathButton()
        {
            OpenPlayerOutputPath();
        }

        [ButtonGroup("打开目录/Dirs")]
        [Button("打开发布目录", ButtonSizes.Large)]
        [EnableIf(nameof(IsPublishCopyEnabled))]
        private void OpenPublishRootButton()
        {
            OpenPublishRoot();
        }

        [PropertySpace(8)]
        [TitleGroup("热更 DLL", "日常热更只需此项")]
        [ButtonGroup("热更 DLL/Actions")]
        [Button("编译并拷贝热更DLL", ButtonSizes.Large)]
        [Tooltip("编译热更程序集并拷贝到热更资源目录；随后自动同步 AOT 元数据清单并拷贝 AOT/热更 DLL 的 .bytes")]
        private void BuildHotFixDllFromOperations()
        {
            BuildDLLCommand.BuildAndCopyDlls(_buildTarget);
        }

        [PropertySpace(8)]
        [TitleGroup("AOT", "AOT 产物维护；GenerateAll 已含本区①②，出包请用下方 GenerateAll")]
        [ButtonGroup("AOT/生成")]
        [Button("① 生成 AOT DLL", ButtonSizes.Large)]
        [Tooltip("= GenerateAll 的第4步。HybridCLR/Generate/StrippedAotDlls：仅首包构建/重建主包时使用；会以 buildScriptsOnly 跑一次 Player 构建，且会【覆盖】AssembliesPostIl2CppStrip 下已冻结的 AOT dll。单独点它不含 Il2CppDef/MethodBridge，不能替代出包前的 GenerateAll。")]
        private void GenerateStrippedAOTDllsFromOperations()
        {
            BuildDLLCommand.GenerateStrippedAOTDllsForTarget(_buildTarget);
        }

        [ButtonGroup("AOT/生成")]
        [Button("② 生成 AOT 列表", ButtonSizes.Large)]
        [Tooltip("= GenerateAll 的第6步。HybridCLR/Generate/AOTGenericReference：产出 AOTGenericReferences.cs 的 PatchedAOTAssemblyList；需先有 ① 的 AOT DLL；Obfuz 开启时基于混淆后热更 DLL 分析。")]
        private void GenerateAOTGenericReferenceFromOperations()
        {
            BuildDLLCommand.GenerateAOTGenericReferenceForTarget(_buildTarget);
        }

        [ButtonGroup("AOT/落地")]
        [Button("③ 同步 AOT 元数据清单", ButtonSizes.Large)]
        [Tooltip("工程自有步骤：解析 AOTGenericReferences.cs 的 PatchedAOTAssemblyList，写入 AOTMetadataManifest.asset")]
        private void SyncAOTMetadataManifestFromOperations()
        {
            BuildDLLCommand.SyncAOTMetadataManifest();
        }

        [ButtonGroup("AOT/落地")]
        [Button("④ 拷贝 AOT 元数据 DLL", ButtonSizes.Large)]
        [Tooltip("工程自有步骤：把裁剪后的 AOT DLL 拷贝到热更资源目录（.bytes）")]
        private void CopyAOTAssembliesFromOperations()
        {
            BuildDLLCommand.CopyAOTAssembliesToAssetPath(_buildTarget);
        }

        [PropertySpace(8)]
        [TitleGroup("出包前生成", "构建 Player 前必须；已含 AOT 区①②，另含 Il2CppDef/MethodBridge")]
        [ButtonGroup("出包前生成/Actions")]
        [Button("GenerateAll", ButtonSizes.Large)]
        [Tooltip("官方『一键执行打包前必须的生成操作』：编译热更 DLL → Il2CppDef → LinkXml → 生成 AOT DLL → MethodBridge → 生成 AOT 列表（Obfuz 开启时走混淆链路）。已完整包含 AOT 区的 ①②，出包时只需点它即可。AOT 侧代码 / link.xml / HybridCLR 版本 / development 开关变化后也需重跑。")]
        private void GenerateAllFromOperations()
        {
            BuildDLLCommand.GenerateAllForTarget(_buildTarget);
        }

        [PropertySpace(8)]
        [TitleGroup("设置")]
        [ButtonGroup("设置/Actions")]
        [Button("刷新设置", ButtonSizes.Large)]
        private void RefreshSettingsButton()
        {
            LoadSettings();
        }

        [ButtonGroup("设置/Actions")]
        [Button("重置默认", ButtonSizes.Large)]
        private void ResetDefaultSettingsButton()
        {
            ApplyConfig(BuildConfig.CreateDefault());
            SaveSettings();
            RefreshCachedTexts();
            AddLog("已重置打包工具默认配置");
        }

        [PropertySpace(8)]
        [FoldoutGroup("构建日志", Expanded = false)]
        [HorizontalGroup("构建日志/Actions")]
        [Button("清空日志", ButtonSizes.Small)]
        [PropertyOrder(100)]
        [EnableIf(nameof(HasBuildLogs))]
        private void ClearBuildLogs()
        {
            _buildLogs.Clear();
        }

        [FoldoutGroup("构建日志", Expanded = false)]
        [ShowInInspector]
        [ReadOnly]
        [HideLabel]
        [PropertyOrder(100)]
        [ListDrawerSettings(Expanded = true, DraggableItems = false, HideAddButton = true, HideRemoveButton = true)]
        private readonly List<string> _buildLogs = new List<string>();

        /// <summary>最近一次 ExecuteBuild 是否失败;用于 MainBuild 一键构建串联时判断是否跳过安装包阶段。</summary>
        private bool _lastBuildFailed;

        [MenuItem(MenuPath, false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildPipelineWindow>();
            window.titleContent = new GUIContent("TEngine 打包工具", EditorGUIUtility.IconContent("BuildSettings.Editor.Small").image);
            window.minSize = new Vector2(760, 680);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            LoadSettings();
        }

        protected override void OnImGUI()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label(_cachedToolbarStatus, EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("保存设置", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    SaveSettings();
                    SaveRuntimePackageViews(flushToDisk: true);
                }
            }
            GUILayout.EndHorizontal();

            SirenixEditorGUI.DrawThickHorizontalSeparator();

            base.OnImGUI();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            EditorApplication.update -= FlushPendingSavesWhenReady;
            if (_runtimePackagesDirty)
            {
                SaveRuntimePackageViews(flushToDisk: true);
            }

            if (_settingDirty)
            {
                AssetDatabase.SaveAssets();
                _settingDirty = false;
            }
        }

        #region 构建执行

        private void ExecuteBuild(bool buildPlayer, string packageName = null)
        {
            var config = CreateConfig();
            _buildLogs.Clear();
            AddLog("========== 开始构建 ==========");
            AddLog($"平台: {config.BuildTarget} | 默认管线: {config.BuildPipeline} | 最小包: {config.MinimalPackage}");
            AddLog(string.IsNullOrWhiteSpace(packageName)
                ? $"资源包: {_cachedPackageSummary}"
                : $"资源包: {packageName}");

            if (string.IsNullOrWhiteSpace(config.PackageVersion))
            {
                _packageVersion = BuildConfig.GetDefaultPackageVersion();
                config.PackageVersion = _packageVersion;
                SaveSettings();
                AddLog($"版本号为空，自动生成: {config.PackageVersion}");
            }

            if (config.PackageVersionMode == PackageVersionMode.PerPackage)
            {
                SyncPerPackageVersionList();
                AutoGeneratePerPackageVersions();
                config.PackageVersionMap = BuildPackageVersionMap();
                var versionSummary = string.Join(", ", _perPackageVersions.Select(x => $"{x.PackageName}={x.Version}"));
                AddLog($"独立版本号模式: {versionSummary}");
            }

            if (config.EnablePublishCopy)
            {
                AddLog($"发布目录: {ReleaseTools.GetPublishOutputRoot(config)}");
                AddLog($"发布平台目录: {ReleaseTools.GetRemotePlatformName(config.BuildTarget)}");
            }

            try
            {
                Application.logMessageReceived += OnBuildLogReceived;

                if (buildPlayer)
                {
                    config.BuildPlayer = true;
                    if (!ReleaseTools.BuildWithConfig(config, true, packageName))
                        throw new Exception("AssetBundle 或 Player 构建失败，已中止后续流程。");
                }
                else
                {
                    config.BuildPlayer = false;
                    if (!ReleaseTools.BuildWithConfig(config, false, packageName))
                        throw new Exception("AssetBundle 构建失败。");
                }

                _lastBuildFailed = false;
                AddLog("========== 构建完成 ==========");
            }
            catch (Exception e)
            {
                _lastBuildFailed = true;
                AddLog($"[错误] {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                Application.logMessageReceived -= OnBuildLogReceived;
            }

            Repaint();
        }

        private void ExecutePublishOnly()
        {
            var config = CreateConfig();
            _buildLogs.Clear();
            AddLog("========== 仅执行发布整理 ==========");
            AddLog($"构建输出目录: {ReleaseTools.GetBuildPlatformOutputRoot(config)}");
            AddLog($"发布目录: {ReleaseTools.GetPublishOutputRoot(config)}");

            var versions = ReleaseTools.GetPublishableVersions(config);
            if (versions.Count <= 0)
            {
                AddLog("[错误] 未找到可整理的版本目录。请先完成 AssetBundle 构建。");
                Repaint();
                return;
            }

            if (config.PackageVersionMode == PackageVersionMode.PerPackage)
            {
                // PerPackage 模式：直接用每包各自版本整理，不需要选公共版本。
                var perPackageConfig = CreateConfig();
                SyncPerPackageVersionList();
                perPackageConfig.PackageVersionMap = BuildPackageVersionMap();
                AddLog($"独立版本号模式: {string.Join(", ", perPackageConfig.PackageVersionMap.Select(x => $"{x.Key}={x.Value}"))}");
                try
                {
                    Application.logMessageReceived += OnBuildLogReceived;
                    if (ReleaseTools.PublishFromExistingBuild(perPackageConfig, string.Empty))
                    {
                        AddLog($"发布目录: {ReleaseTools.GetPublishOutputRoot(perPackageConfig)}");
                        AddLog($"发布平台目录: {ReleaseTools.GetRemotePlatformName(perPackageConfig.BuildTarget)}");
                        AddLog("========== 发布整理完成 ==========");
                    }
                    else
                    {
                        AddLog("[错误] 发布整理执行失败。");
                    }
                }
                catch (Exception e)
                {
                    _lastBuildFailed = true;
                    AddLog($"[错误] {e.Message}");
                    Debug.LogException(e);
                }
                finally
                {
                    Application.logMessageReceived -= OnBuildLogReceived;
                }

                Repaint();
                return;
            }

            if (!string.IsNullOrWhiteSpace(config.PackageVersion) && versions.Contains(config.PackageVersion))
            {
                RunPublishOnly(config.PackageVersion);
                return;
            }

            if (!string.IsNullOrWhiteSpace(config.PackageVersion))
            {
                AddLog($"[WARN] 当前版本号未命中现有构建目录: {config.PackageVersion}");
            }

            if (versions.Count == 1)
            {
                RunPublishOnly(versions[0]);
                return;
            }

            ShowPublishVersionMenu(versions);
            ShowNotification(new GUIContent("请选择要整理的版本"));
            Repaint();
        }

        private void RunPublishOnly(string packageVersion)
        {
            var config = CreateConfig();
            AddLog($"整理版本: {packageVersion}");

            try
            {
                Application.logMessageReceived += OnBuildLogReceived;
                if (ReleaseTools.PublishFromExistingBuild(config, packageVersion))
                {
                    AddLog($"发布目录: {ReleaseTools.GetPublishOutputRoot(config)}");
                    AddLog($"发布平台目录: {ReleaseTools.GetRemotePlatformName(config.BuildTarget)}");
                    AddLog("========== 发布整理完成 ==========");
                }
                else
                {
                    AddLog("[错误] 发布整理执行失败。");
                }
            }
            catch (Exception e)
            {
                AddLog($"[错误] {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                Application.logMessageReceived -= OnBuildLogReceived;
            }

            Repaint();
        }

        private void ShowPublishVersionMenu(IReadOnlyList<string> versions)
        {
            var menu = new GenericMenu();
            for (var i = 0; i < versions.Count; i++)
            {
                var version = versions[i];
                var isRecommended = i == 0;
                var menuLabel = isRecommended ? $"{version}（推荐）" : version;
                menu.AddItem(new GUIContent(menuLabel), false, () => RunPublishOnly(version));
            }

            menu.ShowAsContext();
        }

        private void ExecuteBuildPlayerOnly()
        {
            var config = CreateConfig();
            _buildLogs.Clear();
            AddLog("========== 仅构建 Player ==========");
            AddLog($"平台: {config.BuildTarget} | 输出: {config.PlayerOutputPath}");

            try
            {
                Application.logMessageReceived += OnBuildLogReceived;
                if (!ReleaseTools.BuildImp(
                    BuildConfig.GetBuildTargetGroup(config.BuildTarget),
                    config.BuildTarget,
                    config.PlayerOutputPath,
                    config.CleanPlayerOutputDirectory
                ))
                    throw new Exception("Player 构建失败。");

                AddLog("========== Player 构建完成 ==========");
            }
            catch (Exception e)
            {
                AddLog($"[错误] {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                Application.logMessageReceived -= OnBuildLogReceived;
            }

            Repaint();
        }

        /// <summary>
        /// 仅编译安装包：按「安装包配置」tab 的安装包平台分发到对应构建器。
        /// Windows 走 InnoSetup 直接编译；Linux 的 .deb 由 debkit 生成，这里只提示配置与 WSL 命令。
        /// </summary>
        private void ExecuteBuildInstallerOnly()
        {
            ExecuteInstallerBuild(clearLogs: true);
        }

        /// <summary>
        /// 编译安装包核心逻辑。clearLogs=false 用于与前置构建串联(如 MainBuild 一键构建),
        /// 保留前置 AB/Player 构建日志,接续输出安装包阶段日志。
        /// </summary>
        private void ExecuteInstallerBuild(bool clearLogs)
        {
            var config = CreateConfig();
            if (clearLogs)
            {
                _buildLogs.Clear();
            }
            AddLog("========== 一键构建安装包 ==========");
            AddLog($"安装包平台: {config.InstallerPlatform}");

            if (config.InstallerPlatform == BuildTarget.StandaloneLinux64)
            {
                // Linux 的 .deb 由 Releases/Linux/debkit 在 WSL 里生成：先写 conf，再起 wsl.exe 打包
                try
                {
                    var confPath = DebkitConfig.WriteConfig(CreateDebkitConfData());
                    AddLog($"[debkit] 已写入配置：{confPath}");
                    StartDebkitProcess();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    AddLog($"[错误] 构建 Linux 安装包失败：{e.Message}");
                    Repaint();
                }

                return;
            }

            if (config.InstallerPlatform != BuildTarget.StandaloneWindows64)
            {
                AddLog($"[跳过] 当前安装包平台 {config.InstallerPlatform} 暂未实现安装包构建。");
                Repaint();
                return;
            }

            if (!config.BuildInstaller)
            {
                AddLog("[跳过] 未勾选「构建安装包」,请在「安装包配置」tab 勾选后再试。");
                Repaint();
                return;
            }

            try
            {
                // 串联构建时,前置 ExecuteBuild 已移除日志监听,此处重新挂载以捕获 InnoSetup 阶段日志
                Application.logMessageReceived += OnBuildLogReceived;

                // exe 名取自 PlayerSettings.productName,与 Player 产物命名一致
                var exeName = BuildConfig.GetDefaultPlayerOutputPath(config.InstallerPlatform);
                exeName = Path.GetFileName(exeName);

                // 水印为空时回退用发布者,保证向导左下角有文字
                var watermark = string.IsNullOrWhiteSpace(config.InstallerWatermark)
                    ? config.InstallerPublisher
                    : config.InstallerWatermark;

                var issConfig = new IssInstallerConfig
                {
                    AppName = config.InstallerAppName,
                    AppEnglishName = config.InstallerAppEnglishName,
                    Publisher = config.InstallerPublisher,
                    ExeName = exeName,
                    Password = config.InstallerPassword,
                    Watermark = watermark,
                };

                AddLog("========== 编译 InnoSetup 安装包 ==========");
                AddLog($"安装包版本: {InnoSetupBuilder.ResolveInstallerVersion()}");
                InnoSetupBuilder.BuildInstaller(issConfig, config.IsccPath);
                AddLog($"安装包输出: {InnoSetupBuilder.InstallerOutputDir}");
                AddLog("========== 安装包构建完成 ==========");
            }
            catch (Exception e)
            {
                AddLog($"[错误] {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                Application.logMessageReceived -= OnBuildLogReceived;
            }

            Repaint();
        }

        private void OnBuildLogReceived(string condition, string stackTrace, LogType type)
        {
            string prefix = type switch
            {
                LogType.Error => "[ERR]",
                LogType.Warning => "[WARN]",
                LogType.Assert => "[ASSERT]",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(prefix) || condition.StartsWith("[", StringComparison.Ordinal) ||
                condition.Contains("构建") || condition.Contains("Build"))
            {
                AddLog($"{prefix}{condition}");
            }
        }

        private void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _buildLogs.Add($"[{timestamp}] {message}");

            if (EditorApplication.timeSinceStartup < _nextLogRepaintTime)
            {
                return;
            }

            _nextLogRepaintTime = EditorApplication.timeSinceStartup + 0.1d;
            Repaint();
        }

        #endregion

        #region 设置持久化

        private void LoadSettings()
        {
            _isLoadingSettings = true;

            _cachedUpdateSetting = LoadUpdateSettingCached();
            _setting = BuildPipelineSetting.LoadOrCreate();
            if (!_setting.EditorPrefsImported)
            {
                ImportEditorPrefsIntoSetting(_setting);
                _setting.EditorPrefsImported = true;
                EditorUtility.SetDirty(_setting);
                AssetDatabase.SaveAssets();
                DeleteLegacyEditorPrefs();
            }

            LoadFromSetting(_setting);

            try
            {
                InnoSetupBuilder.EnsureGeneratedIss(CreateInstallerIssConfig());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InnoSetup] 生成脚本初始化失败：{e.Message}");
            }

            ReloadRuntimePackageViews();
            RefreshCachedTexts();
            _isLoadingSettings = false;
        }

        private void LoadFromSetting(BuildPipelineSetting setting)
        {
            _buildTarget = Array.IndexOf(PlatformTargets, setting.BuildTarget) >= 0
                ? setting.BuildTarget
                : GetActiveSupportedBuildTarget();

            _buildPipeline = setting.BuildPipeline;
            _compressOption = setting.CompressOption;
            _packageVersion = setting.PackageVersion;
            _packageVersionMode = setting.PackageVersionMode;
            _outputRoot = string.IsNullOrWhiteSpace(setting.OutputRoot) ? DefaultOutputRoot : setting.OutputRoot;
            _enablePublishCopy = setting.EnablePublishCopy;
            _publishRoot = string.IsNullOrWhiteSpace(setting.PublishRoot) ? DefaultPublishRoot : setting.PublishRoot;
            _cleanPublishPackageDirectory = setting.CleanPublishPackageDirectory;
            _minimalPackage = setting.MinimalPackage;
            _retainTags = setting.RetainTags;
            _enableSharePackRule = setting.EnableSharePackRule;
            _enableAssetPathValidation = setting.EnableAssetPathValidation;
            _useAssetDependencyDB = setting.UseAssetDependencyDB;
            _clearBuildCache = setting.ClearBuildCache;
            _verifyBuildingResult = setting.VerifyBuildingResult;
            _buildinFileCopyOption = setting.BuildinFileCopyOption;
            _fileNameStyle = setting.FileNameStyle;
            _generateCatalogInOutput = setting.GenerateCatalogInOutput;
            _buildHotFixDll = setting.BuildHotFixDll;
            _buildPlayer = setting.BuildPlayer;

            _playerOutputPath = string.IsNullOrWhiteSpace(setting.PlayerOutputPath)
                ? BuildConfig.GetDefaultPlayerOutputPath(_buildTarget)
                : setting.PlayerOutputPath;
            _cleanPlayerOutputDirectory = setting.CleanPlayerOutputDirectory;

            _buildInstaller = setting.BuildInstaller;
            _installerPlatform = Array.IndexOf(PlatformTargets, setting.InstallerPlatform) >= 0
                ? setting.InstallerPlatform
                : BuildTarget.StandaloneWindows64;
            _isccPath = setting.IsccPath;
            _installerAppName = setting.InstallerAppName;
            _installerAppEnglishName = setting.InstallerAppEnglishName;
            _installerPublisher = setting.InstallerPublisher;
            _installerPassword = setting.InstallerPassword;
            _installerWatermark = setting.InstallerWatermark;

            _debkitAppTitle = setting.DebkitAppTitle;
            _debkitWslDistro = setting.DebkitWslDistro;
            _debkitPackageName = setting.DebkitPackageName;
            _debkitExecutable = setting.DebkitExecutable;
            _debkitLogo = setting.DebkitLogo;
            _debkitInstallDir = setting.DebkitInstallDir;
            _debkitSourceDir = setting.DebkitSourceDir;
            _debkitQualityDefault = setting.DebkitQualityDefault;
            _debkitGraphicsDefault = setting.DebkitGraphicsDefault;
            _debkitAutostart = setting.DebkitAutostart;
            _debkitAutostartDelay = setting.DebkitAutostartDelay;
            _debkitCreateDesktopIcon = setting.DebkitCreateDesktopIcon;
            _debkitCompress = setting.DebkitCompress;
            _debkitCompressLevel = setting.DebkitCompressLevel;
            _debkitDepends = setting.DebkitDepends;
            _debkitExtraArgs = setting.DebkitExtraArgs;
            _debkitWritableDirs = setting.DebkitWritableDirs;
            _debkitExclude = setting.DebkitExclude;
            _debkitCategories = setting.DebkitCategories;

            // 旧默认输出路径迁移(兼容 EditorPrefs 导入的历史数据)
            // 兼容三段历史默认值：更早的 ./Builds/、./Publish/；上一版 ./Output/Bundles/、./Output/Publish/。
            var migratedLegacyPaths = false;
            if (IsLegacyDefaultPath(_outputRoot, LegacyOutputRoot) ||
                IsLegacyDefaultPath(_outputRoot, LegacyOutputRootV2))
            {
                _outputRoot = DefaultOutputRoot;
                migratedLegacyPaths = true;
            }

            if (IsLegacyDefaultPath(_publishRoot, LegacyPublishRoot) ||
                IsLegacyDefaultPath(_publishRoot, LegacyPublishRootV2))
            {
                _publishRoot = DefaultPublishRoot;
                migratedLegacyPaths = true;
            }

            // Player 旧目录前缀迁移：./Build/ 与 ./Output/Player/ 都视为旧默认，重置为当前默认路径
            // 兼容绝对路径（历史序列化）与项目相对路径（新版默认）两种形式
            var legacyPlayerBaseAbs = NormalizePath(Application.dataPath + "/../Build/");
            var legacyPlayerBaseV2Abs = NormalizePath(Application.dataPath + "/../Output/Player/");
            var legacyPlayerBaseRel = NormalizePath("./Build/");
            var legacyPlayerBaseV2Rel = NormalizePath("./Output/Player/");
            if (!string.IsNullOrEmpty(_playerOutputPath))
            {
                var normalizedPlayer = NormalizePath(_playerOutputPath) + "/";
                if (normalizedPlayer.StartsWith(legacyPlayerBaseAbs, StringComparison.OrdinalIgnoreCase) ||
                    normalizedPlayer.StartsWith(legacyPlayerBaseV2Abs, StringComparison.OrdinalIgnoreCase) ||
                    normalizedPlayer.StartsWith(legacyPlayerBaseRel + "/", StringComparison.OrdinalIgnoreCase) ||
                    normalizedPlayer.StartsWith(legacyPlayerBaseV2Rel + "/", StringComparison.OrdinalIgnoreCase))
                {
                    _playerOutputPath = BuildConfig.GetDefaultPlayerOutputPath(_buildTarget);
                    migratedLegacyPaths = true;
                }
            }

            // 迁移旧的硬编码可执行文件名到 PlayerSettings.productName，保留用户自定义的目录
            if (MigrateLegacyExecutableName())
            {
                migratedLegacyPaths = true;
            }

            // InnoSetup 文本字段首次为空时补默认值:应用名←productName,发布者←companyName
            // (老工程 asset 无这些字段;水印为空时不在此补,运行时回退用发布者)
            if (string.IsNullOrEmpty(_installerAppName))
            {
                _installerAppName = PlayerSettings.productName ?? string.Empty;
                migratedLegacyPaths = true;
            }
            // 英文名首次为空时补 productName 作为默认(与可执行文件命名一致,纯英文路径更友好)
            if (string.IsNullOrEmpty(_installerAppEnglishName))
            {
                _installerAppEnglishName = PlayerSettings.productName ?? string.Empty;
                migratedLegacyPaths = true;
            }
            if (string.IsNullOrEmpty(_installerPublisher))
            {
                _installerPublisher = PlayerSettings.companyName ?? string.Empty;
                migratedLegacyPaths = true;
            }

            if (migratedLegacyPaths)
            {
                SaveSettings();
            }
        }

        private static void ImportEditorPrefsIntoSetting(BuildPipelineSetting setting)
        {
            var defaultConfig = BuildConfig.CreateDefault();

            var buildTargetIndex = EditorPrefs.GetInt("TEngine_BP_BuildTarget", -1);
            setting.BuildTarget = IsValidPlatformIndex(buildTargetIndex)
                ? PlatformTargets[buildTargetIndex]
                : defaultConfig.BuildTarget;

            var savedBuildPipeline = EditorPrefs.GetString("TEngine_BP_BuildPipeline", EBuildPipeline.ScriptableBuildPipeline.ToString());
            setting.BuildPipeline = Enum.TryParse(savedBuildPipeline, out EBuildPipeline buildPipeline)
                ? buildPipeline
                : EBuildPipeline.ScriptableBuildPipeline;

            setting.CompressOption = (ECompressOption)EditorPrefs.GetInt("TEngine_BP_CompressOption", (int)defaultConfig.CompressOption);
            setting.PackageVersion = EditorPrefs.GetString("TEngine_BP_PackageVersion", string.Empty);
            setting.OutputRoot = EditorPrefs.GetString("TEngine_BP_OutputRoot", DefaultOutputRoot);
            setting.EnablePublishCopy = EditorPrefs.GetBool("TEngine_BP_EnablePublishCopy", false);
            setting.PublishRoot = EditorPrefs.GetString("TEngine_BP_PublishRoot", DefaultPublishRoot);
            setting.CleanPublishPackageDirectory = EditorPrefs.GetBool("TEngine_BP_CleanPublishPackageDirectory", true);
            setting.MinimalPackage = EditorPrefs.GetBool("TEngine_BP_MinimalPackage", false);
            setting.RetainTags = EditorPrefs.GetString("TEngine_BP_RetainTags", string.Empty);
            setting.EnableSharePackRule = EditorPrefs.GetBool("TEngine_BP_EnableSharePack", true);
            setting.EnableAssetPathValidation = EditorPrefs.GetBool("TEngine_BP_EnableAssetPathValidation", true);
            setting.UseAssetDependencyDB = EditorPrefs.GetBool("TEngine_BP_UseDepDB", true);
            setting.ClearBuildCache = EditorPrefs.GetBool("TEngine_BP_ClearCache", false);
            setting.VerifyBuildingResult = EditorPrefs.GetBool("TEngine_BP_VerifyResult", true);
            setting.BuildinFileCopyOption = (EBundledCopyOption)EditorPrefs.GetInt(
                "TEngine_BP_CopyOption", (int)defaultConfig.BuildinFileCopyOption);
            setting.FileNameStyle = (EFileNameStyle)EditorPrefs.GetInt("TEngine_BP_FileNameStyle", (int)defaultConfig.FileNameStyle);
            setting.BuildHotFixDll = EditorPrefs.GetBool("TEngine_BP_BuildDll", true);
            setting.BuildPlayer = EditorPrefs.GetBool("TEngine_BP_BuildPlayer", false);

            setting.PlayerOutputPath = EditorPrefs.GetString("TEngine_BP_PlayerOutput", string.Empty);
        }

        private static void DeleteLegacyEditorPrefs()
        {
            EditorPrefs.DeleteKey("TEngine_BP_BuildTarget");
            EditorPrefs.DeleteKey("TEngine_BP_BuildPipeline");
            EditorPrefs.DeleteKey("TEngine_BP_CompressOption");
            EditorPrefs.DeleteKey("TEngine_BP_PackageVersion");
            EditorPrefs.DeleteKey("TEngine_BP_OutputRoot");
            EditorPrefs.DeleteKey("TEngine_BP_EnablePublishCopy");
            EditorPrefs.DeleteKey("TEngine_BP_PublishRoot");
            EditorPrefs.DeleteKey("TEngine_BP_CleanPublishPackageDirectory");
            EditorPrefs.DeleteKey("TEngine_BP_MinimalPackage");
            EditorPrefs.DeleteKey("TEngine_BP_RetainTags");
            EditorPrefs.DeleteKey("TEngine_BP_EnableSharePack");
            EditorPrefs.DeleteKey("TEngine_BP_EnableAssetPathValidation");
            EditorPrefs.DeleteKey("TEngine_BP_UseDepDB");
            EditorPrefs.DeleteKey("TEngine_BP_ClearCache");
            EditorPrefs.DeleteKey("TEngine_BP_VerifyResult");
            EditorPrefs.DeleteKey("TEngine_BP_CopyOption");
            EditorPrefs.DeleteKey("TEngine_BP_FileNameStyle");
            EditorPrefs.DeleteKey("TEngine_BP_BuildDll");
            EditorPrefs.DeleteKey("TEngine_BP_BuildPlayer");
            EditorPrefs.DeleteKey("TEngine_BP_PlayerPlatform");
            EditorPrefs.DeleteKey("TEngine_BP_PlayerOutput");
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
        }

        private static bool IsLegacyDefaultPath(string path, string legacyDefault)
        {
            return string.Equals(NormalizePath(path), NormalizePath(legacyDefault), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 检测并迁移旧的硬编码可执行文件名（Release_Windows.exe / Release_MacOS.app / Release_Linux）
        /// 到 PlayerSettings.productName，保留用户自定义的目录。返回是否有迁移发生。
        /// </summary>
        private bool MigrateLegacyExecutableName()
        {
            if (string.IsNullOrWhiteSpace(_playerOutputPath))
            {
                return false;
            }

            var currentName = Path.GetFileName(_playerOutputPath);
            var newName = Path.GetFileName(BuildConfig.GetDefaultPlayerOutputPath(_buildTarget));
            if (string.Equals(currentName, newName, StringComparison.Ordinal))
            {
                return false;
            }

            // 仅迁移历史上硬编码的固定名，避免覆盖用户自定义名
            if (!string.Equals(currentName, "Release_Windows.exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(currentName, "Release_MacOS.app", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(currentName, "Release_Linux", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var dir = Path.GetDirectoryName(_playerOutputPath);
            _playerOutputPath = string.IsNullOrWhiteSpace(dir)
                ? BuildConfig.GetDefaultPlayerOutputPath(_buildTarget)
                : Path.Combine(dir, newName);
            return true;
        }

        private void SaveSettings()
        {
            if (_setting == null)
            {
                return;
            }

            _setting.BuildTarget = _buildTarget;
            _setting.BuildPipeline = _buildPipeline;
            _setting.CompressOption = _compressOption;
            _setting.PackageVersion = _packageVersion;
            _setting.PackageVersionMode = _packageVersionMode;
            _setting.OutputRoot = _outputRoot;
            _setting.EnablePublishCopy = _enablePublishCopy;
            _setting.PublishRoot = _publishRoot;
            _setting.CleanPublishPackageDirectory = _cleanPublishPackageDirectory;
            _setting.MinimalPackage = _minimalPackage;
            _setting.RetainTags = _retainTags;
            _setting.EnableSharePackRule = _enableSharePackRule;
            _setting.EnableAssetPathValidation = _enableAssetPathValidation;
            _setting.UseAssetDependencyDB = _useAssetDependencyDB;
            _setting.ClearBuildCache = _clearBuildCache;
            _setting.VerifyBuildingResult = _verifyBuildingResult;
            _setting.BuildinFileCopyOption = _buildinFileCopyOption;
            _setting.FileNameStyle = _fileNameStyle;
            _setting.GenerateCatalogInOutput = _generateCatalogInOutput;
            _setting.BuildHotFixDll = _buildHotFixDll;
            _setting.BuildPlayer = _buildPlayer;
            _setting.PlayerOutputPath = _playerOutputPath;
            _setting.CleanPlayerOutputDirectory = _cleanPlayerOutputDirectory;
            _setting.BuildInstaller = _buildInstaller;
            _setting.InstallerPlatform = _installerPlatform;
            _setting.IsccPath = _isccPath;
            _setting.InstallerAppName = _installerAppName;
            _setting.InstallerAppEnglishName = _installerAppEnglishName;
            _setting.InstallerPublisher = _installerPublisher;
            _setting.InstallerPassword = _installerPassword;
            _setting.InstallerWatermark = _installerWatermark;

            _setting.DebkitAppTitle = _debkitAppTitle;
            _setting.DebkitWslDistro = _debkitWslDistro;
            _setting.DebkitPackageName = _debkitPackageName;
            _setting.DebkitExecutable = _debkitExecutable;
            _setting.DebkitLogo = _debkitLogo;
            _setting.DebkitInstallDir = _debkitInstallDir;
            _setting.DebkitSourceDir = _debkitSourceDir;
            _setting.DebkitQualityDefault = _debkitQualityDefault;
            _setting.DebkitGraphicsDefault = _debkitGraphicsDefault;
            _setting.DebkitAutostart = _debkitAutostart;
            _setting.DebkitAutostartDelay = _debkitAutostartDelay;
            _setting.DebkitCreateDesktopIcon = _debkitCreateDesktopIcon;
            _setting.DebkitCompress = _debkitCompress;
            _setting.DebkitCompressLevel = _debkitCompressLevel;
            _setting.DebkitDepends = _debkitDepends;
            _setting.DebkitExtraArgs = _debkitExtraArgs;
            _setting.DebkitWritableDirs = _debkitWritableDirs;
            _setting.DebkitExclude = _debkitExclude;
            _setting.DebkitCategories = _debkitCategories;

            EditorUtility.SetDirty(_setting);
            _settingDirty = true;
            QueueSettingSave();
        }

        private void QueueSettingSave()
        {
            _nextSettingSaveTime = EditorApplication.timeSinceStartup + 0.75d;
            if (_settingSaveQueued)
            {
                return;
            }

            _settingSaveQueued = true;
            EditorApplication.update += FlushPendingSavesWhenReady;
        }

        private void OnSettingsChanged()
        {
            if (_isLoadingSettings)
            {
                return;
            }

            NormalizeSettings();
            SaveSettings();
            RefreshCachedTexts();
            Repaint();
        }

        private void OnBuildTargetChanged()
        {
            // 平台统一后 Player 跟随目标平台；切平台时重算默认输出路径，确保可执行文件名跟随当前平台与 productName
            _playerOutputPath = BuildConfig.GetDefaultPlayerOutputPath(_buildTarget);
            OnSettingsChanged();
        }

        /// <summary>
        /// 按 PlayerSettings.productName 重新生成可执行文件名，保留用户自定义的目录。
        /// （PlayerSettings.productName 已由 UpdateSettingInspector 自动从 UpdateSetting.projectName 同步过来）
        /// </summary>
        private void SyncPlayerOutputName()
        {
            var defaultPath = BuildConfig.GetDefaultPlayerOutputPath(_buildTarget);
            var newName = Path.GetFileName(defaultPath);

            if (string.IsNullOrWhiteSpace(_playerOutputPath))
            {
                _playerOutputPath = defaultPath;
            }
            else
            {
                var dir = Path.GetDirectoryName(_playerOutputPath);
                _playerOutputPath = string.IsNullOrWhiteSpace(dir) ? defaultPath : Path.Combine(dir, newName);
            }

            OnSettingsChanged();
        }

        private void NormalizeSettings()
        {
            if (_buildPipeline != EBuildPipeline.ScriptableBuildPipeline &&
                _buildPipeline != EBuildPipeline.RawFileBuildPipeline &&
                _buildPipeline != EBuildPipeline.ArchiveFileBuildPipeline)
            {
                _buildPipeline = EBuildPipeline.ScriptableBuildPipeline;
            }

            if (Array.IndexOf(PlatformTargets, _buildTarget) < 0)
            {
                _buildTarget = GetActiveSupportedBuildTarget();
            }
        }

        #endregion

        #region 资源包列表

        private void ReloadRuntimePackageViews()
        {
            var updateSetting = _cachedUpdateSetting;
            _runtimePackages.Clear();
            if (updateSetting == null)
            {
                RefreshCachedTexts();
                return;
            }

            EnsureRuntimePackages(updateSetting);
            foreach (var runtimePackage in updateSetting.RuntimePackages)
            {
                _runtimePackages.Add(RuntimePackageView.FromEntry(runtimePackage));
            }

            _runtimePackagesDirty = false;
            RefreshCachedTexts();
        }

        private void MarkRuntimePackagesDirty()
        {
            if (_isLoadingSettings || _isSavingRuntimePackages)
            {
                return;
            }

            _runtimePackagesDirty = true;
            SaveRuntimePackageViews(flushToDisk: false);
            QueueRuntimePackageSave();
        }

        private void QueueRuntimePackageSave()
        {
            _nextRuntimePackageSaveTime = EditorApplication.timeSinceStartup + 0.75d;
            if (_runtimePackageSaveQueued)
            {
                return;
            }

            _runtimePackageSaveQueued = true;
            EditorApplication.update += FlushPendingSavesWhenReady;
        }

        private void FlushPendingSavesWhenReady()
        {
            var now = EditorApplication.timeSinceStartup;

            if (_runtimePackageSaveQueued && now >= _nextRuntimePackageSaveTime)
            {
                if (_runtimePackagesDirty)
                {
                    SaveRuntimePackageViews(flushToDisk: true);
                }

                _runtimePackageSaveQueued = false;
            }

            if (_settingSaveQueued && now >= _nextSettingSaveTime)
            {
                if (_settingDirty)
                {
                    AssetDatabase.SaveAssets();
                    _settingDirty = false;
                }

                _settingSaveQueued = false;
            }

            if (!_runtimePackageSaveQueued && !_settingSaveQueued)
            {
                EditorApplication.update -= FlushPendingSavesWhenReady;
            }
        }

        private void SaveRuntimePackageViews(bool flushToDisk)
        {
            if (_isLoadingSettings || _isSavingRuntimePackages)
            {
                return;
            }

            var updateSetting = _cachedUpdateSetting;
            if (updateSetting == null)
            {
                return;
            }

            _isSavingRuntimePackages = true;

            if (_runtimePackages.Count <= 0)
            {
                _runtimePackages.Add(RuntimePackageView.FromEntry(CreateRuntimePackageEntry("DefaultPackage", GetAssemblyPackageName())));
            }

            updateSetting.RuntimePackages = _runtimePackages
                .Select(view => view.ToEntry())
                .ToList();

            EnsureRuntimePackages(updateSetting);
            EditorUtility.SetDirty(updateSetting);
            if (flushToDisk)
            {
                AssetDatabase.SaveAssets();
                _runtimePackagesDirty = false;
            }

            RefreshCachedTexts();

            _isSavingRuntimePackages = false;
        }

        private void EnsureRuntimePackages(UpdateSetting updateSetting)
        {
            if (updateSetting.RuntimePackages == null)
            {
                updateSetting.RuntimePackages = new List<RuntimePackageEntry>();
            }

            if (updateSetting.RuntimePackages.Count <= 0)
            {
                updateSetting.RuntimePackages.Add(CreateRuntimePackageEntry("DefaultPackage", GetAssemblyPackageName()));
            }
        }

        private static RuntimePackageEntry CreateRuntimePackageEntry(string packageName, string assemblyPackageName)
        {
            var isAssembly = string.Equals(packageName, assemblyPackageName, StringComparison.Ordinal);
            return new RuntimePackageEntry
            {
                Enable = true,
                PackageName = packageName,
                InitOnStartup = true,
                UpdateManifestOnStartup = true,
                DownloadOnDemand = true,
                SaveVersion = true,
                VersionKey = GetDefaultVersionKey(packageName),
                EncryptionType = isAssembly
                    ? EncryptionType.ChaCha20
                    : EncryptionType.None,
                BuildPipeline = isAssembly
                    ? RuntimePackageBuildPipeline.ArchiveFileBuildPipeline
                    : RuntimePackageBuildPipeline.UseGlobal,
            };
        }

        private static string GetDefaultVersionKey(string packageName)
        {
            if (string.Equals(packageName, "DefaultPackage", StringComparison.Ordinal))
            {
                return "GAME_VERSION";
            }

            if (string.Equals(packageName, "CodePackage", StringComparison.Ordinal))
            {
                return "CODE_VERSION";
            }

            return $"PACKAGE_VERSION_{packageName}";
        }

        private static string GetNextPackageName(UpdateSetting updateSetting)
        {
            var index = updateSetting.RuntimePackages.Count + 1;
            var packageName = $"NewPackage{index}";
            while (updateSetting.RuntimePackages.Exists(x => x != null && string.Equals(x.PackageName, packageName, StringComparison.Ordinal)))
            {
                index++;
                packageName = $"NewPackage{index}";
            }

            return packageName;
        }

        #endregion

        #region 构建预览

        private void RefreshCachedTexts()
        {
            var config = CreateConfig();
            _cachedPackageSummary = GetBuildPackageLogText(config);
            _cachedToolbarStatus =
                $"平台: {_buildTarget}  |  版本: {GetPreviewVersionText()}  |  资源包: {_cachedPackageSummary}";
            _cachedPublishPackagePreviewText = string.Join("\n", GetCurrentPackageNames().Select(packageName =>
                $"{_publishRoot}/{PublishPlatformName}/{packageName}"));

            RebuildFlowSteps(config);
        }

        private void RebuildFlowSteps(BuildConfig config)
        {
            _flowSteps.Clear();
            var assemblyPackageName = GetAssemblyPackageName();
            var buildIncludesAssemblyPackage = SelectedBuildIncludesAssemblyPackage();

            AddFlowStep(config.BuildHotFixDll && buildIncludesAssemblyPackage,
                "同步AOT并编译热更DLL",
                $"构建 {assemblyPackageName} 前执行 SyncAOTMetadataManifest -> BuildAndCopyDlls",
                config.BuildHotFixDll
                    ? $"当前构建不包含 {assemblyPackageName}，跳过"
                    : "热更DLL未启用，跳过");

            AddFlowStep(true,
                "构建 AssetBundle",
                $"平台 {config.BuildTarget} | 版本 {GetPreviewVersionText()} | {GetPreviewBuildPackageText()}",
                string.Empty);

            AddFlowStep(config.EnablePublishCopy,
                "发布整理",
                $"拷贝到 {config.PublishRoot}/{ReleaseTools.GetRemotePlatformName(config.BuildTarget)}/{{资源包名}}",
                "发布整理未启用，跳过");

            AddFlowStep(config.MinimalPackage,
                "最小包处理",
                string.IsNullOrWhiteSpace(config.RetainTags)
                    ? "删除 StreamingAssets 中所有 .bundle，仅保留清单"
                    : $"保留 Tag [{config.RetainTags}] 的 bundle，其余删除",
                "最小包模式未启用，跳过");

            AddFlowStep(config.BuildPlayer,
                "构建 Player",
                config.CleanPlayerOutputDirectory
                    ? $"平台 {config.BuildTarget} | 输出 {config.PlayerOutputPath} | 构建前清空输出目录"
                    : $"平台 {config.BuildTarget} | 输出 {config.PlayerOutputPath}",
                "Player 构建未启用，跳过");

            // 安装包构建已与 Player 解耦:改由「安装包配置」tab 的「一键构建安装包」按钮单独触发
        }

        private void AddFlowStep(bool enabled, string title, string enabledDetail, string skippedDetail)
        {
            var order = enabled
                ? (_flowSteps.Count(x => x.Enabled) + 1).ToString()
                : "-";

            _flowSteps.Add(new FlowStepView
            {
                Order = order,
                Enabled = enabled,
                Title = enabled ? title : $"{title}（跳过）",
                Detail = enabled ? enabledDetail : skippedDetail,
            });
        }

        private string GetPreviewVersionText()
        {
            if (_packageVersionMode == PackageVersionMode.PerPackage)
            {
                if (_perPackageVersions == null || _perPackageVersions.Count <= 0)
                {
                    return "(待生成)";
                }

                return string.Join("\n", _perPackageVersions.Select(x => $"{x.PackageName}: {x.Version}"));
            }

            return string.IsNullOrWhiteSpace(_packageVersion)
                ? "(自动生成)"
                : _packageVersion;
        }

        private string GetPreviewBuildPackageText()
        {
            var packageName = GetSelectedBuildPackageName();
            return string.IsNullOrWhiteSpace(packageName)
                ? _cachedPackageSummary
                : packageName;
        }

        #endregion

        #region 配置转换

        private void ApplyConfig(BuildConfig config)
        {
            _buildTarget = config.BuildTarget;
            _buildPipeline = config.BuildPipeline;
            _compressOption = config.CompressOption;
            _packageVersion = config.PackageVersion;
            _packageVersionMode = config.PackageVersionMode;
            _outputRoot = config.OutputRoot;
            _enablePublishCopy = config.EnablePublishCopy;
            _publishRoot = config.PublishRoot;
            _cleanPublishPackageDirectory = config.CleanPublishPackageDirectory;
            _minimalPackage = config.MinimalPackage;
            _retainTags = config.RetainTags;
            _enableSharePackRule = config.EnableSharePackRule;
            _enableAssetPathValidation = config.EnableAssetPathValidation;
            _useAssetDependencyDB = config.UseAssetDependencyDB;
            _clearBuildCache = config.ClearBuildCache;
            _verifyBuildingResult = config.VerifyBuildingResult;
            _buildinFileCopyOption = config.BuildinFileCopyOption;
            _fileNameStyle = config.FileNameStyle;
            _generateCatalogInOutput = config.GenerateCatalogInOutput;
            _buildHotFixDll = config.BuildHotFixDll;
            _buildPlayer = config.BuildPlayer;
            _playerOutputPath = config.PlayerOutputPath;
            _cleanPlayerOutputDirectory = config.CleanPlayerOutputDirectory;
            _buildInstaller = config.BuildInstaller;
            _installerPlatform = config.InstallerPlatform;
            _isccPath = config.IsccPath;
            _installerAppName = config.InstallerAppName;
            _installerAppEnglishName = config.InstallerAppEnglishName;
            _installerPublisher = config.InstallerPublisher;
            _installerPassword = config.InstallerPassword;
            _installerWatermark = config.InstallerWatermark;
            NormalizeSettings();
        }

        private BuildConfig CreateConfig()
        {
            var config = new BuildConfig
            {
                BuildTarget = _buildTarget,
                BuildPipeline = _buildPipeline,
                CompressOption = _compressOption,
                PackageVersion = _packageVersion,
                PackageVersionMode = _packageVersionMode,
                PackageVersionMap = BuildPackageVersionMap(),
                OutputRoot = _outputRoot,
                EnablePublishCopy = _enablePublishCopy,
                PublishRoot = _publishRoot,
                CleanPublishPackageDirectory = _cleanPublishPackageDirectory,
                MinimalPackage = _minimalPackage,
                RetainTags = _retainTags,
                EnableSharePackRule = _enableSharePackRule,
                EnableAssetPathValidation = _enableAssetPathValidation,
                UseAssetDependencyDB = _useAssetDependencyDB,
                ClearBuildCache = _clearBuildCache,
                VerifyBuildingResult = _verifyBuildingResult,
                BuildinFileCopyOption = _buildinFileCopyOption,
                FileNameStyle = _fileNameStyle,
                GenerateCatalogInOutput = _generateCatalogInOutput,
                BuildHotFixDll = _buildHotFixDll,
                BuildPlayer = _buildPlayer,
                PlayerOutputPath = _playerOutputPath,
                CleanPlayerOutputDirectory = _cleanPlayerOutputDirectory,
                BuildInstaller = _buildInstaller,
                InstallerPlatform = _installerPlatform,
                IsccPath = _isccPath,
                InstallerAppName = _installerAppName,
                InstallerAppEnglishName = _installerAppEnglishName,
                InstallerPublisher = _installerPublisher,
                InstallerPassword = _installerPassword,
                InstallerWatermark = _installerWatermark,
                DebkitAppTitle = _debkitAppTitle,
                DebkitWslDistro = _debkitWslDistro,
                DebkitPackageName = _debkitPackageName,
                DebkitExecutable = _debkitExecutable,
                DebkitLogo = _debkitLogo,
                DebkitInstallDir = _debkitInstallDir,
                DebkitSourceDir = _debkitSourceDir,
                DebkitQualityDefault = _debkitQualityDefault,
                DebkitGraphicsDefault = _debkitGraphicsDefault,
                DebkitAutostart = _debkitAutostart,
                DebkitAutostartDelay = _debkitAutostartDelay,
                DebkitCreateDesktopIcon = _debkitCreateDesktopIcon,
                DebkitCompress = _debkitCompress,
                DebkitCompressLevel = _debkitCompressLevel,
                DebkitDepends = _debkitDepends,
                DebkitExtraArgs = _debkitExtraArgs,
                DebkitWritableDirs = _debkitWritableDirs,
                DebkitExclude = _debkitExclude,
                DebkitCategories = _debkitCategories,
            };
            return config;
        }

        /// <summary>
        /// 构建 PackageVersionMap：PerPackage 模式下从 _perPackageVersions 收集。
        /// </summary>
        private Dictionary<string, string> BuildPackageVersionMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_packageVersionMode != PackageVersionMode.PerPackage || _perPackageVersions == null)
            {
                return map;
            }

            foreach (var entry in _perPackageVersions)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.PackageName))
                {
                    continue;
                }

                var name = entry.PackageName.Trim();
                if (!map.ContainsKey(name))
                {
                    map[name] = entry.Version ?? string.Empty;
                }
            }

            return map;
        }

        private string GetBuildPackageLogText(BuildConfig config)
        {
            var updateSetting = _cachedUpdateSetting;
            var runtimePackages = updateSetting != null
                ? updateSetting.GetEnabledRuntimePackages()
                : null;

            if (runtimePackages == null || runtimePackages.Count <= 0)
            {
                return $"DefaultPackage({config.BuildPipeline})";
            }

            var packageNames = new List<string>(runtimePackages.Count);
            foreach (var runtimePackage in runtimePackages)
            {
                if (runtimePackage == null || string.IsNullOrWhiteSpace(runtimePackage.PackageName))
                {
                    continue;
                }

                packageNames.Add($"{runtimePackage.PackageName.Trim()}({GetDisplayBuildPipeline(config, runtimePackage)})");
            }

            return packageNames.Count > 0 ? string.Join(", ", packageNames) : $"DefaultPackage({config.BuildPipeline})";
        }

        private static EBuildPipeline GetDisplayBuildPipeline(BuildConfig config, RuntimePackageEntry runtimePackage)
        {
            return runtimePackage.BuildPipeline switch
            {
                RuntimePackageBuildPipeline.ScriptableBuildPipeline => EBuildPipeline.ScriptableBuildPipeline,
                RuntimePackageBuildPipeline.BuiltinBuildPipeline => EBuildPipeline.ScriptableBuildPipeline,
                RuntimePackageBuildPipeline.RawFileBuildPipeline => EBuildPipeline.RawFileBuildPipeline,
                RuntimePackageBuildPipeline.ArchiveFileBuildPipeline => EBuildPipeline.ArchiveFileBuildPipeline,
                _ => config.BuildPipeline,
            };
        }

        private List<string> GetCurrentPackageNames()
        {
            var updateSetting = _cachedUpdateSetting;
            var runtimePackages = updateSetting != null
                ? updateSetting.GetEnabledRuntimePackages()
                : null;

            if (runtimePackages == null || runtimePackages.Count <= 0)
            {
                return new List<string> { "DefaultPackage" };
            }

            return runtimePackages
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.PackageName))
                .Select(x => x.PackageName.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private bool SelectedBuildIncludesAssemblyPackage()
        {
            var selectedPackageName = GetSelectedBuildPackageName();
            if (!string.IsNullOrWhiteSpace(selectedPackageName))
            {
                return IsAssemblyPackage(selectedPackageName);
            }

            return GetCurrentPackageNames().Any(IsAssemblyPackage);
        }

        private bool IsAssemblyPackage(string packageName)
        {
            return string.Equals(packageName, GetAssemblyPackageName(), StringComparison.Ordinal);
        }

        private string GetAssemblyPackageName()
        {
            var updateSetting = _cachedUpdateSetting;
            return updateSetting != null
                ? updateSetting.GetAssemblyPackageName()
                : "CodePackage";
        }

        private string GetSelectedBuildPackageName()
        {
            if (string.Equals(_selectedBuildPackageName, AllBuildPackagesDisplayName, StringComparison.Ordinal))
            {
                return null;
            }

            if (GetCurrentPackageNames().Contains(_selectedBuildPackageName, StringComparer.Ordinal))
            {
                return _selectedBuildPackageName;
            }

            _selectedBuildPackageName = AllBuildPackagesDisplayName;
            return null;
        }

        private string[] GetBuildPackageSelectionOptions()
        {
            var options = new List<string> { AllBuildPackagesDisplayName };
            options.AddRange(GetCurrentPackageNames());
            return options.ToArray();
        }

        private void OnBuildPackageSelectionChanged()
        {
            if (!GetBuildPackageSelectionOptions().Contains(_selectedBuildPackageName))
            {
                _selectedBuildPackageName = AllBuildPackagesDisplayName;
            }

            RefreshCachedTexts();
            Repaint();
        }

        #endregion

        #region 路径与平台

        private void ChooseOutputRoot()
        {
            var selected = EditorUtility.OpenFolderPanel("选择AB输出目录", ToAbsolutePath(_outputRoot), string.Empty);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            _outputRoot = ToProjectRelativePath(selected);
            OnSettingsChanged();
        }

        private void OpenOutputRoot()
        {
            EditorUtility.RevealInFinder(ReleaseTools.GetResolvedOutputRoot(CreateConfig()));
        }

        private void ChoosePublishRoot()
        {
            var selected = EditorUtility.OpenFolderPanel("选择发布目录", ToAbsolutePath(_publishRoot), string.Empty);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            _publishRoot = ToProjectRelativePath(selected);
            OnSettingsChanged();
        }

        private void OpenPublishRoot()
        {
            EditorUtility.RevealInFinder(ReleaseTools.GetPublishOutputRoot(CreateConfig()));
        }

        private void OpenPlayerOutputPath()
        {
            var dir = Path.GetDirectoryName(ToAbsolutePath(_playerOutputPath));
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                Debug.LogWarning($"[BuildPipeline] Player 输出目录不存在：{dir}");
                return;
            }

            EditorUtility.RevealInFinder(dir);
        }

        private void ChoosePlayerOutputPath()
        {
            string directory = Path.GetDirectoryName(ToAbsolutePath(_playerOutputPath));
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Application.dataPath;
            }

            string selected = EditorUtility.SaveFilePanel(
                "选择输出路径",
                directory,
                Path.GetFileName(_playerOutputPath),
                string.Empty);

            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            _playerOutputPath = ToProjectRelativePath(selected);
            OnSettingsChanged();
        }

        private void ChooseIsccPath()
        {
            var directory = !string.IsNullOrWhiteSpace(_isccPath) && File.Exists(_isccPath)
                ? Path.GetDirectoryName(_isccPath)
                : Application.dataPath;
            var selected = EditorUtility.OpenFilePanel("选择 ISCC.exe", directory ?? string.Empty, "exe");
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            _isccPath = selected;
            OnSettingsChanged();
        }

        private void OpenIsccPath()
        {
            var iscc = InnoSetupBuilder.ResolveIscc(_isccPath);
            if (string.IsNullOrEmpty(iscc))
            {
                Debug.LogWarning("[InnoSetup] 未找到 ISCC.exe，无法打开。请先安装 Inno Setup 或在「ISCC 路径」手动指定。");
                return;
            }

            EditorUtility.RevealInFinder(iscc);
        }

        private static void OpenFileInEditor(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[InnoSetup] 文件不存在：{path}");
                return;
            }

            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, 1);
        }

        private IssInstallerConfig CreateInstallerIssConfig()
        {
            var exeName = Path.GetFileName(BuildConfig.GetDefaultPlayerOutputPath(BuildTarget.StandaloneWindows64));
            return new IssInstallerConfig
            {
                AppName = _installerAppName,
                AppEnglishName = _installerAppEnglishName,
                Publisher = _installerPublisher,
                ExeName = exeName,
                Password = _installerPassword,
                Watermark = string.IsNullOrWhiteSpace(_installerWatermark) ? _installerPublisher : _installerWatermark,
            };
        }

        private void NormalizePlayerOutputPath()
        {
            if (string.IsNullOrWhiteSpace(_playerOutputPath))
            {
                Debug.LogWarning("[BuildPipeline] Player 输出路径为空，无法规范化。");
                return;
            }

            var platformName = GetPlayerPlatformDirectoryName(_buildTarget);
            var normalized = NormalizePlatformPath(_playerOutputPath, platformName);
            if (normalized == null)
            {
                Debug.LogWarning($"[BuildPipeline] 无法识别输出路径中的平台目录：{_playerOutputPath}");
                return;
            }

            _playerOutputPath = normalized;
            OnSettingsChanged();
        }

        private static string NormalizePlatformPath(string path, string targetPlatform)
        {
            var normalized = path.Replace('\\', '/');
            var platformPattern = @"(^|/)(Windows|Linux|Android|IOS|MacOS|WebGL)(?=/|$)";
            if (Regex.IsMatch(normalized, platformPattern, RegexOptions.IgnoreCase))
            {
                var result = Regex.Replace(normalized, platformPattern, m => m.Groups[1].Value + targetPlatform, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                return path.Contains("\\") ? result.Replace('/', '\\') : result;
            }

            var releasesPattern = @"(^|/)Releases(?=/|$)";
            if (Regex.IsMatch(normalized, releasesPattern, RegexOptions.IgnoreCase))
            {
                var result = Regex.Replace(normalized, releasesPattern, m => m.Value + "/" + targetPlatform, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                return path.Contains("\\") ? result.Replace('/', '\\') : result;
            }
            return null;
        }

        private static string GetPlayerPlatformDirectoryName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64: return "Windows";
                case BuildTarget.StandaloneLinux64: return "Linux";
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "IOS";
                case BuildTarget.StandaloneOSX: return "MacOS";
                case BuildTarget.WebGL: return "WebGL";
                default: return target.ToString();
            }
        }

        private static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Application.dataPath;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            try
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
                var fullPath = Path.GetFullPath(absolutePath).Replace('\\', '/');
                if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath.Substring(projectRoot.Length);
                }

                return fullPath;
            }
            catch
            {
                return absolutePath;
            }
        }

        private static BuildTarget GetActiveSupportedBuildTarget()
        {
            var active = EditorUserBuildSettings.activeBuildTarget;
            return Array.IndexOf(PlatformTargets, active) >= 0 ? active : PlatformTargets[0];
        }

        private static bool IsValidPlatformIndex(int index)
        {
            return index >= 0 && index < PlatformTargets.Length;
        }

        #endregion

        #region Odin 数据

        private static UpdateSetting LoadUpdateSettingCached()
        {
            return Settings.UpdateSetting;
        }

        private bool HasUpdateSetting => _cachedUpdateSetting != null;
        private bool IsUpdateSettingMissing => !HasUpdateSetting;
        private bool IsPublishCopyEnabled => _enablePublishCopy;
        private bool HasBuildLogs => _buildLogs.Count > 0;

        // 安装包配置独立 tab：Windows 显示 InnoSetup 配置，Linux 显示 debkit 配置
        // 注意：仅控制 UI 显隐；Windows 的编译由 ExecuteBuild 在 Player 构建成功后触发，Linux 需到 WSL 里执行
        private bool IsWindowsInstallerPlatform => _installerPlatform == BuildTarget.StandaloneWindows64;
        private bool IsLinuxInstallerPlatform => _installerPlatform == BuildTarget.StandaloneLinux64;
        // Windows 沿用「勾选构建安装包才展开」；Linux 只要选中平台就展开配置（打包在 WSL 里执行，与本开关无关）
        private bool IsInstallerEnabled => IsWindowsInstallerPlatform && _buildInstaller;
        private bool IsDebkitPanelVisible => IsLinuxInstallerPlatform;

        private static ValueDropdownList<BuildTarget> InstallerPlatformOptions => new ValueDropdownList<BuildTarget>
        {
            { "Windows 64-bit（InnoSetup）", BuildTarget.StandaloneWindows64 },
            { "Linux 64-bit（debkit .deb）", BuildTarget.StandaloneLinux64 },
        };

        private static ValueDropdownList<IssExtractSource> IssExtractSourceOptions => new ValueDropdownList<IssExtractSource>
        {
            { "模板脚本 (setup.iss)", IssExtractSource.Template },
            { "实际编译脚本 (setup.generated.iss)", IssExtractSource.Generated },
        };

        // debkit 面板下拉项：只提供 debkit 接受的取值，避免把非法值写进 conf
        private static ValueDropdownList<string> DebkitQualityOptions => new ValueDropdownList<string>
        {
            { "none（由程序决定）", "none" },
            { "索引 0", "0" },
            { "索引 1", "1" },
            { "索引 2", "2" },
            { "索引 3", "3" },
            { "索引 4", "4" },
            { "索引 5", "5" },
        };

        private static ValueDropdownList<string> DebkitGraphicsOptions => new ValueDropdownList<string>
        {
            { "none（Unity 自动挑）", "none" },
            { "opengl（-force-glcore）", "opengl" },
            { "vulkan（-force-vulkan）", "vulkan" },
        };

        private static ValueDropdownList<string> DebkitYesNoOptions => new ValueDropdownList<string>
        {
            { "yes", "yes" },
            { "no", "no" },
        };

        private static ValueDropdownList<string> DebkitCompressOptions => new ValueDropdownList<string>
        {
            { "auto（自动挑）", "auto" },
            { "pigz（多线程 gzip，最快）", "pigz" },
            { "gzip", "gzip" },
            { "xz（最小，最慢）", "xz" },
            { "zstd（需 dpkg ≥ 1.21）", "zstd" },
            { "none（不压缩）", "none" },
        };

        /// <summary>WSL 发行版下拉项：默认 + 本机探测到的发行版（wsl.exe -l -v，缓存 10 分钟）。</summary>
        private static ValueDropdownList<string> DebkitWslDistroOptions
        {
            get
            {
                var options = new ValueDropdownList<string> { { "（默认发行版）", string.Empty } };
                try
                {
                    foreach (var distro in DebkitConfig.ListWslDistros())
                    {
                        options.Add(distro.DisplayName, distro.Name);
                    }
                }
                catch (Exception e)
                {
                    // 探测失败不影响面板使用，退化为只有「默认发行版」
                    Debug.LogWarning($"[debkit] 枚举 WSL 发行版失败：{e.Message}");
                }

                return options;
            }
        }

        private static ValueDropdownList<BuildTarget> BuildTargetOptions => new ValueDropdownList<BuildTarget>
        {
            { "Windows 64-bit", BuildTarget.StandaloneWindows64 },
            { "macOS", BuildTarget.StandaloneOSX },
            { "Linux", BuildTarget.StandaloneLinux64 },
            { "Android", BuildTarget.Android },
            { "iOS", BuildTarget.iOS },
            { "WebGL", BuildTarget.WebGL },
        };

        private static ValueDropdownList<EBuildPipeline> BuildPipelineOptions => new ValueDropdownList<EBuildPipeline>
        {
            { "ScriptableBuildPipeline (SBP)", EBuildPipeline.ScriptableBuildPipeline },
            { "RawFileBuildPipeline (原生文件)", EBuildPipeline.RawFileBuildPipeline },
            { "ArchiveFileBuildPipeline (归档文件)", EBuildPipeline.ArchiveFileBuildPipeline },
        };

        private static ValueDropdownList<ECompressOption> CompressOptions => new ValueDropdownList<ECompressOption>
        {
            { "Uncompressed (不压缩)", ECompressOption.Uncompressed },
            { "LZMA (高压缩)", ECompressOption.LZMA },
            { "LZ4 (快速压缩)", ECompressOption.LZ4 },
        };

        private static ValueDropdownList<RuntimePackageBuildPipeline> PackageBuildPipelineOptions => new ValueDropdownList<RuntimePackageBuildPipeline>
        {
            { "使用全局设置", RuntimePackageBuildPipeline.UseGlobal },
            { "ScriptableBuildPipeline (SBP)", RuntimePackageBuildPipeline.ScriptableBuildPipeline },
            { "RawFileBuildPipeline (原生文件)", RuntimePackageBuildPipeline.RawFileBuildPipeline },
            { "ArchiveFileBuildPipeline (归档文件)", RuntimePackageBuildPipeline.ArchiveFileBuildPipeline },
        };

        private static ValueDropdownList<EncryptionType> EncryptionOptions => new ValueDropdownList<EncryptionType>
        {
            { "无加密", EncryptionType.None },
            { "文件偏移加密", EncryptionType.FileOffSet },
            { "文件流加密", EncryptionType.FileStream },
            { "ChaCha20加密", EncryptionType.ChaCha20 },
        };

        private static ValueDropdownList<EBundledCopyOption> BuildinFileCopyOptions => new ValueDropdownList<EBundledCopyOption>
        {
            { "None (不拷贝)", EBundledCopyOption.None },
            { "ClearAndCopyAll (清空后拷贝全部)", EBundledCopyOption.ClearAndCopyAll },
            { "ClearAndCopyByTags (清空后按Tag拷贝)", EBundledCopyOption.ClearAndCopyByTags },
            { "OnlyCopyAll (仅拷贝全部)", EBundledCopyOption.OnlyCopyAll },
            { "OnlyCopyByTags (仅按Tag拷贝)", EBundledCopyOption.OnlyCopyByTags },
        };

        private static ValueDropdownList<EFileNameStyle> FileNameStyleOptions => new ValueDropdownList<EFileNameStyle>
        {
            { "HashName (哈希名)", EFileNameStyle.HashName },
            { "BundleName (资源包名)", EFileNameStyle.BundleName },
            { "BundleName_HashName (资源包名 + 哈希值)", EFileNameStyle.BundleName_HashName },
        };

        private static ValueDropdownList<PackageVersionMode> PackageVersionModeOptions => new ValueDropdownList<PackageVersionMode>
        {
            { "统一版本号 (所有包共用)", PackageVersionMode.Unified },
            { "独立版本号 (每包各自)", PackageVersionMode.PerPackage },
        };

        private bool IsUnifiedVersionMode => _packageVersionMode == PackageVersionMode.Unified;
        private bool IsPerPackageVersionMode => _packageVersionMode == PackageVersionMode.PerPackage;
        private int PerPackageVersionListCount => _perPackageVersions?.Count ?? 0;

        private void OnPackageVersionModeChanged()
        {
            if (_packageVersionMode == PackageVersionMode.PerPackage)
            {
                SyncPerPackageVersionList();
            }

            OnSettingsChanged();
        }

        /// <summary>
        /// 同步 _perPackageVersions 列表与当前启用的资源包列表一致。
        /// </summary>
        private void SyncPerPackageVersionList()
        {
            var packageNames = GetCurrentPackageNames();
            var existing = _perPackageVersions?.ToDictionary(x => x.PackageName, x => x.Version, StringComparer.Ordinal)
                           ?? new Dictionary<string, string>(StringComparer.Ordinal);
            var newList = new List<PerPackageVersionEntry>();
            foreach (var name in packageNames)
            {
                var version = existing.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
                    ? v
                    : BuildConfig.GetDefaultPackageVersion();
                newList.Add(new PerPackageVersionEntry { PackageName = name, Version = version });
            }

            _perPackageVersions = newList;
        }

        /// <summary>
        /// PerPackage 模式下为所有空版本号自动生成。
        /// </summary>
        private void AutoGeneratePerPackageVersions()
        {
            if (_perPackageVersions == null)
            {
                return;
            }

            var version = BuildConfig.GetDefaultPackageVersion();
            foreach (var entry in _perPackageVersions)
            {
                if (string.IsNullOrWhiteSpace(entry.Version))
                {
                    entry.Version = version;
                }
            }
        }

        [Serializable]
        private sealed class PerPackageVersionEntry
        {
            [TableColumnWidth(150)]
            [LabelText("包名")]
            [ReadOnly]
            public string PackageName = "DefaultPackage";

            [TableColumnWidth(180)]
            [LabelText("版本号")]
            [DelayedProperty]
            public string Version = "";
        }

        [Serializable]
        private sealed class RuntimePackageView
        {
            private static ValueDropdownList<RuntimePackageBuildPipeline> PackagePipelineDropdown =>
                BuildPipelineWindow.PackageBuildPipelineOptions;

            private static ValueDropdownList<EncryptionType> EncryptionDropdown =>
                BuildPipelineWindow.EncryptionOptions;

            [TableColumnWidth(45, Resizable = false)]
            [LabelText("启用")]
            [ToggleLeft]
            public bool Enable = true;

            [TableColumnWidth(150)]
            [LabelText("包名")]
            [DelayedProperty]
            public string PackageName = "DefaultPackage";

            [TableColumnWidth(180)]
            [LabelText("构建管线")]
            [ValueDropdown(nameof(PackagePipelineDropdown))]
            public RuntimePackageBuildPipeline BuildPipeline = RuntimePackageBuildPipeline.UseGlobal;

            [TableColumnWidth(120)]
            [LabelText("加密")]
            [ValueDropdown(nameof(EncryptionDropdown))]
            public EncryptionType EncryptionType = EncryptionType.None;

            [TableColumnWidth(70)]
            [LabelText("清单加密")]
            [ToggleLeft]
            public bool ManifestEncrypted = false;

            [TableColumnWidth(70)]
            [LabelText("初始化")]
            [ToggleLeft]
            public bool InitOnStartup = true;

            [TableColumnWidth(80)]
            [LabelText("更新清单")]
            [ToggleLeft]
            public bool UpdateManifestOnStartup = true;

            [TableColumnWidth(80)]
            [LabelText("下载检查")]
            [ToggleLeft]
            public bool DownloadOnDemand = true;

            [TableColumnWidth(80)]
            [LabelText("保存版本")]
            [ToggleLeft]
            public bool SaveVersion = true;

            [TableColumnWidth(150)]
            [LabelText("版本键")]
            [DelayedProperty]
            public string VersionKey = "GAME_VERSION";

            public RuntimePackageEntry ToEntry()
            {
                var packageName = string.IsNullOrWhiteSpace(PackageName) ? "DefaultPackage" : PackageName.Trim();
                var buildPipeline = BuildPipeline == RuntimePackageBuildPipeline.BuiltinBuildPipeline
                    ? RuntimePackageBuildPipeline.ScriptableBuildPipeline
                    : BuildPipeline;

                return new RuntimePackageEntry
                {
                    Enable = Enable,
                    PackageName = packageName,
                    InitOnStartup = InitOnStartup,
                    UpdateManifestOnStartup = UpdateManifestOnStartup,
                    DownloadOnDemand = DownloadOnDemand,
                    SaveVersion = SaveVersion,
                    VersionKey = string.IsNullOrWhiteSpace(VersionKey) ? GetDefaultVersionKey(packageName) : VersionKey.Trim(),
                    BuildPipeline = buildPipeline,
                    EncryptionType = EncryptionType,
                    ManifestEncrypted = ManifestEncrypted,
                };
            }

            public static RuntimePackageView FromEntry(RuntimePackageEntry entry)
            {
                if (entry == null)
                {
                    entry = CreateRuntimePackageEntry("DefaultPackage", "CodePackage");
                }

                var packageName = string.IsNullOrWhiteSpace(entry.PackageName)
                    ? "DefaultPackage"
                    : entry.PackageName.Trim();
                var buildPipeline = entry.BuildPipeline == RuntimePackageBuildPipeline.BuiltinBuildPipeline
                    ? RuntimePackageBuildPipeline.ScriptableBuildPipeline
                    : entry.BuildPipeline;

                return new RuntimePackageView
                {
                    Enable = entry.Enable,
                    PackageName = packageName,
                    InitOnStartup = entry.InitOnStartup,
                    UpdateManifestOnStartup = entry.UpdateManifestOnStartup,
                    DownloadOnDemand = entry.DownloadOnDemand,
                    SaveVersion = entry.SaveVersion,
                    VersionKey = string.IsNullOrWhiteSpace(entry.VersionKey) ? GetDefaultVersionKey(packageName) : entry.VersionKey.Trim(),
                    BuildPipeline = buildPipeline,
                    EncryptionType = entry.EncryptionType,
                    ManifestEncrypted = entry.ManifestEncrypted,
                };
            }
        }

        [Serializable]
        private sealed class FlowStepView
        {
            [TableColumnWidth(45, Resizable = false)]
            [ReadOnly]
            [LabelText("#")]
            public string Order;

            [TableColumnWidth(55, Resizable = false)]
            [ReadOnly]
            [LabelText("执行")]
            public bool Enabled;

            [TableColumnWidth(150)]
            [ReadOnly]
            [LabelText("步骤")]
            public string Title;

            [ReadOnly]
            [LabelText("说明")]
            public string Detail;
        }

        #endregion
    }
}
