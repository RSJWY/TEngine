# Inno Setup 构建可靠性与模板分离实施计划

## 背景

当前 Inno Setup 已接入 `BuildPipelineWindow`，能够串联 AssetBundle、Player 与安装包构建，但仍存在以下问题：

- AssetBundle 构建失败时可能只提前返回，没有把失败传递给窗口，导致后续继续使用旧 Player 产物生成安装包。
- Player 输出路径允许自定义，但缺少只替换平台目录、保留其余层级的规范化能力。
- Windows64 安装包没有显式拒绝 32 位系统。
- 升级时旧卸载器失败或超时后仍可能继续删除整个 `{app}`。
- `setup.iss` 同时承担模板和实际构建脚本职责，面板回写会修改受版本控制的模板。
- ISCC 同时重定向 stdout/stderr，但当前只同步读取 stdout；失败时可能阻塞，且进度条没有在异常路径清理。

本文记录已经确认的实施边界。后续实现应以本文为准，不重新扩大范围。

## 已确认的设计决策

### 1. 三层职责

- `Releases/Windows/setup.iss`
  - 框架项目提供的 Inno Setup 模板。
  - 纳入版本管理。
  - Unity 面板和安装包构建流程不得回写此文件。
  - 用户可直接修改 `DiskSpanning`、`DiskSliceSize`、`SlicesPerDisk`、`[Files]`、`[Run]`、`[Code]` 等模板结构。

- `Assets/TEngine/Settings/BuildPipelineSetting.asset`
  - 保存构建面板参数。
  - 继续保存 `InstallerPassword`，密码属于实际项目需要同步的构建配置。
  - 其他 Inno Setup 面板参数也继续按现有机制持久化。

- `Releases/Windows/setup.generated.iss`
  - 从模板生成的实际编译脚本。
  - 不纳入版本管理，加入 `.gitignore`。
  - 面板配置只回写此文件。
  - ISCC 只编译此文件。
  - 允许实际项目在生成后继续手动定制；普通打开面板和普通构建不得用模板覆盖已有生成文件。

### 2. 明确不做的事项

- 不引入 `{localappdata}` 数据目录方案。
- 不负责 Unity 游戏配置、日志、缓存、存档等运行时数据迁移；这些由实际项目程序员自行设计。
- Inno Setup 的 `[Files]` 输入目录继续硬编码为 `Releases/Windows/build`，不改为从 Player 输出路径动态派生。
- 保留 `DiskSpanning=yes` 及现有分卷配置；是否关闭分卷由用户手动修改模板 `.iss`。
- `InstallerPassword` 不从 `BuildPipelineSetting.asset` 移除。
- `MyAppId` 仍以 `.iss` 文件中的值为准，不增加面板输入和自动回写。

## 实施任务

### A. 模板与生成脚本分离

修改 `InnoSetupBuilder.cs`：

1. 保留模板路径：

   ```text
   Releases/Windows/setup.iss
   ```

2. 新增实际脚本路径：

   ```text
   Releases/Windows/setup.generated.iss
   ```

3. 增加生成脚本初始化方法：
   - 模板不存在：返回明确错误，不创建空文件。
   - 生成脚本不存在：复制模板生成。
   - 生成脚本已存在：保持原文件，不自动覆盖。
   - 首次复制后，将当前 `BuildPipelineSetting.asset`/面板配置同步到生成脚本。

4. `GetIssDefine`、`WriteIssDefine`、`SyncIssDefines` 和 ISCC 编译入口默认操作 `setup.generated.iss`。

5. 同步的字段保持为：

   ```text
   MyAppName
   MyAppEnglishName
   MyAppVersion
   MyAppPublisher
   MyAppExeName
   MyAppPassword
   BrandWatermark
   ```

6. `MyAppId` 不参与面板同步，首次生成时从模板原样复制。

7. 一次性读取生成脚本、一次性替换全部约定字段、一次性写回，避免当前每个字段重复读写文件。

8. 每个约定字段必须精确匹配一次；缺失或重复时构建失败并指出字段名，不再静默跳过。

9. 写回前正确转义 Inno Setup 预处理器字符串中的双引号等特殊内容，避免面板文本破坏 `.iss` 语法。

