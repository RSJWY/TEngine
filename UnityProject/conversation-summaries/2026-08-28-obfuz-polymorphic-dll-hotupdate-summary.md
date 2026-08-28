# Obfuz 多态 DLL 接入热更构建链路会话总结

> 日期：2026-08-28
> 背景：用户开启 `ObfuzSettings.polymorphicDllSettings.enable` 后询问"热更代码会生成多态 dll 吗"。排查发现开关只解决了运行时支持，产物链路缺生成环节，遂补上。

## 结论

- **排查结论**：仅开启 `enable` 不会生成多态 dll。`GeneratePolymorphicDll` 是 Obfuz4HybridCLR 提供的公开 API 但包内无人调用，TEngine 的 `CopyAOTHotUpdateDlls` 只做"编译→混淆→拷贝"，本地 libil2cpp 也从未注入过多态支持代码。
- **实施结论**：已在 `BuildDLLCommand.CopyAOTHotUpdateDlls` 中接入多态转换，流程变为**编译 → 混淆 → 多态化 → 拷 `.bytes`**，由 `polymorphicDllSettings.enable` 开关分流，关闭时行为与改动前完全一致。Unity 编译零报错。

## 机制研究结论（详细版见 code-research）

1. 多态 dll = 自定义随机化文件结构（签名 `CODEPHPY`），ILSpy 等常规工具打不开；运行时识别按**逐个 dll 的文件头**进行（`Image.cpp` INIT_RAW_IMAGE 分支），热更程序集与 AOT 补充元数据共用同一条路径和规则，**两种格式可任意混用**。
2. `disableLoadStandardDll` 在 `GenerateAll` 时作为 C++ 常量**烧进 App**：为 0 时标准格式回退可用（当前配置）；为 1 时所有经 `Assembly.Load` / `LoadMetadataForAOTAssembly` 加载的 dll 必须全是多态格式，且发包后不可更改。
3. 多态化不需要专门的运行时加载 API：`Assembly.Load` / `RuntimeApi.LoadMetadataForAOTAssembly` 传法不变，只换字节内容。
4. 官方唯一参考实现：obfuz-samples 仓库 `WorkWithHybridCLR/Assets/Editor/BuildCommand.cs`（菜单 Build/CompileAndObfuscateAndCopyToStreamingAssets + TestGenPolymorphicDlls）。

## 代码改动（仅 1 个文件，+33 行）

`Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`：

- `CopyAOTHotUpdateDlls` 拷贝循环内：`enable` 时对每个热更程序集调 `GeneratePolymorphicHotUpdateDll`（内部调 `ObfuscateUtil.GeneratePolymorphicDll`），产物落 `Obfuz/{target}/PolymorphicHotUpdateAssemblies/` 再拷 `.bytes`。
- 新增两个私有助手：`GetPolymorphicHotUpdateAssemblyOutputPath`、`GeneratePolymorphicHotUpdateDll`，守卫 `#if ENABLE_HYBRIDCLR && ENABLE_OBFUZ`，与调用点一致。
- 转换失败直接抛异常中断构建，不静默回退标准格式。

## 关键决策

| 决策点 | 选择 | 理由 |
| --- | --- | --- |
| 插入位置 | `CopyAOTHotUpdateDlls` 混淆后、拷贝前 | 打包窗口与 ReleaseTools 两条路都汇聚于此；不动 Obfuz 包本身 |
| 多态化输入 | **混淆产物**（链式），未混淆程序集取原始产物 | 官方示例用原始产物（未演示链式），但链式若不通等于放弃混淆，不可接受；链式需真机冒烟验证 |
| 一期范围 | 只多态化热更 dll；补充元数据维持标准格式 | `disableLoadStandardDll: 0` 下混用合法，最小改动可回滚 |
| 运行时代码 | 零改动 | 加载 API 不变；Editor 下走编辑器程序集不加载 `.bytes` |

## 待办与风险（返回修改时先看这里）

1. **换多态密钥**：`codeGenerationSecretKey` 仍是官方默认值 `obfuz-polymorphic-key`。冻结参数，必须在第一次多态打 App 前定死，之后不可改（改了旧 App 加载不了新热更 dll）。混淆配置窗口"高级"页有随机按钮与健康检查警告。
2. **打 App 前手动跑 `HybridCLR/ObfuzExtension/GenerateAll`**：把多态支持注入 libil2cpp（当前从未注入过，已验证 `HybridCLRData/il2cpp_plus_repo/libil2cpp/hybridclr` 无 polymorphic 痕迹）。只打资源包不需要。
3. **真机冒烟**：验证"混淆产物 → 多态"链式产物能否真机加载（官方未背书的用法）。若失败，回退方案是把 `GeneratePolymorphicHotUpdateDll` 的输入换成原始编译产物（同官方示例），但那会丢混淆，需再评估。
4. dev 模式 pdb 与多态字节不配对（混淆场景既有问题，非本次引入）。
5. 二期（可选）：开 `disableLoadStandardDll: 1` 防 hook dump 时，需在 `CopyAOTAssembliesToAssetPath` 把 AOTMetadataManifest 全部 dll 过一遍多态化，并重跑 GenerateAll + 重打 App。

## 关键文件

- `UnityProject/Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`（本次改动）
- `Library/PackageCache/com.code-philosophy.obfuz4hybridclr@40c8b6c1a8/Editor/ObfuscateUtil.cs`（`GeneratePolymorphicDll` API）
- `Library/PackageCache/com.code-philosophy.obfuz4hybridclr@40c8b6c1a8/Editor/PrebuildCommandExt.cs`（`GenerateAll` 注入链）
- `Library/PackageCache/com.code-philosophy.obfuz4hybridclr@40c8b6c1a8/Editor/Polymorphic/PolymorphicCodeGenerator.cs`（`disableLoadStandardDll` 烧入逻辑）
- `ProjectSettings/Obfuz.asset`（`polymorphicDllSettings` 配置）

## 相关记录

- [多态 DLL 机制研究](./code-research/2026-08-28-obfuz-polymorphic-dll-mechanism-research.md)
- Books/Fork/obfuscation.md「Obfuz 多态 DLL 热更产物集成」章节
