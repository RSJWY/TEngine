# 2026-08-07 场景加载阶段1超时双门槛修复会话总结

## 背景

GitHub Issue #1 报告：LoadingUI 阶段1 固定 5s 超时，大场景（约 90MB+）打包后首次冷启动常超 5s，真实进度未到 0.9 即被强制收尾，可能资源未就绪就 UnSuspend。

实际代码中 LoadingUI 已在 2026-06-30 重构为 `GameSceneModule`（进度状态机）+ `SwitchUI`（纯展示），5s 固定超时位于 `GameSceneModule.Update` 阶段1，日志前缀 `[GameScene]`。

## 本次改动

### `GameSceneModule.cs`

- 删除固定 `5.0f` 绝对超时。
- 新增常量 `Phase1StallTimeout = 60f`（停滞超时）、`Phase1AbsoluteTimeout = 180f`（绝对超时）。
- 新增字段 `_lastLoadProgress`、`_phase1StallElapsed`，在 `StartSceneLoad` 重置块一并置 0。
- `OnLoadProgress`：`value > _lastLoadProgress` 时重置 `_phase1StallElapsed`，然后记录 `_lastLoadProgress`（先比较再赋值）。
- `Update` 阶段1：停滞累计改为独立 `if`（`_lastLoadProgress > 0f && !_sceneLoadComplete` 守卫），在 if/else if 链之前；超时判断在链尾 `else if`，条件 `(_lastLoadProgress > 0f && _phase1StallElapsed >= Phase1StallTimeout) || _phase1ElapsedTime >= Phase1AbsoluteTimeout`。
- 超时日志补充 `scene/elapsed/stall/rawProgress/display/complete`。

### 修正 issue 方案伪代码 bug

issue 给的伪代码 `if` 累加停滞 / `else if` 判断超时 互斥，会导致停滞超时分支永不触发（只剩绝对超时 180s 兜底）。实际实现将累计与判断分离：累计是独立 `if`，判断在链尾 `else if`。

### 健壮性优化

- 进度提升判定用严格大于 `value > _lastLoadProgress`，不用 `+0.001` 阈值（YooAsset progress 基于字节数单调递增，慢速爬升算健康）。
- 停滞超时取 60s（比 issue 建议的 30s 更稳，给大 bundle 串行解压留时间）；绝对超时 180s 兜底卡死。

## 保留不变

- `suspendLoad=true`、0.9 激活、阶段2收尾动画、陷阱2（phase>=2 拒更新 target）。
- 阶段1正常收尾条件 `_sceneLoadComplete && _displayProgress >= 0.89f`（显示进度平滑追赶等待）。
- `_skipMode` 快速跳过分支。

## 关键设计点

- **停滞累计独立 if**：不能与超时判断放成 if/else if 互斥，否则停滞超时永不触发。
- **冷启动 progress=0 守卫**：`_lastLoadProgress > 0f` 确保解压期 progress 长期为 0 时不累计停滞，避免误杀。
- **三层判定**：慢速爬升（progress 在动）→ 永不算停滞；完全停滞 → 60s 超时；彻底卡死 → 180s 绝对兜底。

## 提交与关联

- commit：`a74ef1d8`（推送到 main）
- Issue #1：已评论改动说明并关闭（reason: completed）
- commit message 含 `(#1)` 引用，GitHub timeline 自动关联

## 文档更新

- `Books/Fork/scene-system.md`：追加"阶段 1 超时双门槛化"专题条目。
- `Books/Fork/CHANGELOG.md`：追加 2026-08-07 记录。
- `Books/Fork/README.md`：最近重点场景系统条目补充超时修复。
- 根 `README.md` 与 `Books/Fork-定制改动说明.md` 未改（非新主题，仅索引）。

## 验证状态

- 未在 Unity 编辑器中编译/运行验证（环境限制）。
- 静态核对：字段/常量/重置点/OnLoadProgress/Update 阶段1逻辑均已在文件中确认。
- 待用户实测：打包后首次进大场景不再 5s 误报、进度跟随到约 90%、二次进入正常、真卡死日志含完整字段、小场景不受影响。

## 后续可选

- `Phase1StallTimeout` / `Phase1AbsoluteTimeout` 改为可配置属性以支持按场景体积调参。
- 大场景预下载 / 分包。
