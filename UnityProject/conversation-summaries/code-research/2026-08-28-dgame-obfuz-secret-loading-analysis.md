# DGame Obfuz 密钥加载处理分析与 TEngine 借鉴方案

> 研究日期：2026-08-28
> 研究对象：DGame（`E:\Unity\DGame\GameUnity`）的 Obfuz 密钥运行时初始化实现，对比 TEngine（本仓库）当前 Obfuz 集成状态
> 关联文档：[2026-08-14 Obfuz 运行时初始化与混淆范围研究](./2026-08-14-obfuz-runtime-and-scope-research.md)

## 一、DGame 的密钥加载实现

### 1.1 核心代码

`Assets/DGame.AOT/Procedure/LoadAssemblyProcedure.cs:53,307-318`：

```csharp
// OnEnter 中最先调用（第53行），先于 LoadAssembly()
public override void OnEnter()
{
    SetUpStaticSecretKey();  // ← 在一切 DLL 加载之前
    // ...后续 LoadAssembly()
}

private void SetUpStaticSecretKey()
{
#if ENABLE_OBFUZ
    DLogger.Info("Enable Obfuz");
    DLogger.Info("SetUpStaticSecret begin");
    EncryptionService<DefaultStaticEncryptionScope>.Encryptor =
        new GeneratedEncryptionVirtualMachine(
            Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey").bytes);
    DLogger.Info("SetUpStaticSecret end");
#else
    DLogger.Info("Disable Obfuz");
#endif
}
```

### 1.2 关键设计点

| 维度 | DGame 做法 | 说明 |
|------|-----------|------|
| **初始化时机** | `LoadAssemblyProcedure.OnEnter` 最前面，先于 `LoadAssembly()` | 确保被混淆的常量/字段在被读取前解密器已就绪 |
| **密钥来源** | `Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey")` | 从 `Resources/Obfuz/` 加载，随主包出包，不参与热更 |
| **VM 实例** | `GeneratedEncryptionVirtualMachine`（Obfuz 自动生成，`Assets/Obfuz/`） | 构造时接收密钥字节流，内部有 256 条 OpCode 指令表 |
| **Scope** | `DefaultStaticEncryptionScope` | 静态加密作用域，用于 AOT/启动早期代码 |
| **动态密钥** | **未使用**（`assembliesUsingDynamicSecretKeys: []`） | 只有静态密钥一条链路 |
| **宏控制** | `#if ENABLE_OBFUZ` | 与热更宏 `ENABLE_HYBRIDCLR` 独立，可单独开关 |

### 1.3 密钥与 VM 文件布局

```
Assets/Resources/Obfuz/
├── defaultStaticSecretKey.bytes      # 静态密钥（Resources.Load 加载）
└── defaultDynamicSecretKey.bytes     # 动态密钥（DGame 未使用，但文件存在）

Assets/Obfuz/
├── GeneratedEncryptionVirtualMachine.cs   # Obfuz 自动生成的加密 VM（256 OpCode）
├── DGame.Obfuz.asmdef                      # 独立程序集定义
└── SymbolObfus/
    └── symbol-mapping.xml                  # 符号映射表（堆栈还原用）
```

### 1.4 Obfuz.asset 配置要点

`ProjectSettings/Obfuz.asset` 关键字段：

| 配置项 | 值 | 说明 |
|--------|-----|------|
| `buildPipelineSettings.enable` | `1` | 打包时自动触发 Obfuz 混淆 |
| `assembliesToObfuscate` | `GameProto, GameBattle, GameLogic` | 3 个热更程序集被混淆 |
| `nonObfuscatedButReferencingObfuscatedAssemblies` | `DGame.Runtime, Assembly-CSharp, DGame.AOT` | 3 个主包程序集不混淆但引用了混淆类型，需同步改写调用点 |
| `obfuscateObfuzRuntime` | `0` | 不混淆 Obfuz runtime 自身 |
| `obfuscationPassSettings.enabledPasses` | `-1`（All） | 全部 Pass 启用 |
| `secretSettings.assembliesUsingDynamicSecretKeys` | `[]` | 未使用动态密钥 |

---

## 二、TEngine 当前 Obfuz 集成状态

### 2.1 已就绪部分

