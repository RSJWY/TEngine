# DeployConfig 控制 Debugger 调试器开关

日期：2026-06-04

## 需求

GameEntry 场景内的 `Debugger`（运行时调试器）此前只能通过 Inspector 序列化字段 `activeWindow`（`DebuggerActiveWindowType` 枚举）控制激活策略。用户希望打包后能通过配置文件现场激活/关闭，并明确将 `DeployConfig` 作为程序的基础打包后配置载体（方案二：扩展现有 DeployConfig，而非新增独立配置文件）。

## 现状梳理

- `Debugger.cs`（`Assets/TEngine/Runtime/Module/DebugerModule/`）是场景内 MonoBehaviour，`Start()` 中按 `activeWindow` 枚举决定是否激活：
  - `AlwaysOpen` → 总是开
  - `OnlyOpenWhenDevelopment` → `Debug.isDebugBuild`
  - `OnlyOpenInEditor` → `Application.isEditor`
  - `AlwaysClose` → 总是关
- 配置加载走 `JsonConfigModule`（`StreamingAssets/Configs/` + `config_manifest.json` 清单 + `IJsonConfigModule.TryGet<T>`），JSON 反序列化用 `Utility.Json.ToObject<T>`（底层 `JsonUtility.FromJson`）。
- `DeployConfig` 由 `ProcedureLaunch.LoadDeployConfigAsync` 加载，`UpdateSetting.GetDeployOverride()` 消费其 `ResDownloadPath` / `FallbackResDownloadPath`。

## 关键时序问题

`Debugger.Start()` 在第一帧执行，早于 `ProcedureLaunch.OnEnter` 触发的 `JsonConfigModule.LoadAllAsync()` 完成。因此**不能在 Start 内直接读配置**。

解决：保留 Start 按 Inspector 初始化，DeployConfig 加载完成后由 `ProcedureLaunch` 二次覆盖。与 `UpdateSetting` 消费 `ResDownloadPath` 的时机一致。

## 改动清单

1. **`DeployConfig.cs`** — 新增 `public string DebuggerActiveWindow;`。
   - 用 string 而非枚举：避免 `JsonUtility` 对缺失枚举字段填默认值 0（=AlwaysOpen）的坑，与现有 `ResDownloadPath` 的「空值回退」语义一致。

2. **`Debugger.cs`** — 将 `Start()` 内的激活策略 switch 抽成公共方法 `ApplyActiveWindowType(DebuggerActiveWindowType type)`，同时回写 `activeWindow` 字段；Start 改为调用 `ApplyActiveWindowType(activeWindow)`。

3. **`ProcedureLaunch.cs`** — `LoadDeployConfigAsync` 在 `LoadAllAsync()` 后调用新增的 `ApplyDebuggerConfig()`：
   - 取 `Debugger.Instance`（为 null 直接返回）。
   - `IJsonConfigModule.TryGet<DeployConfig>` 读配置，字段为空白则返回。
   - `Enum.TryParse<DebuggerActiveWindowType>(..., ignoreCase: true, ...)` 解析，成功则 `Debugger.Instance.ApplyActiveWindowType(type)` 并 Log.Info；失败 Log.Warning 并保留 Inspector 配置。

4. **`DeployConfig.json`** — 示例追加 `"DebuggerActiveWindow": "OnlyOpenWhenDevelopment"`。

5. **文档** — 根 `README.md`「本 Fork 的定制改动 → 运行时配置」追加一条；`Books/Fork-定制改动说明.md` 在 DeployConfig 小节后新增「3. 部署配置控制调试器开关」。

`config_manifest.json` 已声明 `DeployConfig.json`，无需改动。

## 取值与回退

- 取值（大小写不敏感）：`AlwaysOpen` / `OnlyOpenWhenDevelopment` / `OnlyOpenInEditor` / `AlwaysClose`。彻底关闭填 `AlwaysClose`。
- 回退：字段留空、拼错、或场景无 Debugger → 保留 Inspector 原行为。

## 待验证

改动位于主工程层（TEngine.Runtime + Procedure 主包），环境无 Unity 编译/MCP 工具，需在 Unity 编辑器内确认编译无报错。类型可达性已人工核对：`ProcedureLaunch` 已 using `System` 与 `TEngine`，`Enum.TryParse` / `Debugger` / `DebuggerActiveWindowType` / `DeployConfig` 均可达。
