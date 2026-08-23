# InnoSetup 安装包 iss 变量输入补全

## 背景

`setup.iss` 顶部用 `#define` 暴露应用名/版本/发布者/exe 名/AppId/安装密码/水印等元信息，注释声明「打包工具构建时按窗口参数回写」。但 InnoSetup 集成初版（见 [2026-08-23-releases-unify-innosetup-integration-summary](./2026-08-23-releases-unify-innosetup-integration-summary.md)）只在窗口暴露「安装包版本」「ISCC 路径」，`SyncIssDefines` 实际只回写 `MyAppExeName`/`MyAppVersion`；应用名/发布者/密码/水印长期停在 iss 模板写死的占位值。本次补全为窗口输入字段，使一键出包即出即用。

## 关键改动

### 1. 回写范围扩展（InnoSetupBuilder）

- 新增 `IssInstallerConfig` 类承载窗口侧输入：`AppName`/`InstallerVersion`/`Publisher`/`ExeName`/`Password`/`Watermark`。
- `BuildInstaller` 签名由 `(installerVersion, exeName, isccPathOverride)` 改为 `(IssInstallerConfig config, string isccPathOverride)`。
- `SyncIssDefines` 回写由 2 项扩到 6 项：`MyAppName`/`MyAppPublisher`/`MyAppPassword`/`BrandWatermark` + 既有的 `MyAppExeName`/`MyAppVersion`（版本为空时不回写、沿用 iss 现值，避免清空版本号）。
- **`MyAppId` 不回写**，始终以 `setup.iss` 文件内手填值为准（决定升级覆盖 vs 并存身份，需稳定）；iss 顶部注释同步标注此约束。

### 2. 配置字段持久化（BuildConfig / BuildPipelineSetting）

- 两处各新增 4 字段：`InstallerAppName`/`InstallerPublisher`/`InstallerPassword`/`InstallerWatermark`。
- `BuildConfig.CreateDefault()`：应用名默认取 `PlayerSettings.productName`、发布者取 `PlayerSettings.companyName`；密码/水印默认空。
- `BuildPipelineSetting.ApplyDefaults()` 同步赋值 4 字段。

### 3. 窗口 UI 双 Box 分区（BuildPipelineWindow）

- 「安装包配置」tab 拆为两个 BoxGroup：
  - **InnoSetup 安装包**（参数区）：构建安装包开关、安装包版本、应用名称、发布者、安装密码、发布者水印。
  - **ISCC 编译**（工具区）：ISCC 路径（浏览/打开）、ISCC 状态（只读）、iss 脚本（只读）、安装包输出预览、一键构建安装包按钮。
- 4 个新字段统一 `[ShowIf(IsInstallerEnabled)]`/`[DelayedProperty]`/`[OnValueChanged(OnSettingsChanged)]`。
- 全链路同步：`LoadFromSetting`/`SaveSettings`/`ApplyConfigToFields`/`CreateConfig` 四处补齐 4 字段读写。
- `LoadFromSetting` 首次为空时从 `PlayerSettings` 补应用名/发布者默认值并持久化（老工程无这些字段时自动迁移）。
- `ExecuteInstallerBuild` 改为组装 `IssInstallerConfig` 调用 `InnoSetupBuilder.BuildInstaller`；水印为空时回退用发布者值。

### 4. iss 注释更新（setup.iss）

- 顶部回写清单移除 `MyAppId`，注明「MyAppId 不参与回写，始终以本文件内手填值为准」。

## 设计决策（用户拍板）

- **应用名称 MyAppName**：做输入字段，主要填中文软件名；默认 `PlayerSettings.productName`。
- **发布者 MyAppPublisher**：做输入字段；默认 `PlayerSettings.companyName`。最新版 iss 已将默认安装目录改用 `MyAppName`，发布者不再影响安装路径，仅向导显示。
- **应用ID MyAppId**：**不做输入**，以 iss 文件为准，不回写（用户选 c 方案）。
- **安装密码 MyAppPassword**：做输入字段，空=不加密，非空自动启用 `Password`+`Encryption`。
- **发布者水印 BrandWatermark**：做输入字段，默认随发布者，可单独改；为空时回退用发布者值。
- `MyAppExeName`（自动取 `PlayerSettings.productName`）、`MyAppVersion`（窗口「安装包版本」）维持现状。

## 踩坑

- `BuildPipelineWindow.cs` 第一版拆 BoxGroup 时，先用一个 Edit 删掉夹在参数中间的 `_isccPath` 字段声明、打算在第二个 Edit 里随 ISCC 区一起重写；但第二个 Edit 的 old_string 未精确匹配导致只有部分写入，`_isccPath` 字段声明丢失，编译报 `CS0103: _isccPath does not exist`。修复：在水印字段后补回 `_isccPath` 声明，并把 ISCC 区所有只读字段/按钮的 `BoxGroup` 路径统一改为 `Pages/安装包配置/ISCC 编译`。

## 关键文件

- `Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`（`IssInstallerConfig`、`BuildInstaller`、`SyncIssDefines`）
- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`（4 字段 + `CreateDefault` 默认值）
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineSetting.cs`（4 字段 + `ApplyDefaults`）
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`（4 个 UI 字段 + 双 Box 分区 + 全链路同步 + `ExecuteInstallerBuild`）
- `Releases/Windows/setup.iss`（注释更新；本次改动测试数据已回退，未提交）

## 提交

- commit `cdcee775`：仅 4 个 cs 文件；`setup.iss`、`BuildPipelineSetting.asset`（含测试数据）已 `git checkout` 回退，未提交。
- fork 文档已更新：`Books/Fork/resource-build.md` 追加「iss 变量输入补全」专题并修正旧专题过时表述；`Books/Fork/CHANGELOG.md` 2026-08-23 顶部追加一条。

## 待办 / 注意事项

- `MyAppId` 改动影响升级覆盖（同 AppId 覆盖、不同并存），首次部署需在 iss 填稳定值，勿随手改。
- 密码以明文写入 iss 并随仓库可见，敏感场景注意仓库可见范围。
- 老工程升级后首次打开窗口自动从 `PlayerSettings` 补应用名/发布者；密码、水印默认空，需按需填写。