| 组件 | 状态 | 路径 |
|------|------|------|
| Obfuz.asset 配置 | ✅ 有 | `ProjectSettings/Obfuz.asset` |
| 静态密钥文件 | ✅ 有 | `Assets/Resources/Obfuz/defaultStaticSecretKey.bytes` |
| 动态密钥文件 | ✅ 有 | `Assets/Resources/Obfuz/defaultDynamicSecretKey.bytes` |
| VM 代码 | ✅ 有 | `Assets/Obfuz/GeneratedEncryptionVirtualMachine.cs` |
| 符号映射目录 | ✅ 有 | `Assets/Obfuz/SymbolObfus/` |
| 配置窗口 | ✅ 有（比 DGame 更完善） | `Assets/TEngine/Editor/Obfuz/ObfuzConfigWindow.cs` |
| 构建混淆链路 | ✅ 有 | `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`（`#if ENABLE_HYBRIDCLR && ENABLE_OBFUZ`） |
| GameApp ObfuzIgnore | ✅ 有 | `GameApp.cs` 的 `[ObfuzIgnore(ObfuzScope.TypeName \| ObfuzScope.MethodName)]` |
| UIBase ObfuzIgnore | ✅ 有 | `UIBase.cs` 的 `[ObfuzIgnore(ObfuzScope.TypeName, ApplyToChildTypes = true)]` |

### 2.2 缺失部分（关键差距）

| 维度 | TEngine 现状 | DGame | 差距 |
|------|-------------|-------|------|
| **运行时静态密钥初始化** | ✅ **已补**（方案 B，见 5.1） | ✅ 有（方案 A 位置） | ~~关键缺失~~ 已解决；TEngine 进一步用 `AfterAssembliesLoaded` + `!UNITY_EDITOR` 守卫，时机更早更正确 |
| 引用跟随程序集声明 | `TEngine.Runtime, Assembly-CSharp, Launcher` | 3 个主包程序集 | ~~建议补~~ 已补 |
| buildPipeline 自动混淆 | `enable=0`（默认关） | `enable=1`（开） | 可保持关（手动触发更可控） |
| obfuscateObfuzRuntime | `=1`（混淆 runtime） | `=0`（不混淆） | TEngine 更激进；但 `ProcedureLoadAssembly` 属 Assembly-CSharp（不混淆），初始化方法无需 ObfuzIgnore（见 5.1 修正） |

### 2.3 核心问题（已解决）

~~TEngine 的 `ProcedureLoadAssembly.cs` 在 `OnEnter` 中直接调用 `LoadAssembly()`，**没有在加载混淆后的 DLL 前初始化 `EncryptionService<DefaultStaticEncryptionScope>.Encryptor`**。~~

**已修复（2026-08-28）**：`OnEnter` 现在依次执行 `SetUpStaticSecretKey()` → `LoadAssembly()`，静态密钥在加载混淆 DLL 前完成初始化（见 5.1）。

历史背景：TEngine 当前 `enabledPasses = -3`（Symbol + RemoveConstField），未启用加密类 Pass，所以修复前暂不出问题。修复后可安全切到含 ConstEncrypt/FieldEncrypt 的预设。

---

## 三、TEngine Obfuz.asset 与 DGame 配置对比

| 配置项 | TEngine | DGame | 差异说明 |
|--------|---------|-------|---------|
| `buildPipelineSettings.enable` | `0` | `1` | DGame 打包自动混淆；TEngine 需手动触发 |
| `assembliesToObfuscate` | `GameLogic, GameProto` | `GameProto, GameBattle, GameLogic` | DGame 多混淆 GameBattle |
| `nonObfuscatedButReferencingObfuscatedAssemblies` | `TEngine.Runtime, Assembly-CSharp, Launcher` | `DGame.Runtime, Assembly-CSharp, DGame.AOT` | 两者均已声明引用跟随程序集（原研究记 TEngine 为"空"系旧状态，已补） |
| `obfuscateObfuzRuntime` | `1` | `0` | TEngine 混淆 runtime（更激进） |
| `obfuscationPassSettings.enabledPasses` | `-3` | `-1` | TEngine 当前只开 Symbol+RemoveConstField；DGame 全开 |
| `secretSettings.*` | 相同 | 相同 | 密钥路径/种子一致 |
| `encryptionVMSettings.*` | 相同 | 相同 | VM 密钥/指令数一致 |
| `assembliesUsingDynamicSecretKeys` | `[]` | `[]` | 两者都未用动态密钥 |

---

## 四、资源加密与代码混淆的正交关系

Obfuz（代码混淆）和资源加密（FileOffset/FileStream/XXTEA）是**两条独立链路**，无代码耦合：

| 链路 | 作用对象 | DGame | TEngine |
|------|---------|-------|---------|
| Obfuz 代码混淆 | 程序集 DLL | `LoadAssemblyProcedure.SetUpStaticSecretKey` 初始化 | **缺失初始化** |
| 资源加密 | AssetBundle 文件 | 2 种（FileOffset/FileStream），全局统一 | 3 种（+XXTEA），每包独立 |

两者唯一的交集：混淆后的 DLL 本身作为 TextAsset 被打进 AssetBundle，由资源加密保护传输层安全；运行时先解资源加密得到 DLL 字节，再 `Assembly.Load` 后执行混淆代码（此时需要 Obfuz 密钥已就绪）。

---

## 五、TEngine 应借鉴的改进清单

