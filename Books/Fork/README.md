# Fork 定制改动总览

本目录记录当前 fork 相对上游 [ALEXTANGXIAO/TEngine](https://github.com/ALEXTANGXIAO/TEngine) 的定制改动。

文档按用途分层维护：

- `README.md`：只放总览和导航，适合快速了解 fork 改了哪些方向。
- `CHANGELOG.md`：按时间记录新增、调整和修复，适合查看最近变更。
- 专题文档：按系统归档详细设计、使用方式、关键文件和注意事项。

## 改动索引

| 主题 | 说明 | 详细文档 |
| --- | --- | --- |
| 日志系统 | TouchSocket 日志桥接、Unity 日志落盘、日志查看工具 | [logging.md](logging.md) |
| 事件系统 | 按事件 ID 批量移除监听 | [event-system.md](event-system.md) |
| 数据绑定 | 纯数据 DataBinding 运行时、生成器和 Odin 面板 | [data-binding.md](data-binding.md) |
| 运行时配置 | RuntimeConfig、DeployConfig、TOML/JSON 轻量配置 | [runtime-config.md](runtime-config.md) |
| 热更新 | CodePackage、XXTEA、版本确认、AOT 元数据 | [hot-update.md](hot-update.md) |
| 资源打包 | 按包构建、发布整理、打包工具优化 | [resource-build.md](resource-build.md) |
| 场景系统 | DynamicSpawn 通用化、GameSceneModule 进度下沉 | [scene-system.md](scene-system.md) |
| 窗口管理 | Windows Standalone 窗口布局控制 | [window-management.md](window-management.md) |
| 代码混淆 | Obfuz 接入、dnlib 冲突解决、本地包同步脚本、运行时静态密钥初始化、多态 DLL 热更产物 | [obfuscation.md](obfuscation.md) |
| 运行时工具 | `GameTickWatcher` 逻辑计时器（独立 `RuntimeTools` 程序集） | [runtime-tools.md](runtime-tools.md) |
| 计时器模块 | `TimerModule` 链表化、坏帧安全、限定循环次数 | [timer-module.md](timer-module.md) |
| 存档与数据中心 | `ClientSaveDataMgr` 存档框架、`DataCenterSys` 玩家数据中枢 | [save-data.md](save-data.md) |
| UI 组件扩展 | `UIButton`/`UIImage`/`UIText`/`RichTextItem` + `ListPool` 公共化 | [ui-expansion.md](ui-expansion.md) |
| 运行时工具合并 | `Utility.Unity` 补齐组件增删/子节点查找/Layer/EventTrigger/物理/分辨率等；JSON 补 `FromJsonOverwrite` | [utility-merge.md](utility-merge.md) |
| 帧动画模块 | 序列帧动画（场景版+UI版+RawImage版），手写替代 SourceGenerator | [frame-anim.md](frame-anim.md) |
| GameObject 对象池 | 基于 YooAsset location 的异步实例化池，预热/回收/自动销毁 | [game-object-pool.md](game-object-pool.md) |
| 动画模块 | 基于 PlayableGraph 的代码驱动 3D 动画图，多层级混合/权重过渡 | [anim-module.md](anim-module.md) |

## 最近重点

- 热更构建链路接入 Obfuz 多态 DLL：`CopyAOTHotUpdateDlls` 在混淆后按 `polymorphicDllSettings.enable` 调 `GeneratePolymorphicDll` 转多态格式再拷 `.bytes`，产物目录 `Obfuz/{target}/PolymorphicHotUpdateAssemblies/`；运行时加载零改动，补充元数据暂维持标准格式（`disableLoadStandardDll: 0` 混用合法）。
- 迁移 DGame `AnimModule` 到 `TEngine/Runtime/Module/AnimModule/`（框架层）：基于 PlayableGraph 的代码驱动 3D 动画图，封装 Unity 底层 Playable API（`AnimationClipPlayable`/`AnimationMixerPlayable`/`AnimationLayerMixerPlayable`），支持多层级混合/权重过渡/动态增删动画片段/手动驱动；`MemoryObject` API 对齐（`Spawn→Alloc`/`Release→Dealloc`/`OnRelease→InitFromPool+RecycleToPool`），`Module.OnCreate/OnDestroy→OnInit/Shutdown`，`DGameException→Exception`，`DLogger→Log`，私有字段 `_小驼峰`；靠 `ModuleSystem` 反射约定自动注册；热更 `GameModule` 新增 `Anim` 访问器。
- 迁移 DGame `FrameAnimModule`（序列帧动画）到热更层，含场景版（`SpriteRenderer`）、UI 版（`Image`），**新增 `UIFrameRawAnimatorAgent`**（`RawImage` 版，`rawImage.texture = sprite.texture`）；`FrameSpritePool` 的 Roslyn SourceGenerator 改手写 `FrameSpritePool.Gen.cs`；`ModelConfig` Luban 依赖改新建 `FrameAnimConfig` 结构体；`GameTimer` 对象句柄改 `ITimerModule` 的 `int timerId`；`MemoryObject` API 对齐（`Spawn→Alloc`/`Release→Dealloc`/`OnRelease→InitFromPool+RecycleToPool`）。
- 迁移 DGame `GameObjectPoolModule` 到 `TEngine/Runtime/Module/GameObjectPoolModule/`（框架层）：基于 YooAsset location 的异步实例化池，支持预热/容量上限/自动销毁/DontDestroy 常驻/并发建池锁/每帧空池回收；靠 `ModuleSystem` 反射约定自动注册无需手动；热更 `GameModule` 新增 `GameObjectPool` 访问器；Editor 调试窗口菜单 `TEngine Tools/Debugger/GameObject Pool`。

- 合并 DGame `UnityUtil` 缺失方法到 `Utility.Unity`：补回组件增删（`AddMonoBehaviour`/`RmvMonoBehaviour`，TryGetComponent 去重，Editor 防 Asset 误销毁）、子节点查找（`FindChild`/`FindChildByName`/`FindChildComponent`）、`SetLayer` 批量、`AddCustomEventListener`/`RemoveCustomEventListener`（EventTrigger 封装）、随机数/实例化/射线/正则/材质/触摸/数组创建/HashCode/分辨率共 14 个 region；4 个 `Type` 泛型方法标注 `[TypeInferenceRule]`（Obfuz 混淆类型推断，`using UnityEngineInternal;` + `#pragma disable CS0618`）。
- 新建 `UnityExtension.cs`（`TEngine/Runtime/Extension/Unity/`）：`AddCustomEventListener`/`RemoveCustomEventListener` 扩展方法糖衣，`UIBehaviour` 直接调用。
- JSON 体系补 `FromJsonOverwrite`：`IJsonHelper` 接口 + `NewtonsoftJsonHelper`（`PopulateObject`）+ `DefaultJsonHelper`（`JsonUtility.FromJsonOverwrite` 兜底）+ `Utility.Json` 对外 API，四件套同步。
- 迁移 DGame 自研 UI 组件扩展（`UIButton`/`UIImage`/`UIText`/`RichTextItem`）到 `GameLogic/Module/UIModule/Expansion/`；`ListPool<T>` + `Pool<T>` 抽到 `TEngine/Runtime/Core/ListPool/` 公共化（命名空间 `TEngine`、`public`）；`UIButtonClickSoundExtend` 去 Luban 依赖改用资源地址字符串；`RichTextItem` 删 `using DGame` 天然兼容 TEngine `SetSprite` 全局扩展；`UITextOutlineExtend` 描边材质依赖 YooAsset `UGUIPro_UIText`；Editor 脚本隔离到 `Assets/Editor/UIModuleExpansion/` 含配套 `UnityEditorUtil`。`SuperScrollView` 付费插件未迁移。
- 迁移 DGame 的 `ClientSaveData` 存档系统与 `DataCenterSys` 数据中心到 `GameLogic/DataCenter/`，复用 `Singleton<T>`/`IUpdate`/`SingletonSystem` 自动驱动；特性驱动注册、双存储后端（PlayerPrefs/JsonFile）、版本升级、坏档备份、PlayerPrefs→JsonFile 懒迁移、异步线程池写入；`GameLogic.asmdef` 新增 Newtonsoft.Json 引用。
- 整合 DGame `GameTimerModule` 改进到 `TimerModule`：链表存储 O(1) 删除、坏帧 `while` + 10 次上限防栈溢出、新增 `AddLoopCountTimer` 限定循环次数，旧 API 全保留。
- 迁移 DGame 的 `GameTickWatcher` 到独立 `RuntimeTools` 程序集，命名空间与日志 API 适配 TEngine，补全文档注释。
- 新增纯数据 DataBinding 运行时与 Editor 生成器，菜单和 Odin 面板已中文化。
- 日志系统新增 Unity/Task/UniTask 到 TouchSocket `FileLogger` 的统一落盘链路，并补充独立 LogViewer 工具。
- 运行时配置已通用化为 `RuntimeConfigModule`，默认使用 TOML 清单和轻量 TOML 配置，同时保留 JSON 混用能力；单文件加载失败不再中断整体，配置名支持子目录路径。
- 热更新和资源打包侧补强 CodePackage、AOT 元数据清单校验、按包构建管线和打包工具体验。
- 场景系统将加载进度状态机从 UI 下沉到 `GameSceneModule`，DynamicSpawn 示例脚本通用化，阶段 1 超时改为双门槛（停滞 60s + 绝对 180s）修复大场景冷启动误杀。
- 场景系统新增 `SceneEnumConfig` 自动生成工具，扫描场景资源生成 `SceneType`/`SceneConstName`/`SceneTypeMapping`，免去手工同步枚举/常量/映射。
- Windows Standalone 新增 `ScreenModule`，用于控制窗口位置、尺寸、置顶和无边框。
- 接入 Obfuz 代码混淆与 `obfuz4hybridclr`，HybridCLR/Obfuz 本地化解决 dnlib 冲突，配套一键同步脚本支持版本升级。

## 维护规则

新增 fork 改动时，优先更新对应专题文档，并在 [CHANGELOG.md](CHANGELOG.md) 追加时间线记录。

只有出现新的大方向时，才更新仓库根目录 `README.md` 的 fork 概览。原 `Books/Fork-定制改动说明.md` 保留为兼容入口，内容指向本目录。
