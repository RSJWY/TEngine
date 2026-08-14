# 场景枚举自动生成 - 调研记录

> 对应 issue: RSJWY/TEngine#2「添加自动生成场景枚举的功能」
> 调研日期: 2026-08-07
> 关键词: SceneType、SceneConstName、GameSceneModule、YooAsset Scenes Group、AssetBundleCollectorSetting、代码生成、枚举顺序稳定性

---

## 一、Issue 需求

> 目前配置场景列表复杂，请求支持自动生成枚举的功能

核心诉求：用 Editor 工具扫描场景资源，自动生成场景枚举与映射，免去手工同步多处的负担。

---

## 二、当前场景配置全貌（痛点定位）

### 2.1 场景资源体系
- **资源目录**：`Assets/AssetRaw/Scenes/`（当前仅 `MainScene.unity`）
- **YooAsset 收集器**：`Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset`
  - `DefaultPackage` 下有 `Scenes` Group，收集 `Assets/AssetRaw/Scenes`
  - 地址规则 `AddressByFileName`（场景资源地址 = 文件名去 `.unity`）
  - 打包规则 `PackDirectory`，过滤 `CollectAll`
- 即：**场景资源地址（location）= 场景文件名**

### 2.2 手工维护的 4 处（痛点根源）
位于 `Assets/GameScripts/HotFix/GameLogic/Module/GameScene/GameSceneModule.cs`，每新增一个场景需同步修改：

1. **`SceneType` 枚举**（L15-21）—— 手动加枚举值
   - 注释要求「往后追加，不要插入，以免顺序错误」
2. **`SceneConstName` 常量类**（L30-34）—— 手动加字符串常量
3. **`GetSceneName` 的 switch**（L143-153）—— 枚举 -> 资源地址
4. **`GetSceneTypeFromName` 的 if**（L165-183）—— 资源地址/枚举名 -> 枚举（反向查询）

注释明确：「更新后前往 RecordScene 和 GetSceneName 做正反向查询更新」。

### 2.3 SceneType 使用范围（影响自动化方案设计）
| 使用点 | 文件 | 用途 |
|--------|------|------|
| `GameValueStatic.CurrentSceneType/PreviousSceneType` | Static/GameValueStatic.cs | 全局运行时状态，**注释明确「不持久化」** |
| `IGameSceneEvent` 事件参数 | IEvent/IGameSceneEvent.cs | OnSceneLoadStart / OnSceneReady / OnDynamicSpawnComplete |
| `SceneGameManagerBase.TargetSceneType` | SceneGameManager/SceneGameManagerBase.cs | 抽象属性，子类按场景实现管理器 |
| `DynamicSceneSpawner` | Scenes/DynamicSpawn/DynamicSceneSpawner.cs | 监听 OnSceneReady |
| 回放文件名解析 | GameSceneModule.cs:177 | `GetSceneTypeFromName` 用**枚举名字符串**解析（Replay_FlyTest_日期.replay） |
| 启动入口 | GameApp.cs:46 | `GameModule.GameScene.LoadScene(SceneType.MainScene)` |

**关键结论**：
- `SceneType` 的**整数值未参与存档/回放序列化**（GameValueStatic 不持久化；回放用枚举名字符串；GameEvent 进程内不跨进程）。
- 「往后追加」约定主要为代码可读性与防御性，**枚举值顺序稳定性要求不严格**，但稳妥起见生成器仍应保持追加语义。

---

## 三、现有可复用能力

### 3.1 编辑器工具
- `Assets/Editor/ToolbarExtender/EditorSceneTransitionUtility.cs`
  - 已有 `AssetDatabase.FindAssets("t:Scene", new[]{ folder })` 遍历场景文件的能力
  - `FindScenePathInFolder(sceneName, folderPath)` 可复用
  - 注意它扫描的是 `Assets/Scenes`（编辑器启动场景），**不是**业务场景目录 `Assets/AssetRaw/Scenes`
- **无现成代码生成工具**（`*CodeGen*.cs` 搜索无结果）

