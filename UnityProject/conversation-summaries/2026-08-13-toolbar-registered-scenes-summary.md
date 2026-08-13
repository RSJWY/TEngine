# 工具栏 Scene Switcher 新增「注册场景」分组

> 日期: 2026-08-13
> 关键词: Toolbar、Scene Switcher、SceneEnumConfig、注册场景、GenericMenu、MainToolbarDropdown、UnityToolbarExtenderRight、菜单分组

---

## 背景

TEngine 工具栏扩展了场景快速切换下拉菜单（Scene Switcher），原有三组场景来源均为 `AssetDatabase.FindAssets("t:Scene")` 按目录扫描（初始化场景 `Assets/Scenes`、默认场景 `Assets/AssetRaw/Scenes`、其他场景），与 `SceneEnumConfig` 配置资产完全脱节。用户希望将 `SceneEnumConfig` 中已注册的场景也纳入工具栏菜单，便于从配置表视角快速跳转。

## 改动内容

### 1. 新建 SceneEnumConfigSceneSource.cs

- 路径：`Assets/Editor/ToolbarExtender/SceneEnumConfigSceneSource.cs`
- 命名空间：`TEngine`，`internal static class`
- 方法：`GetConfiguredScenes()` 返回 `List<(string sceneName, string scenePath)>`
  - 加载 `Assets/Resources/SceneEnumConfig.asset`（硬编码路径常量）
  - 遍历 `config.Scenes`，过滤 `Active == true && SceneAsset != null`
  - `sceneName`：`EnumName`（若为空回退文件名）；`DisplayName` 非空时显示 `EnumName (DisplayName)`
  - `scenePath`：`AssetDatabase.GetAssetPath(entry.SceneAsset)`
  - 配置资产缺失返回空列表（优雅降级，不报错）

### 2. 新版 MainToolbarExtender.cs（Unity 6.0.3+）

- `MainToolbarDropdownSceneSelector` 类新增 `m_configScenes` 字段
- `UpdateScenes()` 读取：`m_configScenes = SceneEnumConfigSceneSource.GetConfiguredScenes();`
- `ShowDropdownMenu()` 菜单顺序：**初始化场景 -> 注册场景 -> 默认场景 -> 其他场景**

### 3. 旧版 SceneSwitcher.cs（Unity 2021 及以下）

- `UnityToolbarExtenderRight` partial 类新增 `m_ConfigScenes` 字段
- `UpdateScenes()` 读取配置场景
- `OnToolbarGUI_SceneSwitch()` 早退判断加 `m_ConfigScenes.Count == 0`
- 菜单顺序同新版

## 设计决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| 分组名 | 「注册场景」 | 区别于目录扫描的「默认场景」，强调来自 SceneEnumConfig 配置注册 |
| 菜单位置 | 第二位（初始化之后） | 用户指定，注册场景比默认扫描的优先级高 |
| 去重 | 不去重 | 配置场景与默认场景可能重叠，但语义不同（注册 vs 扫描），允许重复出现 |
| 显示名 | `EnumName (DisplayName)` | 枚举名为主，中文备注辅助 |
| 配置缺失 | 返回空列表 | 优雅降级，不影响工具栏其他分组 |
| 工具类位置 | `ToolbarExtender/` 目录 | 与工具栏扩展代码同目录，无 asmdef 程序集障碍 |

## 关键文件索引

| 文件 | 操作 | 说明 |
|------|------|------|
| `Assets/Editor/ToolbarExtender/SceneEnumConfigSceneSource.cs` | 新建 | 读取 SceneEnumConfig 的工具类 |
| `Assets/Editor/ToolbarExtender/Unity6000_OR_New/MainToolbarExtender.cs` | 修改 | 新版工具栏加「注册场景」分组 |
| `Assets/Editor/ToolbarExtender/UnityToolbarExtenderRight/SceneSwitcher.cs` | 修改 | 旧版工具栏加「注册场景」分组 |
| `Assets/Editor/SceneTools/SceneEnumGenerator/SceneEnumConfig.cs` | 未改（数据源） | `SceneEntry` 含 `EnumName`/`DisplayName`/`Active`/`SceneAsset` 字段 |

## 菜单结构

```
[Scene Switcher ▼]
├─ 初始化场景/          (扫描 Assets/Scenes)
├─ 注册场景/            (读 SceneEnumConfig)
│   ├─ MainScene (主UI常驻空场景)
│   └─ ...
├─ 默认场景/            (扫描 Assets/AssetRaw/Scenes)
├─ 其他场景/            (全项目减去初始化+默认)
```
