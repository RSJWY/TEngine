# 会话总结索引

> 本索引只记录文档链接、关键词和一句话结论，详细内容见各总结文档。
> 新增会话总结时，按日期倒序在本文件顶部追加条目；禁止另建按日期拆分的索引文件。
> 代码研究类文档不收录在此，见 [code-research/INDEX.md](./code-research/INDEX.md)。

## 2026-08-29
- [YooAsset 2.x/3.x 差异与运行模式修复](./2026-08-29-yooasset-2-vs-3-and-playmode-fix-summary.md)
  - 关键词：EPlayMode、None=0、EditorPrefs、OfflinePlayMode、EditorSimulateMode、YOOASSET_LEGACY_API、Options API
  - 结论：记录 2.x/3.x 主要 API 和行为差异，并修复扩展工具将下拉索引误存为 3.x 枚举值的问题。
- [YooAsset 3.0.5 无兼容层迁移](./2026-08-29-yooasset-3-migration-summary.md)
  - 关键词：YooAsset 3.0.5、MigrationGuide、YOOASSET_LEGACY_API、InitializePackageAsync、ResourcePackage、IRemoteService、BundleEncryptor、Collector、ClearCacheOptions
  - 结论：运行时和编辑器 YooAsset 集成已切换到 3.x 原生 API，完整解决方案编译通过；未做真实远端下载和目标平台运行验证。

## 2026-08-28
- [Obfuz 多态 DLL 接入热更构建链路](./2026-08-28-obfuz-polymorphic-dll-hotupdate-summary.md)
  - 关键词：PolymorphicDll、GeneratePolymorphicDll、CopyAOTHotUpdateDlls、混淆产物链式、PolymorphicHotUpdateAssemblies、disableLoadStandardDll、GenerateAll注入、多态密钥冻结、补充元数据标准格式、obfuz-samples参考、BuildDLLCommand、ENABLE_OBFUZ
  - 结论：enable 不自动生成多态 dll，已在 CopyAOTHotUpdateDlls 接入混淆后转换，待换密钥/GenerateAll 注入/真机冒烟。
- [TEngine_Fantasy 分支结构与侵入性分析](./2026-08-28-tengine-fantasy-branch-analysis-summary.md)
  - 关键词：TEngine_Fantasy分支、Fantasy.Unity、GameServer、外挂式集成、AOT拆分、TEngine.AOT程序集、GameClient、ProtocolExportTool、NetworkProtocol、proto生成、OuterMessage、OuterOpcode、RouteType、共享协议源、侵入性极小、框架核心零改动、ReadMe补丁清单、SuperScrollView
  - 结论：Fantasy 是外挂式集成，TEngine 框架核心零改动（仅 8 文件 ~290 行边缘改动），协议源头在 GameServer/Tools/NetworkProtocol/，导出工具分发生成代码到两端。

## 2026-08-27
- [DGame AnimModule 迁移到 TEngine](./2026-08-27-anim-module-migration-summary.md)
  - 关键词：AnimModule迁移、PlayableGraph、AnimPlayable、AnimClip、AnimMixer、AnimNode、AnimationWrapper、MemoryObject Alloc/Dealloc、InitFromPool/RecycleToPool、Module OnInit/Shutdown、IUpdateModule、ModuleSystem反射约定注册、DGameException→Exception、DLogger→Log、GameModule.Anim访问器、TEngine.Runtime程序集、3D动画图
  - 结论：PlayableGraph 代码驱动 3D 动画图模块 9 文件迁移完成，MemoryObject/Module/异常/日志 API 全对齐，靠反射约定自动注册，GameModule 新增 Anim 访问器，静态检查零残留待编译验证。
- [DGame FrameAnimModule 与 GameObjectPoolModule 迁移到 TEngine](./2026-08-27-frame-anim-gameobject-pool-migration-summary.md)
  - 关键词：FrameAnimModule迁移、GameObjectPoolModule迁移、UIFrameRawAnimatorAgent、RawImage.sprite.texture、FrameAnimConfig替代ModelConfig、FrameSpritePoolGenerator手写Gen、MemoryObject Spawn→Alloc、Singleton OnDestroy→OnRelease、GameTimer→int timerId、ITimerModule、ModuleSystem反射约定注册、DGameLinkedList→LinkedList、GameModule.GameObjectPool访问器
  - 结论：帧动画模块（场景版+UI版+新建RawImage版）与GameObject对象池模块迁移完成并编译通过，依赖映射全部对齐TEngine，SourceGenerator改手写Gen，模块靠反射约定自动注册。
