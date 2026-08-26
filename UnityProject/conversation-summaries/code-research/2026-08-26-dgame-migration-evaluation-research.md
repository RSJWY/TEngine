# DGame 可迁移功能评估与逐模块迁移指南

> 日期：2026-08-26
> 研究对象：DGame 框架（GitHub AmaniDawn/DGame，本地 `E:\Unity\DGame\GameUnity`）
> 对比目标：本 TEngine 项目（`E:\Unity\TEngine\UnityProject`）
> 目的：盘点 DGame 相对 TEngine 的新增/改进功能，评估迁移价值，输出逐模块迁移清单，供后续逐个迁移使用。

## 一、TEngine 现状盘点（迁移前提）

先确认 TEngine 已有什么，避免重复搬运。

| 维度 | TEngine 现状 | 路径 |
|------|-------------|------|
| Runtime 模块 | AudioModule / DebugerModule / FsmModule / LocalizationModule / ObjectPoolModule / ProcedureModule / ResourceModule / RuntimeConfigModule / SceneModule / ScreenModule / Settings / TimerModule / UpdataDriver | `Assets/TEngine/Runtime/Module/` |
| Core | Constant / DataStruct / GameEvent / GameTime / Log / MemoryPool / Utility | `Assets/TEngine/Runtime/Core/` |
| HotFix GameLogic 模块 | GameScene / UIJump / UIModule（仅 UIBindComponent 空壳 + ErrorLogger） | `Assets/GameScripts/HotFix/GameLogic/Module/` |
| DataCenter | **已有** DataCenterModule / DataCenterSys / ClientSaveDataMgr（且为 DGame 版演进版，加了 IUpdate/OnUpdate） | `Assets/GameScripts/HotFix/GameLogic/DataCenter/` |
| SingletonSystem | **已有** Singleton / SingletonBehaviour / SingletonSystem | `Assets/GameScripts/HotFix/GameLogic/SingletonSystem/` |
| GameTickWatcher | **已有** | `Assets/GameScripts/RuntimeTools/GameTickWatche/` |
| ToolbarExtender | **已有**（含 BuildModeIndicator，比 DGame 更完整） | `Assets/Editor/ToolbarExtender/` |
| OpenFolderHelper | **已有** | `Assets/TEngine/Editor/Utility/OpenFolderHelper.cs` |
| TimerModule | **已有**，支持 LoopCount / 坏帧处理 / unscaled（与 DGame GameTimer 功能等价） | `Assets/TEngine/Runtime/Module/TimerModule/` |
| MemoryPool | **已有** MemoryCollection（与 DGame MemoryCollector 本质相同） | `Assets/TEngine/Runtime/Core/MemoryPool/` |
| Odin 插件 | **已有** | `Assets/Plugins/Odin Inspector/` |
| Obfuz | **已有** | `Assets/Obfuz/` |
| UI 组件扩展（UIButton/UIImage/UIText/RichText/SuperScrollView） | **不存在** | — |
| 红点系统 | **不存在** | — |
| 输入模块 | **不存在** | — |
| AnimModule | **不存在** | — |
| 序列帧动画 | **不存在** | — |
| GameObjectPoolModule | **不存在**（仅有通用 ObjectPoolModule） | — |
| TextModule（多语言文本） | **不存在** | — |
| GuideModule | **不存在** | — |
| GMPanel | **不存在** | — |
| SpineModelHelper | **不存在** | — |

> 关键约束：本项目 CLAUDE.md 明确"未使用 Luban，不考虑沾边"。因此依赖 Luban 配置表（`Tb*` / `GameProto`）的模块在启用 Luban 前不可迁移。

## 二、迁移优先级总览

| 优先级 | 模块 | 价值 | 可行性 | 核心障碍 |
|--------|------|------|--------|---------|
| ★★★★★ | UI 组件扩展（含 SuperScrollView） | 极高 | 高 | 无 |
| ★★★★★ | 红点系统 RedDotModule | 极高 | 高 | 无 |
| ★★★★☆ | 序列帧动画 FrameAnimModule | 高 | 高 | Timer API 对齐 |
| ★★★☆☆ | 输入模块 InputModule | 中高 | 中 | 依赖新输入系统 |
| ★★★☆☆ | 动画模块 AnimModule | 中高 | 中 | Module 基类 + Fsm 集成 |
| ★★★☆☆ | GameObjectPoolModule | 中 | 中 | 资源 API 对齐 |
| ★★☆☆☆ | TextModule | 中 | 低 | 强依赖 Luban |
| ★★☆☆☆ | GMPanel | 中 | 低 | 强依赖 Luban |
| ★★☆☆☆ | GuideModule | 低 | 中 | 依赖存档+事件 |
| ★☆☆☆☆ | SpineModelHelper | 视项目而定 | 中 | 依赖 Spine-Unity |
| 不迁移 | GameTimer / MemoryCollector / MonoDriver / DataCenter / SingletonSystem / GameTickWatcher / ToolbarExtender / OpenFolderHelper / ILocalizationModule | — | — | TEngine 已有等价物 |