### 5.1 ✅ 已实施：运行时静态密钥初始化（方案 B：RuntimeInitializeOnLoadMethod + 延迟报告）

> **方案演进**：初版（方案 A）放 `ProcedureLoadAssembly.OnEnter`，能跑但时机偏晚、且与官方推荐不符。经讨论后改为方案 B——独立 `[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]`，时机最早且职责单一。方案 A 的 ProcedureLoadAssembly 改动已回退。

#### 设计依据

1. **官方推荐时机**（2026-08-14 报告 L20-30）：`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`，主包/AOT 程序集刚加载完、任何被混淆代码执行前。
2. **Obfuz FAQ 约束**（https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/help/faq.md）：
   - **禁止在 Editor 下运行混淆后代码**——Editor 已加载原始未混淆程序集，混淆 DLL 引用混淆后类型会 "TypeLoadException: Could not resolve type"。
   - 未初始化 `EncryptionService<T>.Encryptor` 就跑混淆代码 → `$$Obfuz$RVA$` 类型初始化异常。
3. **TEngine 实情**：`ENABLE_OBFUZ` 是全局宏（`ScriptingDefineSymbols` 设到所有 BuildTargetGroup），Editor 点"开启混淆"后 Editor 下也定义该宏；但 `ProcedureLoadAssembly` 在 `EditorSimulateMode` 走 `GetMainLogicAssembly()`（取 `AppDomain` 已加载的**原始未混淆**程序集），不加载混淆 DLL。
4. **结论**：初始化代码必须用 `#if ENABLE_OBFUZ && !UNITY_EDITOR` 守卫——Editor 下整段不编译，避免对未加密常量误调用 Decrypt 破坏运行。

#### 实现

**初始化侧**：新建 `Assets/GameScripts/ObfuzRuntimeInitializer.cs`（Assembly-CSharp，不混淆）

```csharp
public static class ObfuzRuntimeInitializer
{
#if ENABLE_OBFUZ && !UNITY_EDITOR
    private static bool s_Failed;
    private static string s_ErrorMsg;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void SetUpStaticSecretKey()
    {
        var asset = Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey");
        if (asset == null || asset.bytes == null || asset.bytes.Length == 0)
        {
            s_Failed = true;
            s_ErrorMsg = "Obfuz 静态密钥加载失败：Resources/Obfuz/defaultStaticSecretKey.bytes 缺失或为空。"
                + "已启用 ConstEncrypt/FieldEncrypt 等 Pass，但无密钥将无法解密混淆代码中的常量与字段，程序将退出。";
            Log.Fatal($"[Obfuz] {s_ErrorMsg}");
            return;
        }
        EncryptionService<DefaultStaticEncryptionScope>.Encryptor =
            new GeneratedEncryptionVirtualMachine(asset.bytes);
        Log.Info("[Obfuz] Static secret key initialized (AfterAssembliesLoaded).");
    }

    /// UI 就绪后由启动流程调用，报告初始化阶段延迟的致命错误。
    /// 返回 true 表示存在致命错误、已弹出确认框，调用方应阻断后续流程。
    public static bool CheckFailureAndReport()
    {
        if (!s_Failed) return false;
        LauncherMgr.ShowMessageBox(s_ErrorMsg, Application.Quit);
        return true;
    }
#endif
}
```

**报告侧**：`ProcedureLaunch.OnEnter`（入口流程，紧接 `LauncherMgr.Initialize()` 之后 UI 就绪时）

```csharp
protected override void OnEnter(ProcedureOwner procedureOwner)
{
    base.OnEnter(procedureOwner);
    LauncherMgr.Initialize();

#if ENABLE_OBFUZ && !UNITY_EDITOR
    // Obfuz 静态密钥在 AfterAssembliesLoaded 已尝试初始化；此处 UI 就绪后报告失败并阻断流程。
    if (ObfuzRuntimeInitializer.CheckFailureAndReport())
    {
        return;  // 致命错误：弹窗已出，等待用户确认退出，不继续后续初始化
    }
#endif

    InitLanguageSettings();
    InitSoundSettings();
    LoadDeployConfigAsync().Forget();
}
```

**回退**：`ProcedureLoadAssembly.cs` 恢复到改动前原始状态（删除方案 A 加的 import / OnEnter 调用 / `SetUpStaticSecretKey()` 方法体）。

#### 关键设计点

