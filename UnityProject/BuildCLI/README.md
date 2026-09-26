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
│   ├── app.py                # PySide6 GUI
│   ├── cli.py                # --no-gui 命令行模式
│   ├── unity_runner.py       # Unity batchmode 进程管理 + 日志 tail + 结果判定
│   ├── dto.py                # 表单 → CLIBridge JSON → 命令行参数
│   ├── config_store.py       # 预设 JSON + Unity .asset 只读解析
│   └── unity_locator.py      # Unity.exe 定位（Hub / 注册表 / 盘符 / 版本匹配）
├── presets/                  # 用户预设（_last_session.json 为上次会话，已 git 忽略）
├── logs/                     # 每次构建一个时间戳目录（unity.log + build_request.json，已 git 忽略）
├── build_gui.bat / .sh       # 一键启动
└── requirements.txt          # 仅 PySide6

注：仓库根 BuildCLI/ 下另有旧版 build_android 等脚本，属原仓库历史遗留，与本工具无关。
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

## 已知限制

- 安装包（InnoSetup）构建暂未纳入 CLI（Unity 窗口已有完整实现），后续按需加 action。
- Web 构建请自行确认平台模块安装；切平台首次会触发较长时间的平台库导入。