10. 模板中的 `MyAppPassword` 默认值恢复为空字符串，避免框架模板携带测试密码。

修改 `.gitignore`：

```gitignore
/Releases/Windows/setup.generated.iss
```

### B. 面板打开时初始化生成脚本

修改 `BuildPipelineWindow.cs`：

1. 窗口加载 `BuildPipelineSetting.asset` 后检查 `setup.generated.iss`。
2. 不存在时立即从模板生成，并同步当前持久化配置，使 Inno Setup 参数页无需先执行一次构建即可直接使用。
3. 初始化失败时不阻止整个构建窗口打开；在 Inno Setup 配置页显示明确错误状态，并在 Console 输出原因。
4. 已存在时不得自动用模板覆盖。
5. `IssScriptPath`/状态展示改为显示实际编译脚本路径，同时额外展示模板路径，避免用户修改错文件。
6. 新增按钮：

   ```text
   从模板重新生成
   ```

7. 点击重新生成按钮时：
   - 弹出确认提示，说明会覆盖现有 `setup.generated.iss` 的手工修改。
   - 用户确认后，用当前模板覆盖生成脚本。
   - 重新应用当前面板/`BuildPipelineSetting.asset` 参数。
   - 刷新脚本状态。

### C. 阻断旧产物被误打包

修改 `ReleaseTools.cs` 与 `BuildPipelineWindow.cs`：

1. `BuildWithConfig` 返回明确构建结果，或在任一失败点抛出异常；不得仅记录错误后 `return`。
2. AssetBundle、热更 DLL、Player 任一步失败，一键安装包流程立即中止。
3. `_lastBuildFailed` 只能在全部前置步骤真实成功后设为 `false`。
4. 安装包编译前执行硬编码目录预检：

   ```text
   Releases/Windows/build
   ```

5. 预检内容：
   - build 目录存在。
   - build 目录非空。
   - `MyAppExeName` 对应的主 EXE 存在于 build 根目录。
6. 预检失败时不调用 ISCC，并提示用户先构建 Windows Player。

### D. Player 输出路径“规范化路径”按钮

修改 `BuildPipelineWindow.cs` 中 Player 输出路径区域：

1. 新增按钮：

   ```text
   规范化路径
   ```

2. 目标平台取当前 `PlayerPlatform`。
3. 只替换平台目录这一层，后续子目录和文件名保持不变。

示例：

```text
Releases/Windows/Client/build/Game.exe
```

当目标平台切换为 Linux 后，规范化为：

```text
Releases/Linux/Client/build/Game.exe
```

4. 同时支持项目相对路径和绝对路径。
5. 识别平台目录时采用完整路径段匹配，不做普通字符串替换，避免误改文件名或其他目录中的 `Windows` 文本。
6. 统一使用项目现有的平台目录名称映射，例如：
   - `StandaloneWindows64` -> `Windows`
   - `StandaloneLinux64` -> `Linux`
7. 如果路径中存在可识别的旧平台目录，则只替换该段。
8. 如果路径位于 `Releases` 下但缺少平台段，则在 `Releases` 后插入目标平台段，并保留其余层级。
9. 如果路径无法可靠识别，不覆盖原值，只提示用户手动调整。
10. 规范化完成后回填 `_playerOutputPath` 并调用现有设置保存逻辑。

### E. Windows64 架构限制

修改模板 `Releases/Windows/setup.iss`：

```ini
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
```

确保 Windows64 Player 不会继续安装到不兼容的 32 位系统。

### F. 安全处理旧版本卸载

修改模板 `setup.iss` 的升级逻辑：

1. 保留检测旧版本并询问用户是否卸载的流程。
2. 调用旧卸载器后必须检查：
   - `Exec` 是否成功启动。
   - 卸载器退出码是否表示成功。
   - 卸载注册表项是否在等待时间内消失。
3. 任一条件失败时返回明确错误并终止新安装。
4. 60 秒轮询结束后注册表项仍存在，视为卸载超时，终止安装。
5. 只有确认旧卸载完成后才能继续安装。
6. 删除升级流程中兜底执行的：

   ```pascal
   DelTree(ExpandConstant('{app}'), True, True, True)
   ```

