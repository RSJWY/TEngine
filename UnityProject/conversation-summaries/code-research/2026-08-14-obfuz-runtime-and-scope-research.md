# Obfuz运行时初始化与混淆范围研究

**研究日期**：2026-08-14  
**研究范围**：Obfuz `3.1.0` 运行时初始化、HybridCLR 热更新加载、反射类型映射、TEngine 混淆范围决策  
**关键词**：Obfuz、HybridCLR、EncryptionService、静态密钥、动态密钥、ObfuscationInstincts、ObfuscationTypeMapper、RegisterReflectionType、GameApp、GameLogic、GameProto、字段加密、符号混淆

## 结论摘要

1. 使用加密类 Pass 时，必须先初始化对应 `EncryptionService<TScope>.Encryptor`，再执行该 Scope 内任何混淆代码。
2. 热更新反射类型注册应发生在热更新 DLL 加载后、第一次 TypeMapper 查询和业务初始化前。
3. `RegisterReflectionType<T>()` 只服务于“使用混淆前类型全名查找 Type”的代码，不需要给所有被反射或所有热更新类型注册。
4. 注册不会自动修复 `Type.GetType`/`Assembly.GetType`，查询方必须改用 `ObfuscationTypeMapper.GetTypeByOriginalFullName`。
5. 注册方法可以被混淆；为了减少启动依赖，可以只对其禁用 `MethodBody`，方法名仍可混淆。
6. 生产基线应是大范围 Symbol、小范围函数体混淆、极少量 FieldEncrypt；协议、序列化、反射入口和 Unity 约定名称保持稳定。

## 运行时启动顺序

### 官方要求

静态密钥一般用于 AOT 或启动早期代码，官方建议在程序集加载完成时初始化：

```csharp
[ObfuzIgnore]
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
private static void SetUpStaticSecret()
{
    EncryptionService<DefaultStaticEncryptionScope>.Encryptor =
        new GeneratedEncryptionVirtualMachine(staticSecretBytes);
}
```

此函数不能依赖尚未初始化的解密服务，否则会递归或触发 `$Obfuz$RVA$` 类型初始化异常。

### TEngine推荐顺序

```text
Unity加载主包/AOT程序集
  -> 初始化静态Encryption Scope
  -> YooAsset初始化、检查版本和下载热更新资源
  -> 取得并初始化动态Secret（GameLogic/GameProto若使用动态Scope）
  -> 加载AOT补充元数据
  -> Assembly.Load热更新DLL
  -> 注册需要按原始全名查询的Type
  -> 反射调用GameApp.Entrance
  -> 初始化事件、模块、UI和业务逻辑
```

动态 Secret 应尽量不放在主包中，可随热更新资源下载或由服务端下发。必须在任何使用动态 Scope 的代码执行前初始化。

## RegisterReflectionType的准确语义

### 混淆前源码

```csharp
ObfuscationInstincts.RegisterReflectionType<LoginService>();
```

### Obfuz早期Pass改写结果

`InstinctPass` 会取得混淆前全名，并改写为等价代码：

```csharp
ObfuscationTypeMapper.RegisterType<LoginService>(
    "GameLogic.LoginService");
```

之后 Symbol Pass 可以继续修改 `LoginService` 的实际类型名；泛型 Type 引用会跟随更新，而字符串保存原始全名。

### 什么时候需要注册

需要：

```csharp
// 外部数据、配置或脚本只提供混淆前全名
ObfuscationTypeMapper.GetTypeByOriginalFullName(
    assembly,
    "GameLogic.LoginService");
```

不需要：

```csharp
typeof(LoginService);
Activator.CreateInstance(typeof(LoginService));
GetComponent<LoginService>();
obj.GetType();
```

这些代码直接持有编译期 Type 引用，Obfuz 会同步改写。

### 不能解决的问题

类型注册只处理类型全名，不处理成员名：

```csharp
type.GetMethod("Login");
type.GetField("Gold");
type.GetProperty("Name");
Enum.Parse(typeof(State), "Ready");
```

此类目标必须保留相应成员名，或改用稳定业务 ID/自定义映射。

### 注册时机与重复注册

- 热更新类型只能在 DLL 已加载后注册。
- 必须早于第一次 `GetTypeByOriginalFullName`。
- XLua 必须早于 `LuaEnv` 创建。
- 每个 Type 每个进程只注册一次；重复注册会抛 `ArgumentException`。

### 注册代码能否混淆

可以。注册调用会先被 Instinct Pass 改写，后续 Symbol/Const/Call/ControlFlow 仍可能处理包围它的方法。

保守方案：

```csharp
[ObfuzIgnore(ObfuzScope.MethodBody)]
private static void RegisterReflectionTypes()
{
    ObfuscationInstincts.RegisterReflectionType<LoginService>();
}
```