### 3.2 模块注册
- `GameModule.cs:113` `GameScene => Get<IGameSceneModule>()`，走 `ModuleSystem.GetModule<T>()`
- `IGameSceneModule` 接口（Module/GameScene/IGameSceneModule.cs）暴露 `GetSceneName`/`GetSceneTypeFromName`/`LoadScene` 等

---

## 四、自动生成方案设计

### 4.1 数据源选择
- **首选**：从 `AssetBundleCollectorSetting.asset` 读取 Scenes Group 的收集目录（与打包配置一致，改配置能跟上）
- **fallback**：固定扫描 `Assets/AssetRaw/Scenes`（简单，但若改 YooAsset 配置会脱节）
- 读取 YooAsset 配置可解析该 ScriptableObject，或反射调用 YooAsset API

### 4.2 生成内容（自动化原手工 4 处）
| 生成文件 | 替代原手写 |
|---------|-----------|
| `SceneType.g.cs` | `SceneType` 枚举 |
| `SceneConstName.g.cs` | `SceneConstName` 常量类 |
| `SceneTypeMapping.g.cs` | `GetSceneName` switch + `GetSceneTypeFromName` if（改用 Dictionary） |

`GameSceneModule.cs` 的 `GetSceneName`/`GetSceneTypeFromName` 改为转发到 `SceneTypeMapping`，业务逻辑（三段式进度等）不动。

### 4.3 枚举名清洗
场景文件名可能含中文/特殊字符（注释提到「飞行测试」），需清洗为合法 C# 标识符：
- 非法字符 -> `_`
- 数字开头 -> 前缀 `_`
- 重名 -> 加后缀
- 建议约束场景文件名用英文，或提供自定义映射表

### 4.4 顺序稳定性（持久化注册表）
维护一个 `SceneEnumRegistry`（ScriptableObject 或 json），记录 `场景文件名 -> 枚举名`：
- 新增场景追加新枚举值
- 删除场景保留空缺或标 `[Obsolete]`
- 由于不参与持久化序列化，移除也相对安全

### 4.5 触发方式
- 菜单按钮手动触发（如 `TEngine/工具/生成场景枚举`）
- 可选：`AssetPostprocessor` 监听 `.unity` 变化自动触发

### 4.6 生成位置
- 与现有 `SceneType` 同目录：`Assets/GameScripts/HotFix/GameLogic/Module/GameScene/`
- 需把现有 `SceneType`/`SceneConstName` 从 `GameSceneModule.cs` 拆出改为自动生成
- 生成文件头标注「自动生成，请勿手改」

---

## 五、风险与红线

1. **拆分现有代码**：`SceneType`/`SceneConstName` 当前内嵌在 `GameSceneModule.cs`，拆出独立文件会改动现有文件，需保证 `MainScene` 值不变、引用不破。
2. **Editor 引用热更红线**（CLAUDE.md 红线6）：生成器在 Editor 程序集，生成的代码在 HotFix。生成器**不被热更代码引用**，只运行时用；生成的代码是纯数据/映射，不引用 Editor 程序集，合规。
3. **中文场景名**：枚举名清洗规则需与用户约定，避免生成非法标识符。
4. **从 YooAsset 配置读目录**：需解析 `AssetBundleCollectorSetting.asset` 或反射 YooAsset API，注意 YooAsset 版本（当前 2.3.19）API 兼容。
5. **向后兼容**：`MainScene` 枚举值与字符串必须保持现状，避免破坏 `GameApp.cs:46` 启动流程与回放文件名解析。

---

## 六、待与用户确认的决策点

1. **数据源**：从 YooAsset 配置读目录，还是固定扫描 `Assets/AssetRaw/Scenes`？
2. **生成粒度**：只生成枚举+常量（映射仍手写 switch），还是连映射 Dictionary 一起生成？
3. **枚举名规则**：要求场景文件名必须英文合法标识符，还是提供清洗+自定义映射表？
4. **顺序注册表**：是否引入 `SceneEnumRegistry` ScriptableObject 维护顺序稳定性？
5. **触发方式**：仅菜单手动，还是加 AssetPostprocessor 自动？
6. **现有代码拆分**：是否同意把 `SceneType`/`SceneConstName` 从 `GameSceneModule.cs` 拆出为自动生成文件？
