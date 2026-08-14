# 多态DLL

## 要解决的问题

标准 `Assembly.Load` 接收标准 PE/CLI DLL。即使资源层对 DLL 加密，攻击者在解密后、调用 `Assembly.Load` 前 hook 参数，仍可 dump 完整标准 DLL；也可尝试加载自己构造的标准 DLL。

多态 DLL 修改 DLL 文件和 metadata 结构，让每个项目生成不同布局，并让 HybridCLR native runtime 直接识别该布局，减少“解密为标准 DLL 再加载”的窗口。

## 官方特性

- 常规 ILSpy/dnSpy 无法直接打开；
- metadata 结构不是标准 CLI 布局；
- 数十个 metadata 结构和数百字段可随机排列；
- 每个 codeGenerationSecretKey 产生不同结构；
- 可禁止加载标准 DLL；
- 热更新 DLL、DHE DLL、AOT 补充元数据 DLL 均可转换。

## 版本要求

HybridCLR 自 `8.4.0` 起支持自定义 DLL 结构。实际仍需匹配：

- Obfuz；
- Obfuz4HybridCLR；
- HybridCLR Unity 包；
- HybridCLR native runtime；
- Unity 版本和平台。

只看到配置 `enable: 1` 不能证明功能已启用，必须确认扩展 GenerateAll 已修改 native 代码并成功构建 Player。

## codeGenerationSecretKey

该值决定 DLL/metadata 结构，官方要求：

- 不使用默认值；
- 长度至少 10，并使用复杂组合；
- 新主包可更换；
- 同一主包的热更新不能更换；
- 多主包共享同一多态 DLL 时必须保持一致。

它不是运行时解密 Secret，也不是 Encryption VM secret。三个 secret 应分别管理。

当前工程值 `obfuz-polymorphic-key` 是示例性质，正式发布前必须替换。

## disableLoadStandardDll

开启后：

- `Assembly.Load` 必须传入对应多态 DLL；
- `RuntimeApi.LoadMetadataForAOTAssembly` 也必须传入多态结构；
- 标准 DLL 返回错误。

安全收益：提高第三方 DLL 注入和错误包混用成本。

运营风险：

- 回滚到标准 DLL 会失败；
- 调试工具和离线验证更困难；
- CDN/构建流程漏转任一 DLL 会导致启动失败；
- 多版本主包必须分发匹配结构；
- 原生 runtime 与资源版本不匹配时缺少兼容通道。

建议分两阶段：先启用多态 DLL 但允许标准 DLL，用日志和 CI 验证所有产物；稳定后再评估禁载。

## 生成流程

1. 在 `PolymorphicDllSettings` 设置专用 secret。
2. 开启 `enable`。
3. 执行 `HybridCLR/ObfuzExtension/GenerateAll`，注入 native 支持。
4. 编译 Player。
5. 混淆热更新 DLL。
6. 调用 `ObfuscateUtil.GeneratePolymorphicDll(original, output)` 转换。
7. 对 AOT metadata DLL 同样转换（若计划使用）。
8. 上传匹配当前主包的产物。

不要在客户端下载标准 DLL 后再转换；结构转换应在可信构建环境完成。

## 安全边界

多态 DLL 能提高标准工具失效率和 dump/注入成本，但不是不可逆：

- native runtime 中包含解析结构的逻辑；
- 攻击者可逆向自定义 metadata 读取器；
- 运行时最终仍会形成可执行元数据和代码；
- 内存与调用链仍可动态分析。

它应与符号、常量、调用、控制流、签名校验和服务端验证组合。

## TEngine接入关注点

[ProcedureLoadAssembly.cs](file://Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs) 无需改 C# API 调用形式，仍调用 `Assembly.Load(bytes)`；真正变化在 HybridCLR native runtime 对 bytes 的解释。

需要保证：

- YooAsset 中存放的是多态产物而非混淆标准 DLL；
- PDB dev 流程不要误与多态 release DLL配对；
- AOTMetadataManifest 对应文件也正确转换；
- PackageNote/版本服务区分多态结构 ID；
- `disableLoadStandardDll` 与包版本同时发布。

## 验收

- 常规 .NET 工具不能直接解析最终文件；
- Player 能加载全部热更新程序集；
- AOT metadata 加载成功；
- 标准 DLL 在禁载关闭/开启时表现符合预期；
- 错误结构、旧结构和损坏 DLL 有明确错误日志；
- 多平台、架构和主包版本分别验证；
- 回滚方案经过演练。

## 官方来源

- [多态dll文件](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/hybridclr/polymorphic-dll.md)

