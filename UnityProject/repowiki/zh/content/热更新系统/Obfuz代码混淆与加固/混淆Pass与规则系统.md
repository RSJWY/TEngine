# 混淆Pass与规则系统

## Pass列表

官方文档和 `3.1.0` 源码中可识别的主要 Pass：

| Pass | 目标 | 默认成本 |
|---|---|---|
| SymbolObfus | 类型、字段、方法、参数、property、event 改名 | 运行时几乎无成本 |
| ConstEncrypt | 数值、字符串、数组等常量加密 | 解密和缓存成本 |
| RemoveConstField | 移除可内联的 const 元数据字段 | 低 |
| ExprObfus | 算术/位运算表达式等价重写 | 增加 IL 与执行指令 |
| EvalStackObfus | 执行栈和临时值扰动 | 当前默认 Builder 不运行 |
| FieldEncrypt | 字段以密文形式存储，访问时转换 | 每次读写成本较高 |
| CallObfus | Dispatch/Delegate 隐藏调用目标 | 间接调用和首次解密成本 |
| ControlFlowObfus | 基本块平坦化为状态机 | 分支与代码体积成本 |
| WaterMark | 注入元数据、RVA 和指令水印 | 低到中 |

垃圾代码生成是独立生成工具，不完全等同于上述程序集 Pass。

## 两层规则体系

Obfuz 存在两种容易混淆的规则：

1. **总 Pass 规则**：决定某目标是否启用某个 Pass。
2. **专用 Pass 规则**：在 Pass 已启用的前提下，控制具体级别、类型、白名单、缓存或成员范围。

例如一个函数的常量是否加密，需要同时满足：

```text
EnabledPasses包含ConstEncrypt
AND 总Pass规则没有禁用ConstEncrypt
AND 内置/Attribute没有禁用MethodBody
AND Const规则的disableEncrypt为false
AND 常量类型对应encryptX为true
AND 常量未命中whitelist
```

## 全局EnabledPasses是硬上限

`ObfuscationPassSettings.EnabledPasses` 是所有规则的上限。如果全局未启用 `FieldEncrypt`，规则文件对某字段设置 FieldEncrypt 也不会生效。生产上建议显式选择已评估 Pass，而不是长期保留 `All`。

## 总Pass规则文件

总规则使用 `enablePasses`、`disablePasses`、`addPasses`、`removePasses` 等语义，从 global/assembly/type/member 逐级计算最终集合。核心理解：

- `enablePasses`/`disablePasses` 倾向于定义或限制当前层结果；
- `addPasses`/`removePasses` 在继承结果上增减；
- 子节点继承父节点，再应用自身匹配规则；
- 同一目标可匹配多条规则，配置文件及规则顺序会影响最终结果；
- Pass 名称来自 `ObfuscationPassType` 枚举，可组合。

适合的策略是先给程序集一个安全基线，再对命名空间/类型逐步增加高成本 Pass。

## 专用规则的共同层级

大多数函数体 Pass 规则采用：

```xml
<obfuz>
  <global ... />
  <assembly name="GameLogic" ...>
    <type name="GameLogic.Battle.*" ...>
      <method name="Calculate*" ... />
    </type>
  </assembly>
</obfuz>
```

继承顺序通常是：

```text
Pass Settings默认值
  -> global
  -> assembly
  -> type
  -> method/field/property/event
```

未设置的可空属性继承父级。类型和成员名普遍支持通配符；嵌套类型使用 `/` 表示，例如 `Outer/Inner`。

## enable与disable语义

总 Pass 文档特别区分：

- `enable`：显式启用；
- `disable`：显式禁用；
- 未配置：继承父层。

在组合规则中，“禁用”通常应视为更保守的约束。若同一目标被多个规则匹配，不要靠猜测顺序；应构造最小测试程序集并检查混淆结果和 Builder 计算日志。

## ObfuzIgnoreAttribute

