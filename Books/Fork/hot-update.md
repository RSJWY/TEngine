# 热更新

本页记录 fork 中围绕 HybridCLR、YooAsset 代码包、AOT 元数据和版本更新流程的改动。

## 多包架构

### 背景

原热更新流程把 DLL 混在默认资源包中，不利于后续按包独立发布、独立更新和独立加密。当前 fork 将热更程序集拆为独立 `CodePackage`。

### 改动摘要

- 热更程序集从 `DefaultPackage` 拆出为独立 `CodePackage`。
- DLL 和 AOT 元数据独立发布与更新。
- `UpdateSetting` 引入运行时资源包列表 `RuntimePackages`。
- 每个包可配置：
  - 是否启用
  - 启动时是否初始化
  - 是否更新清单
  - 是否参与下载检查
  - 是否保存版本记录
  - `VersionKey`
- 运行时初始化、清单更新、下载器创建流程均改为按包执行。
- `HostPlayMode` / `WebPlayMode` 远端版本不可用时支持回退到本地已缓存版本。
- `EditorSimulateMode` / `OfflinePlayMode` 只读取本地包版本并加载本地清单，不使用 PlayerPrefs 旧版本回退。
- 远端目录由统一平台目录改为每包独立子目录：`{host}/{project}/{platform}/{packageName}/...`。
- 程序集包判定收敛为依赖 `AssemblyPackageName` 与包名推断，移除 `IsAssemblyPackage` 字段。