- [DGame Utility 散件迁移到 TEngine（第二梯队）](./2026-08-27-dgame-utility-migration-summary.md)
  - 关键词：EmptyGraph、NestedScrollRect、CircleLayoutGroup、UIEffectSortingOrder、UIDragListener、UIExtension、UIImageEffect、EaseUtil、EaseType、UIMat.mat、Utility.Unity、AddMonoBehaviour、Utility.Tween空壳、UIModule.UIRoot、UIModule.Instance.UICamera、同步LoadAsset、GUID一致、命名空间遮蔽、GameLogic.Utility遮蔽TEngine.Utility、Editor命名空间遮蔽UnityEditor.Editor、CS0117、CS0118
  - 结论：7 散件+EaseUtil+UIMat 材质全部迁移完成，纠正 AddMonoBehaviour 误判，Tween 空壳用 EaseUtil 绕开，修复 GameLogic.Utility 与 Editor 命名空间遮蔽两个编译坑。
- [DGame UI 组件扩展迁移到 TEngine（第一梯队）](./2026-08-27-dgame-ui-expansion-migration-summary.md)
  - 关键词：UIButton、UIImage、UIText、RichTextItem、ListPool、Pool、TEngine.Core、SetSpriteExtensions、GameModule.Resource、GameModule.Audio、DLogger→Log、ClickSound去Luban、SysSoundID、SoundConfigMgr、DOTween、AudioType二义性、UnityEditorUtil、Shader、Editor隔离、SuperScrollView未迁移、Utility散件未迁移
  - 结论：四组件+ListPool+Shader迁移完成，ListPool 公共化到 TEngine Core，ClickSound 去 Luban 改资源地址，零 DGame 残留待 Unity 编译验证。

## 2026-08-26
- [DGame ClientSaveData + DataCenter 迁移到 TEngine GameLogic](./2026-08-26-clientsavedata-datacenter-migration-summary.md)
  - 关键词：ClientSaveDataMgr、BaseClientSaveData、ClientSaveDataAttribute、DataCenterSys、DataCenterModule、PlayerData、SystemSaveData、Singleton、IUpdate、Newtonsoft.Json、UniTask、Utility.PlayerPrefs、JsonFile、PerRoleID、SaveDataVersion、OnUpgradeData、坏档备份、懒迁移、GameLogic.asmdef、RuntimeTools废弃方案
  - 结论：DGame 存档系统+数据中心迁移到 GameLogic/DataCenter/，复用 Singleton<IUpdate>，GameLogic.asmdef 新增 Newtonsoft.Json 引用。

## 2026-08-23
- [InnoSetup 安装包 iss 变量输入补全](./2026-08-23-innosetup-iss-define-input-fields-summary.md)
  - 关键词：setup.iss、IssInstallerConfig、SyncIssDefines、MyAppName、MyAppPublisher、MyAppPassword、BrandWatermark、MyAppId不回写、BuildConfig、BuildPipelineSetting、BuildPipelineWindow、双Box分区、ISCC编译区
  - 结论：iss 回写由 2 项扩到 6 项，AppId 以 iss 为准；窗口拆参数区/ISCC 区，补 4 字段输入。
- [统一构建产物到 Releases/ + InnoSetup 集成进 BuildPipelineWindow](./2026-08-23-releases-unify-innosetup-integration-summary.md)
  - 关键词：Releases、Bundles、Windows/build、Windows/setup、Publish 扁平化、FullReleaseBuilder→InnoSetupBuilder、FindIscc、D盘扫描、IsccPath 兜底、BuildInstaller、BuildPipelineWindow、Output→Releases 迁移、setup.iss
  - 结论：两套并行打包体系合并到 Releases/ 平铺；InnoSetupBuilder 迁入 TEngine 并复用 ReleaseTools，消除重复实现。

