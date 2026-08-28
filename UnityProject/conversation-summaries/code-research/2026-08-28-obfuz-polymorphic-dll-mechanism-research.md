# Obfuz 多态 DLL（PolymorphicDll）机制研究

> 日期：2026-08-28
> 研究动机：确认 `polymorphicDllSettings.enable` 开启后热更 dll 是否自动多态化、补充元数据是否必须跟随、`disableLoadStandardDll` 的确切语义。结论已用于 `BuildDLLCommand.CopyAOTHotUpdateDlls` 的集成实施。

## 结论速览

1. `enable` 只负责"运行时能加载"，**不负责生成**；生成必须显式调 `ObfuscateUtil.GeneratePolymorphicDll`，官方包内外均无自动调用点。
2. 格式识别按**逐个 dll 文件头**（魔数 `CODEPHPY`），标准/多态格式可任意混用，除非 `disableLoadStandardDll` 在 GenerateAll 时烧为 1。
3. 补充元数据是否要跟着多态化**完全由 `disableLoadStandardDll` 决定**，与"是否开了多态"无关。
4. 加载 API 零改动：`Assembly.Load` / `RuntimeApi.LoadMetadataForAOTAssembly` 传法不变，只换字节。

## 三个组件的职责分工

| 组件 | 位置 | 职责 |
| --- | --- | --- |
| `PrebuildCommandExt.GenerateAll`（菜单 HybridCLR/ObfuzExtension/GenerateAll） | obfuz4hybridclr | 编译热更 dll → `GeneratePolymorphicCodesWhenEnable()` 注入 C++ → link.xml → 裁剪 AOT → 混淆 → MethodBridge → AOT 泛型。**不含 dll 多态化步骤** |
| `ObfuscateUtil.GeneratePolymorphicDll(originalDllPath, outputDllPath)` | obfuz4hybridclr | 单个 dll 的格式转换：dnlib `ModuleDefMD.Load` → `PolymorphicMetadataWriter(secretKey)` → `PolymorphicModuleWriter.Write`。公开 API，需项目侧自己接管线 |
| `PolymorphicCodeGenerator` | obfuz4hybridclr | 把 `Templates~/*.tpl` 注入本地 `HybridCLRData/il2cpp_plus_repo/libil2cpp/hybridclr/`：复制 `MetadataReader.h`、生成 `PolymorphicRawImage.cpp/.h`、`PolymorphicDefs.h`、`PolymorphicDatas.h`，并改写 `Image.cpp` 的 `INIT_RAW_IMAGE` 区域 |

依赖：HybridCLR 包 ≥ 8.4.0（自定义 dll 结构能力）；本项目 8.13.0 满足。dnlib 的多态 Writer 来自 Obfuz 内置定制版 dnlib（`dnlib.DotNet.PolymorphicWriter` 命名空间）。

## 运行时识别逻辑（烧入 App 的 C++）

`PolymorphicCodeGenerator.GenerateRawImageInit()` 改写 `Image.cpp` 生成的分支：

```cpp
if (std::strncmp((const char*)imageData, "CODEPHPY", 8) == 0)
    _rawImage = new PolymorphicRawImage();      // 多态格式
else if (_disableLoadStandardImage)             // GenerateAll 时的 C++ 常量
    return LoadImageErrorCode::UNKNOWN_IMAGE_FORMAT;
else
    _rawImage = new RawImage();                 // 回退标准格式
```

- 多态 dll 文件头以 8 字节签名 `CODEPHPY` 开头，随后是 `PolymorphicImageHeaderData`（formatVersion/formatVariant/section 表等，全部按 secretKey 随机化布局）。
- 热更程序集（`Assembly.Load`）与补充元数据（`LoadMetadataForAOTAssembly` → homologous image 加载）走同一个 RawImage 初始化点，规则统一。
- `disableLoadStandardDll: 1` 是**App 级冻结参数**：改它必须重跑 GenerateAll + 重打 App；之后每轮热更的所有 dll（含补充元数据）必须持续以多态格式生成。

## 官方参考实现（唯一现成范例）

仓库 <https://github.com/focus-creative-games/obfuz-samples>，`WorkWithHybridCLR/Assets/Editor/BuildCommand.cs`：

```csharp
CompileDllCommand.CompileDll(target);
ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath);

if (ObfuzSettings.Instance.polymorphicDllSettings.enable)
{
    // 补充元数据 dll（裁剪后的 mscorlib）也转多态 → StreamingAssets/mscorlib.dll.bytes
    ObfuscateUtil.GeneratePolymorphicDll($"{stripDir}/mscorlib.dll", $"{sa}/mscorlib.dll.bytes");
    // 热更 dll —— 注意输入是原始编译产物，不是混淆产物
    ObfuscateUtil.GeneratePolymorphicDll($"{hotUpdateDllDir}/HotUpdate.dll", $"{sa}/HotUpdate.dll.bytes");
}
else { /* 原有混淆产物拷贝循环 */ }
```

要点与局限：

- 示例的 if/else 结构、`enable` 分流位置可直接借鉴。
- **示例未演示"混淆产物 → 多态"链式**：多态分支输入取原始 dll（混淆输出生成了但没用）。链式技术上成立（混淆产物仍是标准 CLI 格式，dnlib 可重读写），属官方未背书用法，需真机验证。
- 文档页（obfuz.com/docs/manual/hybridclr/polymorphic-dll）无示例代码、无 demo 仓库链接；obfuz4hybridclr 包 README 仅一行。

## 密钥与冻结参数矩阵

| 参数 | 影响范围 | 冻结时机 |
| --- | --- | --- |
| `polymorphicDllSettings.codeGenerationSecretKey` | 决定 dll 文件结构随机化布局 | 第一次多态打 App 前定死；此后热更 dll 必须同密钥生成，App 发布后不可改 |
| `disableLoadStandardDll` | 烧进 App 的加载校验行为 | 同上，改它 = 发新 App |
| `secretSettings.defaultStaticSecretKey` / VM `codeGenerationSecretKey` | 混淆解密基础设施 | 既有冻结规则，与多态正交 |

## 排查线索备忘

- 判断本地 libil2cpp 是否注入过：`HybridCLRData/il2cpp_plus_repo/libil2cpp/hybridclr/metadata/` 下应出现 `PolymorphicRawImage.cpp/.h`，或 grep `CODEPHPY`。
- 判断产物是否多态：文件头 8 字节应为 `CODEPHPY`（标准 dll 是 `MZ`）。
- 多态产物目录与混淆目录不能同路径（`ObfuscateHotUpdateAssemblies` 对输入输出同目录有显式检查）。

## 相关记录

- 实施记录：[会话总结](../2026-08-28-obfuz-polymorphic-dll-hotupdate-summary.md)
- fork 文档：`Books/Fork/obfuscation.md`「Obfuz 多态 DLL 热更产物集成」
