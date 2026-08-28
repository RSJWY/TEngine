# DGame 与 TEngine 启动加载流程对比研究

> 研究对象：
> - DGame：`E:\Unity\DGame\GameUnity`（GitHub: AmaniDawn/DGame）
> - TEngine：`E:\Unity\TEngine\UnityProject`（本仓库 fork）
>
> 研究时间：2026-08-28

## 一、启动流程总览

### 1.1 DGame 流程链

```
GameEntry.Awake()                          [Assets/DGame.AOT/GameEntry.cs:7]
  ├── ModuleSystem.GetModule<IMonoDriver>()
  ├── ModuleSystem.GetModule<IResourceModule>()
  ├── ModuleSystem.GetModule<IFsmModule>()
  ├── DontDestroyOnLoad(this)
  └── Settings.ProcedureSettings.StartProcedure().Forget()   [ProcedureSettings.cs:20]
        │  反射读取 availableProcedureTypeNames + startProcedureTypeName
        │  Activator.CreateInstance 各 Procedure
        │  m_procedureModule.Initialize(fsmModule, procedures)
        └── StartProcedure(LaunchProcedure)

主包流程（不可热更，均在 DGame.AOT/Procedure/）：
LaunchProcedure(1)  →  SplashProcedure(2)  →  InitPackageProcedure(3)
  →  InitResourceProcedure(4)
       ├── 单机/编辑器 → PreloadProcedure(9)
       └── 联机/Web → CreateDownloaderProcedure(5) → DownloadFileProcedure(6)
            → DownloadOverProcedure(7) → [ClearCacheProcedure(8)] → PreloadProcedure(9)
  →  PreloadProcedure(9) → LoadAssemblyProcedure(10) → StartGameProcedure(11)
  →  反射调用 GameStart.Entrance(object[])  [HotFix/GameLogic/GameStart.cs:26]
        ├── GameEventLauncher.Init()
        ├── Utility.UnityUtil.AddDestroyListener(OnDestroy)
        ├── InitLanguageSettings()
        └── StartGame() => GameModule.UIModule.ShowWindow<MainWindow>()
```

### 1.2 TEngine 流程链

```
GameEntry.Awake()                         [Assets/GameScripts/GameEntry.cs:6]
  ├── ModuleSystem.GetModule<IUpdateDriver>()
  ├── ModuleSystem.GetModule<IResourceModule>()
  ├── ModuleSystem.GetModule<IDebuggerModule>()
  ├── ModuleSystem.GetModule<IFsmModule>()
  ├── DontDestroyOnLoad(this)
  └── Settings.ProcedureSetting.StartProcedure().Forget()

主包流程（不可热更，均在 GameScripts/Procedure/）：
ProcedureLaunch  →  ProcedureSplash  →  ProcedureInitPackage  →  ProcedureInitResources
  →  [ProcedureCreateDownloader → ProcedureDownloadFile → ProcedureDownloadOver → ProcedureClearCache]
  →  ProcedurePreload  →  ProcedureLoadAssembly  →  ProcedureStartGame
  →  反射调用 GameApp.Entrance(object[])  [HotFix/GameLogic/GameApp.cs:29]
        ├── GameEventHelper.Init()
        ├── _hotfixAssembly = objects[0]
        ├── Utility.Unity.AddDestroyListener(Release)
        └── StartGameLogic()
              ├── GameModule.Screen.ApplyAll()
              └── GameModule.GameScene.LoadScene(SceneType.MainScene)
```

### 1.3 流程对照表

