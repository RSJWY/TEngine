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
        private const string MenuPath = "TEngine/Build/打包工具窗口";
        private const string AllBuildPackagesDisplayName = "全部资源包";

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
        private string _outputRoot = "./Builds/";

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
        private string _publishRoot = "./Publish/";

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
        private string PublishRuleText => $"{_publishRoot}/{GetPreviewProjectName()}/{PublishPlatformName}/{{资源包名}}";

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
        [ShowIf(nameof(_buildPlayer))]
        [DelayedProperty]
        [OnValueChanged(nameof(OnSettingsChanged))]
        [SerializeField]
        private string _playerOutputPath = string.Empty;

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
        [ButtonGroup("操作/Settings")]
        [Button("刷新设置", ButtonSizes.Medium)]
        private void RefreshSettingsButton()
        {
            LoadSettings();
        }

        [ButtonGroup("操作/Settings")]
        [Button("重置默认", ButtonSizes.Medium)]
        private void ResetDefaultSettingsButton()
        {
            ApplyConfig(BuildConfig.CreateDefault());
            SaveSettings();
            RefreshCachedTexts();
            AddLog("已重置打包工具默认配置");
        }

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
        [ButtonGroup("操作/Build")]
        [Button("构建 AssetBundle", ButtonSizes.Large)]
        [GUIColor(0.45f, 0.75f, 1f)]
        private void BuildAssetBundleButton()
        {
            SaveSettings();
            ExecuteBuild(false, GetSelectedBuildPackageName());
        }

        [ButtonGroup("操作/Build")]
        [Button("构建 Player", ButtonSizes.Large)]
        private void BuildPlayerButton()
        {
            SaveSettings();
            ExecuteBuildPlayerOnly();
        }

        [ButtonGroup("操作/Build")]
        [Button("打开发布目录", ButtonSizes.Large)]
        [EnableIf(nameof(IsPublishCopyEnabled))]
        private void OpenPublishRootButton()
        {
            OpenPublishRoot();
        }

        [ButtonGroup("操作/FullBuild")]
        [Button("一键构建 (AB + Player)", ButtonSizes.Large)]
        [GUIColor(0.35f, 0.95f, 0.55f)]
        private void FullBuildButton()
        {
            _buildPlayer = true;
            SaveSettings();
            ExecuteBuild(true, GetSelectedBuildPackageName());
        }

        [ButtonGroup("操作/FullBuild")]
        [Button("仅执行发布整理", ButtonSizes.Large)]
        [EnableIf(nameof(IsPublishCopyEnabled))]
        private void PublishOnlyButton()
        {
            SaveSettings();
            ExecutePublishOnly();
        }

        [TitleGroup("操作")]
        [BoxGroup("操作/构建日志")]
        [HorizontalGroup("操作/构建日志/Actions")]
        [Button("清空日志", ButtonSizes.Small)]
        [EnableIf(nameof(HasBuildLogs))]
        private void ClearBuildLogs()
        {
            _buildLogs.Clear();
        }

        [TitleGroup("操作")]
        [BoxGroup("操作/构建日志")]
        [ShowInInspector]
        [ReadOnly]
        [HideLabel]
        [ListDrawerSettings(Expanded = true, DraggableItems = false, HideAddButton = true, HideRemoveButton = true)]
        private readonly List<string> _buildLogs = new List<string>();

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
            EditorApplication.update -= FlushRuntimePackagesWhenReady;
            if (_runtimePackagesDirty)
            {
                SaveRuntimePackageViews(flushToDisk: true);
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

                AddLog("========== 构建完成 ==========");
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

            var defaultConfig = BuildConfig.CreateDefault();
            var buildTargetIndex = EditorPrefs.GetInt("TEngine_BP_BuildTarget", -1);
            _buildTarget = IsValidPlatformIndex(buildTargetIndex)
                ? PlatformTargets[buildTargetIndex]
                : GetActiveSupportedBuildTarget();

            const string buildPipelineKey = "TEngine_BP_BuildPipeline";
            var savedBuildPipeline = EditorPrefs.GetString(buildPipelineKey, EBuildPipeline.ScriptableBuildPipeline.ToString());
            if (!Enum.TryParse(savedBuildPipeline, out EBuildPipeline buildPipeline) ||
                buildPipeline == EBuildPipeline.BuiltinBuildPipeline)
            {
                buildPipeline = EBuildPipeline.ScriptableBuildPipeline;
            }

            _buildPipeline = buildPipeline;
            _compressOption = (ECompressOption)EditorPrefs.GetInt("TEngine_BP_CompressOption", (int)defaultConfig.CompressOption);
            _packageVersion = EditorPrefs.GetString("TEngine_BP_PackageVersion", string.Empty);
            _outputRoot = EditorPrefs.GetString("TEngine_BP_OutputRoot", "./Builds/");
            _enablePublishCopy = EditorPrefs.GetBool("TEngine_BP_EnablePublishCopy", false);
            _publishRoot = EditorPrefs.GetString("TEngine_BP_PublishRoot", "./Publish/");
            _cleanPublishPackageDirectory = EditorPrefs.GetBool("TEngine_BP_CleanPublishPackageDirectory", true);
            _minimalPackage = EditorPrefs.GetBool("TEngine_BP_MinimalPackage", false);
            _retainTags = EditorPrefs.GetString("TEngine_BP_RetainTags", string.Empty);
            _enableSharePackRule = EditorPrefs.GetBool("TEngine_BP_EnableSharePack", true);
            _useAssetDependencyDB = EditorPrefs.GetBool("TEngine_BP_UseDepDB", true);
            _clearBuildCache = EditorPrefs.GetBool("TEngine_BP_ClearCache", false);
            _verifyBuildingResult = EditorPrefs.GetBool("TEngine_BP_VerifyResult", true);
            _buildinFileCopyOption = (EBuildinFileCopyOption)EditorPrefs.GetInt(
                "TEngine_BP_CopyOption", (int)defaultConfig.BuildinFileCopyOption);
            _fileNameStyle = (EFileNameStyle)EditorPrefs.GetInt("TEngine_BP_FileNameStyle", (int)defaultConfig.FileNameStyle);
            _buildHotFixDll = EditorPrefs.GetBool("TEngine_BP_BuildDll", true);
            _buildPlayer = EditorPrefs.GetBool("TEngine_BP_BuildPlayer", false);

            var playerPlatformIndex = EditorPrefs.GetInt("TEngine_BP_PlayerPlatform", -1);
            _playerPlatform = IsValidPlatformIndex(playerPlatformIndex)
                ? PlatformTargets[playerPlatformIndex]
                : GetActiveSupportedBuildTarget();

            _playerOutputPath = EditorPrefs.GetString("TEngine_BP_PlayerOutput",
                BuildConfig.GetDefaultPlayerOutputPath(_playerPlatform));

            ReloadRuntimePackageViews();
            RefreshCachedTexts();
            _isLoadingSettings = false;
        }

        private void SaveSettings()
        {
            EditorPrefs.SetInt("TEngine_BP_BuildTarget", GetPlatformIndex(_buildTarget));
            EditorPrefs.SetString("TEngine_BP_BuildPipeline", _buildPipeline.ToString());
            EditorPrefs.SetInt("TEngine_BP_CompressOption", (int)_compressOption);
            EditorPrefs.SetString("TEngine_BP_PackageVersion", _packageVersion);
            EditorPrefs.SetString("TEngine_BP_OutputRoot", _outputRoot);
            EditorPrefs.SetBool("TEngine_BP_EnablePublishCopy", _enablePublishCopy);
            EditorPrefs.SetString("TEngine_BP_PublishRoot", _publishRoot);
            EditorPrefs.SetBool("TEngine_BP_CleanPublishPackageDirectory", _cleanPublishPackageDirectory);
            EditorPrefs.SetBool("TEngine_BP_MinimalPackage", _minimalPackage);
            EditorPrefs.SetString("TEngine_BP_RetainTags", _retainTags);
            EditorPrefs.SetBool("TEngine_BP_EnableSharePack", _enableSharePackRule);
            EditorPrefs.SetBool("TEngine_BP_UseDepDB", _useAssetDependencyDB);
            EditorPrefs.SetBool("TEngine_BP_ClearCache", _clearBuildCache);
            EditorPrefs.SetBool("TEngine_BP_VerifyResult", _verifyBuildingResult);
            EditorPrefs.SetInt("TEngine_BP_CopyOption", (int)_buildinFileCopyOption);
            EditorPrefs.SetInt("TEngine_BP_FileNameStyle", (int)_fileNameStyle);
            EditorPrefs.SetBool("TEngine_BP_BuildDll", _buildHotFixDll);
            EditorPrefs.SetBool("TEngine_BP_BuildPlayer", _buildPlayer);
            EditorPrefs.SetInt("TEngine_BP_PlayerPlatform", GetPlatformIndex(_playerPlatform));
            EditorPrefs.SetString("TEngine_BP_PlayerOutput", _playerOutputPath);
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
            if (string.IsNullOrWhiteSpace(_playerOutputPath))
            {
                _playerOutputPath = BuildConfig.GetDefaultPlayerOutputPath(_playerPlatform);
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
            EditorApplication.update += FlushRuntimePackagesWhenReady;
        }

        private void FlushRuntimePackagesWhenReady()
        {
            if (!_runtimePackagesDirty)
            {
                _runtimePackageSaveQueued = false;
                EditorApplication.update -= FlushRuntimePackagesWhenReady;
                return;
            }

            if (EditorApplication.timeSinceStartup < _nextRuntimePackageSaveTime)
            {
                return;
            }

            SaveRuntimePackageViews(flushToDisk: true);
            _runtimePackageSaveQueued = false;
            EditorApplication.update -= FlushRuntimePackagesWhenReady;
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
                $"{_publishRoot}/{GetPreviewProjectName()}/{PublishPlatformName}/{packageName}"));

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
                $"拷贝到 {config.PublishRoot}/{GetPreviewProjectName()}/{ReleaseTools.GetRemotePlatformName(config.BuildTarget)}/{{资源包名}}",
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

        private static string GetPreviewProjectName()
        {
            return Settings.UpdateSetting != null ? Settings.UpdateSetting.GetProjectName() : "Demo";
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

        private static int GetPlatformIndex(BuildTarget target)
        {
            var index = Array.IndexOf(PlatformTargets, target);
            return index >= 0 ? index : 0;
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