| 维度 | 决策 | 理由 |
|------|------|------|
| 初始化时机 | `AfterAssembliesLoaded` | 官方推荐最早时机；覆盖主包 AOT 代码被混淆的常量 |
| Editor 守卫 | `#if ENABLE_OBFUZ && !UNITY_EDITOR` | FAQ 禁止 Editor 跑混淆代码；EditorSimulateMode 加载原始未混淆程序集，注入 Encryptor 反而破坏运行 |
| 失败报告时机 | `ProcedureLaunch.OnEnter`（LauncherMgr.Initialize 之后） | AfterAssembliesLoaded 时场景/UI 未就绪无法弹窗；ProcedureLaunch 是入口流程、必经、且紧接 LauncherMgr.Initialize 后 UI 可用 |
| 失败报告 UI | `LauncherMgr.ShowMessageBox(msg, Application.Quit)` | 只传 onConfirm，onUpdate/onCancel 传 null → LoadTipsUI 仅显示确认按钮；点击确认 `Application.Quit()` |
| 失败阻断 | `return` 跳过语言/声音/部署配置 + OnUpdate 卡在 `_deployConfigLoaded=false` | 密钥缺失下继续跑到 LoadAssembly 会因 Encryptor null 而崩成乱码，不如明确阻断 |
| 初始化类位置 | `Assets/GameScripts/`（Assembly-CSharp，不混淆） | 与 GameEntry.cs 同层；Assembly-CSharp 在 `nonObfuscatedButReferencingObfuscatedAssemblies`，无需 `[ObfuzIgnore]` |
| Launcher 永不混淆 | 已在 `nonObfuscatedButReferencingObfuscatedAssemblies` | Launcher 代码可安全在密钥初始化前/后执行；但 UI 显示仍受场景依赖制约（故延迟报告） |

#### 为何不用 `[ObfuzIgnore]`（沿袭方案 A 的修正结论）

`ObfuzRuntimeInitializer` 在 Assembly-CSharp，不在 `assembliesToObfuscate = {GameLogic, GameProto}` 中，任何 Pass 都不会处理它，attribute 完全多余。`obfuscateObfuzRuntime=1` 影响的是 `Packages/com.code-philosophy.obfuz/Runtime`（Obfuz.Runtime 程序集自身），与 Assembly-CSharp 无关。

#### 对 DGame 原版的改进

1. DGame 放 `ProcedureLoadAssembly.OnEnter`（方案 A 位置），TEngine 改为 `AfterAssembliesLoaded`（方案 B，时机更早更正确）。
2. DGame 直接 `Resources.Load(...).bytes` 无校验，TEngine 增加密钥 asset/bytes 空值检查 + `Log.Fatal` + 延迟弹窗。
3. DGame 无 Editor 守卫（DGame 的 `ENABLE_OBFUZ` 是否区分 Editor 未考证），TEngine 显式 `!UNITY_EDITOR` 规避 FAQ 警告。

### 5.2 ✅ 已就绪：引用跟随程序集声明

`Obfuz.asset` 的 `nonObfuscatedButReferencingObfuscatedAssemblies` 已配置：

```yaml
nonObfuscatedButReferencingObfuscatedAssemblies:
- TEngine.Runtime
- Assembly-CSharp
- Launcher
```

覆盖了主包中所有引用混淆类型（GameLogic/GameProto）但自身不混淆的程序集，调用点会被 Obfuz 同步改写，不会 `MissingMethodException`。

### 5.3 动态密钥扩展方案（部分已落地）

两者都未用动态密钥（`assembliesUsingDynamicSecretKeys: []`）。本节为 TEngine 制定完整的动态密钥落地方案。

#### 5.3.1 为什么需要动态密钥

| 维度 | 静态密钥（现状） | 动态密钥（目标） |
|------|-----------------|-----------------|
| 适用范围 | AOT / 启动早期代码（Assembly-CSharp、TEngine.Runtime、Launcher） | 热更新程序集（GameLogic、GameProto） |
| 密钥绑定 | 与主包绑定，主包发布后不可改 | 与热更版本绑定，可随热更轮换 |
| 轮换能力 | ❌ 不可轮换（旧主包无法解密新密钥） | ✅ 每次热更可换新密钥 |
| 安全等级 | 中（密钥随主包出包，可逆向提取） | 高（密钥可不随主包出包，由服务端下发或热更资源下载） |
| 逆向成本 | 提取主包即得密钥 | 需逆向混淆 VM + 动态获取密钥，成本显著提升 |

核心价值：**动态密钥让热更代码的加密密钥可随版本轮换**，攻击者从旧包提取的密钥无法解密新热更版本的混淆常量/字段。

#### 5.3.2 Obfuz 动态密钥的工作原理（源码验证）

**混淆期（编辑器/打包时）**——`Obfuscator.cs:284-296` + `ObfuscationPassContext.cs:50-72`：

```csharp
// Obfuscator.CreateEncryptionScopeProvider()
var defaultStaticScope = CreateEncryptionScope(_coreSettings.defaultStaticSecretKey);  // 静态 scope
var defaultDynamicScope = CreateEncryptionScope(_coreSettings.defaultDynamicSecretKey); // 动态 scope
// 校验：assembliesUsingDynamicSecretKeys 中的程序集必须在 assembliesToObfuscate 中
return new EncryptionScopeProvider(defaultStaticScope, defaultDynamicScope, _assembliesUsingDynamicSecretKeys);

// EncryptionScopeProvider.GetScope(module)
// → 若 module 属于 assembliesUsingDynamicSecretKeys → 返回 dynamicScope（用动态密钥加密）
// → 否则 → 返回 staticScope（用静态密钥加密）
```