| 步骤 | DGame | TEngine | 差异要点 |
|------|-------|---------|---------|
| 入口 MonoBehaviour | `GameEntry`（DGame.AOT） | `GameEntry`（GameScripts/Assembly-CSharp） | TEngine 额外初始化 `IUpdateDriver` + `IDebuggerModule` |
| 框架核心初始化 | `RootModule` MonoBehaviour（场景挂载）独立于 GameEntry | 无独立 RootModule，由 GameEntry + ModuleSystem 驱动 | DGame 的基础设置（帧率/语言/内存池）在 RootModule.Awake；TEngine 分散到 ProcedureLaunch |
| 启动流程节点 | `ProcedureSettings` ScriptableObject（反射 availableProcedureTypeNames） | `ProcedureSetting`（类似机制） | 基本一致 |
| 闪屏 | SplashProcedure | ProcedureSplash | 一致 |
| 初始化 Package | InitPackageProcedure（单包 `DefaultPackageName`） | ProcedureInitPackage（**多包** `GetRuntimePackages()` 循环） | **TEngine 支持多资源包** |
| 初始化资源/清单 | InitResourceProcedure（单包版本+清单） | ProcedureInitResources（**多包循环** + 本地版本回退 + 版本确认弹窗 + PackageNote 模式校验） | TEngine 远超 |
| 创建下载器 | CreateDownloaderProcedure（单包） | ProcedureCreateDownloader（**多包聚合统计**） | TEngine 聚合多包总量再提示 |
| 下载文件 | DownloadFileProcedure（单包，失败直接弹框退回 CreateDownloader） | ProcedureDownloadFile（**多包顺序下载** + 指数退避重试 2/5/10s + 重试计数 + 失败对话框） | TEngine 远超 |
| 下载完成 | DownloadOverProcedure | ProcedureDownloadOver（多包版本号写入 + skip 标记） | TEngine 更完善 |
| 清理缓存 | ClearCacheProcedure | ProcedureClearCache | 一致 |
| 预加载 | PreloadProcedure（PRELOAD 标签） | ProcedurePreload（PRELOAD + WEBGL_PRELOAD 标签） | 基本一致 |
| 加载程序集 | LoadAssemblyProcedure（Obfuz 密钥 + DLL + AOT 元数据） | ProcedureLoadAssembly（**PDB 缓存** + DLL + **AOTMetadataManifest 动态列表** + 多包指定 packageName） | TEngine 更完善 |
| 开始游戏 | StartGameProcedure → 反射 GameStart.Entrance | ProcedureStartGame → 反射 GameApp.Entrance | TEngine 多一帧 Yield |
| 热更入口类名 | `GameStart` | `GameApp` | 命名不同 |
| 热更后首动作 | `ShowWindow<MainWindow>()` | `GameModule.Screen.ApplyAll()` + `LoadScene(MainScene)` | 设计取向不同 |

---

## 二、核心差异深度分析

### 2.1 多资源包支持（TEngine 的重大优势）

**DGame**：全程围绕 `_resourceModule.DefaultPackageName` 单包操作。
- `InitPackageProcedure.InitPackage()` 只初始化一个默认包
- `InitResourceProcedure` 只对一个包做版本请求/清单更新
- `CreateDownloaderProcedure` 只对一个包创建下载器
- 版本记录 key 硬编码 `"GAME_VERSION"`

**TEngine**：`UpdateSetting.RuntimePackages` 列表驱动全流程。
- `ProcedureBase.GetRuntimePackages()` 返回 `List<RuntimePackageEntry>`，每个条目可独立配置：
  - `InitOnStartup`（是否启动时初始化）
  - `UpdateManifestOnStartup`（是否启动时更新清单）
  - `DownloadOnDemand`（是否按需下载）
  - `SaveVersion`（是否持久化版本号）
  - `VersionKey`（独立 PlayerPrefs key）
- `ProcedureInitResources` 对每个包循环：请求版本→本地回退→版本确认弹窗→更新清单→PackageNote 模式校验
- `ProcedureCreateDownloader` 聚合多包的 `TotalDownloadCount/Bytes` 后统一提示
- `ProcedureDownloadFile` 按包顺序串行下载，每包独立重试计数

**结论**：TEngine 的多包架构适合中大型项目（代码包/场景包/UI包分离），DGame 只适合单包小型项目。

### 2.2 版本回退与断网容错（TEngine 显著更强）

**DGame `InitResourceProcedure`**：
- 远端版本请求失败时，`IsNeedUpdate()` 检查 `UpdateStyle.Optional` + 本地版本记录
- 无本地版本 → 弹框重试；有本地版本 → 可选进入预载
- 逻辑较简单，仅处理单包

