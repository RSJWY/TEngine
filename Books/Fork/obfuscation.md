# 代码混淆（Obfuz）

## Obfuz 接入与 dnlib 冲突解决

### 背景

项目接入 Obfuz 做代码混淆，并配合 `obfuz4hybridclr` 扩展包支持 HybridCLR 热更工作流。Obfuz 内置的是**定制版 dnlib**（新增 `PolymorphicWriter` 等多态 dll 相关类型），而 HybridCLR 内置的是官方原版 dnlib。两者同时存在时 Unity 可能把原版 dnlib 解析给 `obfuz4hybridclr`，导致 `dnlib.DotNet.PolymorphicWriter` 找不到的编译错误（CS0234/CS0246）。

### 改动摘要

- `com.code-philosophy.hybridclr`、`com.code-philosophy.obfuz` 从 git URL 包转为 `Packages/` 下的**本地包**（manifest 改为 `file:` 引用），包内容提交进版本库。
- 移除本地 HybridCLR 包的 `Plugins/dnlib.dll`，全项目只保留 Obfuz 的定制 dnlib（官方原版的功能超集，HybridCLR 代码可正常编译）。
- 新增一键同步脚本 `Packages/sync-hybridclr-local.sh` / `sync-obfuz-local.sh`（及对应 `.bat` 双击包装），负责拉取指定版本、同步为本地包、删除 HybridCLR 的 dnlib、改写 manifest。
- `com.code-philosophy.obfuz4hybridclr` 仍为 git URL 包（不含 dnlib，无冲突）。

### 使用方式

```bash
# 安装/升级到最新稳定 tag（自动解析，跳过预发布版）
bash Packages/sync-hybridclr-local.sh
bash Packages/sync-obfuz-local.sh

# 指定版本（tag / 分支 / 完整 commit SHA 均可）
bash Packages/sync-hybridclr-local.sh v8.13.0
bash Packages/sync-obfuz-local.sh v3.1.0
```

也可双击对应 `.bat` 运行。默认从 GitHub 拉取，国内网络可用环境变量切 gitee 镜像：

```bash
SYNC_HYBRIDCLR_REPO=https://gitee.com/focus-creative-games/hybridclr_unity.git bash Packages/sync-hybridclr-local.sh
SYNC_OBFUZ_REPO=https://gitee.com/focus-creative-games/obfuz.git bash Packages/sync-obfuz-local.sh
```

混淆功能开关：菜单 `Obfuz/Settings...` → Build Pipeline Settings → `Enable`，关闭后构建流程与未装 Obfuz 一致。

### 注意事项

- **升级 HybridCLR/Obfuz 必须重跑对应脚本**，不要用 Package Manager 直接更新——脚本会重新执行删 dnlib 和 manifest 改写，漏掉则冲突复发。
- 本地包已入库（跟随 `Packages/MCPForUnity` 先例），拉代码即可用，无需先跑脚本；脚本只在升级时需要。
- App 发布后不要修改 Obfuz 的静态密钥；App 与热更包的混淆状态需保持一致。
- Obfuz 与 HybridCLR 都通过 Package Manager 更新会重新引入双 dnlib，属已知禁区。

### 关键文件

- `UnityProject/Packages/sync-hybridclr-local.sh`、`UnityProject/Packages/sync-hybridclr-local.bat`
- `UnityProject/Packages/sync-obfuz-local.sh`、`UnityProject/Packages/sync-obfuz-local.bat`
- `UnityProject/Packages/manifest.json`
- `UnityProject/Packages/com.code-philosophy.hybridclr/`、`UnityProject/Packages/com.code-philosophy.obfuz/`

## Obfuz 运行时静态密钥初始化

### 背景