**运行时**——必须手动初始化 `EncryptionService<DefaultDynamicEncryptionScope>.Encryptor`：

```csharp
// 动态 scope 的解密器，与静态 scope 使用同一个 GeneratedEncryptionVirtualMachine 类
// 但接收的是动态密钥字节流（defaultDynamicSecretKey.bytes）
EncryptionService<DefaultDynamicEncryptionScope>.Encryptor =
    new GeneratedEncryptionVirtualMachine(dynamicSecretBytes);
```

**关键约束**：
1. `assembliesUsingDynamicSecretKeys` 中的程序集必须同时出现在 `assembliesToObfuscate` 中（Obfuscator.cs:290-293 会校验，不满足抛异常）。
2. 同一个 `GeneratedEncryptionVirtualMachine` 类同时服务静态和动态 scope——区别仅在于喂给构造函数的密钥字节不同。VM 代码生成密钥（`codeGenerationSecretKey`）决定的是指令表结构，与静态/动态密钥无关，不可随热更改。
3. 动态密钥初始化必须在**使用动态 Scope 的任何混淆代码执行前**完成，否则触发 `$$Obfuz$RVA$` 类型初始化异常。

#### 5.3.3 TEngine 动态密钥落地方案

##### A. 配置变更（`ProjectSettings/Obfuz.asset`）

> **已落地**：`dynamicSecretKeyOutputPath` 已从 `Assets/Resources/Obfuz/` 迁移到 `Assets/AssetRaw/DLL/Obfuz/defaultDynamicSecretKey.bytes`，密钥文件作为 YooAsset 热更资源管理、不随主包出包。`defaultStaticSecretKey` / `defaultDynamicSecretKey` 种子值与 `assembliesUsingDynamicSecretKeys` 待在 `ObfuzConfigWindow`「加密与密钥」页统一配置（页面已交付，见 5.3.4）。

当前 Obfuz.asset `secretSettings` 实际状态：

```yaml
secretSettings:
  defaultStaticSecretKey: Code Philosophy-Static                              # 待替换为自定义种子（后续 editor 页面）
  defaultDynamicSecretKey: Code Philosophy-Dynamic                            # 待替换为自定义种子（后续 editor 页面）
  staticSecretKeyOutputPath: Assets/Resources/Obfuz/defaultStaticSecretKey.bytes   # 静态密钥仍在 Resources（AfterAssembliesLoaded 时 YooAsset 未就绪）
  dynamicSecretKeyOutputPath: Assets/AssetRaw/DLL/Obfuz/defaultDynamicSecretKey.bytes  # ← 已迁移到热更资源目录
  randomSeed: 0
  assembliesUsingDynamicSecretKeys: []                                        # 待填入 GameLogic（后续 editor 页面）
```

> **静态/动态密钥路径分离的设计依据**：静态密钥在 `AfterAssembliesLoaded` 初始化，此时 YooAsset 尚未就绪，必须走 `Resources.Load`（主包内）；动态密钥在 `ProcedureLoadAssembly` 初始化，此时 YooAsset 已完成热更下载，走 `LoadAssetAsync`（热更资源）。两者 IO 路径不同，故配置路径必须分离。
>
> **GameProto 慎重**：协议 DTO 的字段名若被序列化框架（JSON/网络协议）按名反射，启用 FieldEncrypt 会破坏序列化。建议 GameProto **不**纳入动态密钥 scope，或仅启用 Symbol+RemoveConstField Pass（不启用 FieldEncrypt）。

##### B. 密钥分发策略（已落地）

动态密钥文件 `defaultDynamicSecretKey.bytes` **不应随主包出包**（否则等于静态密钥）。两种分发方式：

| 方式 | 实现 | 适用场景 | 复杂度 |
|------|------|---------|--------|
| **方式一：热更资源下载** | 将密钥文件作为热更资源打入 YooAsset 资源包，运行时通过 `LoadAssetAsync<TextAsset>` 加载 | 中等安全要求、无服务端密钥管理 | 低 |
| **方式二：服务端下发** | 登录/设备认证后，服务端通过加密通道下发密钥字节；与用户/设备/版本绑定 | 高安全要求 | 高 |

**已采用方式一**（TEngine 已有 YooAsset 热更基础设施）：
- ✅ `defaultDynamicSecretKey.bytes` 已迁移到 `Assets/AssetRaw/DLL/Obfuz/`（YooAsset 热更资源目录）
- ✅ 主包不包含该文件——通过 YooAsset 的 buildin 资源过滤或独立资源包配置排除
- 运行时在热更 DLL 加载前、通过 YooAsset 的 `LoadAssetAsync<TextAsset>` 加载密钥

