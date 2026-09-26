# TEngine BuildCLI（Python GUI 构建工具）

与 Unity 内「TEngine 打包工具」窗口构建部分对齐的命令行/GUI 构建方案，位于项目根 `BuildCLI/`。

## 启动

```powershell
# Windows
UnityProject\BuildCLI\build_gui.bat
```

```bash
# macOS / Linux
./BuildCLI/build_gui.sh
```

或手动方式：

```powershell
cd BuildCLI
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt       # 仅 PySide6
python -m tengine_build               # GUI
```

命令行模式（无 GUI，适合 CI）：

```powershell
python -m tengine_build --no-gui run --action buildAb --target StandaloneWindows64
python -m tengine_build --no-gui run --action hotfixDll --target Android
python -m tengine_build --no-gui run --preset my-preset
# 多动作按顺序执行（自动分段合并，减少 Unity 冷启动）：
python -m tengine_build --no-gui run --action generateAll --action hotfixDll --action build
# 批量任务（动作队列 + 绑定预设）：
python -m tengine_build --no-gui run --batch first-package
python -m tengine_build --no-gui batch --list
```

## 功能（与 Unity 打包工具窗口对齐）

| GUI 区域 | 对应 Unity 侧 |
|---|---|
| 目标平台 / 构建管线 / 压缩方式 | `BuildConfig.BuildTarget / BuildPipeline / CompressOption` |
| 版本号模式（统一 / 独立） | `PackageVersionMode`（Unified / PerPackage），PerPackage 表格可「从上次构建读取」 |
| AB 输出目录 / 发布目录 / Player 输出 | `OutputRoot / PublishRoot / PlayerOutputPath` |
| 热更 DLL（编译拷贝 / GenerateAll / AOT 清单 / AOT DLL） | `BuildDLLCommand.*` |
| 构建动作（AB / AB+Player / Player / 发布整理 / 切换平台） | `ReleaseTools.BuildWithConfig / PublishFromExistingBuild / BuildImp / ActivateBuildTarget` |
| 高级（共享打包 / 路径校验 / 依赖DB / 清缓存 / 校验 / 内置拷贝 / 文件名风格 / Catalog） | `BuildConfig` 高级字段 |

## 架构

```
BuildCLI/
├── tengine_build/            # Python 包
│   ├── app.py                # PySide6 GUI（含「批量执行」标签页）
│   ├── cli.py                # --no-gui 命令行（支持 --batch / 可重复 --action）
│   ├── unity_runner.py       # Unity batchmode 进程管理 + BatchRun 分段合并执行
│   ├── dto.py                # 表单 → CLIBridge JSON（支持 actions[] 多动作）
│   ├── config_store.py       # 预设/批量任务 JSON + Unity .asset 只读解析
│   └── unity_locator.py      # Unity.exe 定位（Hub / 注册表 / 盘符 / 版本匹配）
├── presets/                  # 用户预设（_last_session.json 为上次会话，已 git 忽略）
├── batches/                  # 批量任务（有序动作队列 + 绑定预设，随仓库提交）
├── logs/                     # 每次构建一个时间戳目录（unity.log + build_request.json，已 git 忽略）
├── build_gui.bat / .sh       # 一键启动
└── requirements.txt          # 仅 PySide6
```

Unity 侧仅新增一个入口文件 `Assets/TEngine/Editor/ReleaseTools/CLIBridge.cs`：
`Unity.exe -batchmode -quit -executeMethod TEngine.CLIBridge.Run -tengineConfig=<json>`，
JSON 字段与 GUI 表单一一对应，构建逻辑完全复用 `ReleaseTools.BuildWithConfig` 等既有实现，
退出码 0/1 回传 Python 判定结果。

## 数据流与安全边界

- GUI 表单 → `logs/<时间戳>/build_request.json` → Unity `-tengineConfig` → `CLIBridge` → `BuildConfig` → `ReleaseTools`
- 对 `BuildPipelineSetting.asset` / `UpdateSetting.asset` **只读**（“从 Unity 窗口配置读取”按钮），
  Python 侧配置保存在 `presets/*.json`，不会写 Unity 资产。
- Player 构建需要 GPU，batchmode 不加 `-nographics`。
- 每次构建的完整 Unity 日志与请求 JSON 落盘 `logs/`，可导出比对。

## 版本号模式

- **Unified**：单一版本号输入框，留空自动生成（格式 `yyyy-MM-dd-分钟数`，与 Unity 侧一致）。
- **PerPackage**：按 `UpdateSetting.asset` 启用包列表逐包填写；「从上次构建读取」扫描
  `Releases/Bundles/<平台>/<包名>/` 下最新版本目录（与 `ReleaseTools.GetPackageVersionDirectories` 同源）；
  留空的包由 Unity 侧 `ResolvePackageVersion` 兜底（map → 目录 → 自动生成）。

## 批量执行（多步骤队列）

预设保持纯表单快照不动；新增独立的**批量任务**（`batches/*.json`）记录有序动作队列 + 绑定预设（可选）：

```json
{
  "name": "first-package",
  "preset": "win-std",
  "steps": ["generateAll", "hotfixDll", "build", "publish"],
  "stopOnFailure": true
}
```

**分段合并执行**：`generateAll` / `switchPlatform` 会触发脚本重编译 + 域重载，中断 `-executeMethod` 执行流，故各自**独立成一个 Unity 进程**；其余动作合并到**同一进程内顺序执行**（CLIBridge 收到 `actions[]` 后循环调用 `Execute`，全部完成才 Exit）。

- 首包 `generateAll → hotfixDll → build → publish`：4 次冷启动 → **2 次**（`[generateAll] | [hotfixDll→build→publish]`）
- 日常热更 `hotfixDll → buildAb → publish`：**1 次**

GUI 在「批量执行」标签页管理任务（新建/编辑/删除/运行），运行时横幅显示 `第 2/4 步：热更DLL（段 2/2）`，失败停在出错步骤。CLI 用 `--batch 名称` 或可重复 `--action` 传入队列。

## 已知限制

- 安装包（InnoSetup）构建暂未纳入 CLI（Unity 窗口已有完整实现），后续按需加 action。
- Web 构建请自行确认平台模块安装；切平台首次会触发较长时间的平台库导入。