**TEngine `ProcedureInitResources`**：
- **本地版本回退**：远端失败时读 `PlayerPrefs` 存的历史版本，`UpdatePackageManifestAsync(savedVersion)` 恢复本地清单，`_usedLocalPackageVersion` 标记走 fallback 路径
- **版本确认弹窗**：`ConfirmPackageVersion` 协程，区分首次/强制/可选三种情况，5 秒自动确认
- **PackageNote 模式校验**：读 YooAsset `package.GetPackageNote()`（JSON），比对 `metadata.mode` 与 `UpdateSetting.BuildMode`，不匹配直接弹框退出——**防止 dev/release 资源包混用**
- fallback 后还有 `HandleLocalPackageVersionFallback` 二次提示用户

**结论**：TEngine 在弱网/断网/版本不一致场景的容错远超 DGame。

### 2.3 下载重试机制（TEngine 完胜）

**DGame `DownloadFileProcedure`**：
- 下载错误回调 `OnDownloadErrorCallback` 直接 `ShowMessageBox(SwitchState<CreateDownloaderProcedure>, Application.Quit)`
- 即：失败后让用户选「重试（回到创建下载器）」或「退出」
- 无自动重试、无退避、无重试计数

**TEngine `ProcedureDownloadFile`**：
- `RetryDelaysSeconds = { 2, 5, 10 }` 指数退避
- `DownloadRetryCountKey` 在 ProcedureOwner 中跨状态持久化重试计数
- 自动重试达上限后才弹「已自动重试 N 次」对话框
- 多包串行下载，每包失败独立重试，`CurrentDownloadPackageKey` 追踪当前包

**结论**：TEngine 有生产级的下载容错，DGame 是玩具级。

### 2.4 程序集加载（TEngine 更工程化）

| 维度 | DGame | TEngine |
|------|-------|---------|
| Obfuz 混淆密钥 | `SetUpStaticSecretKey()` 加载 Resources 密钥 | 无（Obfuz 可选，未在流程内） |
| PDB 支持 | 无 | **有**：`_setting.WillGeneratePdb` 时先加载 pdb 缓存到 `_pdbBytesCache`，再加载 dll 时 `Assembly.Load(dllBytes, pdbBytes)` 带符号——**热更域异常带堆栈** |
| AOT 元数据列表来源 | `UpdateSettings.AOTMetaAssemblies` 硬编码列表 | **运行时加载 `AOTMetadataManifest` ScriptableObject**，location 无效/为空时回退 `UpdateSetting.AOTMetaAssemblies`——**AOT 列表随包走，可热更** |
| 程序集包名 | 隐含默认包 | `_assemblyPackageName = _setting.GetAssemblyPackageName()` 显式指定代码所在包 |
| 日志 | `DLogger.Log` 简单 | `Log.Debug` 分级 + 详细 `[AOTMetadata]` 前缀 |

**结论**：TEngine 的 PDB 缓存机制和 AOTMetadataManifest 动态列表都是 DGame 没有的工程化能力，对调试和裁剪后的 AOT 泛型补全更友好。

### 2.5 框架核心初始化方式差异

**DGame**：依赖场景中挂载的 `RootModule` MonoBehaviour（`RootModule.cs:112` Awake）。
- 优势：基础设置（帧率/语言/内存池/LogHelper/JsonHelper/StringUtilHelper）在 Unity 场景层级配置，可视化
- 劣势：依赖场景预设，换场景/空场景启动需保证 RootModule 存在；`RootModule.Awake` 与 `GameEntry.Awake` 执行顺序不可控（靠 Script Execution Order）

**TEngine**：无独立 RootModule，基础设置分散到 `ProcedureLaunch`。
- 优势：入口集中在 GameEntry + 流程状态机，无场景依赖，启动顺序由流程驱动可控
- 劣势：帧率/后台运行等基础设置需在流程节点里配置（TEngine 实际由 `RootModule.Instance` 在 TEngine.Runtime 提供，但语言/音频在 ProcedureLaunch 初始化）

**结论**：TEngine 的「流程驱动一切」更解耦，DGame 的「场景挂载 + 入口」更直观但耦合场景。

### 2.6 本地化策略差异

**DGame**：
- 启动文本靠 `UpdateUIDefine` 单例 + `Resources/Config/UpdateUIDefine.json`（`InitConfigData` 反序列化覆盖默认值）
- 所有提示文案是 `TextDefine` 字段，可通过 json 现场覆盖
- 语言在 `GameStart.InitLanguageSettings()`（热更域）初始化

