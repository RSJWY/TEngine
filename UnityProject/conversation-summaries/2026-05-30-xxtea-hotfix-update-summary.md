# 2026-05-30 XXTEA 与热更新提示恢复会话总结

## 背景

本次会话围绕两个连续需求：

1. 热更 DLL/代码包加密：希望使用 XXTEA，并且不要全局加密所有资源包，只保护代码包。
2. 热更新提示行为回归：检测到远端新版本后，不应一律强制更新；应恢复“有本地版本可取消更新、无本地版本强制更新”的流程。

项目规则要求 L2-L4 任务先查询 `tengine-dev` skill。本会话已查询热更、资源和 UI 相关规范。

## 已提交内容

### 提交 1

- Commit: `f93aab90 支持代码包 XXTEA 加密配置。`

主要内容：

- 新增 `EncryptionType.XXTEA`。
- 新增 XXTEA 打包加密服务：`XXTEAEncryption`。
- 新增运行时解密服务：`XXTEADecryption`。
- 新增 Web 解密服务：`XXTEAWebDecryption`。
- 构建期从全局加密配置改为按 `RuntimePackageEntry.EncryptionType` 判断。
- 运行时初始化资源包时，按 `packageName` 查询 `UpdateSetting.GetRuntimePackage(packageName).EncryptionType`，再创建对应解密服务。
- 构建窗口资源包列表增加“加密方式”选择。
- 移除构建窗口全局加密选择，避免所有包都被同一策略加密。
- `UpdateSetting.asset` 设置：
  - `DefaultPackage`: `EncryptionType: 0`，不加密。
  - `CodePackage`: `EncryptionType: 3`，XXTEA。
- 资源清单流程没有接入自定义加解密，仍走 YooAsset 默认 `RequestPackageVersionAsync` / `UpdatePackageManifestAsync`。

关键文件：

- `Assets/TEngine/Runtime/Module/ResourceModule/EncryptionType.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Services.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `Assets/TEngine/Runtime/Core/UpdateSetting.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Settings/UpdateSetting.asset`

注意：XXTEA 解密当前是整包读入内存后 `AssetBundle.LoadFromMemory/Async`，对代码包/DLL 包合适，但大资源包会增加峰值内存。当前需求只保护代码包。

### 提交 2

- Commit: `cf75d022 恢复热更新可选更新提示。`

主要内容：

- 恢复热更新下载前的可选更新判断。
- `ProcedureCreateDownloader` 检测到下载文件后：
  - 如果 `UpdateStyle.Optional`，且所有待下载包都有本地版本记录，并且本地版本与远端版本不同：弹出确认/取消提示。
  - 不操作时沿用 `AutoStartDownloadDelaySeconds = 10f`，倒计时后自动确认更新。
  - 如果没有本地版本记录：只显示确认按钮，强制更新。
  - 如果 `UpdateNotice.NoNotice`：直接跳过更新并进入本地资源流程。
- 用户取消更新时：
  - 回退待下载包到本地版本清单。
  - 设置 `SkipDownloadVersionSaveKey`。
  - 进入 `ProcedureDownloadOver`。
- `ProcedureDownloadOver` 看到 `SkipDownloadVersionSaveKey` 后，不写入远端版本记录，避免用户取消更新后误把远端版本记成本地已更新版本。

关键文件：

- `Assets/GameScripts/Procedure/ProcedureBase.cs`
- `Assets/GameScripts/Procedure/ProcedureCreateDownloader.cs`
- `Assets/GameScripts/Procedure/ProcedureDownloadOver.cs`

## 当前工作区状态

提交 `cf75d022` 后仍有未提交改动，未纳入上述提交：

- `Assets/AssetRaw/UI/BattleMainUI.prefab`
- `Assets/GameScripts/HotFix/GameLogic/GameApp.cs`
- `Assets/Scenes/main.unity`
- `Assets/TEngine/Settings/UpdateSetting.asset`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/HybridCLRSettings.asset`
- `ProjectSettings/ProjectSettings.asset`
- `Assets/AssetRaw/DLL/`

这些改动可能来自 Unity 打包、热更 DLL 生成、编辑器配置或用户手动修改。后续不要误删或误提交，除非用户明确要求。

## 验证情况

已做：

- `git diff --check` 检查相关目标文件。
- 检查 FSM `SetData<T>` / `GetData<T>` 支持 `bool`。
- 检查运行时按包选择解密服务的引用路径。
- 检查资源清单请求/更新流程未做自定义加解密。

未做：

- 没有跑 Unity 编译。
- 没有完整打包验证。
- 没有运行 HostPlay/OfflinePlay 真机或编辑器流程验证。

## 后续建议

1. 先开 Unity 编译，重点检查：
   - `ProcedureCreateDownloader.cs` 中多行逐字插值字符串是否符合当前 Unity C# 版本。
   - `SkipDownload(downloadPackageNames).Forget()` 和 YooAsset operation `await manifestOperation` 是否编译通过。
2. 用 `HostPlayMode` 测试热更新流程：
   - 首次无本地版本记录：应强制更新，只能确认。
   - 有本地版本记录且远端版本不同：应弹确认/取消，10 秒后自动确认。
   - 取消更新：应回退本地清单并进入游戏，不写入远端版本记录。
3. 测试代码包加密：
   - 重新构建资源包，确认 `CodePackage` 使用 XXTEA。
   - 清缓存后跑一次，确认 `ProcedureLoadAssembly` 能正常加载热更 DLL。
   - 确认 `DefaultPackage` 不被加密且资源正常加载。

## 历史范围说明

用户说明：`fa7ffd455f80a7ecf2ce5b3547ae137d43bd0be7` 是他们最早的提交，再往前是 fork 历史。后续排查“用户改动导致的回归”时，应从该提交之后看，不要把更早 fork 历史当作用户改动依据。