## 2026-08-16
- [Obfuz 混淆配置窗口 ObfuzConfigWindow](./2026-08-16-obfuz-config-window-summary.md)
  - 关键词：ObfuzConfigWindow、OdinEditorWindow、ObfuzSettings、健康检查、混淆通道预设、密钥/VM生成、OBFUZ_INSTALLED、TEngine/Build/混淆配置窗口
  - 结论：新增 Odin 中文混淆配置窗口，直编 ProjectSettings/Obfuz.asset，含健康检查与预设。
- [RuntimeConfigModule 加载容错与子目录配置名](./2026-08-16-runtime-config-fault-tolerance-summary.md)
  - 关键词：RuntimeConfigModule、LoadAllAsync、IsLoaded、单文件容错、跳过策略、NormalizeConfigName、子目录配置名、格式校验前置、Obfuz DTO 安全性
  - 结论：单文件失败不再中断整体加载，IsLoaded 改为流程完成语义；配置名保留子目录路径消除同名冲突。

## 2026-08-15
- [dev/release 与 Obfuz 解耦 + 构建模式面板 + pdb 可配置开关](./2026-08-15-obfuz-release-decouple-buildmode-window-summary.md)
  - 关键词：Obfuz、ENABLE_RELEASE、ENABLE_OBFUZ、OBFUZ_INSTALLED、BuildModeWindow、GeneratePdb、三宏解耦
  - 结论：dev/release 与混淆彻底解耦为三个宏，新增 Odin 构建模式面板与 pdb 开关。

## 2026-08-13
- [CodePackage 三子目录 dll 拆分构建逻辑适配](./2026-08-13-codepackage-three-subdir-dll-summary.md)
  - 关键词：CodePackage、AssetRaw/DLL、AOT/HotDll/PDB子目录、BuildDLLCommand、AOTMetadataManifest、pdb拷贝、YooAsset收集器
  - 结论：构建脚本适配 DLL 三子目录拆分，新增 pdb 拷贝并同步收集器配置。
- [工具栏 Scene Switcher 新增「注册场景」分组](./2026-08-13-toolbar-registered-scenes-summary.md)
  - 关键词：Toolbar、Scene Switcher、SceneEnumConfig、注册场景、GenericMenu、MainToolbarExtender、新旧版工具栏
  - 结论：工具栏场景切换菜单新增读取 SceneEnumConfig 的「注册场景」分组。

## 2026-08-07
- [场景加载阶段1超时双门槛修复](./2026-08-07-scene-phase1-timeout-fix-summary.md)
  - 关键词：GameSceneModule、阶段1超时、Phase1StallTimeout、Phase1AbsoluteTimeout、停滞检测、冷启动、大场景
  - 结论：阶段1固定5秒超时改为60秒停滞+180秒绝对双门槛，修复大场景误收尾。

## 2026-06-30
- [SwitchUI 场景加载进度拆分到 GameSceneModule](./2026-06-30-switchui-scene-progress-refactor-summary.md)
  - 关键词：GameSceneModule、SwitchUI、IUpdateModule、DisplayProgress、三段式进度、suspendLoad、LoadingUI废弃
  - 结论：场景加载进度状态机迁入 GameSceneModule，SwitchUI 瘦身为纯展示。
- [临时会话交接：纯数据 DataBinding 方案](./binding-handoff-2026-06-30.md)
  - 关键词：DataBinding、BindableProperty、BindableSignal、BindingScope、Editor生成器、SyncFrom/Flush、.g.cs、纯数据层
  - 结论：实现纯数据 DataBinding 基础设施与 Editor 代码生成器，静态编译通过待实测。

## 2026-06-27
- [DynamicSpawn 通用化与示例脚本](./2026-06-27-dynamic-spawn-generalization-summary.md)
  - 关键词：DynamicSceneSpawner、SpawnPointSceneSpawner、ExampleSceneGameManager、DynamicSpawnPoint、GUID保留
  - 结论：机库专用脚本通用化改名为 SpawnPointSceneSpawner 与示例 Manager。