## 三、第一梯队：高价值模块详解

### 1. UI 组件扩展（UIModule/Expansion）★★★★★

**源路径**：`E:\Unity\DGame\GameUnity\Assets\Scripts\HotFix\GameLogic\Module\UIModule\Expansion\`

**内容清单**：

| 子目录 | 内容 | 关键文件 |
|--------|------|---------|
| `UIButton/` | 继承 UnityEngine.UI.Button 的增强按钮 | BaseUIButton.cs、UIButton.cs + 5 个 Extend（ClickProtect/ClickScale/ClickSound/DoubleClick/LongPress）+ Editor |
| `UIImage/` | 继承 Image 的增强图片 | BaseUIImage.cs、UIImage.cs + MirrorExtend/RoundedCornersExtend/MaskExtend + Editor |
| `UIText/` | 继承 Text 的增强文本 | BaseUIText.cs、UIText.cs + GradientColor/Shadow/Outline/Spacing/VertexColor/Circle + Editor |
| `RichTextItem/` | 图文混排、超链接、动画表情 | RichTextItem.cs、RichTextParser.cs、RichTextData.cs、RichTextConfig.cs + Editor |
| `SuperScrollView/` | 循环列表/网格/瀑布流（第三方库 v2.5.5） | LoopListView2、LoopGridView、LoopStaggeredGridView + ItemPool + Item |
| 根目录散件 | 辅助组件 | CircleLayoutGroup、NestedScrollRect、EmptyGraph、UIDragListener、UIEffectSortingOrder、UIExtension、UIImageEffect、GridItemGroup、ItemPosMgr、ClickEventListener、CommonDefine |

**核心设计要点**：
1. UIButton 采用**组合式扩展**：核心 BaseUIButton 持有 5 个独立 Extend 对象（ClickProtect/ClickScale/LongPress/DoubleClick/ClickSound），各自独立开关，互不耦合，按生命周期（Awake/OnEnable/OnPointerDown/OnPointerUp/OnPointerClick/OnUpdate/OnDisable/OnDestroy）分派调用
2. 点击保护：`IsUseClickProtect` + `CanClick` + 倒计时，防连点
3. 点击缩放：OnPointerDown 缩小、OnPointerUp 回弹，transform.localScale 操控
4. 长按：IUpdateSelectedHandler 每帧检测持续时长，支持持续触发模式
5. SuperScrollView 是成熟的第三方开源库，对象池复用 Item，支持正向/反向、跳转、动态增删

**外部依赖**：
- UnityEngine.UI（UGUI，TEngine 自带）
- 少数 Editor 脚本可能用 Odin（TEngine 已有 Odin）
- SuperScrollView 自带 ListPool/Pool，无外部依赖

**迁移可行性**：**高**。代码集中在 `Expansion/` 下，与 DGame 框架耦合极低（仅命名空间 `GameLogic`）。

**迁移改造点**：
1. 命名空间 `GameLogic` → 按目标程序集调整（放 GameLogic 程序集则保持，或统一改）
2. Editor 脚本（`*Editor.cs`、`*DrawEditor.cs`）必须放在 `Assets/Editor/` 下，**禁止进热更程序集**（CLAUDE.md 红线第6条）
3. 程序集划分：运行时部分进 `GameLogic` 热更程序集；SuperScrollView 因是通用库，可考虑放 `GameBase` 或独立程序集
4. 验证是否有 `DLogger`/`DGame` 命名空间残留，替换为 TEngine 对应物
5. SuperScrollView 的 `Scripts.meta` 子目录需一并搬移

**迁移步骤建议**：
1. 先整体拷贝 `Expansion/` 到 TEngine `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/Expansion/`
2. 全局替换命名空间残留
3. 把 `*Editor.cs` / `*DrawEditor.cs` 移到 `Assets/Editor/UIModuleExpansion/`
4. 编译验证，修复 asmdef 引用

---

### 2. 红点系统 RedDotModule ★★★★★

**源路径**：`E:\Unity\DGame\GameUnity\Assets\Scripts\HotFix\GameLogic\Module\RedDotModule\`

**文件清单**：
- `RedDotModule.cs`（525 行，核心管理器，Singleton）
- `RedDotNode.cs`（354 行，树节点）
- `RedDotType.cs`（红点类型枚举：Dot/Number 等）
- `RedDotTreeConfig.cs`（编辑器可视化配置 Asset）
- `RedDotItem.cs`（红点 UI 项组件）
- `Config/RedDotTreeConfig.asset`（配置示例）
- `Editor/RedDotTreeCodeGenerator.cs`（代码生成器，生成 RedDotPathDefine_Gen.g.cs）
- `Editor/RedDotTreeConfigCreator.cs`（配置创建器）
- `Editor/RedDotTreeEditorWindow.cs`（可视化编辑窗口）
- `Gen/RedDotPathDefine_Gen.g.cs`（自动生成代码示例）

**核心设计要点**：
1. **双模式注册**：ID 模式（`Register(int id, string path, string[] segments)`，预分配 ID，推荐）+ 路径兼容模式（`Register(string path)`，自动分配 ID 从 10000 起）
2. **树状结构**：Root 节点固定 ID=0，路径以 `/` 分段，逐级创建/查找节点
3. **聚合策略** `RedDotAggregateStrategy`：`Or`（任一子有则显1）、`Sum`（累加）、`Max`（取最大）
4. **脏标记 + 向上冒泡**：叶子 SetValue → NotifyValueChanged → PropagateToParent 重算父节点；非叶子用 Recalculate + IsDirty 递归重算
5. **事件监听**：`AddListener(Action<RedDotNode>)`，值变化时回调
6. **编辑器可视化配置 + 代码生成**：在窗口里搭树 → 存 Asset → 生成 `RedDotPathDefine_Gen` 静态路径常量，业务代码用强类型路径

**外部依赖**：
- `Singleton<RedDotModule>`（TEngine 已有 SingletonSystem）
- `DLogger`（DGame 日志，替换为 TEngine Log）
- `RedDotPathDefine.RegisterAll()`（生成的注册入口）

**迁移可行性**：**高**。核心逻辑零框架耦合。

**迁移改造点**：
1. `Singleton<T>` 基类 API 对齐：DGame 用 `OnInit/OnDestroy/Release`，TEngine 用 `OnInit/OnDestroy/Destroy`（TEngine 版用 `Register`/`DestroySingleton`，字段 `m_instance`）。需确认 TEngine Singleton 的 `Register` 方法名并调整 RedDotModule 中 `SingletonSystem.Retain(_instance)` 调用
2. `DLogger.Error/Warning/Log` → TEngine `Log.Error/Warning/Log`
3. Editor 代码移到 `Assets/Editor/RedDotModule/`
4. `RedDotPathDefine_Gen.g.cs` 的生成路径需调整
5. 命名空间 `GameLogic` 保持或调整

**迁移步骤建议**：
1. 拷贝 `RedDotModule/` 核心文件到 `Assets/GameScripts/HotFix/GameLogic/Module/RedDotModule/`
2. Editor 文件移到 `Assets/Editor/RedDotModule/`
3. 对齐 Singleton/Log API
4. 清理 Gen 目录的旧生成文件，重新生成验证

## 四、第二梯队：高价值但需 API 对齐

### 3. 序列帧动画 FrameAnimModule ★★★★☆

**源路径**：`E:\Unity\DGame\GameUnity\Assets\Scripts\HotFix\GameLogic\Module\FrameAnimModule\`

**文件清单**：FrameSpriteMgr.cs（318 行，Singleton 调度器）、FrameSpritePool.cs（对象池）、FrameClip.cs（帧片段）、FrameAnimatorAgent.cs（场景代理）、UIFrameAnimatorAgent.cs（UI 代理）、FrameAnimName.cs

**核心设计**：
- 统一调度器 FrameSpriteMgr 持有缩放/非缩放两套代理列表，用 GameTimer 以 0.015625f（64Hz）间隔 tick
- FrameSpritePool 按资源地址缓存已加载的帧动画资源
- 场景/UI 双代理，复用同一调度
- 代理有 UpdateIndex，注册/反注册 O(1)

**迁移改造点**：
1. `GameTimer m_scaledTimer` → TEngine TimerModule。**API 差异**：DGame 返回 `GameTimer` 对象，TEngine 返回 `int timerId`。FrameSpriteMgr 需持有 id 并在 OnDestroy 时 `RemoveTimer(id)`。或考虑给 TEngine TimerModule 加返回对象的重载
2. 资源加载 API 对齐（FrameSpritePool 内部的 LoadAssetAsync → `GameModule.Resource.LoadAssetAsync`）
3. Singleton/Log 对齐

### 4. 输入模块 InputModule ★★★☆☆

**源路径**：`E:\Unity\DGame\GameUnity\Assets\Scripts\HotFix\GameLogic\Module\InputModule\`

**文件清单**：IInputModule.cs、InputModule.cs、IInputComponent.cs、ActorInputComponent.cs、IInputContextLayer.cs、ActorInputContextLayer.cs、GameInputActions.cs（自动生成的 InputAction）、InputDefine.cs

**核心设计**：基于 Unity 新输入系统（Input System package），组件化（IInputComponent）+ 上下文层（IInputContextLayer 按实体 ID 隔离）+ 改键（InteractiveRebind）+ 绑定信息查询。全部代码在 `#if ENABLE_INPUT_SYSTEM` 下。

