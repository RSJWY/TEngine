# EncryptionVM与密钥体系

## 三个不要混淆的概念

Obfuz 加密体系包含三个不同的随机化层：

| 层 | 典型配置 | 决定什么 | 能否热更新轮换 |
|---|---|---|---|
| VM 代码生成 | `EncryptionVMSettings.CodeGenerationSecretKey`、Opcode Count | 运行时代码包含哪套加解密指令 | VM 在 AOT 时不能随热更新修改 |
| Secret Key | Static/Dynamic Secret Key | 同一 VM 下实际使用的密钥材料 | 静态通常不能；动态可以 |
| 对象随机化 | `SecretSettings.RandomSeed`、每对象 salt/ops | 每个常量、字段、调用使用的组合 | 可变，但影响产物稳定性 |

只修改 `RandomSeed` 不会生成一套新的 VM；只修改动态密钥也不会改变 DLL 文件结构；修改 VM secret 会改变运行时解密代码，必须与主包同步。

## IEncryptor

Obfuz 的统一接口支持：

- byte block 加解密；
- int/long/float/double；
- byte array；
- string 的 UTF-8 数据。

每个操作接收：

```text
data  原文或密文
ops   加密指令组合
salt  当前对象的额外随机参数
```

Pass 不直接依赖某个固定算法，而是通过 `IEncryptor` 和泛型 `EncryptionService<TScope>` 调用当前 Scope 的 VM 实例。

## ops编码

Encryption VM 预生成 `EncryptionOpCodeCount` 条不同指令。每个被保护对象随机选择最多四条指令，并把编号编码进一个 `int ops`。

官方默认 Opcode Count 为 256，所以每个 opcode 可占 8 bit，四条正好编码到 32 bit。解密顺序与加密顺序相反，编码时倒序组合，便于运行时正序取出解密操作。

加密等级 1-4 即选择多少条指令。更多指令并不等于密码学强度指数增加，因此官方建议等级 1。

## EncryptionVM

VM 根据 secret 确定性生成每个 opcode 的实现。原语包括：

- Add；
- Multiple；
- Xor；
- BitRotate；
- Add/Multiply/Xor/Rotate 的多种组合。

每条指令还包含自己的随机参数，因此即使同类原语也不同。

### Opcode Count约束

- 必须为 2 的幂；
- 最小 64；
- 默认 256；
- 官方不建议超过 1024，避免生成类过大。

### VM放在哪个程序集

优先放 AOT：

- IL2CPP 编译为机器码；
- 解密性能更好；
- 热更新 DLL 不直接包含完整 VM 实现；
- 更难通过普通 C# 反编译恢复。

如果只混淆热更新代码，可以把 VM 放热更新程序集，换取每次热更新更灵活的 VM，但性能和保护更差，官方不推荐。

## EncryptionService与初始化

运行时代码形态：

```csharp
EncryptionService<MyScope>.Encryptor =
    new GeneratedEncryptionVirtualMachine(secretBytes);
```

之后混淆代码调用：

```csharp
EncryptionService<MyScope>.Decrypt(value, ops, salt);
```

若在设置 Encryptor 之前执行任何对应 Scope 的混淆代码，会出现 RVA 类型初始化异常或 NullReference/TypeInitializationException。

### 初始化时机

静态 Scope 通常在：

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
```

或更早且可控的主包入口初始化。Obfuz 会保护早期 RuntimeInitialize 方法体，避免初始化逻辑自己依赖尚未初始化的解密服务。

动态 Scope 则在热更新包、认证或远程密钥完成后初始化，但必须在加载并执行使用该 Scope 的类型静态构造器之前完成。

## Secret Key生成

官方 VM 构造通常需要长度 1024 的 byte key。Obfuz 使用 `KeyGenerator.GenerateKey(initialString, length)`，以 SHA-512 对起始字符串重复哈希并填充目标长度。

这是一种确定性派生，不意味着把短、公开的字符串变成高熵秘密。正式 secret 应：

- 使用足够随机的长值；
- 不使用官方默认值、项目名或常见口令；
- 不写进公开 Wiki、日志或版本库；
- 在 CI 秘密变量/安全制品中管理；
- 构建输出只记录 secret 标识/版本，不记录内容。

## EncryptionScope

Scope 是空标记类型，用泛型静态字段隔离多个 Encryptor：

```csharp
public sealed class AotStaticScope : IEncryptionScope { }
public sealed class HotUpdateDynamicScope : IEncryptionScope { }
```

不同程序集或规则可选择不同 Scope，从而：

- AOT 使用静态密钥；
- 热更新使用动态密钥；
- 不同业务域使用不同密钥；
- 密钥按阶段加载。

Scope 数量过多会增加初始化、密钥分发和规则复杂度。一般 2-4 个清晰域足够。

## 静态密钥

适用 AOT 和启动早期代码。约束：

- 主包发布后不能修改，否则旧主包无法解密新热更新产物或自身代码；
- 必须随主包可获得，因此无法成为绝对秘密；
- 可通过拆分、native 包装、设备派生提高提取成本，但仍应按可逆向设计；
- 不应直接使用 `Resources` 中明文默认 bytes 作为最终方案。

## 动态密钥

适用热更新程序集。可在每次热更新轮换，但客户端最终仍需要获得密钥。合理策略：

- 登录/设备认证后由服务端下发；
- 与包版本、渠道、用户或设备绑定；
- 使用传输加密和签名；
- 内存中最小化生存时间；
- 失败时明确阻断执行，不回退到默认密钥；
- 保留旧客户端兼容窗口或多版本密钥服务。

官方入门示例把动态密钥放资源中只用于展示机制，不是生产密钥管理范式。

## salt与RandomSeed

salt 为每个对象提供额外随机参数。`RandomSeed` 驱动确定性随机过程，影响 ops、salt、辅助结构和多个 Pass 结果。

轮换 RandomSeed：

- 可以让产物变化；
- 会扩大热更新 diff；
- 不要求重新生成 VM；
- 仍必须与构建时加密结果一致；
- 应按版本记录以便复现构建。

## 参数冻结矩阵

| 参数 | 新主包 | 同主包热更新 | 说明 |
|---|---:|---:|---|
| VM CodeGenerationSecretKey | 可换 | 禁止（VM在AOT时） | 解密代码结构已固化 |
| VM Opcode Count | 可换 | 禁止 | ops 解码基数变化 |
| Static Secret | 可换 | 禁止 | 旧主包静态 Scope 不匹配 |
| Dynamic Secret | 可换 | 可换 | 前提是运行时获得对应版本密钥 |
| RandomSeed | 可换 | 可换 | 影响 diff 和复现性 |
| Encryption Level | 可换 | 可换 | 影响性能与产物，不宜频繁变 |

## TEngine上线缺口

当前 [Obfuz.asset](file://ProjectSettings/Obfuz.asset)：

- VM secret 为 `Obfuz`；
- Static/Dynamic secret 为官方示例；
- `RandomSeed = 0`；
- 输出路径对应文件/目录不存在；
- 未看到 Encryptor 初始化代码。

因此当前不能启用加密相关 Pass 后直接发布。必须先设计 Scope、生成 VM/密钥、实现初始化、验证启动顺序，并把版本信息纳入发布流程。

## 官方来源

- [加密](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/encryption.md)
- [使用动态Secret Key](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/beginner/dynamic-secret-key.md)
- [设置](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/configuration.md)

