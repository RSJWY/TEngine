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

### 5.3 可选：动态密钥扩展（未来）

两者都未用动态密钥。若未来需要更高安全级别：
- 在 `GameApp.Entrance`（热更入口）中、任何使用动态 Scope 的代码前初始化
- 密钥文件作为热更资源通过 YooAsset 加载（而非 Resources.Load）
- `assembliesUsingDynamicSecretKeys` 填入使用动态密钥的程序集名
- 动态密钥可随热更轮换，增加逆向难度

---

## 六、结论

TEngine 的 Obfuz 集成在**编辑器侧**（ObfuzConfigWindow 配置窗口、BuildDLLCommand 混淆链路、健康检查）已经比 DGame 更完善。

**运行时初始化已于 2026-08-28 补齐，方案演进 A→B**：
- ~~方案 A：放 `ProcedureLoadAssembly.OnEnter`~~（已回退）——能跑但时机偏晚、与官方推荐不符。
- **方案 B（最终）**：独立 `ObfuzRuntimeInitializer`，`[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]` 初始化 + `#if ENABLE_OBFUZ && !UNITY_EDITOR` 守卫；失败延迟到 `ProcedureLaunch.OnEnter`（`LauncherMgr.Initialize` 后 UI 就绪）经 `LauncherMgr.ShowMessageBox` 弹仅含确认按钮的原生对话框，点击 `Application.Quit()` 退出。

方案 B 相对 DGame 的三点改进：① 时机从 ProcedureLoadAssembly 提前到 AfterAssembliesLoaded（覆盖主包 AOT 代码被混淆的常量）；② 密钥 asset/bytes 空值校验 + `Log.Fatal` + 延迟弹窗（DGame 无校验）；③ `!UNITY_EDITOR` 守卫规避 FAQ "Editor 不可跑混淆代码" 警告。

引用跟随程序集声明（`nonObfuscatedButReferencingObfuscatedAssemblies`）也已就位（`TEngine.Runtime, Assembly-CSharp, Launcher`）。

至此 TEngine 的 Obfuz 链路完整。当前 `enabledPasses=-3`（只 Symbol+RemoveConstField）不出问题，切到含 ConstEncrypt/FieldEncrypt 的预设前现已具备运行时解密能力。**注意**：启用加密 Pass 后只能在真机验证，不可在 Editor 下测试（Obfuz FAQ 明确禁止）。

**研究→实施中修正的误判**（供后续参考）：
1. `[ObfuzIgnore(ObfuzScope.MethodBody)]` 不需要——`ObfuzRuntimeInitializer` 属 Assembly-CSharp，不在 `assembliesToObfuscate` 中，任何 Pass 都不会处理它。
2. "引用跟随程序集声明缺失"系旧状态，Obfuz.asset 现已配置完整。
3. 方案 A 时机偏晚——ProcedureLoadAssembly 在流程机中部，晚于 YooAsset/场景/GameEntry；若主包 AOT 代码被混淆会漏。方案 B 提前到 AfterAssembliesLoaded 修正此隐患。

---

## 关联文档

- [2026-08-14 Obfuz 运行时初始化与混淆范围研究](./2026-08-14-obfuz-runtime-and-scope-research.md) — Obfuz 3.1.0 运行时初始化顺序、RegisterReflectionType 语义、混淆范围决策矩阵
- [2026-08-28 DGame 与 TEngine 启动加载流程对比研究](./2026-08-28-dgame-vs-tengine-startup-flow-comparison.md) — 两个项目完整启动流程链路对比
