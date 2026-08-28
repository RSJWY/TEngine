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
| **运行时静态密钥初始化** | ❌ **ProcedureLoadAssembly 中无 SetUpStaticSecretKey** | ✅ 有 | **关键缺失** |
| 引用跟随程序集声明 | 空 | 3 个主包程序集 | 建议补 |
| buildPipeline 自动混淆 | `enable=0`（默认关） | `enable=1`（开） | 可保持关（手动触发更可控） |
| obfuscateObfuzRuntime | `=1`（混淆 runtime） | `=0`（不混淆） | TEngine 更激进，需确保初始化方法有 ObfuzIgnore |

### 2.3 核心问题

TEngine 的 `ProcedureLoadAssembly.cs` 在 `OnEnter` 中直接调用 `LoadAssembly()`，**没有在加载混淆后的 DLL 前初始化 `EncryptionService<DefaultStaticEncryptionScope>.Encryptor`**。

后果：如果启用了 `ConstEncrypt`/`FieldEncrypt` 等 Pass，混淆后的常量/字段在运行时无法解密，会崩溃或读到错误值。

TEngine 当前 `enabledPasses = -3`（Symbol + RemoveConstField），暂未启用加密类 Pass，所以暂时不会崩。但一旦通过 ObfuzConfigWindow 切到"均衡"或"强化"预设（含 ConstEncrypt/FieldEncrypt），就会出问题。

---

## 三、TEngine Obfuz.asset 与 DGame 配置对比

| 配置项 | TEngine | DGame | 差异说明 |
|--------|---------|-------|---------|
| `buildPipelineSettings.enable` | `0` | `1` | DGame 打包自动混淆；TEngine 需手动触发 |
| `assembliesToObfuscate` | `GameLogic, GameProto` | `GameProto, GameBattle, GameLogic` | DGame 多混淆 GameBattle |
| `nonObfuscatedButReferencingObfuscatedAssemblies` | **空** | `DGame.Runtime, Assembly-CSharp, DGame.AOT` | **TEngine 缺引用跟随声明** |
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

### 5.1 必须补：运行时静态密钥初始化

在 `ProcedureLoadAssembly.cs` 的 `OnEnter` 最前面加 `SetUpStaticSecretKey()`，参考 DGame 实现：

```csharp
protected override void OnEnter(IFsm<IProcedureModule> procedureOwner)
{
    base.OnEnter(procedureOwner);
    Log.Debug($"HybridCLR ProcedureLoadAssembly OnEnter, package: {_assemblyPackageName}");
    _procedureOwner = procedureOwner;
    SetUpStaticSecretKey();  // 新增
    LoadAssembly().Forget();
}

#if ENABLE_OBFUZ
[Obfuz.Ignore(ObfuzScope.MethodBody)]  // 方法体不能被加密 Pass 处理
#endif
private void SetUpStaticSecretKey()
{
#if ENABLE_OBFUZ
    Log.Info("Enable Obfuz");
    EncryptionService<DefaultStaticEncryptionScope>.Encryptor =
        new GeneratedEncryptionVirtualMachine(
            Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey").bytes);
    Log.Info("SetUpStaticSecret complete");
#else
    Log.Info("Disable Obfuz");
#endif
}
```

**注意事项**：
- `ProcedureLoadAssembly` 属于 Assembly-CSharp（主包，不热更），引用 Obfuz runtime 无热更边界问题
- 密钥文件在 `Resources/` 下随主包出包，不参与热更——静态密钥必须固化在主包
- 方法需 `[ObfuzIgnore(ObfuzScope.MethodBody)]`，因为 `obfuscateObfuzRuntime=1` 时 Obfuz runtime 自身被混淆，初始化方法若被 ConstEncrypt/ExprObfus 处理可能产生循环依赖

### 5.2 建议补：引用跟随程序集声明

`Obfuz.asset` 的 `nonObfuscatedButReferencingObfuscatedAssemblies` 应补上：

```yaml
nonObfuscatedButReferencingObfuscatedAssemblies:
- TEngine.Runtime
- Assembly-CSharp
- Launcher
```

不补的后果：这些程序集 IL 中对混淆后类型/方法的调用点不会被同步改写，可能运行时 `MissingMethodException`。

### 5.3 可选：动态密钥扩展（未来）

两者都未用动态密钥。若未来需要更高安全级别：
- 在 `GameApp.Entrance`（热更入口）中、任何使用动态 Scope 的代码前初始化
- 密钥文件作为热更资源通过 YooAsset 加载（而非 Resources.Load）
- `assembliesUsingDynamicSecretKeys` 填入使用动态密钥的程序集名
- 动态密钥可随热更轮换，增加逆向难度

---

## 六、结论

TEngine 的 Obfuz 集成在**编辑器侧**（ObfuzConfigWindow 配置窗口、BuildDLLCommand 混淆链路、健康检查）已经比 DGame 更完善，但**运行时缺了最关键的一环**——ProcedureLoadAssembly 中没有像 DGame 那样在加载 DLL 前初始化静态密钥。

补上 `SetUpStaticSecretKey()` + 引用跟随程序集声明，TEngine 的 Obfuz 链路就完整了。当前 `enabledPasses=-3`（只 Symbol+RemoveConstField）暂不出问题，但切到含加密 Pass 的预设前必须补上。

---

## 关联文档

- [2026-08-14 Obfuz 运行时初始化与混淆范围研究](./2026-08-14-obfuz-runtime-and-scope-research.md) — Obfuz 3.1.0 运行时初始化顺序、RegisterReflectionType 语义、混淆范围决策矩阵
- [2026-08-28 DGame 与 TEngine 启动加载流程对比研究](./2026-08-28-dgame-vs-tengine-startup-flow-comparison.md) — 两个项目完整启动流程链路对比
