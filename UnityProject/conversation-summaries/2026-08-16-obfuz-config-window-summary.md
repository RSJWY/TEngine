# Obfuz 混淆配置窗口（ObfuzConfigWindow）会话总结

## 背景

此前 Obfuz 配置只有两个割裂的入口：
- `BuildModeWindow`（TEngine/Build/构建模式窗口）：只管 ENABLE_OBFUZ 宏开关；
- `Project Settings -> Obfuz`（ObfuzSettingsProvider）：官方 IMGUI 面板，十几个嵌套 Settings 无分组堆叠、英文、无校验。

用户要求参考 repowiki 的 Obfuz 专题文档（`repowiki/zh/content/热更新系统/Obfuz代码混淆与加固/`），做一个类似"TEngine打包工具窗口"的独立 Odin 窗口，目标：清晰明确、方便使用、有通用性。

## 产出

新增 `Assets/TEngine/Editor/Obfuz/ObfuzConfigWindow.cs`（约 1450 行，单文件，菜单 `TEngine/Build/混淆配置窗口`，priority 51）。

### 设计决策

1. **不新建配置类、不改 Obfuz 包**：直接读写 `ObfuzSettings.Instance`（序列化于 `ProjectSettings/Obfuz.asset`），官方构建管线（ObfuscatorBuilder / ObfuzSettingsProvider / ObfuzMenu）无感知。全部代码在 TEngine.Editor 程序集，Obfuz 包保持原样可升级。
2. **OBFUZ_INSTALLED 宏双分支**：窗口主体在 `#if OBFUZ_INSTALLED` 内（该宏由 TEngine.Editor.asmdef 的 versionDefines 自动生成）；未安装时 `#else` 分支提供占位窗口（InfoBox 提示安装）。
3. **代理 using 别名**隔离对 Obfuz 包类型的引用：`ObfuzSettingsAsset`/`ObfuzPassType`/`ObfuzProxyMode` 等，便于宏切换时代码干净。
4. **保存策略**：所有 setter 都调 `MarkDirty()` → 防抖 0.6s 后 `ObfuzSettings.Save()`（写回 ProjectSettings/Obfuz.asset）；生成类操作（VM/密钥/垃圾代码）前先 `FlushSave()` 确保读到最新配置；OnDestroy 时兜底 FlushSave。

### 窗口结构（5+1 分页，全中文 LabelText + Tooltip）

| 分页 | 内容 |
|---|---|
| 总览 | ENABLE_OBFUZ 宏状态（联动 BuildDLLCommand.SetObfuzSafe）、buildPipelineSettings.enable 状态、健康检查表（级别+着色）、快捷生成按钮（VM/密钥/垃圾代码/产物目录） |
| 程序集 | assembliesToObfuscate（ValueDropdown 从 `HybridCLR.Editor.SettingsUtil.HotUpdateAssemblyNamesIncludePreserved` 取候选 + 一键填充）、引用跟随程序集、搜索路径、obfuscateObfuzRuntime |
| 混淆通道 | 4 个预设（最小=Symbol+RemoveConst；均衡=+Const+Expr；强化=+Call+ControlFlow+Field；全部=-1）、9 个 Pass 开关（直接位运算 enabledPasses）、各 Pass 参数与规则文件 |
| 加密与密钥 | VM 密钥/指令数（2的幂≥64校验+一键修正256）/输出路径+生成按钮、静态/动态密钥（默认值检测 InfoBox）、密钥路径、动态密钥程序集、随机种子 |
| 符号与映射 | debug 模式、前缀、反射兼容检测、mapping 文件路径（正式/Debug）、符号规则、自定义改名策略 |
| 垃圾代码 | defaultTask 全字段编辑、附加任务列表、生成/清理按钮 |
| 高级 | 回调顺序、targetRuntime、多态 DLL（默认密钥检测）、水印 |

### 健康检查规则（源自 wiki 的上线缺口分析）

- 错误：静态/动态/VM/多态密钥仍为官方默认值；指令数非 2 的幂或 <64；待混淆程序集为空
- 警告：加密 Pass 开启但 VM 代码/密钥文件未生成；enabledPasses == All；未混淆 Obfuz.Runtime
- 提示：randomSeed=0；mapping 文件存在/不存在（版本管理提醒）

### 踩坑记录

1. **`ListDrawerSettings(Editable = false)` 不存在**：本项目 Odin 版本该特性没有 `Editable` 属性（CS0246 把它当成类型解析），正确写法是 `IsReadOnly = true`。
2. **`Expanded` 已过时（CS0618）**：应使用 `DefaultExpandedState`（控制默认展开态）或 `ShowFoldout`。`TableList.AlwaysExpanded` 和 `FoldoutGroup.Expanded` 是不同类型的合法属性，不受影响。
3. **`EditorGUIUtility.IconContent("Cryptic")` 等图标名不可靠**：用了 `d_SceneViewVisibility`。
4. Odin 的 `GUIColor("$LevelColor")` 支持字符串成员引用（嵌套类内部私有属性也可），健康检查表按级别着色利用了这一点。

### 关键 API 对应（验证过源码）

- `ObfuzSettings.Instance / Save()`：`Packages/com.code-philosophy.obfuz/Editor/Settings/ObfuzSettings.cs`
- 生成操作直接转发 `Obfuz.Unity.ObfuzMenu.GenerateEncryptionVM/SaveSecretFile/GenerateGarbageCodes/CleanGeneratedGarbageCodes`
- 宏开关联动 `BuildDLLCommand.SetObfuzSafe/IsObfuzActiveSafe`（Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs）
- 热更程序集候选 `HybridCLR.Editor.SettingsUtil.HotUpdateAssemblyNamesIncludePreserved`（Packages/com.code-philosophy.hybridclr/Editor/SettingsUtil.cs:88）

### 验证

Unity 2022.3.62f2 编辑器内编译通过（Editor.log 无 CS 错误）。运行时行为待用户在编辑器中打开窗口实测。

## 后续可选

- 若需要"一键初始化"（生成 VM + 密钥 + 首次混淆跑通）可再加强；
- fork 定制文档（Books/Fork/）待用户确认后按 fork-docs skill 补录。
