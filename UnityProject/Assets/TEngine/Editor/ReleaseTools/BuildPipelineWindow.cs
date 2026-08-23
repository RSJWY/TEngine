using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private string _cachedPackageSummary = "DefaultPackage(ScriptableBuildPipeline)";
        private string _cachedToolbarStatus = string.Empty;
        private string _cachedPublishPackagePreviewText = "DefaultPackage";

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/基础设置")]
        [LabelText("目标平台")]
        [ValueDropdown(nameof(BuildTargetOptions))]
        [OnValueChanged(nameof(OnSettingsChanged))]
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
        [HorizontalGroup("Pages/快速构建/基础设置/Version")]
        [LabelText("资源版本号")]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _packageVersion = string.Empty;

        [TabGroup("Pages", "快速构建")]
        [HorizontalGroup("Pages/快速构建/基础设置/Version", Width = 70)]
        [Button("自动", ButtonSizes.Small)]
        private void GeneratePackageVersion()
        {
            _packageVersion = BuildConfig.GetDefaultPackageVersion();
            OnSettingsChanged();
        }

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
            var updateSetting = Settings.UpdateSetting;
            if (updateSetting == null)
            {
                return;
            }

            EnsureRuntimePackages(updateSetting);
            _runtimePackages.Add(RuntimePackageView.FromEntry(CreateRuntimePackageEntry(GetNextPackageName(updateSetting))));
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
            Selection.activeObject = Settings.UpdateSetting;
            EditorGUIUtility.PingObject(Settings.UpdateSetting);
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
        private EBuildinFileCopyOption _buildinFileCopyOption = EBuildinFileCopyOption.ClearAndCopyAll;

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

        [TabGroup("Pages", "高级")]
        [FoldoutGroup("Pages/高级/热更 DLL")]
        [HorizontalGroup("Pages/高级/热更 DLL/Actions")]
        [Button("编译并拷贝热更DLL", ButtonSizes.Medium)]
        private void BuildHotFixDllNow()
        {
            BuildDLLCommand.BuildAndCopyDlls();
        }

        [TabGroup("Pages", "高级")]
        [HorizontalGroup("Pages/高级/热更 DLL/Actions")]
        [Button("同步 AOT 元数据清单", ButtonSizes.Medium)]
        private void SyncAOTMetadataManifestNow()
        {
            BuildDLLCommand.SyncAOTMetadataManifest();
        }

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/Player 设置")]
        [LabelText("构建 Player")]
        [ToggleLeft]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _buildPlayer;

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/Player 设置")]
        [LabelText("Player平台")]
        [ValueDropdown(nameof(BuildTargetOptions))]
        [ShowIf(nameof(_buildPlayer))]
        [OnValueChanged(nameof(OnPlayerPlatformChanged))]
        [SerializeField]
        private BuildTarget _playerPlatform;

        [TabGroup("Pages", "发布与Player")]
        [BoxGroup("Pages/发布与Player/Player 设置")]
        [LabelText("输出路径")]
        [InlineButton(nameof(ChoosePlayerOutputPath), "浏览")]
        [InlineButton(nameof(SyncPlayerOutputName), "同步名字")]
        [ShowIf(nameof(_buildPlayer))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _playerOutputPath = string.Empty;

        // ============ 安装包配置（独立 Tab，为后续 Linux 安装包留位） ============
        // 平台选择：决定下方显示哪类安装包配置；与「发布与Player」的 Player 平台独立，
        // 默认跟随当前 Player 平台，便于在 Windows 上独立配置 InnoSetup。
        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/平台选择")]
        [LabelText("安装包平台")]
        [ValueDropdown(nameof(InstallerPlatformOptions))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private BuildTarget _installerPlatform = BuildTarget.StandaloneWindows64;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [LabelText("构建安装包")]
        [ToggleLeft]
        [ShowIf(nameof(IsWindowsPlayerPlatform))]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private bool _buildInstaller;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [LabelText("安装包版本")]
        [Tooltip("对应 setup.iss 的 MyAppVersion，影响安装包文件名；为空则沿用 iss 现有值")]
        [ShowIf(nameof(IsInstallerEnabled))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _installerVersion = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [LabelText("ISCC 路径")]
        [Tooltip("手动指定 ISCC.exe 路径兜底；为空则自动按注册表/PATH/ProgramFiles 查找")]
        [InlineButton(nameof(ChooseIsccPath), "浏览")]
        [InlineButton(nameof(OpenIsccPath), "打开")]
        [ShowIf(nameof(IsInstallerEnabled))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _isccPath = string.Empty;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("ISCC 状态")]
        [MultiLineProperty(2)]
        [ShowIf(nameof(IsInstallerEnabled))]
        private string IsccStatusText
        {
            get
            {
                var resolved = InnoSetupBuilder.ResolveIscc(_isccPath);
                return string.IsNullOrWhiteSpace(resolved)
                    ? "未找到 ISCC.exe（请安装 Inno Setup 或在上方手动指定路径）"
                    : $"已就绪：{resolved}";
            }
        }

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("iss 脚本")]
        [ShowIf(nameof(IsInstallerEnabled))]
        private string IssScriptPath => InnoSetupBuilder.IssPath;

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("安装包输出")]
        [MultiLineProperty(2)]
        [ShowIf(nameof(IsInstallerEnabled))]
        private string InstallerOutputPreview =>
            $"主程序产物：{InnoSetupBuilder.PlayerBuildDir}\n安装包输出：{InnoSetupBuilder.InstallerOutputDir}";

        [TabGroup("Pages", "安装包配置")]
        [BoxGroup("Pages/安装包配置/InnoSetup 安装包")]
        [Button("一键构建安装包", ButtonSizes.Medium)]
        [GUIColor(0.35f, 0.95f, 0.55f)]
        [ShowIf(nameof(IsInstallerEnabled))]
        [EnableIf(nameof(IsInstallerEnabled))]
        private void BuildInstallerButton()
        {
            SaveSettings();
            ExecuteBuildInstallerOnly();
        }

        [TabGroup("Pages", "快速构建")]
        [BoxGroup("Pages/快速构建/构建流程预览")]
        [TableList(ShowIndexLabels = false, AlwaysExpanded = true, IsReadOnly = true)]
        [ListDrawerSettings(Expanded = true, DraggableItems = false, HideAddButton = true, HideRemoveButton = true)]
        [ReadOnly]
        [SerializeField]
        private List<FlowStepView> _flowSteps = new List<FlowStepView>();

        [TitleGroup("操作")]
        [LabelText("构建资源包")]
        [ValueDropdown(nameof(GetBuildPackageSelectionOptions))]
        [OnValueChanged(nameof(OnBuildPackageSelectionChanged))]
        [SerializeField]
        private string _selectedBuildPackageName = AllBuildPackagesDisplayName;

        [TitleGroup("操作")]
        [ButtonGroup("操作/MainBuild")]
        [Button("构建 AssetBundle", ButtonSizes.Large)]
        [GUIColor(0.45f, 0.75f, 1f)]
        private void BuildAssetBundleButton()
        {
            SaveSettings();
            ExecuteBuild(false, GetSelectedBuildPackageName());
        }

        [ButtonGroup("操作/MainBuild")]
        [Button("一键构建 (AB + Player)", ButtonSizes.Large)]
        [GUIColor(0.35f, 0.95f, 0.55f)]
        private void FullBuildButton()
        {
            _buildPlayer = true;
            SaveSettings();
            ExecuteBuild(true, GetSelectedBuildPackageName());
        }

        // 安装包构建已与 Player 解耦,这里单独触发;仅 Windows 且勾选「构建安装包」时可用
        [ButtonGroup("操作/MainBuild")]
        [Button("一键构建安装包", ButtonSizes.Medium)]
        [GUIColor(0.35f, 0.95f, 0.55f)]
        [EnableIf(nameof(IsInstallerEnabled))]
        private void MainBuildInstallerButton()
        {
            SaveSettings();
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

        [TitleGroup("操作")]
        [ButtonGroup("操作/MoreActions")]
        [Button("构建 Player", ButtonSizes.Large)]
        private void BuildPlayerButton()
        {
            SaveSettings();
            ExecuteBuildPlayerOnly();
        }

        [ButtonGroup("操作/MoreActions")]
        [Button("仅执行发布整理", ButtonSizes.Large)]
        [EnableIf(nameof(IsPublishCopyEnabled))]
        private void PublishOnlyButton()
        {
            SaveSettings();
            ExecutePublishOnly();
        }

        [ButtonGroup("操作/MoreActions")]
        [Button("打开发布目录", ButtonSizes.Large)]
        [EnableIf(nameof(IsPublishCopyEnabled))]
        private void OpenPublishRootButton()
        {
            OpenPublishRoot();
        }

        [TitleGroup("操作")]
        [ButtonGroup("操作/HotFix")]
        [Button("编译并拷贝热更DLL", ButtonSizes.Large)]
        private void BuildHotFixDllFromOperations()
        {
            BuildHotFixDllNow();
        }

        [ButtonGroup("操作/HotFix")]
        [Button("同步 AOT 元数据清单", ButtonSizes.Large)]
        private void SyncAOTMetadataManifestFromOperations()
        {
            SyncAOTMetadataManifestNow();
        }

        [TitleGroup("操作")]
        [ButtonGroup("操作/Settings")]
        [Button("刷新设置", ButtonSizes.Large)]
        private void RefreshSettingsButton()
        {
            LoadSettings();
        }

        [ButtonGroup("操作/Settings")]
        [Button("重置默认", ButtonSizes.Large)]
        private void ResetDefaultSettingsButton()
        {
            ApplyConfig(BuildConfig.CreateDefault());
            SaveSettings();
            RefreshCachedTexts();
            AddLog("已重置打包工具默认配置");
        }

        [TitleGroup("操作")]
        [FoldoutGroup("操作/构建日志", Expanded = false)]
        [HorizontalGroup("操作/构建日志/Actions")]
        [Button("清空日志", ButtonSizes.Small)]
        [PropertyOrder(100)]
        [EnableIf(nameof(HasBuildLogs))]
        private void ClearBuildLogs()
        {
            _buildLogs.Clear();
        }

        [TitleGroup("操作")]
        [FoldoutGroup("操作/构建日志", Expanded = false)]
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
                    ReleaseTools.BuildWithConfig(config, true, packageName);
                }
                else
                {
                    config.BuildPlayer = false;
                    ReleaseTools.BuildWithConfig(config, false, packageName);
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
                AddLog("[错误] 未找到可整理的公共版本目录。请先完成 AssetBundle 构建。");
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
            AddLog($"平台: {config.PlayerPlatform} | 输出: {config.PlayerOutputPath}");

            try
            {
                Application.logMessageReceived += OnBuildLogReceived;
                ReleaseTools.BuildImp(
                    BuildConfig.GetBuildTargetGroup(config.PlayerPlatform),
                    config.PlayerPlatform,
                    config.PlayerOutputPath
                );

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
        /// 当前仅支持 Windows(InnoSetup)；后续 Linux 安装包实现后在此按平台自动分发。
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

            if (config.InstallerPlatform != BuildTarget.StandaloneWindows64)
            {
                // 后续 Linux 安装包实现后,在此按平台分发到对应构建器
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

                AddLog("========== 编译 InnoSetup 安装包 ==========");
                InnoSetupBuilder.BuildInstaller(config.InstallerVersion, exeName, config.IsccPath);
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

            ReloadRuntimePackageViews();
            RefreshCachedTexts();
            _isLoadingSettings = false;
        }

        private void LoadFromSetting(BuildPipelineSetting setting)
        {
            _buildTarget = Array.IndexOf(PlatformTargets, setting.BuildTarget) >= 0
                ? setting.BuildTarget
                : GetActiveSupportedBuildTarget();

            _buildPipeline = setting.BuildPipeline == EBuildPipeline.BuiltinBuildPipeline
                ? EBuildPipeline.ScriptableBuildPipeline
                : setting.BuildPipeline;
            _compressOption = setting.CompressOption;
            _packageVersion = setting.PackageVersion;
            _outputRoot = string.IsNullOrWhiteSpace(setting.OutputRoot) ? DefaultOutputRoot : setting.OutputRoot;
            _enablePublishCopy = setting.EnablePublishCopy;
            _publishRoot = string.IsNullOrWhiteSpace(setting.PublishRoot) ? DefaultPublishRoot : setting.PublishRoot;
            _cleanPublishPackageDirectory = setting.CleanPublishPackageDirectory;
            _minimalPackage = setting.MinimalPackage;
            _retainTags = setting.RetainTags;
            _enableSharePackRule = setting.EnableSharePackRule;
            _useAssetDependencyDB = setting.UseAssetDependencyDB;
            _clearBuildCache = setting.ClearBuildCache;
            _verifyBuildingResult = setting.VerifyBuildingResult;
            _buildinFileCopyOption = setting.BuildinFileCopyOption;
            _fileNameStyle = setting.FileNameStyle;
            _buildHotFixDll = setting.BuildHotFixDll;
            _buildPlayer = setting.BuildPlayer;

            _playerPlatform = Array.IndexOf(PlatformTargets, setting.PlayerPlatform) >= 0
                ? setting.PlayerPlatform
                : GetActiveSupportedBuildTarget();
            _playerOutputPath = string.IsNullOrWhiteSpace(setting.PlayerOutputPath)
                ? BuildConfig.GetDefaultPlayerOutputPath(_playerPlatform)
                : setting.PlayerOutputPath;

            _buildInstaller = setting.BuildInstaller;
            _installerPlatform = Array.IndexOf(PlatformTargets, setting.InstallerPlatform) >= 0
                ? setting.InstallerPlatform
                : BuildTarget.StandaloneWindows64;
            _installerVersion = setting.InstallerVersion;
            _isccPath = setting.IsccPath;

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
            var legacyPlayerBase = NormalizePath(Application.dataPath + "/../Build/");
            var legacyPlayerBaseV2 = NormalizePath(Application.dataPath + "/../Output/Player/");
            if (!string.IsNullOrEmpty(_playerOutputPath) &&
                (NormalizePath(_playerOutputPath).StartsWith(legacyPlayerBase, StringComparison.OrdinalIgnoreCase) ||
                 NormalizePath(_playerOutputPath).StartsWith(legacyPlayerBaseV2, StringComparison.OrdinalIgnoreCase)))
            {
                _playerOutputPath = BuildConfig.GetDefaultPlayerOutputPath(_playerPlatform);
                migratedLegacyPaths = true;
            }

            // 迁移旧的硬编码可执行文件名到 PlayerSettings.productName，保留用户自定义的目录
            if (MigrateLegacyExecutableName())
            {
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
            setting.UseAssetDependencyDB = EditorPrefs.GetBool("TEngine_BP_UseDepDB", true);
            setting.ClearBuildCache = EditorPrefs.GetBool("TEngine_BP_ClearCache", false);
            setting.VerifyBuildingResult = EditorPrefs.GetBool("TEngine_BP_VerifyResult", true);
            setting.BuildinFileCopyOption = (EBuildinFileCopyOption)EditorPrefs.GetInt(
                "TEngine_BP_CopyOption", (int)defaultConfig.BuildinFileCopyOption);
            setting.FileNameStyle = (EFileNameStyle)EditorPrefs.GetInt("TEngine_BP_FileNameStyle", (int)defaultConfig.FileNameStyle);
            setting.BuildHotFixDll = EditorPrefs.GetBool("TEngine_BP_BuildDll", true);
            setting.BuildPlayer = EditorPrefs.GetBool("TEngine_BP_BuildPlayer", false);

            var playerPlatformIndex = EditorPrefs.GetInt("TEngine_BP_PlayerPlatform", -1);
            setting.PlayerPlatform = IsValidPlatformIndex(playerPlatformIndex)
                ? PlatformTargets[playerPlatformIndex]
                : defaultConfig.PlayerPlatform;
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
            var newName = Path.GetFileName(BuildConfig.GetDefaultPlayerOutputPath(_playerPlatform));
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
                ? BuildConfig.GetDefaultPlayerOutputPath(_playerPlatform)
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
            _setting.OutputRoot = _outputRoot;
            _setting.EnablePublishCopy = _enablePublishCopy;
            _setting.PublishRoot = _publishRoot;
            _setting.CleanPublishPackageDirectory = _cleanPublishPackageDirectory;
            _setting.MinimalPackage = _minimalPackage;
            _setting.RetainTags = _retainTags;
            _setting.EnableSharePackRule = _enableSharePackRule;
            _setting.UseAssetDependencyDB = _useAssetDependencyDB;
            _setting.ClearBuildCache = _clearBuildCache;
            _setting.VerifyBuildingResult = _verifyBuildingResult;
            _setting.BuildinFileCopyOption = _buildinFileCopyOption;
            _setting.FileNameStyle = _fileNameStyle;
            _setting.BuildHotFixDll = _buildHotFixDll;
            _setting.BuildPlayer = _buildPlayer;
            _setting.PlayerPlatform = _playerPlatform;
            _setting.PlayerOutputPath = _playerOutputPath;
            _setting.BuildInstaller = _buildInstaller;
            _setting.InstallerPlatform = _installerPlatform;
            _setting.InstallerVersion = _installerVersion;
            _setting.IsccPath = _isccPath;

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

        private void OnPlayerPlatformChanged()
        {
            // 切平台时重新生成输出路径，确保可执行文件名跟随当前平台与 productName
            _playerOutputPath = BuildConfig.GetDefaultPlayerOutputPath(_playerPlatform);
            OnSettingsChanged();
        }

        /// <summary>
        /// 按 PlayerSettings.productName 重新生成可执行文件名，保留用户自定义的目录。
        /// （PlayerSettings.productName 已由 UpdateSettingInspector 自动从 UpdateSetting.projectName 同步过来）
        /// </summary>
        private void SyncPlayerOutputName()
        {
            var defaultPath = BuildConfig.GetDefaultPlayerOutputPath(_playerPlatform);
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
            if (_buildPipeline == EBuildPipeline.BuiltinBuildPipeline)
            {
                _buildPipeline = EBuildPipeline.ScriptableBuildPipeline;
            }

            if (Array.IndexOf(PlatformTargets, _buildTarget) < 0)
            {
                _buildTarget = GetActiveSupportedBuildTarget();
            }

            if (Array.IndexOf(PlatformTargets, _playerPlatform) < 0)
            {
                _playerPlatform = GetActiveSupportedBuildTarget();
            }
        }

        #endregion

        #region 资源包列表

        private void ReloadRuntimePackageViews()
        {
            var updateSetting = Settings.UpdateSetting;
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

            var updateSetting = Settings.UpdateSetting;
            if (updateSetting == null)
            {
                return;
            }

            _isSavingRuntimePackages = true;

            if (_runtimePackages.Count <= 0)
            {
                _runtimePackages.Add(RuntimePackageView.FromEntry(CreateRuntimePackageEntry("DefaultPackage")));
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

        private static void EnsureRuntimePackages(UpdateSetting updateSetting)
        {
            if (updateSetting.RuntimePackages == null)
            {
                updateSetting.RuntimePackages = new List<RuntimePackageEntry>();
            }

            if (updateSetting.RuntimePackages.Count <= 0)
            {
                updateSetting.RuntimePackages.Add(CreateRuntimePackageEntry("DefaultPackage"));
            }
        }

        private static RuntimePackageEntry CreateRuntimePackageEntry(string packageName)
        {
            return new RuntimePackageEntry
            {
                Enable = true,
                PackageName = packageName,
                InitOnStartup = true,
                UpdateManifestOnStartup = true,
                DownloadOnDemand = true,
                SaveVersion = true,
                VersionKey = GetDefaultVersionKey(packageName),
                EncryptionType = Settings.UpdateSetting != null &&
                                 string.Equals(packageName, Settings.UpdateSetting.AssemblyPackageName, StringComparison.Ordinal)
                    ? EncryptionType.XXTEA
                    : EncryptionType.None,
                BuildPipeline = RuntimePackageBuildPipeline.UseGlobal,
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
                $"平台 {config.PlayerPlatform} | 输出 {config.PlayerOutputPath}",
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
            _outputRoot = config.OutputRoot;
            _enablePublishCopy = config.EnablePublishCopy;
            _publishRoot = config.PublishRoot;
            _cleanPublishPackageDirectory = config.CleanPublishPackageDirectory;
            _minimalPackage = config.MinimalPackage;
            _retainTags = config.RetainTags;
            _enableSharePackRule = config.EnableSharePackRule;
            _useAssetDependencyDB = config.UseAssetDependencyDB;
            _clearBuildCache = config.ClearBuildCache;
            _verifyBuildingResult = config.VerifyBuildingResult;
            _buildinFileCopyOption = config.BuildinFileCopyOption;
            _fileNameStyle = config.FileNameStyle;
            _buildHotFixDll = config.BuildHotFixDll;
            _buildPlayer = config.BuildPlayer;
            _playerPlatform = config.PlayerPlatform;
            _playerOutputPath = config.PlayerOutputPath;
            _buildInstaller = config.BuildInstaller;
            _installerPlatform = config.InstallerPlatform;
            _installerVersion = config.InstallerVersion;
            _isccPath = config.IsccPath;
            NormalizeSettings();
        }

        private BuildConfig CreateConfig()
        {
            return new BuildConfig
            {
                BuildTarget = _buildTarget,
                BuildPipeline = _buildPipeline,
                CompressOption = _compressOption,
                PackageVersion = _packageVersion,
                OutputRoot = _outputRoot,
                EnablePublishCopy = _enablePublishCopy,
                PublishRoot = _publishRoot,
                CleanPublishPackageDirectory = _cleanPublishPackageDirectory,
                MinimalPackage = _minimalPackage,
                RetainTags = _retainTags,
                EnableSharePackRule = _enableSharePackRule,
                UseAssetDependencyDB = _useAssetDependencyDB,
                ClearBuildCache = _clearBuildCache,
                VerifyBuildingResult = _verifyBuildingResult,
                BuildinFileCopyOption = _buildinFileCopyOption,
                FileNameStyle = _fileNameStyle,
                BuildHotFixDll = _buildHotFixDll,
                BuildPlayer = _buildPlayer,
                PlayerPlatform = _playerPlatform,
                PlayerOutputPath = _playerOutputPath,
                BuildInstaller = _buildInstaller,
                InstallerPlatform = _installerPlatform,
                InstallerVersion = _installerVersion,
                IsccPath = _isccPath,
            };
        }

        private static string GetBuildPackageLogText(BuildConfig config)
        {
            var runtimePackages = Settings.UpdateSetting != null
                ? Settings.UpdateSetting.GetEnabledRuntimePackages()
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
                _ => config.BuildPipeline,
            };
        }

        private List<string> GetCurrentPackageNames()
        {
            var runtimePackages = Settings.UpdateSetting != null
                ? Settings.UpdateSetting.GetEnabledRuntimePackages()
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

        private static bool IsAssemblyPackage(string packageName)
        {
            return string.Equals(packageName, GetAssemblyPackageName(), StringComparison.Ordinal);
        }

        private static string GetAssemblyPackageName()
        {
            return Settings.UpdateSetting != null
                ? Settings.UpdateSetting.GetAssemblyPackageName()
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

        private void ChoosePlayerOutputPath()
        {
            string directory = Path.GetDirectoryName(_playerOutputPath);
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

            _playerOutputPath = selected;
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

        private bool HasUpdateSetting => Settings.UpdateSetting != null;
        private bool IsUpdateSettingMissing => !HasUpdateSetting;
        private bool IsPublishCopyEnabled => _enablePublishCopy;
        private bool HasBuildLogs => _buildLogs.Count > 0;

        // 安装包配置独立 tab：Windows 平台下显示 InnoSetup 配置，后续可扩展 Linux 安装包
        // 注意：仅控制 UI 显隐；实际编译仍由 ExecuteBuild 在 Player 构建成功后按 Windows 平台触发
        private bool IsWindowsPlayerPlatform => _installerPlatform == BuildTarget.StandaloneWindows64;
        private bool IsInstallerEnabled => IsWindowsPlayerPlatform && _buildInstaller;

        private static ValueDropdownList<BuildTarget> InstallerPlatformOptions => new ValueDropdownList<BuildTarget>
        {
            { "Windows 64-bit", BuildTarget.StandaloneWindows64 },
            // 后续 Linux 安装包配置扩展位
        };

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
        };

        private static ValueDropdownList<EncryptionType> EncryptionOptions => new ValueDropdownList<EncryptionType>
        {
            { "无加密", EncryptionType.None },
            { "文件偏移加密", EncryptionType.FileOffSet },
            { "文件流加密", EncryptionType.FileStream },
            { "XXTEA加密", EncryptionType.XXTEA },
        };

        private static ValueDropdownList<EBuildinFileCopyOption> BuildinFileCopyOptions => new ValueDropdownList<EBuildinFileCopyOption>
        {
            { "None (不拷贝)", EBuildinFileCopyOption.None },
            { "ClearAndCopyAll (清空后拷贝全部)", EBuildinFileCopyOption.ClearAndCopyAll },
            { "ClearAndCopyByTags (清空后按Tag拷贝)", EBuildinFileCopyOption.ClearAndCopyByTags },
            { "OnlyCopyAll (仅拷贝全部)", EBuildinFileCopyOption.OnlyCopyAll },
            { "OnlyCopyByTags (仅按Tag拷贝)", EBuildinFileCopyOption.OnlyCopyByTags },
        };

        private static ValueDropdownList<EFileNameStyle> FileNameStyleOptions => new ValueDropdownList<EFileNameStyle>
        {
            { "HashName (哈希名)", EFileNameStyle.HashName },
            { "BundleName (资源包名)", EFileNameStyle.BundleName },
            { "BundleName_HashName (资源包名 + 哈希值)", EFileNameStyle.BundleName_HashName },
        };

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
                };
            }

            public static RuntimePackageView FromEntry(RuntimePackageEntry entry)
            {
                if (entry == null)
                {
                    entry = CreateRuntimePackageEntry("DefaultPackage");
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
