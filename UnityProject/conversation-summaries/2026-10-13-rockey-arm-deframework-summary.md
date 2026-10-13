# RockeyARM 模块去框架化改造（Assets/RockeyARM 包化 + TEngine 消费侧适配）

日期：2026-10-13
分支：feat/rockey-arm（仅推送 gitea：gitea.rsjwy.com/rsjwy/TEngine.git）

## 背景与目标

`Assets/RockeyARM` 计划整体导出为 Unity 包，要求：核心 `RockeyArmManager` 零框架依赖（同时支持 TEngine 与裸使用）；`ExitErrorUI.prefab` 脱离 TEngine UI 体系（参考 `Assets/Launcher`）；`UsageSamples` 只保留调用示例；`RockeyArmManager.cs` 位置不动。

## 最终结构

```
Assets/RockeyARM/
├── RockeyARM.asmdef          核心程序集：仅引用 UniTask + Unity.TextMeshPro，namespace RockeyArm
├── RockeyArmManager.cs       核心主逻辑（位置未动）
├── RockeyArmManager.Consts.cs / .Platform.cs
├── IRockeyArm.cs             核心接口 + LicenseErrorInfo/RockeyErrorKind（原 IRockeyArmModule.cs 改名重写）
├── UI/                       RockeyUIBase / RockeyUIMgr（仿 Launcher 纯 C# UI + Resources.Load）/ ExitErrorUI.cs
├── Resources/UIWindow/ExitErrorUI.prefab   已移除 UIBindComponent，根自带 Canvas 可独立显示
├── TEngine/                  适配层：RockeyARM.TEngine.asmdef（新 GUID 7ff8dfe7…）+ RockeyArmModule + IRockeyArmModule
└── UsageSamples/             BareSample（直调核心）/ TEngineSample（注册适配层），均为纯调用示例
```

## 核心去框架化的三个注入点

- 生命周期：`OnInit/Shutdown` → `Init()/Release()`（Release 幂等）；
- 日志：`InfoLogger` / `WarningLogger` / `ErrorLogger` 三个 `Action<string>` 委托属性，未注入则静默；
- 错误：`GameEvent.Send` → `event Action<LicenseErrorInfo> LicenseError`；
- `LICENSE_ERROR_EVENT_ID` 与 `SEND_FULL_ERROR_TO_GAME_EVENT` 移至适配层 `RockeyArmModule`；
- `#if !UNITY_EDITOR` 三处短路段按用户要求原样保留（编辑器测试用）。

## TEngine 消费侧适配（本次提交）

- `ProcedureLaunch.cs`：注册 `new RockeyArmModule()`（此前为 `new RockeyArmManager()`）；
- `GameApp.cs`：事件 ID 改 `RockeyArmModule.LICENSE_ERROR_EVENT_ID`；弹窗改 `RockeyUIMgr.ShowUI<ExitErrorUI>`（不再走 `GameModule.UI.ShowUI`）；
- `GameLogic.asmdef`：追加适配程序集引用 `GUID:7ff8dfe72e284133a1de97c68ab4de4b`；
- `GameModule.cs`：仅注释更新；
- repowiki《RockeyARM加密狗授权模块》全文重写 + 《当前Fork定制功能》一行更新。

## HybridCLR / 打包链排查结论

- `HybridCLRSettings.asset` 热更程序集仅 `GameProto`/`GameLogic`，核心与适配层均为 AOT，无需登记；
- AOT 元数据清单以构建期生成的 `AOTMetadataManifest` JSON 为准（`ProcedureLoadAssembly` 运行时加载），`RockeyARM.TEngine.dll` 下次构建自动纳入；
- `UpdateSetting.AOTMetaAssemblies` 回退列表从未包含 RockeyARM，无需改动（该文件仅有用户自己的 GeneratePdb 改动，未提交）。

## 实施过程中修掉的问题

1. 适配 asmdef 被 Unity 导入时追加 `TEngine.Runtime` 名称引用，与 GUID 引用形成重复引用 → 删除名称行；
2. `TEngine.Log.Info/Warning/Error` 带 `[Conditional]` 特性不能直接转委托（CS1618）→ 改 `msg => Log.Info(msg)` lambda 包装；
3. `GameApp.cs` 注释笔误“红点 UI 框架”→“TEngine UI 框架”。

## 验证

- workflow.py 不可用（本机 Python 3.10 < 3.11，且项目为 Unity 2022 该 CLI 无效），全程走 Unity MCP（com.coplaydev.unity-mcp，HTTP 8080/mcp）：
  - `refresh_unity(mode=force, compile=request)` 后 `read_console(types=[error])` → 0 错误；
  - `manage_asset get_info` 确认 prefab 正常识别为 GameObject；
- 未做 PlayMode 运行期验证（真狗校验/Editor 短路行为）。

## 提交记录

- `ccab3b04` refactor(rockey-arm): 模块迁移 + 去框架化 + 适配层 + UI/示例/wiki（48 文件，消费侧 4 文件除外）
- 消费侧适配提交：待推送（用户自行 push gitea）

## 遗留

- 运行期 PlayMode 冒烟（Editor 短路放行路径 + 弹窗显示）未跑；
- 下次真实构建时需确认 `AOTMetadataManifest` 中包含 `RockeyARM.TEngine.dll`；
- origin（github）远端始终未推送。