##### C. 运行时初始化时机（关键）

回顾 TEngine 启动流程链（`ProcedureLaunch → ProcedureSplash → ProcedureInitPackage → ProcedureInitResources → ProcedureCreateDownloader → ProcedureDownloadFile → ProcedureLoadAssembly → ProcedureStartGame`）：

```
AfterAssembliesLoaded（静态密钥初始化，已有 ObfuzRuntimeInitializer）
  ↓
ProcedureLaunch（UI 就绪，检查静态密钥失败报告）
  ↓
ProcedureInitPackage / ProcedureInitResources / ProcedureDownloadFile（YooAsset 初始化 + 热更下载）
  ↓
ProcedureLoadAssembly（加载热更 DLL 前，必须先初始化动态密钥！）  ← 新增动态密钥初始化点
  ↓
Assembly.Load(热更DLL) → GameApp.Entrance
```

**动态密钥初始化必须插入 `ProcedureLoadAssembly.LoadAssembly()` 中、`Assembly.Load(dllBytes)` 之前**——因为热更 DLL 中的混淆代码一旦被 Assembly.Load 加载，其类型静态构造器可能立即执行（引用了被混淆的常量/字段），此时动态 scope 的 Encryptor 必须已就绪。

##### D. 实现代码方案

**方案 1：扩展 `ObfuzRuntimeInitializer`（推荐——职责集中）**

在 `Assets/GameScripts/ObfuzRuntimeInitializer.cs`（Assembly-CSharp，不混淆）中新增动态密钥初始化：

```csharp
public static class ObfuzRuntimeInitializer
{
#if ENABLE_OBFUZ && !UNITY_EDITOR
    private static bool s_Failed;
    private static string s_ErrorMsg;
    private static byte[] s_DynamicSecretKey;  // 动态密钥字节缓存

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void SetUpStaticSecretKey()
    {
        // ... 静态密钥初始化（已有，不变）...
    }

    /// <summary>
    /// 加载动态密钥。由 ProcedureLoadAssembly 在加载热更 DLL 前调用。
    /// 密钥文件作为热更资源通过 YooAsset 加载，不随主包出包。
    /// </summary>
    /// <returns>true=成功；false=失败（已记 Log.Fatal，调用方应阻断加载流程）</returns>
    public static async UniTask<bool> SetUpDynamicSecretKeyAsync()
    {
        // 静态密钥初始化失败则不继续
        if (s_Failed)
        {
            return false;
        }

        // 通过 YooAsset 加载动态密钥（密钥文件作为热更 TextAsset 资源）
        // location：defaultDynamicSecretKey，放在热更资源包中
        TextAsset keyAsset = null;
        try
        {
            keyAsset = await GameModule.Resource.LoadAssetAsync<TextAsset>("defaultDynamicSecretKey");
        }
        catch (Exception e)
        {
            s_Failed = true;
            s_ErrorMsg = $"Obfuz 动态密钥加载失败：YooAsset 加载异常。{e.Message}";
            Log.Fatal($"[Obfuz] {s_ErrorMsg}");
            return false;
        }

        if (keyAsset == null || keyAsset.bytes == null || keyAsset.bytes.Length == 0)
        {
            s_Failed = true;
            s_ErrorMsg = "Obfuz 动态密钥加载失败：defaultDynamicSecretKey 资源缺失或为空。"
                + "已将 GameLogic 纳入 assembliesUsingDynamicSecretKeys，但无动态密钥将无法解密混淆代码。";
            Log.Fatal($"[Obfuz] {s_ErrorMsg}");
            if (keyAsset != null) GameModule.Resource.UnloadAsset(keyAsset);
            return false;
        }

        s_DynamicSecretKey = keyAsset.bytes;
        GameModule.Resource.UnloadAsset(keyAsset);

        EncryptionService<DefaultDynamicEncryptionScope>.Encryptor =
            new GeneratedEncryptionVirtualMachine(s_DynamicSecretKey);
        Log.Info("[Obfuz] Dynamic secret key initialized (before Assembly.Load).");
        return true;
    }

    public static bool CheckFailureAndReport()
    {
        if (!s_Failed) return false;
        LauncherMgr.ShowMessageBox(s_ErrorMsg, Application.Quit);
        return true;
    }
#endif
}
```

**调用侧：`ProcedureLoadAssembly.LoadAssembly()` 中插入动态密钥初始化**

