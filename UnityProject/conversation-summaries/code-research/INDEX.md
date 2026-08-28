# 代码研究索引

## 2026-08-28
- [Obfuz 多态 DLL（PolymorphicDll）机制研究](./2026-08-28-obfuz-polymorphic-dll-mechanism-research.md)
  - 关键词：PolymorphicDll、GeneratePolymorphicDll、CODEPHPY签名、Image.cpp INIT_RAW_IMAGE、disableLoadStandardDll烧入App、PolymorphicRawImage、obfuz-samples WorkWithHybridCLR、混淆产物链式、补充元数据跟随、密钥冻结参数、GenerateAll注入libil2cpp
  - 结论：enable 只管运行时支持，生成需显式调 GeneratePolymorphicDll；格式按文件头逐个识别可混用，补充元数据是否跟随由 disableLoadStandardDll 决定。
- [DGame Obfuz 密钥加载处理分析与 TEngine 借鉴方案](./2026-08-28-dgame-obfuz-secret-loading-analysis.md)（方案 B 最终实施 + 动态密钥方案部分落地）
  - 关键词：Obfuz密钥初始化实施、ObfuzRuntimeInitializer、RuntimeInitializeOnLoadMethod、AfterAssembliesLoaded、EncryptionService注入、DefaultStaticEncryptionScope、GeneratedEncryptionVirtualMachine、Resources.Load静态密钥、空值校验Log.Fatal、延迟报告、CheckFailureAndReport、ProcedureLaunch.OnEnter、LauncherMgr.ShowMessageBox、仅确认按钮Application.Quit、ENABLE_OBFUZ宏、!UNITY_EDITOR守卫、Obfuz FAQ禁止Editor跑混淆代码、EditorSimulateMode加载原始程序集、方案A已回退、ProcedureLoadAssembly恢复原状、动态密钥方案、DefaultDynamicEncryptionScope、assembliesUsingDynamicSecretKeys、EncryptionScopeProvider.GetScope、SetupDynamicSecretKeyAsync、YooAsset加载热更密钥、密钥迁移AssetRaw/DLL/Obfuz、密钥不随主包出包、密钥轮换后续editor页面、参数冻结矩阵、GameLogic动态scope、ProcedureLoadAssembly.Assembly.Load前初始化
  - 结论：静态密钥用 AfterAssembliesLoaded 初始化（!UNITY_EDITOR 守卫），失败延迟到 ProcedureLaunch 弹框退出；动态密钥密钥文件已迁移到 AssetRaw/DLL/Obfuz（YooAsset 热更资源），方案在 ProcedureLoadAssembly.Assembly.Load 前用 YooAsset 加载初始化 DefaultDynamicEncryptionScope，密钥种子替换和轮换留待后续 editor 页面。
- [DGame Obfuz 密钥加载处理分析与 TEngine 借鉴方案](./2026-08-28-dgame-obfuz-secret-loading-analysis.md)
  - 关键词：Obfuz密钥、SetUpStaticSecretKey、EncryptionService、DefaultStaticEncryptionScope、GeneratedEncryptionVirtualMachine、Resources.Load密钥、ENABLE_OBFUZ宏、ProcedureLoadAssembly、密钥初始化时机、nonObfuscatedReferencingAssemblies、obfuscateObfuzRuntime、动态密钥未用、ObfuzConfigWindow、资源加密正交、ConstEncrypt/FieldEncrypt前置条件
  - 结论：TEngine 编辑器侧 Obfuz 集成已强于 DGame，但运行时缺关键一环——ProcedureLoadAssembly 未初始化静态密钥，补上 SetUpStaticSecretKey + 引用跟随声明即完整。（已实施，见上方更新条目）