7. 本次不修改 `[UninstallDelete]` 的既有产品策略，不讨论 Unity 运行时用户数据迁移。

### G. 修复 ISCC 卡死与进度条残留

修改 `InnoSetupBuilder.cs`：

1. stdout 和 stderr 必须并行、异步读取，避免任一管道缓冲区写满造成互相等待。
2. 同时保存完整 stdout/stderr：
   - 成功时输出正常编译日志。
   - 失败时异常信息包含 ExitCode、stdout 和 stderr。
3. `Process.Start` 返回空、进程启动异常等情况提供明确错误。
4. 增加合理的编译超时保护；超时后结束 ISCC 进程并报告超时，不让 Unity 永久等待。
5. `EditorUtility.DisplayProgressBar` 与 `ClearProgressBar` 必须用 `try/finally` 配对。
6. 无论以下哪种情况发生，都必须清除进度条：
   - 生成脚本失败。
   - 构建前校验失败。
   - 找不到 ISCC。
   - ISCC 返回非零退出码。
   - ISCC 输出读取异常。
   - ISCC 超时。

## 建议实施顺序

1. 模板/生成脚本路径拆分及 `.gitignore`。
2. 窗口打开时初始化生成脚本，以及“从模板重新生成”按钮。
3. `SyncIssDefines` 单次、严格、可转义写回。
4. ISCC 并行日志读取、超时和进度条 `finally`。
5. 安装包构建前的硬编码目录和 EXE 校验。
6. 构建结果向上传播，阻断旧产物打包。
7. Player 输出路径“规范化路径”按钮。
8. 模板架构限制和旧版本卸载安全修正。
9. Unity 编译、ISCC 成功/失败路径验证。
10. 更新 fork 构建文档和 CHANGELOG。

## 验收条件

### 生成脚本

- 删除 `setup.generated.iss` 后打开构建窗口，会自动重新生成。
- 已存在的生成脚本不会因为打开窗口或普通构建而被模板覆盖。
- 点击“从模板重新生成”并确认后，生成脚本采用最新模板，同时重新应用面板配置。
- 修改面板参数后，只改变 `setup.generated.iss`，`setup.iss` 的 Git 状态保持干净。
- 密码继续保存到 `BuildPipelineSetting.asset`，并能同步到生成脚本。
- `MyAppId` 保持模板值，不被面板覆盖。

### 构建阻断

- 主动制造 AssetBundle 构建失败后，不会执行 Player/ISCC 后续步骤。
- 主动制造 Player 构建失败后，不会执行 ISCC。
- 删除 `Releases/Windows/build` 后，仅构建安装包会在 ISCC 前失败。
- 删除 build 根目录下主 EXE 后，仅构建安装包会在 ISCC 前失败。

### 路径规范化

- 规范化只替换平台路径段，所有后续子目录和文件名保持不变。
- 相对路径、绝对路径均可处理。
- 无法可靠识别的路径不会被静默重写。

### Inno Setup

- Windows64 安装包在不兼容架构上被拒绝。
- 旧卸载器返回失败时，新安装中止，且不执行 `DelTree({app})`。
- 旧卸载注册表项 60 秒后仍存在时，新安装中止。
- 保留模板中的分卷设置并继续生成 `.exe + .bin`。

### ISCC 与 Unity 编辑器状态

- ISCC 成功时 Unity Console 能看到完整输出。
- ISCC 语法错误时 Unity Console/异常能看到 stdout、stderr 和 ExitCode。
- ISCC 失败或超时后，Unity 进度条必定关闭，不需要强退编辑器。

## 预计涉及文件

- `Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs`
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`
- `Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs`（仅当平台路径映射需要抽取公共方法）
- `Assets/TEngine/Editor/ReleaseTools/BuildPipelineSetting.cs`（预计保留字段，可能无需结构修改）
- `Releases/Windows/setup.iss`
- `.gitignore`
- fork 构建相关文档与 `Books/Fork/CHANGELOG.md`

## 当前状态

- 仅完成方案确认和计划记录。
- 尚未修改任何构建功能代码或 Inno Setup 模板。
- 工作区已有的 Obfuz 与 Unity `UserSettings` 未跟踪文件属于用户现有内容，后续实施不得修改或清理。