```csharp
private async UniTaskVoid LoadAssembly()
{
    _loadAssemblyComplete = false;
    _hotfixAssemblyList = new List<Assembly>();

#if ENABLE_OBFUZ && !UNITY_EDITOR
    // 动态密钥必须在 Assembly.Load 热更 DLL 前初始化！
    // 此时 YooAsset 已初始化（ProcedureInitResources 已完成），可加载热更密钥资源
    if (!await ObfuzRuntimeInitializer.SetUpDynamicSecretKeyAsync())
    {
        // 动态密钥加载失败：标记失败，由 OnUpdate/后续流程报告
        // 此处不 return（保持与静态密钥相同的延迟报告模式）
        // 但绝不能继续 Assembly.Load——否则混淆代码无密钥会崩
        _loadAssemblyComplete = true;  // 跳过加载，直接进入 OnUpdate 的完成检查
        return;
    }
#endif

    // ... 后续原有的 AOT metadata + DLL 加载逻辑不变 ...
}
```

> **注意**：`ProcedureLoadAssembly` 属 Assembly-CSharp（不混淆），调用 `ObfuzRuntimeInitializer` 无 ObfuzIgnore 需求。动态密钥初始化方法用 `#if ENABLE_OBFUZ && !UNITY_EDITOR` 守卫，与静态密钥一致。

##### E. Editor 守卫与测试约束

| 约束 | 说明 |
|------|------|
| `!UNITY_EDITOR` 守卫 | 与静态密钥一致——EditorSimulateMode 加载原始未混淆程序集，常量未加密，注入 Encryptor 会破坏运行 |
| 真机验证 | 启用动态密钥后只能在真机测试（Obfuz FAQ 禁止 Editor 跑混淆代码） |
| 密钥文件不入主包 | ✅ 已迁移到 `Assets/AssetRaw/DLL/Obfuz/`（YooAsset 热更资源目录）；主包只保留 `Assets/Resources/Obfuz/defaultStaticSecretKey.bytes` |

##### F. 密钥轮换流程（「加密与密钥」页承载，暂无自动化）

> **轮换为手动分步操作**：密钥种子更新、密钥文件重新生成、`assembliesUsingDynamicSecretKeys` 配置已由 `ObfuzConfigWindow`「加密与密钥」页承载；一键轮换、种子/文件失配检测、密钥台账等自动化增强待定。以下记录轮换原理供参考。

动态密钥的核心价值是轮换。轮换步骤（由后续 editor 页面封装）：

1. 在 `Obfuz.asset` 中修改 `defaultDynamicSecretKey` 为新种子值
2. 运行 `Obfuz/GenerateSecretKeyFile` 重新生成密钥文件
3. 将新的 `defaultDynamicSecretKey.bytes` 打入热更资源包
4. 用混淆器重新混淆热更 DLL（`assembliesUsingDynamicSecretKeys` 中的程序集会自动用新动态密钥加密）
5. 发布热更版本

**不可轮换项**（参数冻结矩阵）：
- `codeGenerationSecretKey`（VM 代码生成密钥）——VM 在 AOT 时固化，不可随热更改
- `encryptionOpCodeCount`——ops 解码基数，不可改
- `defaultStaticSecretKey`——静态密钥与主包绑定，不可改

#### 5.3.4 实施检查清单

- [x] ~~将 `defaultDynamicSecretKey.bytes` 从 `Assets/Resources/Obfuz/` 移除，改为热更资源~~ → 已迁移到 `Assets/AssetRaw/DLL/Obfuz/`
- [x] ~~`Obfuz.asset` 的 `dynamicSecretKeyOutputPath` 同步更新~~ → 已改为 `Assets/AssetRaw/DLL/Obfuz/defaultDynamicSecretKey.bytes`
- [x] ~~密钥轮换 editor 页面~~ → 已随 `ObfuzConfigWindow`「加密与密钥」分页交付（2026-08-16 会话，`TEngine/Build/混淆配置窗口`）：静态/动态种子编辑+随机、`assembliesUsingDynamicSecretKeys` 下拉（候选取自 HybridCLR 热更程序集）、生成密钥文件按钮、默认值健康检查与参数冻结提示，无需再建独立窗口
- [ ] 实际操作：替换 `defaultDynamicSecretKey` 默认种子（`Obfuz.asset` 当前仍为 `Code Philosophy-Dynamic`；静态种子 `Code Philosophy-Static` 同样未替换）——在「加密与密钥」页操作
- [ ] 实际操作：将 `GameLogic` 填入 `assembliesUsingDynamicSecretKeys`（当前为空）——在「加密与密钥」页操作
- [ ] 实际操作：运行"生成密钥文件"重新生成密钥文件——在「加密与密钥」页操作
- [x] ~~实现 `ObfuzRuntimeInitializer.SetUpDynamicSecretKeyAsync()`~~ → 已实现（commit a66b0020：YooAsset 热更资源加载 + 空值校验 + 失败延迟报告）
- [x] ~~在 `ProcedureLoadAssembly.LoadAssembly()` 中、`Assembly.Load` 前调用动态密钥初始化~~ → 已接入（`ProcedureLoadAssembly.cs:98`，位于热更 DLL 加载循环之前，失败弹窗并阻断 `Assembly.Load`）
- [ ] 真机验证：静态密钥 → 动态密钥 → 热更 DLL 加载 → GameApp.Entrance 全链路
- [ ] 评估 `GameProto` 是否纳入动态 scope（序列化兼容性验证）