该方法由 `GameApp.Entrance` 直接调用，因此方法名可以混淆。只有当它本身也通过字符串反射调用时才需要保留方法名。

推荐放在热更新入口最前面：

```csharp
public static void Entrance(object[] objects)
{
    RegisterReflectionTypes();
    GameEventHelper.Init();
    // 其余业务初始化
}
```

若注册方法体允许常量/调用等 Pass，则必须确保动态 Encryption Scope 已先初始化。

## 混淆范围决策

### 通常混淆什么

1. **符号名称**：内部类型、namespace、私有字段、内部方法、参数、property/event。
2. **核心方法体**：关键常量、表达式、调用关系和控制流。
3. **少量运行时字段**：货币、战斗关键状态、风控状态等。

### 通常保留什么

- `GameApp.Entrance` 等主包反射入口；
- UI 类型名等于资源地址的类型；
- JSON、网络协议和存档字段名；
- Unity 序列化字段、UnityEvent 方法和 Unity 消息函数；
- 通过字符串反射查找的成员；
- 第三方公开接口和 native 回调；
- 热路径、Burst/DOTS、资源和网络底层的高成本函数体 Pass。

### TEngine推荐矩阵

| 区域 | Symbol | Const/Expr | Call/ControlFlow | FieldEncrypt |
|---|---|---|---|---|
| `GameLogic`内部类型和成员 | 默认开启 | 核心方法开启，level 1 | 少量低频关键方法 | 极少数关键状态 |
| UI派生类型 | 保留类型名；内部成员按需 | 通常关闭或低强度 | 关闭 | 关闭 |
| `GameApp`入口 | 保留类型名和入口方法名 | 注册/启动方法保守 | 关闭 | 关闭 |
| `GameProto`协议DTO | 类型名视协议而定；字段/property默认保留 | 通常关闭 | 关闭 | 关闭 |
| JSON/存档/配置模型 | 协议名必须稳定或显式指定 | 通常关闭 | 关闭 | 关闭 |
| `TEngine.Runtime`、`Launcher` | 一般不主动混淆 | 关闭 | 关闭 | 关闭；但需同步被混淆程序集引用 |

### 推荐初始基线

```text
GameLogic/GameProto
  -> Symbol + RemoveConstField

GameLogic核心业务方法
  -> Const(level 1) + Expr(Basic)

少量安全关键调用链
  -> Call或ControlFlow

少量关键运行时状态
  -> FieldEncrypt(level 1)

协议、存档、配置、Unity序列化、反射入口
  -> 保留协议需要的名称，禁用FieldEncrypt
```

## 当前工程落点

关键文件：

| 文件 | 作用 |
|---|---|
| `ProjectSettings/Obfuz.asset` | 程序集、Pass、Secret、VM、mapping、多态 DLL 配置 |
| `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs` | HybridCLR 编译和 Obfuz 热更新 DLL 混淆 |
| `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs` | AOT metadata 与热更新 DLL 加载 |
| `Assets/GameScripts/HotFix/GameLogic/GameApp.cs` | 热更新反射入口与推荐注册位置 |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs` | UI 派生类型名保护 |

当前需要后续实现/确认：

- 实现静态和动态 EncryptionService 初始化；
- 在 `GameApp.Entrance` 最前注册实际需要原始全名查询的类型；
- 建立专用 XML 规则，替代 `EnabledPasses=All` 且规则为空；
- 明确 GameProto 序列化协议，保留协议字段/property 名；
- 生成并归档 VM、密钥标识和 symbol mapping；
- 在真机验证反射、UI、序列化和热更新启动链。

## 关联文档

- `repowiki/zh/content/热更新系统/Obfuz代码混淆与加固/EncryptionVM与密钥体系.md`
- `repowiki/zh/content/热更新系统/Obfuz代码混淆与加固/反射与序列化兼容.md`
- `repowiki/zh/content/热更新系统/Obfuz代码混淆与加固/混淆Pass与规则系统.md`
- `repowiki/zh/content/热更新系统/Obfuz代码混淆与加固/HybridCLR集成.md`
- `repowiki/zh/content/热更新系统/Obfuz代码混淆与加固/性能与程序集体积.md`

## 官方证据

- Obfuz `manual/encryption.md`
- Obfuz `manual/reflection.md`
- Obfuz `manual/obfuscation-instincts.md`
- Obfuz `manual/hybridclr/work-with-hybridclr.md`
- Obfuz `3.1.0`：`Runtime/ObfuscationInstincts.cs`
- Obfuz `3.1.0`：`Runtime/ObfuscationTypeMapper.cs`
- Obfuz `3.1.0`：`Editor/ObfusPasses/Instinct/InstinctPass.cs`

