# CodePackage 三子目录 dll 拆分构建逻辑适配

> 日期: 2026-08-13
> 关键词: CodePackage、AssetRaw/DLL、AOT、HotDll、PDB、子目录拆分、BuildDLLCommand、UpdateSetting、AssemblyTextAssetPath、AOTAssemblySubPath、HotUpdateAssemblySubPath、PdbAssemblySubPath、AOTMetadataManifest、pdb 拷贝、.gitignore、YooAsset 收集器

---

## 背景

原来 `CodePackage` 资源包的 dll 全部平铺在 `AssetRaw/DLL` 根目录（AOT dll、热更 dll、manifest 混在一起）。用户将其细分为三个子目录 `AOT/`、`HotDll/`、`PDB/`，`AOTMetadataManifest.asset` 移到 `AOT/` 子目录下，但构建脚本和配置仍指向根目录，处于半改造不一致状态。

## 改动内容

### 1. UpdateSetting.cs — 新增子路径字段 + 辅助方法

- 新增三个可配置子路径字段（`Assets/TEngine/Runtime/Core/UpdateSetting.cs:157-160`）：
  - `AOTAssemblySubPath = "AOT"`
  - `HotUpdateAssemblySubPath = "HotDll"`
  - `PdbAssemblySubPath = "PDB"`
- 新增四个辅助方法（`:404-423`），统一返回 `Assets/...` 开头的 Unity 资产路径：
  - `GetAOTAssemblyAssetPath()`
  - `GetHotUpdateAssemblyAssetPath()`
  - `GetPdbAssemblyAssetPath()`
  - `GetAOTMetadataManifestAssetPath()` — 返回 `Assets/AssetRaw/DLL/AOT/AOTMetadataManifest.asset`

### 2. UpdateSetting.asset — 同步序列化数据

- 在 `AssemblyTextAssetPath` 后新增三行子路径字段。

### 3. BuildDLLCommand.cs — 核心拷贝逻辑适配

- **manifest 路径**（`:298`）：改用 `UpdateSetting.GetAOTMetadataManifestAssetPath()`，指向 `AOT/` 子目录。
- **AOT dll 拷贝目标**（`:204`）：`aotAssembliesDstDir` 追加 `AOTAssemblySubPath` → `AssetRaw/DLL/AOT/`。
- **热更 dll 拷贝目标**（`:363`）：`hotfixAssembliesDstDir` 追加 `HotUpdateAssemblySubPath` → `AssetRaw/DLL/HotDll/`。
- **新增 pdb 拷贝**（`:372` + `:379-395`）：`CopyPdbToAssetPath()` 方法，development 构建有 `.pdb` 时拷贝到 `AssetRaw/DLL/PDB/*.pdb.bytes`，不存在则静默跳过。文件名用 `Path.GetFileNameWithoutExtension(dll)` 去后缀拼 `.pdb`。
- **Obfuz 分支目标路径**（`:183`）：同步追加 `HotUpdateAssemblySubPath`。

### 4. .gitignore — 扩展忽略规则

- 保留原根目录 `*.dll.bytes` 规则（向后兼容）。
- 新增 `**/*.dll.bytes`、`**/*.pdb.bytes` 及对应 `.meta` 覆盖三个子目录。

### 5. YooAsset 收集器配置（用户提前处理）

- `AssetBundleCollectorSetting.asset`：AOT 分组 `CollectPath` 补为 `Assets/AssetRaw/DLL/AOT`。
- `AssetBundleCollectorConfig.xml`（根目录版 + Editor 版）：用户已同步三分组配置。

## 未修改

- `ProcedureLoadAssembly.cs`：运行时 addressable 模式（`_enableAddressable = true` 硬编码）按文件名寻址，dll 移到子目录后无需改动。非 addressable 分支当前未使用。
- `ReleaseTools.cs`：调用 `BuildDLLCommand.BuildAndCopyDlls()`，路径变化对外透明。

## 改动后的数据流

```
构建期：
  SyncAOTMetadataManifest() -> Assets/AssetRaw/DLL/AOT/AOTMetadataManifest.asset
  CompileDll(target) -> HybridCLRData/HotUpdateDlls/{Target}/*.dll (+ *.pdb if development)
  CopyAOTAssembliesToAssetPath() -> AssetRaw/DLL/AOT/*.dll.bytes
  CopyHotUpdateAssembliesToAssetPath() -> AssetRaw/DLL/HotDll/*.dll.bytes
  CopyPdbToAssetPath() -> AssetRaw/DLL/PDB/*.pdb.bytes (仅 development)

YooAsset 收集（CodePackage 三分组）：
  AOT 分组   -> 收集 AssetRaw/DLL/AOT  (manifest + AOT dll)
  HotDll 分组 -> 收集 AssetRaw/DLL/HotDll (热更 dll)
  PDB 分组   -> 收集 AssetRaw/DLL/PDB  (pdb)

运行时（不改动）：addressable 模式按文件名寻址，跨子目录无影响
```

## 关键文件索引

| 文件 | 改动 |
|------|------|
| `Assets/TEngine/Runtime/Core/UpdateSetting.cs` | 新增 3 子路径字段 + 4 辅助方法 |
| `Assets/TEngine/Settings/UpdateSetting.asset` | 同步序列化数据 |
| `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs` | 拷贝目标改子目录 + manifest 路径 + 新增 pdb 拷贝 |
| `.gitignore` | 扩展 dll/pdb 忽略规则覆盖子目录 |
| `Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset` | AOT 分组 CollectPath（用户处理） |
| `Assets/AssetBundleCollectorConfig.xml` | 三分组同步（用户处理） |