---

## 六、结论

TEngine 的 Obfuz 集成在**编辑器侧**（ObfuzConfigWindow 配置窗口、BuildDLLCommand 混淆链路、健康检查）已经比 DGame 更完善。

**运行时静态密钥初始化已于 2026-08-28 补齐，方案演进 A→B**：
- ~~方案 A：放 `ProcedureLoadAssembly.OnEnter`~~（已回退）——能跑但时机偏晚、与官方推荐不符。
- **方案 B（最终）**：独立 `ObfuzRuntimeInitializer`，`[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]` 初始化 + `#if ENABLE_OBFUZ && !UNITY_EDITOR` 守卫；失败延迟到 `ProcedureLaunch.OnEnter`（`LauncherMgr.Initialize` 后 UI 就绪）经 `LauncherMgr.ShowMessageBox` 弹仅含确认按钮的原生对话框，点击 `Application.Quit()` 退出。

方案 B 相对 DGame 的三点改进：① 时机从 ProcedureLoadAssembly 提前到 AfterAssembliesLoaded（覆盖主包 AOT 代码被混淆的常量）；② 密钥 asset/bytes 空值校验 + `Log.Fatal` + 延迟弹窗（DGame 无校验）；③ `!UNITY_EDITOR` 守卫规避 FAQ "Editor 不可跑混淆代码" 警告。

引用跟随程序集声明（`nonObfuscatedButReferencingObfuscatedAssemblies`）也已就位（`TEngine.Runtime, Assembly-CSharp, Launcher`）。

**动态密钥扩展方案（5.3）已部分落地**：密钥文件已迁移到 `Assets/AssetRaw/DLL/Obfuz/defaultDynamicSecretKey.bytes`（YooAsset 热更资源目录），`Obfuz.asset` 的 `dynamicSecretKeyOutputPath` 已同步更新。通过源码验证了 Obfuz 的 `EncryptionScopeProvider.GetScope()` 按 `assembliesUsingDynamicSecretKeys` 分配静态/动态 scope 的机制；方案核心是在 `ProcedureLoadAssembly.LoadAssembly()` 中、`Assembly.Load` 前调用 `ObfuzRuntimeInitializer.SetUpDynamicSecretKeyAsync()`（通过 YooAsset 加载热更密钥资源），确保动态 scope 的 Encryptor 在混淆代码执行前就绪。运行时初始化已落地（2026-08-28，commit 21e61c9b 静态 / a66b0020 动态）。密钥轮换 editor 页面已于 2026-08-16 随 `ObfuzConfigWindow`「加密与密钥」分页交付（种子编辑与随机、`assembliesUsingDynamicSecretKeys` 选择、密钥文件生成、默认值健康检查与参数冻结提示），无需再建独立窗口；剩余为实际操作项（替换默认种子、填入 GameLogic、重新生成密钥文件）、真机验证与 GameProto 动态 scope 评估。

至此 TEngine 的 Obfuz 链路完整。当前 `enabledPasses=-3`（只 Symbol+RemoveConstField）不出问题，切到含 ConstEncrypt/FieldEncrypt 的预设前现已具备运行时解密能力。**注意**：启用加密 Pass 后只能在真机验证，不可在 Editor 下测试（Obfuz FAQ 明确禁止）。

**研究→实施中修正的误判**（供后续参考）：
1. `[ObfuzIgnore(ObfuzScope.MethodBody)]` 不需要——`ObfuzRuntimeInitializer` 属 Assembly-CSharp，不在 `assembliesToObfuscate` 中，任何 Pass 都不会处理它。
2. "引用跟随程序集声明缺失"系旧状态，Obfuz.asset 现已配置完整。
3. 方案 A 时机偏晚——ProcedureLoadAssembly 在流程机中部，晚于 YooAsset/场景/GameEntry；若主包 AOT 代码被混淆会漏。方案 B 提前到 AfterAssembliesLoaded 修正此隐患。

---

## 关联文档

- [2026-08-14 Obfuz 运行时初始化与混淆范围研究](./2026-08-14-obfuz-runtime-and-scope-research.md) — Obfuz 3.1.0 运行时初始化顺序、RegisterReflectionType 语义、混淆范围决策矩阵
- [2026-08-28 DGame 与 TEngine 启动加载流程对比研究](./2026-08-28-dgame-vs-tengine-startup-flow-comparison.md) — 两个项目完整启动流程链路对比
