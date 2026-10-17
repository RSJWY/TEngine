# 构建产物中不可发布目录（BackUpThisFolder / DoNotShip）处置研究

- 日期：2026-10-17
- 范围：`Assets/TEngine/Editor/ReleaseTools/`、`Releases/Windows/`、Unity 2022.3.62f3 IL2CPP 增量构建行为、Inno Setup Excludes
- 状态：研究完成，方案待用户确认后实施

## 问题背景

构建 Player 输出到 `Releases/Windows/build/` 后，目录下出现 1.1 GB 的
`hotUnitydemo_BackUpThisFolder_ButDontShipItWithYourGame`。安装包脚本按
`Source: "build\*"` 递归打包整个 `build/`，该目录会被原样打进安装包。

## 关键事实

### 1. `_BackUpThisFolder_ButDontShipItWithYourGame` 不是“旧构建备份”，是 IL2CPP 增量编译缓存

- 内容：`il2cppOutput/`、`Managed/`、`GameAssembly.pdb`（本项目该 pdb 496 MB）。
- 证据：目录内文件时间戳与最近几次构建一致（GameAssembly.pdb = 2026-10-16 21:24，与
  build 根目录 GameAssembly.dll 同刻）；若是旧 Data 备份应含 globalgamemanagers 等运行时文件。
- Unity 官方语义：IL2CPP 增量构建管线把生成的 C++ 中间产物缓存在此目录；保留时下次构建只
  重编变更文件，删除/搬走则冷启动全量重编（社区实测冷 IL2CPP 可达 30+ 分钟，热缓存可压到
  5 分钟级）。CI 方案（game-ci）专门缓存此目录以提速。
- 结论：**删除或“备份到别处”都会让每次出包付出全量重编 IL2CPP 的代价；搬走 = 删除**（缓存
  必须留在 Unity 期望的原路径才能复用）。正确做法是**原地保留 + 打包时排除**。

### 2. `_BurstDebugInformation_DoNotShip` 才是纯调试垃圾

- Burst 开启调试信息时生成（开发构建常见），内容为反汇编/转储，无缓存价值，可直接删除或排除。
- 当前 `Releases/Windows/build/` 下未出现（release 构建），但清理逻辑应覆盖。

### 3. 本项目相关代码链路

- Player 构建入口：`ReleaseTools.BuildImp`（`Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:829`），
  `BuildPipeline.BuildPlayer` 输出 `config.PlayerOutputPath`（即 `Releases/Windows/build/xxx.exe`）。
- 安装包：`InnoSetupBuilder`（`Assets/TEngine/Editor/ReleaseTools/InnoSetupBuilder.cs`），
  `PlayerBuildDir = Releases/Windows/build`；`ValidatePlayerBuild` 仅校验目录非空 + exe 存在。
- 打包脚本：`Releases/Windows/setup.iss`（用户模板，构建流程只回写 `#define`，不回写结构）→
  复制为 `setup.generated.iss`；打包行为由第 66 行决定：
  `Source: "build\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs`
- 命令行入口 `BuildCLI`（Python）最终也走同一 Editor 管线。

## 方案对比

| 方案 | 出包速度 | 磁盘 | 安装包体积 | 评价 |
| --- | --- | --- | --- | --- |
| 构建后删除备份目录 | 每次全量重编 IL2CPP，显著变慢 | 省 1.1 GB | 干净 | 次优 |
| 构建后搬到外部备份区 | 等价于删除（下次冷启动） | 外部持续膨胀 | 干净 | 最差，两头不占 |
| 原地保留 + iss `Excludes` 排除 | 增量最快 | 不变 | 干净 | **推荐** |

## 推荐实施（待用户确认）

1. `setup.iss` 第 66 行追加参数：
   `Excludes: "*_BackUpThisFolder_ButDontShipItWithYourGame,*_BurstDebugInformation_DoNotShip"`
   （模式不含路径分隔符时对任意深度同名目录生效，排除目录即跳过其全部内容；通配前缀兼容
   改产品名后的新目录。）注意：改的是用户模板 `setup.iss`；已存在的 `setup.generated.iss`
   需删除或用「重新生成」刷新，否则沿用旧副本。
2. `InnoSetupBuilder.ValidatePlayerBuild` 增加兜底：发现 `*_BackUpThisFolder*` /
   `*_BurstDebugInformation_DoNotShip` 目录且 iss 无对应 Excludes 时 LogError（阻止把 1 GB
   垃圾打进安装包）。
3. 可选：构建前清理“孤儿”备份目录——前缀与当前 productName 不符的旧缓存无法被复用，可安全删。
4. 若用户坚持删除策略：在 `BuildImp` 构建成功后按上述两个模式删除，但需接受出包变慢。

## 参考来源

- Unity Discussions: Self Compiling il2cpp output — https://discussions.unity.com/t/self-compiling-il2cpp-output/704998
- Unity Manual: Incremental Build Pipeline — https://docs.unity3d.com/6000.1/Documentation/Manual/incremental-build-pipeline.html
- game-ci/unity-builder（CI 缓存该目录提速增量 IL2CPP）— issues #612、#716
- Inno Setup [Files] Excludes 文档 — https://jrsoftware.org/ishelp/topic_filessection.htm
- Stack Overflow: Inno Setup exclude a directory and its files — https://stackoverflow.com/questions/10645269/inno-setup-exclude-a-directory-and-its-files-also