**迁移障碍**：强依赖 Input System package 和 `ENABLE_INPUT_SYSTEM` 宏。需 TEngine 项目已切到新输入系统（Player Settings → Active Input Handling = Both/Input System）。

**迁移改造点**：确认项目输入系统配置；`GameInputActions.cs` 需用 Input Action Asset 重新生成。

### 5. 动画模块 AnimModule ★★★☆☆

**源路径**：`E:\Unity\DGame\GameUnity\Assets\DGame\Runtime\Module\AnimModule\`

**文件清单**：IAnimModule.cs、AnimModule.cs、IAnimPlayable.cs、AnimPlayable.cs、AnimMixer.cs、AnimNode.cs、AnimState.cs、AnimClip.cs、AnimationWrapper.cs

**核心设计**：封装 Playable API，AnimPlayable 管理一层动画图，支持多片段混合（AnimMixer），与 FsmModule 集成实现状态切换自动播动画。

**迁移改造点**：`Module` 基类对齐（DGame 的 `Module` 在 `DGame.Runtime.Core.ModuleSystem`，TEngine 在 `TEngine.Runtime`）；命名空间 `DGame` → `TEngine`；FsmModule 集成接口需对齐 TEngine 的 IFsmModule。

### 6. GameObjectPoolModule ★★★☆☆

**源路径**：`E:\Unity\DGame\GameUnity\Assets\DGame\Runtime\Module\GameObjectPoolModule\`

**文件清单**：IGameObjectPoolModule.cs、GameObjectPoolModule.cs、GameObjectPool.cs、GameObjectPoolRoot.cs、GameObjectPoolDebugInfo.cs

**核心设计**：专用 GameObject 池，异步 Spawn（LoadGameObjectAsync）+ Recycle + 自动销毁计时 + 调试快照。按资源 location 管理多池。

**迁移改造点**：资源加载 API 对齐（YooAsset LoadGameObjectAsync）；Module 基类对齐；放 Runtime 层（非热更）。

## 五、第三梯队：依赖 Luban 或价值有限

### 7. TextModule ★★☆☆☆
依赖 Luban `TbTextConfig` + `GameProto` 命名空间。**本项目未用 Luban，暂不迁移**。若未来启用 Luban 可考虑：G.cs（多语言文本快捷取用）、TextConfigMgr、TextDefine、UITextIDBinder + Editor。

### 8. GMPanel ★★☆☆☆
依赖 Luban `TbGmConfig`。同上暂不迁移。结构为配置表驱动的 GM 面板，含服务端/客户端/批量/其他四类快捷 GM。

### 9. GuideModule ★★☆☆☆
GuideMgr + GuideClickListener + GuideSaveData，依赖存档系统（TEngine 已有 ClientSaveDataMgr）+ 事件系统。功能轻量，可参考自研。

### 10. SpineModelHelper ★☆☆☆☆
SpineModelHelper.cs + UISpineModelHelper.cs，依赖 Spine-Unity 运行时。仅在使用 Spine 时有价值。

## 六、明确不迁移清单（TEngine 已有等价物）

| DGame 模块 | TEngine 对应 | 不迁移原因 |
|-----------|-------------|-----------|
| GameTimer | TimerModule | 功能完全等价（LoopCount/坏帧/unscaled 都有），仅返回对象 vs id 的差异，无实质收益 |
| MemoryCollector | MemoryPool.MemoryCollection | 本质相同，仅 Spawn/Release 命名 + Capacity 属性 |
| MonoDriver | UpdataDriver | 功能相同，替换无收益 |
| DataCenter / ClientSaveDataMgr | 已有（演进版） | TEngine 版本反而更新（加了 IUpdate/OnUpdate） |
| SingletonSystem | 已有 | TEngine 版更规范 |
| GameTickWatcher | 已有 | 完全重复 |
| ToolbarExtender | 已有（更完整） | TEngine 还多 BuildModeIndicator |
| OpenFolderHelper | 已有 | 完全重复 |
| ILocalizationModule | LocalizationModule | 差异不大 |

## 七、迁移执行顺序建议

按"价值高、障碍低"优先原则：

1. **UI 组件扩展（含 SuperScrollView）** — 先搬，立即能用，无任何框架依赖
2. **红点系统** — 次搬，仅 Singleton/Log 对齐
3. **序列帧动画** — 第三搬，需先决定 TimerModule 是否加返回对象重载
4. （视需求）InputModule / AnimModule / GameObjectPoolModule — 按项目实际需求决定

每个模块迁移时遵循：拷贝 → 命名空间/基类/Log 对齐 → Editor 隔离 → 编译验证 → 提交。
