# Fork 资源、热更新与代码保护

## 当前基线

- YooAsset 使用 3.x 原生 API，不启用 `YOOASSET_LEGACY_API`。
- 热更程序集位于独立 `CodePackage`，不是 `DefaultPackage`。
- `UpdateSetting.RuntimePackages` 是运行时初始化和 Editor 构建共用的数据源。
- `CodePackage` 默认使用 `ArchiveFileBuildPipeline` 与 `EncryptionType.ChaCha20`。
- 当前已删除 XXTEA。不要生成 `EncryptionType.XXTEA`、`XXTEAEncryption` 或相关配置。

## 运行时资源包模型

`RuntimePackageEntry` 的关键字段：

```csharp
PackageName
Enable
InitOnStartup
UpdateManifestOnStartup
DownloadOnDemand
SaveVersion
VersionKey
BuildPipeline
EncryptionType
```

程序集包名从 `UpdateSetting.GetAssemblyPackageName()` 获取，不要硬编码 `CodePackage`。远端目录按包组织：`{host}/{project}/{platform}/{packageName}/...`。

## 构建管线与加密

| `RuntimePackageBuildPipeline` | 用途 |
| --- | --- |
| `UseGlobal` | 使用全局构建设置 |
| `ScriptableBuildPipeline` | 普通 AssetBundle |
| `RawFileBuildPipeline` | 原生文件包 |
| `ArchiveFileBuildPipeline` | 多个原始文件归档为 ArchiveBundle |
| `BuiltinBuildPipeline` | 仅旧序列化值兼容，运行时收敛到 SBP；不要新选用 |

| `EncryptionType` | 当前实现 |
| --- | --- |
| `None` | 不加密 |
| `FileOffSet` | 32 字节偏移伪加密 |
| `FileStream` | 变长 keyed XOR；枚举名保留历史名称 |
| `ChaCha20` | 32 字节 key + 12 字节 nonce，RFC 7539 |

Builtin/Sandbox 优先使用流式或偏移式解密；Web 和 ArchiveBundle 使用 `IBundleMemoryDecryptor`。ArchiveBundle 会整包解密到内存，设计大代码包时评估峰值。

密钥配置位于 `Assets/Resources/EncryptConfigs/`。修改密钥、nonce 或算法后必须重新构建资源，并清理旧 StreamingAssets、沙盒缓存和远端旧版本。

## YooAsset 3 运行模式

`EPlayMode` 已新增 `None = 0`。Editor UI 必须保存真实枚举值，不能把下拉框索引直接写入 `EditorPrefs["EditorPlayMode"]`。

- `EditorSimulateMode`：读取编辑器模拟清单。
- `OfflinePlayMode`：只读 StreamingAssets 已构建包，不访问远端；进入 Play 前先构建并复制资源。
- `HostPlayMode` / `WebPlayMode`：请求远端版本，失败时可按配置回退本地已缓存版本。

下载器使用 YooAsset 3 API：

```csharp
var downloader = GameModule.Resource.CreateResourceDownloader(packageName);
downloader.StartDownload();
await downloader.Task;
```

不要生成 2.x 的 `BeginDownload()` 调用。清单操作返回 `LoadPackageManifestOperation`，缓存清理返回 `ClearCacheOperation`。

## Archive CodePackage 二进制加载

ArchiveBundle 中的 DLL、PDB、AOT 元数据和 Obfuz 动态密钥以 `RawFileObject` 返回：

```csharp
var raw = await GameModule.Resource.LoadAssetAsync<RawFileObject>(location, ct, packageName);
byte[] bytes = raw.GetBytes();
GameModule.Resource.UnloadAsset(raw);
```

非 Archive 管线继续兼容 `TextAsset.bytes`。这段差异只应存在于热更新二进制加载链路；普通业务资源仍使用 `IResourceModule` 的类型化 API，不要到处判断构建管线。

## HybridCLR 与 AOT 元数据

- CodePackage 目录分为 `AOT/`、`HotDll/`、`PDB/`，子目录由 `UpdateSetting` 配置。
- AOT 清单资产为 `Assets/AssetRaw/DLL/AOT/AOTMetadataManifest.asset`。
- Archive CodePackage 不能还原该 `ScriptableObject`，运行时回退 `UpdateSetting.AOTMetaAssemblies`。
- 构建前执行 AOT 元数据同步；manifest 缺少 `AOTGenericReferences.PatchedAOTAssemblyList` 项时必须中断构建。
- 拷贝 AOT DLL 时源文件缺失是构建错误，不能静默跳过。

## Obfuz

### 宏与模式

- `OBFUZ_INSTALLED`：Editor 程序集的包存在性门控。
- `ENABLE_OBFUZ`：是否执行混淆链路。
- `ENABLE_RELEASE`：release 模式；release 不生成/加载 PDB。
- Editor 禁止运行混淆后代码，静态和动态密钥初始化均受 `!UNITY_EDITOR` 约束。

### 密钥初始化

- 静态密钥在 `AfterAssembliesLoaded` 从 `Resources/Obfuz/defaultStaticSecretKey.bytes` 初始化。
- 动态密钥在 `Assembly.Load` 前从程序集包的 `defaultDynamicSecretKey` 加载；Archive 管线读 `RawFileObject`，其它管线读 `TextAsset`。
- 初始化失败必须阻断后续程序集加载，并在 Launcher UI 可用后报告。

### 多态 DLL

构建链路为：

```text
编译 -> Obfuz 混淆 -> GeneratePolymorphicDll -> 拷贝 .dll.bytes
```

- `polymorphicDllSettings.enable` 只控制产物转换分流。
- 打 App 前必须执行 `HybridCLR/ObfuzExtension/GenerateAll`，把多态加载支持注入 libil2cpp。
- 多态密钥必须在第一次发布 App 前确定，发布后不可修改。
- 当前 `disableLoadStandardDll = 0`，热更 DLL 可多态化，AOT 补充元数据仍可保持标准格式。
- 运行时仍使用 `Assembly.Load` / `LoadMetadataForAOTAssembly`，不要发明专用加载 API。

## 常见禁区

- 不要恢复 XXTEA 或固定单字节 XOR。
- 不要硬编码 `CodePackage`、AOT/HotDll/PDB 子目录或版本键。
- 不要让 EditorSimulate/Offline 走远端版本确认。
- 不要在 Editor 中验证 Obfuz 混淆 DLL。
- 不要把 ArchiveBundle 当成可直接返回 `TextAsset`/`ScriptableObject` 的普通 Bundle。