- [DGame 与 TEngine 启动加载流程对比研究](./2026-08-28-dgame-vs-tengine-startup-flow-comparison.md)
  - 关键词：启动流程对比、GameEntry、ProcedureLaunch、ProcedureInitPackage、ProcedureInitResources、ProcedureCreateDownloader、ProcedureDownloadFile、ProcedureLoadAssembly、GameStart.Entrance、GameApp.Entrance、RootModule、RuntimePackageEntry多包、本地版本回退、版本确认弹窗、PackageNote模式校验、指数退避重试、PDB缓存加载、AOTMetadataManifest动态列表、UpdateUIDefine、Obfuz密钥、LoadScene场景驱动、ShowWindow UI驱动
  - 结论：TEngine 在多资源包/弱网容错/下载重试/PDB调试/动态AOT列表上完胜；DGame 轻量直观适合单包小项目。

## 2026-08-27
- [DGame AnimModule 迁移到 TEngine 研究记录](./2026-08-27-AnimModule迁移到TEngine.md)
  - 关键词：AnimModule迁移、PlayableGraph、AnimPlayable、AnimClip、AnimMixer、AnimNode、AnimationWrapper、MemoryObject Alloc/Dealloc、InitFromPool/RecycleToPool、Module OnInit/Shutdown、IUpdateModule、ModuleSystem反射约定注册、DGameException→Exception、DLogger→Log、GameModule.Anim访问器、TEngine.Runtime程序集、3D动画图
  - 结论：PlayableGraph 代码驱动 3D 动画图模块 9 文件迁移完成，MemoryObject OnRelease 拆分为 InitFromPool+RecycleToPool，Module OnCreate/OnDestroy→OnInit/Shutdown，靠反射约定自动注册无需手动，静态检查零残留待编译验证。
- [DGame 可迁移功能评估与逐模块迁移指南](./2026-08-26-dgame-migration-evaluation-research.md)
  - 关键词：DGame迁移、UI组件扩展、UIButton、UIImage、UIText、RichTextItem、ListPool、Pool、TEngine.Core、SetSpriteExtensions、GameModule.Resource、GameModule.Audio、DLogger→Log、ClickSound去Luban、SysSoundID、SoundConfigMgr、DOTween、Shader、Editor隔离、asmdef引用、HybridCLR热更
  - 结论：第一梯队四组件迁移完成——ListPool 抽到 TEngine Core；UIButton 的 ClickSound 去 Luban 改用资源地址 string；RichTextItem 删 using DGame 天然兼容 TEngine SetSprite；零 DGame 残留，待 Unity 编译验证。
- [DGame 模块迁移到 TEngine 研究记录](./2026-08-27-DGame模块迁移到TEngine.md)
  - 关键词：FrameAnimModule迁移、GameObjectPoolModule迁移、MemoryObject、Spawn→Alloc、Singleton、OnDestroy→OnRelease、GameTimer→int timerId、ITimerModule、GameModule.Resource/Timer、FrameSpritePoolGenerator、SourceGenerator手写Gen、ModuleSystem反射约定注册、FrameAnimConfig替代ModelConfig、DGameLinkedList→LinkedList、UIFrameRawAnimatorAgent、RawImage.sprite.texture
  - 结论：FrameAnimModule（含新建 UIFrameRawAnimatorAgent）与 GameObjectPoolModule 迁移完成，依赖映射全部对齐 TEngine；FrameSpritePool 的 Roslyn 生成器改手写 Gen.cs；模块靠反射约定自动注册无需手动；GameModule 新增 GameObjectPool 访问器；待 Unity 编译验证。

## 2026-08-26
- [DGame 可迁移功能评估与逐模块迁移指南](./2026-08-26-dgame-migration-evaluation-research.md)
  - 关键词：DGame迁移评估、UI组件扩展、UIButton、UIImage、UIText、RichTextItem、SuperScrollView、LoopListView2、RedDotModule、RedDotNode、FrameAnimModule、FrameSpriteMgr、InputModule、AnimModule、GameObjectPoolModule、TextModule、GMPanel、GuideModule、迁移优先级、Luban依赖、Singleton对齐
  - 结论：盘点 DGame 全部新增模块对比 TEngine 现状，输出三梯队迁移清单；第一梯队 UI 扩展+红点+序列帧无障碍可搬，第二梯队需 API 对齐，依赖 Luban 的暂不迁移。
