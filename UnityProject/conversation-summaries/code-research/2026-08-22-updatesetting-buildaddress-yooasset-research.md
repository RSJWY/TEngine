# UpdateSetting.BuildAddress 与 YooAsset 内置资源复制链路研究

**研究日期**：2026-08-22
**研究范围**：`UpdateSetting.BuildAddress` / `isAutoAssetCopeToBuildAddress` 字段用途，结合 YooAsset 2.3.19 `BuildinFileRoot` / `TaskCopyBuildinFiles` 内置资源复制机制
**关键词**：UpdateSetting、BuildAddress、isAutoAssetCopeToBuildAddress、YooAsset、BuildinFileRoot、GetStreamingAssetsRoot、DefaultYooFolderName、TaskCopyBuildinFiles、StreamingAssets、ReleaseTools、FullReleaseBuilder、死代码

## 结论摘要

1. **YooAsset 内置资源复制目标由 `BuildinFileRoot` 决定**，TEngine 在 `ReleaseTools.cs:257` 和 `FullReleaseBuilder.cs:83` 均将其设为 `AssetBundleBuilderHelper.GetStreamingAssetsRoot()`。
2. `GetStreamingAssetsRoot()` → `YooAssetSettingsData.GetYooDefaultBuildinRoot()` → `Application.streamingAssetsPath + DefaultYooFolderName`。本项目 `YooAssetSettings.asset` 配 `DefaultYooFolderName = package`，故复制目标 = `Assets/StreamingAssets/package/{PackageName}/`。
3. 实际复制动作由 YooAsset 的 `TaskCopyBuildinFiles.CopyBuildinFilesToStreaming()` 执行：从 `BuildOutputRoot` 版本目录拷贝 `.bundle / .version / .hash / BuildinCatalog.bytes` 到 `BuildinFileRoot`。`Assets/StreamingAssets/package/{DefaultPackage,CodePackage}/` 下文件即此产物。
4. **`UpdateSetting.BuildAddress`（默认 `../../Builds/Unity_Data/StreamingAssets`）是 TEngine 自定义的死配置**：全项目搜索 `BuildAddress / GetBuildAddress() / IsAutoAssetCopeToBuildAddress()` 只有定义与序列化值，**零调用方**，不参与 YooAsset 任何流程。
5. `isAutoAssetCopeToBuildAddress`（默认 `false`）同样是死代码，其意图「把内置资源复制到打出的 Player 目录」在 fork 中从未实现，且默认值路径 `../../Builds/Unity_Data/StreamingAssets` 与当前 `FullReleaseBuilder` 的 `Releases/app` 打包路径不匹配。
6. 修改这两个字段对打包行为**零影响**——YooAsset 只认 `BuildinFileRoot`，而 `BuildinFileRoot` 取自 YooAsset 自身的 `DefaultYooFolderName`。

## 完整链路

### YooAsset 内置资源复制链路（实际生效）

```
ReleaseTools.BuildInternalWithConfig / FullReleaseBuilder.BuildYooAssetBundle
  └─ buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot()
         │
         ▼  (YooAsset 源码)
  AssetBundleBuilderHelper.GetStreamingAssetsRoot()
         = YooAssetSettingsData.GetYooDefaultBuildinRoot()
         = Application.streamingAssetsPath + Setting.DefaultYooFolderName
         = Assets/StreamingAssets + "package"   （本项目配置）
         = Assets/StreamingAssets/package/      ← 实际复制目标根
         │
         ▼  (YooAsset 源码 TaskCopyBuildinFiles.CopyBuildinFilesToStreaming)
  源: BuildOutputRoot 下版本目录（{project}/Bundles 或 Output/Bundles）
  目标: {BuildinFileRoot}/{PackageName}/
  动作: 按 BuildinFileCopyOption 拷贝 .bundle/.version/.hash/BuildinCatalog
  结果: Assets/StreamingAssets/package/{DefaultPackage,CodePackage}/ 落地
```

### `BuildAddress` 与链路的关系

```
UpdateSetting.BuildAddress = "../../Builds/Unity_Data/StreamingAssets"
  └─ ❌ 不在上述链路任何环节；零调用方；死配置
UpdateSetting.isAutoAssetCopeToBuildAddress = false
  └─ ❌ 同上；意图「复制到 Player 目录」未实现；路径亦与现 Releases/app 路线不符
```

## 关键源码定位

| 关注点 | 位置 |
|--------|------|
| BuildAddress 字段定义 | `Assets/TEngine/Runtime/Core/UpdateSetting.cs:247-251` |
| GetBuildAddress() / IsAutoAssetCopeToBuildAddress() | `UpdateSetting.cs:512-523`（仅定义，无调用方） |
| BuildinFileRoot 赋值 | `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:257`、`Assets/Editor/Build/FullReleaseBuilder.cs:83` |
| GetStreamingAssetsRoot | `Library/PackageCache/com.tuyoogame.yooasset@2.3.19/Editor/AssetBundleBuilder/AssetBundleBuilderHelper.cs:23-26` |
| GetYooDefaultBuildinRoot | `.../Runtime/Settings/YooAssetSettingsData.cs:194-200` |
| DefaultYooFolderName 默认值 | `.../Runtime/Settings/YooAssetSettings.cs:11`（默认 "yoo"） |
| 项目实际配置 | `Assets/TEngine/Settings/Resources/YooAssetSettings.asset` → `DefaultYooFolderName: package` |
| 实际复制执行 | `.../Editor/AssetBundleBuilder/BuildPipeline/BaseTasks/TaskCopyBuildinFiles.cs:14-80` |
| 实际产物验证 | `Assets/StreamingAssets/package/{DefaultPackage,CodePackage}/*.bundle|.version|.hash|BuildinCatalog.bytes` |

## BuildinFileCopyOption 枚举（YooAsset，TEngine 透传）

| 枚举 | 含义 |
|------|------|
| `None` | 不拷贝（模拟构建用） |
| `ClearAndCopyAll` | 清空后拷贝全部（默认） |
| `ClearAndCopyByTags` | 清空后按 Tag 拷贝 |
| `OnlyCopyAll` | 仅拷贝全部（不清空，用于多包追加场景） |
| `OnlyCopyByTags` | 仅按 Tag 拷贝 |

TEngine 在 `ReleaseTools.GetBuildinFileCopyOption()` 中对「多包追加构建」自动把 `ClearAndCopy*` 降级为 `OnlyCopy*`，避免后构建的包清掉前一个包的内置文件。

## 处理建议

- **A. 删除（推荐）**：`BuildAddress`、`isAutoAssetCopeToBuildAddress` 及其两个 getter 均为冗余死配置，删除可消除「改了无效」的误导。YooAsset 内置资源复制已由 `DefaultYooFolderName + BuildinFileCopyOption` 完整覆盖。
- **B. 重新接上**：若确需「打出 Player 后把 StreamingAssets 再复制一份到 Player 目录」的能力，需在 `FullReleaseBuilder`/`ReleaseTools` 中实现读取 `GetBuildAddress()` 的复制逻辑，并校正默认路径（现 `Releases/app` 路线）。当前 Inno Setup 打安装包流程不依赖此能力。
