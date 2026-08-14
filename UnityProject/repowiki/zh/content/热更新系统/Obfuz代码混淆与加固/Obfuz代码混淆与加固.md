# Obfuz代码混淆与加固

> 本专题系统整理 Obfuz 官方中文文档，并以 Obfuz `3.1.0` 源码和本 TEngine 工程的实际接入代码进行交叉验证。它不是安装速记，而是用于设计、实施、发布、排障和长期维护混淆方案的完整知识库。

## 研究基线

| 项目 | 基线 |
|---|---|
| Obfuz 文档仓库 | `focus-creative-games/obfuz-doc`，提交 `1dbb503`（2026-07-07） |
| Obfuz 源码仓库 | `focus-creative-games/obfuz`，提交 `fa23450`（2026-07-19） |
| 源码包版本 | `3.1.0` |
| 工程配置 | [ProjectSettings/Obfuz.asset](file://ProjectSettings/Obfuz.asset) |
| TEngine 构建接入 | [BuildDLLCommand.cs](file://Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs) |
| 热更新入口保护 | [GameApp.cs](file://Assets/GameScripts/HotFix/GameLogic/GameApp.cs) |
| UI 类型名保护 | [UIBase.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs) |

官方资料：

- [Obfuz 文档仓库](https://github.com/focus-creative-games/obfuz-doc)
- [Obfuz 源码仓库](https://github.com/focus-creative-games/obfuz)
- [Obfuz4HybridCLR](https://github.com/focus-creative-games/obfuz4hybridclr)

## Obfuz解决什么问题

Obfuz 是基于 dnlib 的 .NET/Unity IL 重写器。它不仅改名，还会重写函数体、常量、字段存储、调用路径、控制流和程序集文件结构。其目标是提高静态分析、动态调试、内存检索、调用关系恢复、热更新 DLL dump 与二次注入的成本。

```text
源码程序集
  -> 编译为托管 DLL
  -> 加载待混淆及引用程序集
  -> 反射兼容检测与 Instinct 扫描
  -> 常量/字段/表达式/调用/控制流等函数体 Pass
  -> 清理 IL
  -> 符号混淆与映射记录
  -> 移除 Obfuz Attribute
  -> 输出混淆 DLL / mapping / 多态 DLL
  -> IL2CPP 或 HybridCLR 加载
```

任何混淆都不是安全边界。Obfuz 的正确定位是“延长逆向分析时间、提高自动化工具失效率并增强版本差异”，不能替代服务端权威校验、协议认证、资源签名、反作弊或敏感数据最小化。

## 阅读导航

### 基础与配置

1. [Obfuz架构与混淆管线](./Obfuz架构与混淆管线.md)：核心对象、两阶段 Pipeline、Pass 顺序和产物。
2. [安装与基础配置](./安装与基础配置.md)：安装、菜单、`ObfuzSettings` 全局字段和首次配置。
3. [程序集分类与依赖处理](./程序集分类与依赖处理.md)：四类程序集、引用同步与搜索路径。
4. [混淆Pass与规则系统](./混淆Pass与规则系统.md)：Pass 开关、规则继承、Attribute 优先级和函数体共同规则。

### 混淆能力

5. [符号混淆](./符号混淆.md)：类型和成员改名、mapping、Debug 模式、自定义 RenamePolicy。
6. [常量与表达式混淆](./常量与表达式混淆.md)：常量 RVA 加密、循环缓存、表达式重写和 const 字段移除。
7. [字段加密](./字段加密.md)：字段密文存储、读写重写、序列化边界和 `[EncryptField]`。
8. [函数调用与控制流混淆](./函数调用与控制流混淆.md)：Dispatch/Delegate、延迟解密、控制流平坦化。
9. [执行栈与其他混淆能力](./执行栈与其他混淆能力.md)：Eval Stack、垃圾代码、水印及当前实现状态。
10. [EncryptionVM与密钥体系](./EncryptionVM与密钥体系.md)：`IEncryptor`、ops、salt、Scope、静态与动态密钥。

### 兼容性与工程化

11. [反射与序列化兼容](./反射与序列化兼容.md)：离线检测、TypeMapper、Instinct、Unity/Newtonsoft 序列化。
12. [Unity特殊兼容规则](./Unity特殊兼容规则.md)：Unity 消息、MonoBehaviour、ScriptableObject、Burst/DOTS、UnityEvent。
13. [构建管线与独立混淆](./构建管线与独立混淆.md)：Player Build 回调、事件、link.xml、独立运行 API。
14. [增量混淆与版本管理](./增量混淆与版本管理.md)：mapping 稳定性、参数冻结、热更新轮换与归档。
15. [HybridCLR集成](./HybridCLR集成.md)：Obfuz4HybridCLR、GenerateAll、CompileAndObfuscate 和 TEngine 流程。
16. [多态DLL](./多态DLL.md)：随机化 DLL/metadata 结构、标准 DLL 禁载和版本约束。
17. [XLua集成](./XLua集成.md)：原始类型名映射和注册时机。

### 运行维护

18. [堆栈还原与故障排查](./堆栈还原与故障排查.md)：DeobfuscateStackTrace、构建/运行故障树和检查表。
19. [性能与程序集体积](./性能与程序集体积.md)：各 Pass 成本、热路径策略和官方体积数据解读。
20. [版本差异与已知问题](./版本差异与已知问题.md)：文档与 `3.1.0` 实现差异、过时内容与工程缺口。

## TEngine当前状态

当前工程已有一部分接入代码，但不能据此认定“已具备安全发布能力”。

| 项目 | 当前状态 | 判断 |
|---|---|---|
| 待混淆程序集 | `GameLogic`、`GameProto` | 已配置 |
| 引用同步程序集 | `TEngine.Runtime`、`Launcher`、`Assembly-CSharp` | 已配置，仍需随 asmdef 变化审计 |
| `ENABLE_OBFUZ` | 已用于开发/发布模式与构建分支 | 已接入 |
| 热更新 DLL 混淆 | 调用 `ObfuscateUtil.ObfuscateHotUpdateAssemblies` | 已有代码路径 |
| 入口名保护 | `GameApp` 禁止类型名和入口方法名混淆 | 已接入 |
| UI 类型名保护 | `UIBase` 子类统一禁用类型名混淆 | 已接入，安全但保护强度偏保守 |
| 构建 Pipeline | `buildPipelineSettings.enable: 0` | 当前关闭 |
| Encryption VM | 配置了输出路径，但 `Assets/Obfuz` 不存在 | 未生成/未纳入工程 |
| 静态/动态密钥 | 仍是示例默认值 | 不可用于正式发布 |
| `RandomSeed` | `0` | 未做版本随机化策略 |
| symbol mapping | 路径已配置，文件不存在 | 尚无可验证的稳定映射 |
| 多态 DLL | 配置为启用，但生成代码和产物未核实 | 仅配置，不能视为生效 |
| 包依赖 | `Packages/manifest.json` 未显式声明 Obfuz 包 | 需确认包来源、锁定方式与 CI 可复现性 |

## 推荐实施顺序

1. 锁定 Obfuz、Obfuz4HybridCLR、HybridCLR 的兼容版本和来源。
2. 根据 asmdef 引用图重新审计两类程序集列表。
3. 生成专用 Encryption VM，替换全部默认密钥和多态 DLL secret。
4. 建立静态/动态 Scope 初始化流程，并在任何混淆代码运行前初始化。
5. 从 Symbol + RemoveConstField 的低风险基线开始。
6. 对反射、序列化、UI 地址、IOC、事件字符串和配置驱动类型进行审计。
7. 再按模块逐步启用 Const、Expr、Call、ControlFlow、FieldEncrypt。
8. 归档每个正式版本的配置快照、密钥标识、mapping 和构建产物哈希。
9. 建立真机冒烟、热更新兼容、堆栈还原和性能基准。
10. 最后评估多态 DLL 与禁载标准 DLL，避免过早破坏调试和回滚通道。

## 官方文档覆盖说明

本专题覆盖官方仓库中的简介、入门教程、全部 manual、FAQ 和发布日志。多个短文被按实际工程主题合并，但其关键配置、默认行为、限制、示例语义和故障结论均在相应章节中保留。每篇文档末尾列出对应官方来源，便于回查原文。

