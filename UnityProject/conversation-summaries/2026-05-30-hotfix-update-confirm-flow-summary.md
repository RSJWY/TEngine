# 热更新版本确认与下载流程改造会话总结

日期：2026-05-30

## 背景

本次会话围绕启动热更新提示不弹窗的问题展开。最初排查 `UpdateSetting.asset` 中的 `UpdateStyle`、`UpdateNotice` 与 `ResourceModuleDriver.updatableWhilePlaying` 的关系，确认主场景里 `updatableWhilePlaying = true` 会绕过启动阶段的 `ProcedureCreateDownloader` 弹窗流程。

随后讨论边玩边下的实际表现，明确其语义更偏向“启动不拦截，运行中按需下载资源”，不适合复用原先下载阶段的强确认弹窗。

## 关键决策

1. “没有找到本地版本记录”的判断不应放在 `ProcedureCreateDownloader` 下载阶段。
2. 资源包版本判断应前移到 `ProcedureInitResources` 获取远端资源包版本后处理。
3. 版本比对日志需要同时打印上次本地版本和本次远端版本。
4. 首次没有本地版本记录时，只显示确定选项，并在 5 秒后自动确认。
5. 有本地版本且远端版本变化时，弹窗让用户选择：
   - 确定：使用远端版本并进入完整性检查/下载；
   - 取消：继续使用本地版本，但仍进入完整性检查。
6. 弱联网无法获取远端版本时：
   - 若有本地版本记录，则回退本地清单，并继续走资源完整性检查；
   - 若本地资产不完整，必须进入下载流程补齐；
   - 若无本地版本记录，则按初始化失败处理。
7. `ProcedureCreateDownloader` 只负责资源包完整性检查与实际下载，不再决定版本选择。
8. 启动更新相关按钮和提示文本需要中文化。
9. 自动确认倒计时需要显示到实际触发按钮上，例如 `确定(5)` 或 `取消(5)`。

## 已修改内容

- `ProcedureInitResources.cs`
  - 获取远端版本后立即与本地版本记录比对。
  - 首次/版本变化时弹出确认框，5 秒自动确认。
  - 记录版本选择结果，并控制后续是否进入下载完整性检查。
  - 远端版本获取失败时回退本地版本，并继续检查资源完整性。

- `ProcedureCreateDownloader.cs`
  - 删除下载阶段的本地版本记录判断与跳过下载逻辑。
  - 改为只检查当前清单下缺失文件数量和大小。
  - 已在版本阶段确认更新时，不重复弹窗，直接下载。
  - 本地回退或资源缺失场景下，提示“资源包不完整，需要下载缺失文件”。

- `ProcedureBase.cs`
  - 新增 `ConfirmedVersionUpdateKey`，用于标记用户已确认使用远端版本。

- `ProcedureInitPackage.cs` / `ProcedureDownloadOver.cs`
  - 清理 `ConfirmedVersionUpdateKey`，避免状态残留影响后续流程。

- `ProcedureDownloadFile.cs`
  - 下载失败弹窗中文化。

- `LoadTipsUI.cs`
  - 默认按钮文本从英文改为中文：确定 / 取消 / 更新。
  - 自动确认倒计时同步显示在实际触发按钮上。

## 验证

执行：

```bash
dotnet build UnityProject.sln --no-restore
```

结果：

```text
已成功生成。
0 个警告
0 个错误
```

同时执行过 `git diff --check`，未发现空白错误，仅有 Git 关于 LF/CRLF 的工作区提示。
