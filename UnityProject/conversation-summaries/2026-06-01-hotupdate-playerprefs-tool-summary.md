# 热更 PlayerPrefs 版本记录清理工具会话总结

日期：2026-06-01

## 背景

频繁测试热更新时，项目会把“上一次成功更新到的资源包版本号”写入 PlayerPrefs。用户在 Windows 注册表中手动删除后，Unity 再次写入时偶发出现注册表不立即显示、重启后才正常的问题，因此需要一个项目内的快速清理方案。

## 定位结果

- 版本记录读取位置：`Assets/GameScripts/Procedure/ProcedureInitResources.cs`
  - `GetLocalPackageVersion` 通过 `Utility.PlayerPrefs.GetString(versionKey, string.Empty)` 读取本地版本。
- 版本记录写入位置：`Assets/GameScripts/Procedure/ProcedureDownloadOver.cs`
  - 下载完成后通过 `Utility.PlayerPrefs.SetString(GetVersionPlayerPrefsKey(runtimePackage), packageVersion)` 写入版本。
- VersionKey 来源：`Assets/TEngine/Settings/UpdateSetting.asset`
  - `DefaultPackage` 当前为 `GAME_VERSION`
  - `CodePackage` 当前为 `CODE_VERSION`
- 项目 Windows Editor PlayerPrefs 注册表路径来源：
  - CompanyName：`DefaultCompany`
  - ProductName：`hotUnity`
  - 路径：`HKEY_CURRENT_USER\Software\Unity\UnityEditor\DefaultCompany\hotUnity`

## 已完成改动

### 第一次提交

提交：`2c1fca99 添加热更版本记录清理菜单`

新增了 `Assets/TEngine/Editor/Utility/HotUpdatePlayerPrefsTool.cs` 和 `.meta`，用于通过 Unity Editor 菜单清理热更版本 PlayerPrefs key。

### 本次待提交优化

将 `HotUpdatePlayerPrefsTool` 优化为 `EditorWindow`，只保留一个菜单入口：

```text
TEngine/HotUpdate/Package Version PlayerPrefs
```

窗口功能：

- 自动读取 `UpdateSetting.RuntimePackages` 中每个包的 `VersionKey`。
- 展示包名、启用状态、`SaveVersion`、`VersionKey`、当前 PlayerPrefs 值。
- 支持：刷新、选中有记录、全选、取消选择、清理选中、清理全部、单行清理。
- 删除之前的快捷菜单入口：
  - `TEngine/HotUpdate/Clear Saved Package Versions`
  - `TEngine/HotUpdate/Clear GAME_VERSION`
  - `TEngine/HotUpdate/Clear CODE_VERSION`

## 安全约束

- 不调用 `PlayerPrefs.DeleteAll()`。
- 只对窗口展示的 `VersionKey` 执行 `Utility.PlayerPrefs.DeleteKey(key)`。
- 清理后立即调用 `Utility.PlayerPrefs.Save()`。
- 不直接操作 Windows 注册表，避免 Unity PlayerPrefs 缓存/哈希键表现造成误判。
- 不清理语言、音量等其它 PlayerPrefs 数据。

## 验证情况

已做静态检查：

- 只剩 1 个 `MenuItem`。
- 仅保留窗口菜单入口。
- 无 `DeleteAll` 字样。
- 清理逻辑为 `DeleteKey(key)` + `Save()`。
- `git diff --check` 未报告空白错误。

尚未在 Unity 内实际编译和点击窗口验证。下次打开 Unity 后建议检查：

1. 菜单 `TEngine/HotUpdate/Package Version PlayerPrefs` 是否出现。
2. 窗口是否正确列出 `UpdateSetting.RuntimePackages` 中的包。
3. 清理单个 key 后，对应 PlayerPrefs 当前值是否变为“未记录”。
4. Play 热更流程是否按首次版本记录重新检查/下载。