Obfuz 的 `ConstEncrypt`/`FieldEncrypt` 等 Pass 会在编译期把常量与字段加密，运行时必须有 `EncryptionService<DefaultStaticEncryptionScope>.Encryptor` 注入才能解密。未初始化就跑混淆代码会触发 `$$Obfuz$RVA$` 类型初始化异常。Obfuz 官方推荐在 `[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]` 时机初始化静态密钥——这是主包/AOT 程序集刚加载完、任何被混淆代码执行前的最早时机。

### 改动摘要

- 新增 `ObfuzRuntimeInitializer`（Assembly-CSharp，不混淆），用 `[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]` 加载 `Resources/Obfuz/defaultStaticSecretKey.bytes` 并构造 `GeneratedEncryptionVirtualMachine` 注入 `EncryptionService<DefaultStaticEncryptionScope>.Encryptor`。
- `#if ENABLE_OBFUZ && !UNITY_EDITOR` 守卫：Editor 下整段不编译。Obfuz 官方 FAQ 明确禁止 Editor 运行混淆后代码（Editor 已加载原始未混淆程序集，混淆 DLL 引用混淆后类型会 `TypeLoadException`）；且 `EditorSimulateMode` 加载原始未混淆程序集，注入 Encryptor 反会把正常常量当密文解、破坏运行。
- 失败延迟报告：`AfterAssembliesLoaded` 时场景/UI 未就绪，无法弹窗。失败仅记标志 + `Log.Fatal`，由 `ProcedureLaunch.OnEnter` 在 `LauncherMgr.Initialize()` 之后 UI 可用时调 `CheckFailureAndReport()` 消费——经 `LauncherMgr.ShowMessageBox` 弹仅含确认按钮的原生对话框，点击后 `Application.Quit()` 退出。
- 失败阻断：`ProcedureLaunch` 弹窗后 `return` 跳过语言/声音/部署配置加载，`OnUpdate` 因 `_deployConfigLoaded` 恒 false 卡住，直到用户确认退出。

### 使用方式

无需手动调用。启用 `ENABLE_OBFUZ` 宏并打包真机后，`AfterAssembliesLoaded` 自动触发初始化。密钥文件 `Resources/Obfuz/defaultStaticSecretKey.bytes` 随主包出包，不参与热更。

关闭 Obfuz（宏未定义）时初始化代码整段不编译，零副作用。

### 注意事项

- **Editor 下不可测混淆代码**：Obfuz FAQ 明确禁止。启用加密 Pass 后只能在真机验证，不要在 EditorSimulateMode 下测试。
- App 发布后不要修改静态密钥；App 与热更包的混淆状态需保持一致。
- 密钥缺失失败时游戏会弹窗并退出，不会继续跑到 `ProcedureLoadAssembly`（那里会因 Encryptor 为 null 而崩成乱码）。
- `Launcher` 程序集在 `nonObfuscatedButReferencingObfuscatedAssemblies`（永不混淆），报告链路全程无混淆风险；但 UI 显示仍受场景依赖制约，故报告延迟到 `ProcedureLaunch` 而非 `AfterAssembliesLoaded`。
- 初始化类 `ObfuzRuntimeInitializer` 在 Assembly-CSharp（不混淆），无需 `[ObfuzIgnore]`。

### 关键文件

- `UnityProject/Assets/GameScripts/ObfuzRuntimeInitializer.cs`
- `UnityProject/Assets/GameScripts/Procedure/ProcedureLaunch.cs`（`OnEnter` 调 `CheckFailureAndReport`）
- `UnityProject/Assets/Resources/Obfuz/defaultStaticSecretKey.bytes`
- `UnityProject/Assets/Obfuz/GeneratedEncryptionVirtualMachine.cs`
- `UnityProject/ProjectSettings/Obfuz.asset`

### 相关记录

- `UnityProject/conversation-summaries/code-research/2026-08-28-dgame-obfuz-secret-loading-analysis.md`
- `UnityProject/conversation-summaries/code-research/2026-08-14-obfuz-runtime-and-scope-research.md`