- [打包工具 Odin 迁移与卡顿优化](./2026-06-27-odin-build-pipeline-window-summary.md)
  - 关键词：OdinEditorWindow、BuildPipelineWindow、TableList、ValueDropdown、延迟保存、日志节流、RuntimePackageView
  - 结论：打包窗口迁移 Odin 并通过延迟落盘、缓存文本、日志节流消除卡顿。

## 2026-06-04
- [AOT 元数据打包期校验与打包工具按钮](./2026-06-04-aot-metadata-manifest-build-validation.md)
  - 关键词：AOTMetadataManifest、AOTGenericReferences、SyncAOTMetadataManifest、BuildFailedException、单向校验、BuildDLLCommand
  - 结论：构建期对 AOT manifest 做单向严格校验，缺项中断构建并新增同步菜单按钮。
- [打包工具窗口「构建流程预览」面板](./2026-06-04-build-window-flow-preview.md)
  - 关键词：构建流程预览、BuildPipelineWindow、FlowStep、步骤编号、灰显跳过、零侵入
  - 结论：零侵入新增构建流程预览面板，按执行顺序显式摊开五步与跳过状态。
- [DeployConfig 控制 Debugger 调试器开关](./2026-06-04-deployconfig-debugger-toggle-summary.md)
  - 关键词：DeployConfig、Debugger、DebuggerActiveWindow、ApplyActiveWindowType、ProcedureLaunch、时序覆盖
  - 结论：DeployConfig 新增字段可打包后现场控制 Debugger 开关，空值回退 Inspector。

## 2026-06-03
- [LogViewer 独立仓库拆分与自动构建配置](./2026-06-03-logviewer-independent-repo.md)
  - 关键词：LogViewer、Git Subtree、TEngine-LogView、GitHub Actions、Release、Wails
  - 结论：LogViewer 用 Subtree 拆到独立仓库，配好 Actions 自动构建并发布 v1.0.0。
- [LogViewer 日志查看工具](./2026-06-03-logviewer-tool-summary.md)
  - 关键词：LogViewer、Go、Wails v2、富文本剥离、堆栈折叠、OnFileDrop、build.bat编码、单体exe
  - 结论：用 Go+Wails 实现日志查看工具，踩坑修复拖拽与构建脚本编码问题。
- [ScreenModule 窗口布局控制模块](./2026-06-03-screen-module-summary.md)
  - 关键词：ScreenModule、IScreenModule、WindowsScreenNative、SetWindowPos、多屏布局、ScreenConfig.json、AOT层
  - 结论：新增 Windows 多屏窗口控制模块，单屏已验证、多屏已实现但待实测。

## 2026-06-02
- [事件系统新增按事件类型批量取消监听](./2026-06-02-event-removeall-listeners.md)
  - 关键词：GameEvent、EventDispatcher、EventDelegateData、RemoveAllListeners、延迟增删、const事件ID、向后兼容
  - 结论：新增 RemoveAllListeners 凭事件 ID 清空监听，复用延迟删除机制且回调中安全。
- [轻量 JSON 配置模块与部署地址覆盖](./2026-06-02-json-config-deploy-summary.md)
  - 关键词：JsonConfigModule、NewtonsoftJsonHelper、DeployConfig、StreamingAssets/Configs、config_manifest、ProcedureLaunch、地址覆盖
  - 结论：新增 JsonConfigModule 接入 Newtonsoft，DeployConfig 可明文覆盖热更服务器地址。
- [提交部署配置 + 完善 JsonConfigModule 注释](./2026-06-02-jsonconfigmodule-comments-and-commit-summary.md)
  - 关键词：部署配置提交、JsonConfigModule注释、JsonConfigManifest、对象缓存键、LoadAllAsync、提交推送
  - 结论：分拣提交部署配置功能，并为 JsonConfigModule 补全中文注释后推送远端。
- [README 定制改动章节 + Books 子文档](./2026-06-02-readme-fork-changes-doc-summary.md)
  - 关键词：README、Fork定制改动章节、Books/Fork-定制改动说明.md、两级文档结构、维护约定
  - 结论：README 新增 Fork 定制改动章节并配套 Books 详细子文档，建立两处同步约定。