- [DGame ClientSaveData 存档系统深度分析](./2026-08-26-dgame-clientsavedata-research.md)
  - 关键词：ClientSaveDataMgr、BaseClientSaveData、ClientSaveDataAttribute、ClientSaveDataHelper、SystemSaveData、PlayerPrefsUtil、JsonFile、PerRoleID、SaveDataVersion、OnUpgradeData、PopulateObject、懒迁移、坏档备份、corrupt、SaveAsync、SwitchToThreadPool、key冲突校验、单例缓存
  - 结论：特性驱动注册+双存储后端+版本升级+懒迁移+坏档保护+异步写入的成熟存档框架，TEngine 完全无对应物。
- [TEngine TimerModule vs DGame GameTimer 对比研究](./2026-08-26-tengine-timer-vs-dgame-gametimer-research.md)
  - 关键词：TimerModule、GameTimer、GameTimerModule、ITimerModule、TimerHandler、DGameLinkedList、LoopCount、坏帧处理、isUnscaled、句柄、节点池、系统定时器
  - 结论：DGame GameTimer 是 TEngine TimerModule 改良版——对象引用句柄、限定循环次数、坏帧 while+10 上限防栈溢出、双向链表+节点池 O(1) 删除。

## 2026-08-22
- [UpdateSetting.BuildAddress 与 YooAsset 内置资源复制链路研究](./2026-08-22-updatesetting-buildaddress-yooasset-research.md)
  - 关键词：UpdateSetting、BuildAddress、isAutoAssetCopeToBuildAddress、YooAsset、BuildinFileRoot、GetStreamingAssetsRoot、DefaultYooFolderName、TaskCopyBuildinFiles、StreamingAssets、ReleaseTools、FullReleaseBuilder、死代码
  - 结论：BuildAddress 是死配置零调用；YooAsset 内置资源复制走 BuildinFileRoot=StreamingAssets/package（由 DefaultYooFolderName 决定），与 BuildAddress 无关。

## 2026-08-14
- [Obfuz运行时初始化与混淆范围研究](./2026-08-14-obfuz-runtime-and-scope-research.md)
  - 关键词：Obfuz、HybridCLR、EncryptionService、静态密钥、动态密钥、ObfuscationInstincts、ObfuscationTypeMapper、RegisterReflectionType、GameApp、GameLogic、GameProto、字段加密、符号混淆
  - 结论：静态/动态 Scope 初始化顺序、热更新 DLL 加载后的类型注册时机、TypeMapper 使用边界、注册代码混淆规则、业务代码与数据结构的分层混淆策略。

## 2026-08-13
- [PDB加载与打包残留提醒研究](./2026-08-13-pdb-load-and-build-warning-research.md)
  - 关键词：HybridCLR、PDB、Assembly.Load、DevelopmentBuild、BuildDLLCommand、YooAsset、打包检查、符号泄露
  - 结论：梳理 PDB 生成、拷贝和运行时加载链路，以及构建与资源打包前的残留检测方案。
- **issue3-pdb-and-build-mode** — HybridCLR pdb加载、YooAsset PackageNote、dev/release模式框架、启动匹配、obfuz骨架
- **issue3-dev-release-mode-pdb-loading** — 阶段一实施：ENABLE_OBFUZ宏判断模式、PackageNote JSON存储、启动校验、运行时pdb加载、打包前残留检测

## 2026-08-07
- [场景枚举自动生成研究](./2026-08-07-scene-enum-auto-generate-research.md)
  - 关键词：SceneType、SceneConstName、GameSceneModule、YooAsset Scenes Group、代码生成、枚举顺序稳定性
  - 结论：定位场景配置的多处手工同步问题，并设计由 Editor 扫描场景资源生成枚举与双向映射的方案。
