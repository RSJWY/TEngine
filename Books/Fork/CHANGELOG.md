# Fork 改动时间线

本文件按时间记录 fork 中的重要定制改动。专题设计和使用说明见同目录下对应文档。

## 2026-08-29

- `CodePackage` 接入 YooAsset 3.0.5 `ArchiveFileBuildPipeline`，补齐 ArchiveBundle 构建、编辑器模拟、Builtin/Host/Web 加载、ChaCha20 加密解密和 `RawFileObject` 热更字节加载；修复密钥配置 Player 编译与 ChaCha20 仅输出 keystream 的错误。详见 [yooasset-3-migration.md](yooasset-3-migration.md)。
- 记录上游 TEngine 正式支持 YooAsset 3.x 后的收敛计划：优先合并上游 `ResourceModule`，统一二进制加载 API，补充归档加密往返测试并评估整包内存解密峰值。
- YooAsset 升级到 3.0.5 并完成无 `YOOASSET_LEGACY_API` 迁移；补充 2.x/3.x 差异、OfflinePlayMode 使用要求，并修复 `EPlayMode` 枚举偏移导致的编辑器运行模式误判。详见 [yooasset-3-migration.md](yooasset-3-migration.md)。

## 2026-08-28

- 热更构建链路接入 Obfuz 多态 DLL：`CopyAOTHotUpdateDlls` 在混淆后按 `polymorphicDllSettings.enable` 调 `GeneratePolymorphicDll` 把热更程序集转为多态格式（产物目录 `Obfuz/{target}/PolymorphicHotUpdateAssemblies/`），运行时加载代码零改动，补充元数据暂维持标准格式。详见 [obfuscation.md](obfuscation.md)。
- 新增 `ObfuzRuntimeInitializer`：用 `[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]` 初始化静态密钥（`#if ENABLE_OBFUZ && !UNITY_EDITOR` 守卫），失败延迟到 `ProcedureLaunch` 经 `LauncherMgr.ShowMessageBox` 弹仅含确认的对话框并退出。Editor 下不编译，规避 Obfuz FAQ 禁止 Editor 跑混淆代码的约束。详见 [obfuscation.md](obfuscation.md)。

## 2026-08-27

- 迁移 DGame `AnimModule` 到 `TEngine/Runtime/Module/AnimModule/`（框架层）：基于 PlayableGraph 的代码驱动 3D 动画图，封装 Unity 底层 Playable API（`AnimationClipPlayable`/`AnimationMixerPlayable`/`AnimationLayerMixerPlayable`），支持多层级混合/权重过渡/动态增删动画片段/手动驱动；9 个 .cs 文件自成体系无外部依赖；`MemoryObject` API 对齐（`Spawn→Alloc`/`Release→Dealloc`/`OnRelease→InitFromPool+RecycleToPool`），`Module.OnCreate/OnDestroy→OnInit/Shutdown`，`DGameException→Exception`，`DLogger→Log`，私有字段 `_小驼峰`；靠 `ModuleSystem` 反射约定自动注册（接口→实现类去 `I` 前缀）；热更 `GameModule` 新增 `Anim` 访问器。

## 2026-08-27

- 迁移 DGame `FrameAnimModule`（序列帧动画）到 `GameLogic/Module/FrameAnimModule/`：含 `FrameAnimatorAgent`（场景版 `SpriteRenderer`）、`UIFrameAnimatorAgent`（UI 版 `Image`），**新增 `UIFrameRawAnimatorAgent`**（`RawImage` 版，`rawImage.texture = sprite.texture`）；`FrameSpritePool` 的 Roslyn SourceGenerator 改手写 `FrameSpritePool.Gen.cs`；`ModelConfig` Luban 依赖改新建 `FrameAnimConfig` 结构体；`GameTimer` 对象句柄改 `ITimerModule` 的 `int timerId`；`MemoryObject` API 对齐（`Spawn→Alloc`/`Release→Dealloc`/`OnRelease→InitFromPool+RecycleToPool`）；私有字段 `_小驼峰`。
- 迁移 DGame `GameObjectPoolModule` 到 `TEngine/Runtime/Module/GameObjectPoolModule/`（框架层）：基于 YooAsset location 的异步实例化池，支持预热/容量上限/自动销毁/DontDestroy 常驻/并发建池锁/每帧空池回收；靠 `ModuleSystem` 反射约定自动注册（接口→实现类去 `I` 前缀）；`DGameLinkedList`→`LinkedList`，`AddMonoBehaviour` 内联，`DGameException`→`Exception`；热更 `GameModule` 新增 `GameObjectPool` 访问器；Editor 调试窗口菜单 `TEngine Tools/Debugger/GameObject Pool`。