### 关键文件

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Services.cs`
- `Assets/GameScripts/Procedure/ProcedureInitPackage.cs`
- `Assets/GameScripts/Procedure/ProcedureInitResources.cs`
- `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-05-28-hotfix-multipackage-summary.md`

## 代码包 XXTEA 加密

### 背景

代码包中的 DLL 更需要保护，但全局加密所有资源包会增加大资源包的内存压力和加载成本。

### 改动摘要

- 新增 `EncryptionType.XXTEA`。
- 新增打包加密 `XXTEAEncryption`。
- 新增运行时解密 `XXTEADecryption`。
- 新增 Web 解密 `XXTEAWebDecryption`。
- 构建期和运行时按 `RuntimePackageEntry.EncryptionType` 逐包判断。
- 构建窗口移除全局加密选项，改为每包选择加密方式。
- 默认配置：`DefaultPackage` 不加密，`CodePackage` 使用 XXTEA。

### 注意事项

XXTEA 解密为整包读入内存后 `AssetBundle.LoadFromMemory`，适合代码包和 DLL。大资源包使用会增加峰值内存。

### 关键文件

- `Assets/TEngine/Runtime/Module/ResourceModule/EncryptionType.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Services.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-05-30-xxtea-hotfix-update-summary.md`

## 版本确认、清单加载与下载流程

### 背景

热更新时需要区分“远端可更新包”和“本地包”。`EditorSimulateMode` 与 `OfflinePlayMode` 的版本文件来自本地模拟构建目录或 `StreamingAssets`，读取失败通常表示本地包产物或路径错误，继续使用 PlayerPrefs 里的旧版本不能修复问题。`HostPlayMode` 和 `WebPlayMode` 才需要远端版本确认、失败回退和下载检查。

### 改动摘要

- `ProcedureInitResources` 按 PlayMode 分流清单加载逻辑。
- `EditorSimulateMode` / `OfflinePlayMode`：读取本地包版本，加载同版本本地 manifest，成功后直接进入预加载。
- 本地包版本读取或 manifest 加载失败时直接报初始化错误并允许重试，不读取 `VersionKey`，不弹版本确认，不执行旧版本回退。
- `HostPlayMode` / `WebPlayMode`：读取 `VersionKey` 保存的上次成功版本，请求当前包版本并执行版本比对。
- 远端版本请求失败且存在保存版本时，回退加载保存版本 manifest，并提示用户可重试远端版本或继续本地完整性检查。
- 检测到版本变化时，`UpdateStyle.Force` 只允许确认更新，`UpdateStyle.Optional` 且已有本地版本时允许取消并继续使用本地版本。
- 用户不操作时，版本确认按 `VersionConfirmAutoDelaySeconds` 倒计时自动确认。
- `ProcedureCreateDownloader` 只负责按包检查缺失文件和执行下载；已确认版本更新时直接开始下载，否则显示资源完整性下载确认。
- `ProcedureDownloadOver` 只在下载/检查结束后写入本次选择的包版本记录。

### 关键文件

- `Assets/GameScripts/Procedure/ProcedureInitResources.cs`
- `Assets/GameScripts/Procedure/ProcedureCreateDownloader.cs`
- `Assets/GameScripts/Procedure/ProcedureDownloadOver.cs`
- `Assets/GameScripts/Procedure/ProcedureBase.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-05-30-xxtea-hotfix-update-summary.md`
- `UnityProject/conversation-summaries/2026-05-30-hotfix-update-confirm-flow-summary.md`

## AOT 元数据热更清单

### 背景

原 AOT 元数据列表耦合在基础包序列化引用中，后续热更补充 AOT DLL 不够灵活。

### 改动摘要

- 新增 `AOTMetadataManifest` ScriptableObject。
- 新增 `Assets/AssetRaw/DLL/AOTMetadataManifest.asset`。
- manifest 随 `CodePackage` 被 YooAsset 收集并热更。
- 构建期 `BuildDLLCommand` 优先读取 manifest。
- manifest 为空时回退 `UpdateSetting.AOTMetaAssemblies`。
- 构建期自动合并 `AOTGenericReferences.PatchedAOTAssemblyList` 的缺失项。
- 运行时 `ProcedureLoadAssembly` 优先从 `CodePackage` 加载 manifest，再调用 `HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly`。

### 注意事项

旧基础包无法直接享受该机制。需要先发一次包含新逻辑的基础包，后续才能通过 `CodePackage` 热更 manifest 与新增 AOT DLL。

### 相关记录

- `UnityProject/conversation-summaries/aot-metadata-manifest-hotfix-summary.md`
- `UnityProject/conversation-summaries/AOTMetaAssemblies-summary.md`

## AOT 元数据打包期校验

### 背景

如果 `AOTMetadataManifest.asset` 缺少 `AOTGenericReferences.PatchedAOTAssemblyList` 中的程序集，运行时可能出现 `ExecutionEngineException`。这类问题应该在打包期中断。

### 改动摘要

- 打 AB 包时单向校验 `AOTMetadataManifest.asset`。
- manifest 必须包含 `AOTGenericReferences.PatchedAOTAssemblyList` 的全部程序集。
- 缺失时中断构建。
- manifest 含手动补充的额外项时仅告警。
- 拷贝 AOT DLL 时，源文件不存在改为报错中断，不再静默跳过。
- 新增菜单 `HybridCLR/Build/Sync AOT Metadata Manifest`。
- 打包工具窗口新增「同步 AOT 元数据清单」「编译并拷贝热更DLL」按钮。
- 同步 manifest 时保留手动添加项。

### 关键文件

- `Assets/TEngine/Editor/ReleaseTools/BuildDLLCommand.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/AssetRaw/DLL/AOTMetadataManifest.asset`

## PlayerPrefs 版本记录清理工具

### 背景

反复测试热更新时，经常需要清理“上次成功更新版本号”。直接清理全部 PlayerPrefs 风险太高。

### 改动摘要

- 新增 Editor 窗口。
- 菜单入口：`TEngine/HotUpdate/Package Version PlayerPrefs`。
- 自动读取 `UpdateSetting.RuntimePackages` 中各包的 `VersionKey`。
- 展示包名、启用状态、`SaveVersion`、`VersionKey`、当前 PlayerPrefs 值。
- 支持刷新、选中有记录、全选、清理选中、清理全部、单行清理。
- 仅对展示的 `VersionKey` 执行 `DeleteKey` 后 `Save`。
- 不调用 `DeleteAll`，不操作注册表，不影响其他 PlayerPrefs。

### 关键文件

- `Assets/TEngine/Editor/Utility/HotUpdatePlayerPrefsTool.cs`

### 相关记录

- `UnityProject/conversation-summaries/2026-06-01-hotupdate-playerprefs-tool-summary.md`
