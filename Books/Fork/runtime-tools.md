# 运行时工具

本页记录 fork 中迁移自 DGame 并适配到 TEngine 的运行时工具类。这些工具位于独立程序集 `RuntimeTools`，供热更和主程序代码按需使用。

## GameTickWatcher 游戏逻辑计时器

### 背景

在性能分析、调试以及游戏循环耗时统计时，需要一个轻量、高精度且无侵入的计时工具。DGame 项目在热更层内置了 `GameTickWatcher`，基于 `System.Diagnostics.Stopwatch` 提供秒级（float）耗时测量。本 fork 将其迁移过来，脱离热更程序集放入独立工具程序集，使其可被主程序与热更代码共同使用。

### 改动摘要

- 新增 `RuntimeTools` 程序集（`Assets/GameScripts/RuntimeTools/RuntimeTools.asmdef`），引用 TEngine，`autoReferenced` 开启，全平台可用。
- 迁移 `GameTickWatcher` 到 `RuntimeTools` 命名空间。
- 将原 DGame 的 `DGame` 命名空间引用改为 `TEngine`，日志由 `DLogger.Info` 改为 `Log.Info`。
- 补全完整的 XML 文档注释，说明构造即启动、`Restart`、`ElapseTime`、`LogUsedTime`、`ToString` 的语义和典型场景。
- 行为与 DGame 原版保持一致：构造即启动计时、`Restart` 清零重启、`ElapseTime` 返回秒（float）。

### 使用方式

```csharp
// 构造即开始计时
var watcher = new GameTickWatcher();

// ... 待测逻辑 ...

// 获取秒级耗时
float used = watcher.ElapseTime();

// 直接打日志：输出 "Used Time: X"
watcher.LogUsedTime();

// 在循环中每帧重置测量
watcher.Restart();
```

### 适用场景

- 测量某段代码或游戏循环单帧逻辑的执行耗时。
- 统计两次调用之间的时间间隔。
- 调试、性能分析时快速输出耗时，无需手动拼字符串。

### 注意事项

- 基于 `System.Diagnostics.Stopwatch`，精度通常为微秒级或更高，但 `ElapseTime` 返回 `float`（秒），在游戏场景下精度已足够。
- 该类位于 `RuntimeTools` 程序集，非热更代码；热更代码可引用该程序集使用。
- `LogUsedTime` 直接调用 `Log.Info`，生产环境请按需评估日志量。
- 不提供暂停/继续能力，如需分段计时请使用多个实例或调用 `Restart`。

### 关键文件

- `Assets/GameScripts/RuntimeTools/RuntimeTools.asmdef`
- `Assets/GameScripts/RuntimeTools/GameTickWatche/GameTickWatcher.cs`

### 相关记录

- 迁移自 [DGame](https://github.com/AmaniDawn/DGame) `Assets/Scripts/HotFix/GameLogic/GameTickWatcher/GameTickWatcher.cs`。