## 2026-08-27

- 迁移 DGame `Utility/` 7 个 UGUI 散件到 `GameLogic/Module/UIModule/Expansion/Utility/`：`EmptyGraph`（零顶点 Graphic）、`NestedScrollRect`（嵌套滚动冲突解决）、`CircleLayoutGroup`（圆形/扇形布局）、`UIEffectSortingOrder`（特效排序同步 Canvas）、`UIDragListener`（拖拽事件聚合）、`UIExtension`（SetActive 防抖 + UniTask 缓动 + 坐标转换）、`UIImageEffect`（灰度 + 圆形遮罩二合一）。
- 搬 `EaseUtil.cs` + `EaseType` 枚举到 `Expansion/Utility/EaseUtil/`（命名空间 `DGame`→`GameLogic`），自包含 UniTask 缓动工具，TEngine 原生 `Utility.Tween` 是空壳无实现。

## 2026-08-27

- 删除 TEngine 原生 `Utility.Tween` 僵尸模块：`Assets/TEngine/Runtime/Extension/Tween/` 整目录（`Utility.Tween.cs` 845 行 + `ITweenHelper.cs` 79 行 + 2 个 .meta）。845 行代码里 82 处 `if (_tweenHelper == null) throw new GameFrameworkException("ITweenHelper is invalid.")`，全仓库无任何 `ITweenHelper` 实现类、无 `SetTweenHelper` 调用，调任何 Tween API 都会抛异常。本次迁移引入的 `GameLogic.EaseUtil` 已满足缓动需求，后续上游若补充实现可再合并回来。
- `UIDragListener` 的 `DGame.Utility.UnityUtil.AddMonoBehaviour` 改 `TEngine.Utility.Unity.AddMonoBehaviour`（第一梯队已补齐此 API，签名一致）。
- `UIExtension` 内联 `TryGetMouseDownUIPos`（从 DGame `MathUtil` 抽单方法），`UIModule.UICanvas`→`UIModule.UIRoot`、`UIModule.UICamera`→`UIModule.Instance.UICamera`。
- `UIImageEffect` 的 `GameModule.ResourceModule.LoadAsset<Material>` 改 `GameModule.Resource.LoadAsset<Material>`（同步 API 签名一致）；`UIMat.mat` 材质复制到 `Assets/AssetRaw/Materials/`，引用的 `Sprites Shader.shader` GUID 已在第一梯队迁移时保留，材质直接生效。
- `CircleLayoutGroupEditor`/`UIEffectSortingOrderEditor` 迁移到 `Assets/Editor/UIModuleExpansion/Utility/`，依赖已迁移的 `UnityEditorUtil.LayoutFrameBox`。
- 迁移来源：[DGame](https://github.com/AmaniDawn/DGame) `Assets/Scripts/HotFix/GameLogic/Module/UIModule/Expansion/Utility/` + `Assets/Scripts/HotFix/GameLogic/Module/UIModule/Editor/`。
- 编译修复（命名空间遮蔽）：① `EaseUtil.cs` 原按 DGame 习惯写为 `namespace GameLogic { public static partial class Utility { ... } }`，导致 `GameLogic.Utility` 遮蔽 `TEngine.Utility`，`UIDragListener`/`BaseClientSaveData` 报 `CS0117`；修复为 `EaseUtil`/`EaseType` 独立平级类，`GameLogic` 命名空间下不保留 `Utility` 类名。② `CircleLayoutGroupEditor`/`UIEffectSortingOrderEditor` 的 `: Editor` 报 `CS0118`（`Assets/Editor/` 下 `Editor` 是命名空间），修复为 `: UnityEditor.Editor` 完全限定名。

## 2026-08-27

- 合并 DGame `UnityUtil` 缺失方法到 `Utility.Unity`：补回组件增删（`AddMonoBehaviour`/`RmvMonoBehaviour`，TryGetComponent 去重）、子节点查找（`FindChild`/`FindChildByName`/`FindChildComponent`）、`SetLayer` 批量、`AddCustomEventListener`/`RemoveCustomEventListener`（EventTrigger 封装）、随机数/实例化/射线/正则/材质/触摸/数组创建/HashCode/分辨率等共 14 个 region；4 个 `Type` 参数泛型方法标注 `[TypeInferenceRule]`（Obfuz 混淆类型推断），需 `using UnityEngineInternal;` + `#pragma warning disable CS0618`。
- 新建 `UnityExtension.cs`（`TEngine/Runtime/Extension/Unity/`）：`AddCustomEventListener`/`RemoveCustomEventListener` 扩展方法糖衣，`UIBehaviour` 直接调用。
- JSON 体系补 `FromJsonOverwrite`：`IJsonHelper` 接口 + `NewtonsoftJsonHelper`（`PopulateObject`）+ `DefaultJsonHelper`（`JsonUtility.FromJsonOverwrite` 兜底）+ `Utility.Json` 对外 API，四件套同步。
- 迁移来源：[DGame](https://github.com/AmaniDawn/DGame) `Assets/DGame/Runtime/Core/Utility/UnityUtil.cs` + `ExtensionUtil.cs`。

## 2026-08-27

- 迁移 DGame 自研 UI 组件扩展到 `GameLogic/Module/UIModule/Expansion/`：`UIButton`（5 Extend：点击保护/缩放/长按/双击/音效）、`UIImage`（圆角/遮罩/镜像）、`UIText`（描边/渐变/阴影/字间距/顶点色/环形）、`RichTextItem`（图文混排）；`ListPool<T>`+`Pool<T>` 抽到 `TEngine/Runtime/Core/ListPool/` 公共化（`internal`→`public`，命名空间 `GameLogic`→`TEngine`）；2 个配套 Shader 迁移；Editor 脚本隔离到 `Assets/Editor/UIModuleExpansion/` 含配套 `UnityEditorUtil`。
- `UIButtonClickSoundExtend` 去 Luban 依赖：`int m_clickSoundID`（查 `SoundConfigMgr` 表）改为 `string m_clickSoundLocation`（直接资源地址），`SetClickSoundID(int)`→`SetClickSoundLocation(string)`；`BaseUIButton` 对应方法名同步。
- `UITextOutlineExtend` 的 `DGame.Utility.UnityUtil.FindObjectOfType` 改 `Object.FindObjectOfType`；`DGame.DLogger.Error` 改 `Log.Error`；`GameModule.ResourceModule`→`GameModule.Resource`；`GameModule.AudioModule`→`GameModule.Audio`。
- `RichTextItem`/`RichTextConfig` 删 `using DGame`，TEngine `SetSpriteExtensions` 是全局静态类无命名空间，`image.SetSprite(...)` 天然兼容。
- `UIButtonClickSoundExtend` 加 `using AudioType = TEngine.AudioType;` 别名消除 `TEngine.AudioType` 与 `UnityEngine.AudioType` 二义性。
- `SuperScrollView`（付费第三方插件）和 `Utility/` 散件（四组件无引用）未迁移。

## 2026-08-26

- 迁移 DGame `ClientSaveData` 存档系统与 `DataCenterSys` 数据中心到 `GameLogic/DataCenter/`：特性驱动注册、双存储后端（PlayerPrefs/JsonFile）、版本升级、坏档备份、PlayerPrefs→JsonFile 懒迁移、异步线程池写入；复用 `Singleton<T>`/`IUpdate`/`SingletonSystem` 自动驱动，避免重复实现；`GameLogic.asmdef` 新增 Newtonsoft.Json 引用。
- 整合 DGame `GameTimerModule` 改进到 `TimerModule`：底层 `List<Timer>` 改为 `GameFrameworkLinkedList<T>`（O(1) 删除 + 节点池）；坏帧处理由递归改为 `while` + `MaxBadFrameCheckCount=10` 上限，消除栈溢出风险；新增 `AddLoopCountTimer` 支持限定循环次数；旧 API（`int` 句柄、`params` 传参）全保留，业务代码零改动；`DestroySystemTimer` 补 `Dispose`/`Clear`。
- 迁移 DGame 的 `GameTickWatcher` 到独立 `RuntimeTools` 程序集（`Assets/GameScripts/RuntimeTools/`），命名空间改为 `RuntimeTools`，日志由 `DLogger.Info` 改为 `Log.Info`，补全 XML 文档注释；行为不变（构造即启动、`Restart` 清零、`ElapseTime` 返回秒）。

## 2026-08-24

- Inno Setup 改为版本管理模板与本地生成脚本分离，构建面板补充脚本状态和打开/重新生成入口；构建失败可阻断旧产物打包，ISCC 异常与超时会完整输出日志并清理进度条。
- InnoSetup 安装目录改用英文名：`setup.iss` 新增 `#define MyAppEnglishName`，`GetDefaultDir` 改用它作为默认安装目录（`MyAppName` 中文仅用于显示/图标/安装包文件名）；`InnoSetupBuilder`/`IssInstallerConfig` 增加 `AppEnglishName` 字段并回写，为空时回退用 `AppName`；`BuildConfig`/`BuildPipelineSetting`/`BuildPipelineWindow` 新增 `InstallerAppEnglishName` 持久化字段与「软件英文名」UI（默认取 `PlayerSettings.productName`），全链路同步。

## 2026-08-23

- 补全 InnoSetup 安装包变量输入：`InnoSetupBuilder` 新增 `IssInstallerConfig`，`SyncIssDefines` 回写范围由 `MyAppExeName`/`MyAppVersion` 扩展到 6 项（含 `MyAppName`/`MyAppPublisher`/`MyAppPassword`/`BrandWatermark`）；`MyAppId` 不回写、以 iss 手填值为准。窗口「安装包配置」拆为「InnoSetup 安装包」参数区与「ISCC 编译」工具区，新增应用名/发布者/安装密码/水印 4 个字段（默认取 `PlayerSettings`）。
- 统一构建产物到 `UnityProject/Releases/` 平铺：`Bundles/`、`Windows/{setup.iss,build/,setup/}`、`Linux/build/`、`Publish/`；`BuildConfig`/`BuildPipelineSetting`/`BuildPipelineWindow`/`ReleaseTools` 默认路径由 `Output/` 改为 `Releases/`，Windows/Linux Player 归 `Releases/{平台}/build/`，其它平台 Player 仍走 `Output/Player/`。
- 发布整理目录扁平化：去掉 `{项目名}` 一层，结构变为 `Releases/Publish/{平台}/{包名}/`。
- 已入库 `BuildPipelineSetting.asset` 的旧 `Output/` 路径自动迁移到 `Releases/`。
- InnoSetup 集成进 `BuildPipelineWindow`：`FullReleaseBuilder` 迁移为 `TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`（纳入 `TEngine` 命名空间），删除其重复的 YooAsset 构建实现、复用 `ReleaseTools.BuildWithConfig`；`FindIscc` 改注册表+PATH 查找；新增 `BuildInstaller`/`InstallerVersion` 字段与 UI，`ExecuteBuild` 在 Player 后按需编译安装包；删除独立窗口与 `Build/一键出安装包` 菜单。
- `.gitignore` 补 `Releases/` 产物忽略（保留 `setup.iss`），删去废弃 `/Publish/`。

## 2026-08-22

- 清理 `UpdateSetting` 死配置：删除 `BuildAddress`、`isAutoAssetCopeToBuildAddress` 字段及对应 getter，它们从未被代码读取；YooAsset 内置资源复制实际由 `BuildinFileRoot = StreamingAssets + DefaultYooFolderName(package)` 决定，与这两个字段无关。

## 2026-08-16

- `RuntimeConfigModule` 加载容错：单个配置失败（缺失、重名、格式不支持）只记录错误并跳过，不再中断整体加载；`IsLoaded` 改为"一次加载流程完成"语义，下游统一按 `TryGet` 兜底默认值。
- 配置名支持 `Configs` 相对子目录路径：缓存键保留目录、只去扩展名（如 `sub/Foo`），不同子目录同名文件不再冲突。
- 配置扩展名格式校验前置到读文件之前，避免读完内容才发现格式不支持。

## 2026-08-15

- 接入 Obfuz 代码混淆（含 `obfuz4hybridclr` 扩展）：HybridCLR 与 Obfuz 转为 `Packages/` 本地包以解决双 dnlib 冲突（移除 HybridCLR 的 dnlib，全项目共用 Obfuz 定制 dnlib），新增 `Packages/sync-*-local` 一键同步脚本，支持指定版本、自动解析最新稳定 tag 和 gitee 镜像切换。

## 2026-08-13

- `CodePackage` 资源目录拆分为 `AOT/`、`HotDll/`、`PDB/` 三子目录：`UpdateSetting` 新增可配置子路径字段，`BuildDLLCommand` 拷贝目标与 manifest 路径同步适配，新增 pdb 符号拷贝逻辑，YooAsset 收集器三分组各收集对应子目录，`.gitignore` 扩展忽略规则。

## 2026-08-07

- 修复 `GameSceneModule` 阶段 1 固定 5s 超时误杀大场景冷启动慢加载：改为停滞超时 60s + 绝对超时 180s 双门槛，冷启动 progress=0 期间不累计停滞，超时日志补全排查字段。
- 新增场景枚举自动生成工具 `SceneEnumConfig`：扫描场景资源自动生成 `SceneType`/`SceneConstName`/`SceneTypeMapping`，替代手工同步 4 处；GUID 追踪改名、枚举值顺序稳定、Odin 表格编辑，含重复 key 三层防护。
- `SceneEnumConfig` 增强：同步目录联动 YooAsset Scenes Group（避免配置脱节）、场景增删改自动提示同步、生成前校验场景在收集范围内。

## 2026-07-01

- 新增纯数据 DataBinding 运行时与 Editor 生成器，菜单和 Odin 面板统一中文化。
- DataBinding 生成代码补充公开同步函数注释，生成器面板支持单模型重新生成。
- 补充 DataBinding Attribute 用途、生成行为和限制说明。
- 移除 `DataBindFormat`，DataBinding 只同步源字段原值，展示格式由订阅方或 UI 层决定。

## 2026-06-30

- 热更清单加载按 PlayMode 分流：Editor/Offline 只读本地包，Host/Web 保留远端失败回退。
- `JsonConfigModule` 通用化为 `RuntimeConfigModule`，默认清单和轻量配置切换为 TOML。
- 场景加载进度从 `SwitchUI` / `LoadingUI` 下沉到 `GameSceneModule`，UI 降为纯展示。
- 新增 `DisplayProgress`，由 `SwitchUI` 每帧读取并渲染进度条和百分比。
- 场景加载终结顺序调整为 `回调 -> 关加载页 -> OnSceneReady`，对齐 `DynamicSceneSpawner` 契约。
- Fork 改动说明文档改为分层结构：README 概览、索引页、专题文档和时间线。

## 2026-06-27

- `HangarSceneSpawner` 通用化为 `SpawnPointSceneSpawner`。
- `HangarManager` 调整为 `ExampleSceneGameManager` 示例脚本。
- `BuildPipelineWindow` 迁移为 `OdinEditorWindow`。
- 打包工具增加构建流程预览、资源包表格延迟落盘、状态缓存和日志刷新节流。

## 2026-06-04

- `DeployConfig` 新增 `DebuggerActiveWindow` 字段。
- `Debugger` 抽出 `ApplyActiveWindowType`，支持部署配置二次覆盖调试器激活策略。

## 2026-06-03

- 新增 `Tools/LogViewer/` 桌面日志查看工具。
- 支持日志打开、拖入、级别筛选、关键词高亮、富文本标签剥离和堆栈折叠。

## 2026-06-02

- 新增 TouchSocket 与 Unity Console 的日志桥接。
- 新增 `UnityLoggerBridge`，统一落盘 Unity、Task、UniTask 日志与未观察异常。
- 新增 `JsonConfigModule`，用于从 `StreamingAssets/Configs` 加载轻量 JSON 配置。
- 新增 `DeployConfig`，支持打包后覆盖热更资源服务器地址。

## 2026-06-01

- 新增 `TEngine/HotUpdate/Package Version PlayerPrefs` 工具。
- 支持按 `RuntimePackages` 的 `VersionKey` 清理热更新版本记录。

## 2026-05-30

- 新增按包构建管线，资源包可分别选择 YooAsset 构建管线。
- 新增发布整理流程，统一发布目录与运行时远端平台名。
- 新增代码包 XXTEA 加密，仅对 `CodePackage` 等指定包应用。
- 恢复版本确认与下载流程：有本地版本可取消，无本地版本强制更新。
- 增强部署配置和运行时配置管理流程。

## 2026-05-28

- 热更 DLL 从 `DefaultPackage` 拆分到独立 `CodePackage`。
- `UpdateSetting` 引入 `RuntimePackages`，运行时初始化、清单更新和下载器创建改为按包执行。

## 未单独标注日期

- 新增 `AOTMetadataManifest`，支持 AOT 元数据清单随 `CodePackage` 热更。
- 增加 AOT 元数据打包期校验，缺少 `AOTGenericReferences.PatchedAOTAssemblyList` 必需程序集时中断构建。
- 新增 `Utility.Toml` 和默认 `TomlynTomlHelper`，提供 TOML 序列化门面。
- 新增 `ScreenModule`，支持 Windows Standalone 下控制多显示器窗口位置、大小、置顶和无边框。
- 新增 `GameEvent.RemoveAllListeners`，支持按事件 ID 批量移除监听。
