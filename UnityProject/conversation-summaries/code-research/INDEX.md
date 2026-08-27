# 代码研究索引

## 2026-08-27
- [DGame 可迁移功能评估与逐模块迁移指南](./2026-08-26-dgame-migration-evaluation-research.md)
  - 关键词：DGame迁移、UI组件扩展、UIButton、UIImage、UIText、RichTextItem、ListPool、Pool、TEngine.Core、SetSpriteExtensions、GameModule.Resource、GameModule.Audio、DLogger→Log、ClickSound去Luban、SysSoundID、SoundConfigMgr、DOTween、Shader、Editor隔离、asmdef引用、HybridCLR热更
  - 结论：第一梯队四组件迁移完成——ListPool 抽到 TEngine Core；UIButton 的 ClickSound 去 Luban 改用资源地址 string；RichTextItem 删 using DGame 天然兼容 TEngine SetSprite；零 DGame 残留，待 Unity 编译验证。

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