**TEngine**：
- 启动文本靠 `LoadText` 单例（`LoadText.Instance.Label_xxx`）
- 语言在 `ProcedureLaunch.InitLanguageSettings()`（主包域）初始化——**早于热更**
- 多了 `IRuntimeConfigModule.LoadAllAsync()` 在 ProcedureLaunch 加载部署配置（可现场覆盖热更地址/Debugger 策略）

**结论**：TEngine 把本地化和部署配置前置到主包流程，使得热更域启动时环境已就绪；DGame 的语言初始化在热更域，若热更 DLL 加载失败则语言也未初始化。

### 2.7 热更入口首动作差异

**DGame `GameStart.StartGame()`**：`GameModule.UIModule.ShowWindow<MainWindow>()`——直接开主 UI 窗口。

**TEngine `GameApp.StartGameLogic()`**：
- `GameModule.Screen.ApplyAll()`——多屏显示配置（异步）
- `GameModule.GameScene.LoadScene(SceneType.MainScene)`——加载主场景

**结论**：TEngine 走「场景驱动」路线（先进主场景再开 UI），DGame 走「UI 驱动」路线（直接弹主窗口）。TEngine 更适合 3D 场景游戏，DGame 更适合 2D/UI 向游戏。

---

## 三、优劣总结

### TEngine 的优势（生产级框架）

1. **多资源包架构**：RuntimePackages 列表驱动全流程，支持按需初始化/下载/版本记录，适合分包发布（代码包、场景包、UI 包独立热更）
2. **弱网/断网容错**：本地版本回退 + 版本确认弹窗 + PackageNote 模式校验，防止 dev/release 包混用
3. **下载重试机制**：指数退避（2/5/10s）+ 重试计数 + 多包串行，生产级可靠性
4. **PDB 调试支持**：热更 DLL 可带符号加载，热更域异常有堆栈，调试体验好
5. **AOT 元数据动态化**：AOTMetadataManifest 随包走，裁剪后的 AOT 列表可热更，无需改 UpdateSetting 重新出包
6. **本地化前置**：语言/音频在主包 ProcedureLaunch 初始化，热更域启动时环境就绪
7. **部署配置现场覆盖**：IRuntimeConfigModule 可现场覆盖热更地址、Debugger 策略，免重新出包
8. **场景驱动启动**：先 LoadScene 再开 UI，适合 3D 游戏

### DGame 的优势（轻量直观）

1. **RootModule 场景挂载可视化**：帧率/语言/内存池/Helper 类型在 Inspector 配置，新人友好
2. **UpdateUIDefine json 现场覆盖**：所有启动文案可通过 Resources/json 现场改，无需重新出包
3. **Obfuz 密钥内嵌流程**：`SetUpStaticSecretKey` 在 LoadAssembly 流程内，混淆密钥随包加载
4. **UpdateStyle/UpdateNotice 枚举**：强制/可选更新 + 是否提示，组合清晰
5. **单包简单**：流程链路短，代码量少，适合小型项目快速上手
6. **UI 驱动启动**：直接 ShowWindow，适合 2D/UI 向轻量游戏

### 共同的不足

1. **Preload 标签机制相同**：都用 `GetAssetInfos("PRELOAD")` 同步收集 + 回调式加载，无优先级/分帧，大 preload 列表会卡顿
2. **下载速度统计相同**：都用 `Time.deltaTime` 采样的平均速度，非滑动窗口，前几帧抖动大
3. **ClearCache 触发条件相同**：`DownloadOver` 里 `_needClearCache` 字段从未被赋值为 true（两边的 ProcedureDownloadOver/DownloadOverProcedure 都有此字段但无赋值点），实际永远走 Preload 分支——**疑似死代码**

### 谁更适合谁

- **TEngine**：中大型项目、需要分包热更、3D 场景游戏、注重生产可靠性和调试体验
- **DGame**：小型项目、单包够用、2D/UI 向游戏、希望流程简单直观可快速上手

> 备注：DGame 本身就是从 TEngine 早期版本 fork 改造而来（命名空间 DGame、RootModule、ProcedureSettings 等结构高度相似），保留了 TEngine 的流程骨架，做了轻量化简化和 UI 文案可配置化，但砍掉了多包/重试/PDB/动态 AOT 列表等工程化能力。
