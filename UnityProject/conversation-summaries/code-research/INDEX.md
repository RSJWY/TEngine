# 代码研究索引

## 2026-08-26
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
