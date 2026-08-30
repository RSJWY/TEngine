# YooAsset 资源清单加密接入

## 背景

YooAsset 3.x 原生提供 `IManifestEncryptor` / `IManifestDecryptor` 两个对称接口支持清单加密，但 fork 之前 `ResourceModule` 未注入 `IManifestDecryptor`，构建端即使选了加密器运行时也无法解密清单。

## 决策

- **独立开关 + 固定算法**：`RuntimePackageEntry` 新增 `bool ManifestEncrypted`，per-package 控制；算法固定为 ChaCha20（RFC 7539），不暴露为枚举。
- **密钥隔离**：新增 `ManifestChaCha20KeyConfig`，与 Bundle 用的 `ChaCha20KeyConfig` 独立存放，避免从清单解密链路逆向到 Bundle 密钥。
- **与 Bundle 加密解耦**：`EncryptionType` 只管 Bundle，`ManifestEncrypted` 只管清单，互不联动。
- **breaking change 策略**：未来更换算法不做运行时兼容迁移，清单版本与 Bundle 版本绑定，不存在线上旧清单需兼容场景。

## 实现

### 配置

- `RuntimePackageEntry.ManifestEncrypted`（`UpdateSetting.cs`），默认 false。
- `NormalizeRuntimePackageEntry` 同步该字段。
- `ManifestChaCha20KeyConfig`（`Crypto/ManifestChaCha20KeyConfig.cs`）继承 `CryptoKeyConfig<T>`，32B key + 12B nonce 单资产，路径 `Resources/EncryptConfigs/ManifestChaCha20KeyConfig.asset`，Editor 下自动创建并生成随机密钥。

### 加解密器

- `ManifestChaCha20Encryptor` / `ManifestChaCha20Decryptor`（`ResourceModule.ManifestCrypto.cs`），实现 YooAsset 的 `IManifestEncryptor` / `IManifestDecryptor`，复用 `ChaCha20Util`。

### 运行时注入

- `ResourceModule.AddManifestDecryptor(FileSystemParameters, bool)` 按 `ManifestEncrypted` 注入 `EFileSystemParameter.ManifestDecryptor`。
- 四个 `Create*FileSystemParameters`（Builtin/Sandbox/WebServer/WebNetwork）签名加 `bool manifestEncrypted` 参数，调用方经 `GetManifestEncrypted(packageName)` 取值。
- 微信小游戏分支经 `WechatFileSystemCreater` 返回的 `FileSystemParameters` 同样调用 `AddManifestDecryptor`。
- `EditorSimulateMode` 不经过这些 Create 方法，清单加密对它不生效（与 Bundle 加密在 EditorSimulate 下的行为一致）。

### 构建端

- `ReleaseTools` 按 `runtimePackage.ManifestEncrypted` 同时设置：
  - `BuildParameters.ManifestEncryptor`：`TaskCreateManifest` 序列化加密清单用。
  - `BuildParameters.ManifestDecryptor`：`TaskCreateCatalog` 反序列化刚加密的清单生成首包 Catalog 用（缺它会导致 `FileMagic` 校验失败）。

## 关键发现

1. **YooAsset 不做算法协商**：清单文件不带"用哪个解密器"的元数据，运行时注入什么解密器就得用什么，配错只报 `Manifest file format is invalid.`。
2. **构建端必须同时设 Encryptor 和 Decryptor**：`TaskCreateCatalog.cs:20` 读 `ManifestDecryptor` 反序列化清单生成 Catalog，只设 Encryptor 会导致 Catalog 生成失败。
3. **密钥载体**：fork 既有约定是 `CryptoKeyConfig<T>` 单 ScriptableObject 承载 key+nonce（见 `ChaCha20KeyConfig`），不是两个文件。

## 未做

- 未做 batchmode 编译验证（用户开着 Unity，实例冲突会崩溃）。所有改动经静态复核：命名空间、签名、调用点、枚举值已逐一确认。
- 未更新根 `README.md` 和 `Fork-定制改动说明.md`（无新主题分类，归入既有 `resource-build.md`）。

## 文档

- `Books/Fork/resource-build.md`：新增"资源清单加密"专题条目。
- `Books/Fork/CHANGELOG.md`：2026-08-30 追加一条。