- [TouchSocket 日志桥接功能](./2026-06-02-touchsocket-logger-summary.md)
  - 关键词：TouchSocket、LoggerBase、AddUnityDebugLogger、UnityLoggerBridge、FileLogger、日志落盘、NuGet
  - 结论：TouchSocket 日志进 Unity Console，Unity/Task/UniTask 日志自动落盘，走功能分支提交。

## 2026-06-01
- [热更 PlayerPrefs 版本记录清理工具](./2026-06-01-hotupdate-playerprefs-tool-summary.md)
  - 关键词：HotUpdatePlayerPrefsTool、EditorWindow、VersionKey、PlayerPrefs、GAME_VERSION、CODE_VERSION、注册表
  - 结论：新增编辑器窗口按包清理热更版本 PlayerPrefs 记录，规避注册表缓存问题。

## 2026-05-30
- [热更新版本确认与下载流程改造](./2026-05-30-hotfix-update-confirm-flow-summary.md)
  - 关键词：ProcedureInitResources、ProcedureCreateDownloader、ConfirmedVersionUpdateKey、版本比对、确认弹窗、5秒自动确认、本地版本回退
  - 结论：版本确认弹窗前移至初始化阶段，下载阶段只查完整性，弱网可回退本地。
- [资源包构建管线与默认包来源整理](./2026-05-30-resource-package-pipeline-and-default-package-summary.md)
  - 关键词：RuntimePackageEntry、BuildPipeline、SBP、RawFile、移除BBP、GetDefaultPackageName、ResourceModuleDriverInspector
  - 结论：构建管线支持按包配置并移除 BBP，默认包来源统一收敛到 RuntimePackages。
- [资源包发布整理流程优化](./2026-05-30-resource-package-publish-workflow-summary.md)
  - 关键词：发布整理、EnablePublishCopy、PublishRoot、GetRemotePlatformName、仅执行发布整理、平台名404、BuildPipelineWindow
  - 结论：新增构建后发布整理与仅整理能力，统一远端平台目录名避免 404。
- [运行时部署配置管理方案](./2026-05-30-runtime-config-management-summary.md)
  - 关键词：RuntimeConfigSystem、DeployConfig、ResDownLoadPath、部署配置、UpdateSetting、主包侧、设计方案
  - 结论：设计主包侧轻量部署配置方案，外部 JSON 优先、UpdateSetting 兜底（未实施）。
- [XXTEA 与热更新提示恢复](./2026-05-30-xxtea-hotfix-update-summary.md)
  - 关键词：XXTEAEncryption、XXTEADecryption、EncryptionType、按包加密、UpdateStyle.Optional、可选更新恢复、LoadFromMemory
  - 结论：代码包支持 XXTEA 按包加密，恢复有本地版本可取消更新的可选流程。

## 2026-05-28
- [热更新多包与打包工具改造](./2026-05-28-hotfix-multipackage-summary.md)
  - 关键词：CodePackage、UpdateSetting、RuntimePackages、ResourceModule、ProcedureInitPackage、多包初始化、远端按包子目录、IsAssemblyPackage
  - 结论：热更 DLL 拆为独立 CodePackage，运行时已支持多包初始化/更新/下载与断网回退。

## 无日期条目
- [TEngine AOTMetaAssemblies 来源确认](./AOTMetaAssemblies-summary.md)
  - 关键词：AOTMetaAssemblies、UpdateSetting、手动维护、patchAOTAssemblies、UpdateSettingEditor、AOTGenericReferences、无自动回写
  - 结论：确认 AOTMetaAssemblies 靠手动维护，AOTGenericReferences 不会自动回写。
- [AOT 元数据热更清单改造](./aot-metadata-manifest-hotfix-summary.md)
  - 关键词：AOTMetadataManifest、ScriptableObject、CodePackage热更、BuildDLLCommand、ProcedureLoadAssembly、自动合并、回退UpdateSetting
  - 结论：新增可随 CodePackage 热更的 AOTMetadataManifest，构建与运行时优先读取。