`[ObfuzIgnore]` 可用于 type、method、field、property、event，不能用于 assembly。

主要字段：

| 字段 | 含义 |
|---|---|
| `Scope` | 禁用的目标范围，默认 `All` |
| `ApplyToNestedTypes` | 嵌套类型是否继承，默认 true |
| `ApplyToChildTypes` | 派生类/接口实现类是否继承，默认 false |

常用 Scope：

- `TypeName`：保留类型名；
- `Field`：禁止字段符号混淆和字段加密等字段 Pass；
- `MethodName`：保留方法名；
- `MethodParameter`：保留参数；
- `MethodBody`：禁用函数体 Pass；
- `Property`、`Event`：保留相应元数据；
- `All`：类型及成员全部跳过适用混淆。

关键细节：方法标记 `[ObfuzIgnore]` 后，方法体自身不被混淆，但其中对“其他已改名/已加密元数据”的引用仍必须更新，否则程序集无法运行。

本工程用法：

```csharp
[ObfuzIgnore(ObfuzScope.TypeName | ObfuzScope.MethodName)]
public partial class GameApp { ... }

[ObfuzIgnore(ObfuzScope.TypeName, ApplyToChildTypes = true)]
public class UIBase { ... }
```

第一处保护反射入口名；第二处保护所有 UI 派生类的类型名，以兼容 `typeof(T).Name` 资源地址等协议。

## EncryptFieldAttribute

`[EncryptField]` 用于明确要求字段加密，适合少量关键状态。它不是“忽略规则”的反义词：字段类型必须受支持，读写仍受序列化和跨程序集边界约束。详细见[字段加密](./字段加密.md)。

## 内置函数体排除

官方函数体规则会无条件排除：

- `Obfuz.Runtime` 内函数体；
- `$Obfuz$` 生成类型和方法；
- `GeneratedEncryptionVirtualMachine`；
- 加载时机早于或等于 `AfterAssembliesLoaded` 的 `[RuntimeInitializeOnLoadMethod]` 方法体。

这些目标即使规则文件要求启用也不会参与相关函数体混淆。它们的符号名是否改名由 Symbol 策略另行决定。

## 规则设计建议

### 基线

```text
全业务程序集：Symbol + RemoveConstField
关键业务命名空间：+ Const + Expr
核心计算方法：+ Call + 适度ControlFlow
少量关键字段：FieldEncrypt或[EncryptField]
热循环/渲染/物理/序列化底层：MethodBody禁用或专用降级
```

### 避免空规则文件配合All

当前工程 `EnabledPasses = All` 且所有 `RuleFiles` 为空，会把各 Pass 的默认规则大范围应用。正式接入应显式建立规则目录，并通过代码域划分，而不是发现性能/兼容问题后到处添加 Attribute。

### 规则文件管理

- 规则文件纳入版本管理。
- 每个文件只负责一个主题或 Pass，避免巨型 XML。
- 用注释说明“为何排除”，不要只写类型名。
- 重构命名空间/类型后检查通配规则是否仍命中。
- 在 CI 中解析 XML 并检查引用文件存在。
- 每次升级 Obfuz 后用小样本验证规则继承语义。

## 规则验证方法

1. 使用 Debug Symbol 模式确认目标命中范围。
2. 对每个 Pass 准备包含命中与不命中目标的最小程序集。
3. 用 ILSpy/dnSpy 离线检查结果，但不要在原始 Editor AppDomain 直接执行混淆 DLL。
4. 对字段、反射、序列化和跨程序集访问做真机行为测试。
5. 对规则变更比较 mapping、DLL 大小、方法 IL 和性能基准。

## 官方来源

- [Obfuscation Pass](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/obfuscation-pass.md)
- [函数体混淆](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/method-body-obfuscation.md)
- [Obfuz CustomAttributes](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/customattributes.md)
- [设置](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/configuration.md)

