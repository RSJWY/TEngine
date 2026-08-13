# issue #3 (pdb + dev/release 模式) 代码库研究报告

**研究日期**：2026-08-13  
**研究范围**：HybridCLR 热更 Assembly 加载、YooAsset 打包流程、exe 构建流程、模式标记与匹配机制

---

## 核心发现

### 1. YooAsset 原生支持每包元数据：PackageNote

**位置**：`Library/PackageCache/com.tuyoogame.yooasset@2.3.19/Runtime/ResourcePackage/PackageManifest.cs:72`

```csharp
public string PackageNote;
```

**全链路已打通**：
- 打包写入：`Editor/AssetBundleBuilder/BuildParameters.cs:52` → `TaskCreateManifest.cs:45` 写入清单
- 序列化：`Runtime/ResourcePackage/ManifestTools.cs:71` 写进 binary 清单
- 反序列化：`Runtime/ResourcePackage/Operation/Internal/DeserializeManifestOperation.cs:114` 读回
- 运行时 API：`ResourcePackage.GetPackageNote()` (`ResourcePackage.cs:312-315`)

**默认行为**：留空时 YooAsset 自动填 `DateTime.Now.ToString()` (`BuildParameters.cs:166-168`)，必须显式赋值覆盖。

**项目当前未使用**：`ReleaseTools.cs:216-230` 组装 `BuildParameters` 时未设置 `PackageNote`。

**用途**：天然的每包独立元数据槽，可用于标记 dev/release 模式，无需塞标记资产文件、无需改资源收集配置。

---

### 2. HybridCLR pdb 加载机制

**版本支持**：v6.4.0+ 支持 `Assembly.Load(byte[] rawAssembly, byte[] rawSymbolStore)`  
**项目版本**：v8.8.0 (`Library/PackageCache/com.code-philosophy.hybridclr@4df417e56a/package.json:3`)

**编译产物控制**：
- `CompileDllCommand.CompileDll(target, developmentBuild)` (`CompileDllCommand.cs:15-28`)
- `developmentBuild=true` → `ScriptCompilationOptions.DevelopmentBuild` (:22) → Unity 编译器输出 `.dll` + `.pdb` 到 `HybridCLRData/HotUpdateDlls/<Target>/`
- 项目调用：`BuildDLLCommand.cs:149`/`:158` 调 `CompileDll(target)` 无 bool 参数 → 默认取 `EditorUserBuildSettings.development` (`CompileDllCommand.cs:33`)

**当前运行时加载**：`Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs:196`
```csharp
var assembly = Assembly.Load(textAsset.bytes);  // 单参数，未加载 pdb
```

**当前 Editor 拷贝**：`Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs:359-372` `CopyHotUpdateAssembliesToAssetPath`
```csharp
foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
{
    string dllPath = $"{hotfixDllSrcDir}/{dll}";
    string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
    System.IO.File.Copy(dllPath, dllBytesPath, true);  // 只拷 dll，未拷 pdb
}
```

**需新增**：同步拷贝 `{assembly}.pdb` → `{assembly}.pdb.bytes`，运行时同时加载 pdb TextAsset 并用双参数 `Assembly.Load`。

---

### 3. exe 侧编译期模式标记现成范式

**位置**：`Assets/TEngine/Runtime/Core/UpdateSetting.cs:124-134`

```csharp
public bool Enable
{
    get
    {
#if ENABLE_HYBRIDCLR
        return true;
#else
        return false;
#endif
    }
}
```

**特点**：
- 编译期宏 (`ENABLE_HYBRIDCLR`) 焊死进运行时属性
- 与实际编译状态强绑定，无法靠改配置文件伪造
- 零运行时成本（编译时常量折叠）

**可复用**：照抄此模式，用 `#if !ENABLE_OBFUZ` 判定 dev/release（未开混淆 = dev，开混淆 = release）。

---

### 4. 打包流程与弹窗插入点

**YooAsset 打包总入口**：`Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:181` `BuildInternalWithConfig`
- 所有打包路径（GUI、菜单、CLI）最终汇聚于此
- `:216-230` 组装 `BuildParameters` → 插入 `PackageNote` 赋值点
- 方法开头 → pdb 残留检测 + `EditorUtility.DisplayDialog` 弹窗点

**exe (Player) 构建入口**：`ReleaseTools.cs:684-707` `BuildImp`
```csharp
BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
{
    ...
    options = BuildOptions.None  // 硬编码，从不设 Development
};
BuildPipeline.BuildPlayer(buildPlayerOptions);
```

**BuildConfig 字段**：`Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs:9-42`  
无任何 `development` / `mode` / `pdb` 相关字段 → 干净扩展点。

**现有弹窗参考**：
- `Assets/GameScripts/Procedure/ProcedureInitPackage.cs:106` `LauncherMgr.ShowMessageBox` + `Application.Quit`
- `Assets/Launcher/Scripts/LoadTipsUI.cs:72-101` `SetAllCallback`：`onCancel=null` 时隐藏取消按钮 (`:98-101`)，`autoConfirmDelay=0` 无超时

---

### 5. 启动流程与模式匹配校验点

**清单更新流程**：`Assets/GameScripts/Procedure/ProcedureInitResources.cs:86-194` `InitResources` 协程

逐包循环 (`:92`)：
1. `RequestPackageVersionAsync` (`:104`) 获取版本号
2. `UpdatePackageManifestAsync` (`:181`) 更新清单 → **此时 PackageNote 可读**
3. `SavePackageVersionData` (`:189`)

**校验插入点**：`:191` 之后、`:193 _initResourcesComplete = true` 之前

逐包调用 `YooAssets.GetPackage(runtimePackage.PackageName).GetPackageNote()` 与 `UpdateSetting.BuildMode` 比对，不匹配则 `ShowMessageBox` + `yield break`。

**ResourceModule 包访问**：`Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- 内部用 `YooAssets.GetPackage(name)` (`:306`/`:327`/`:347`)
- 未封装 `GetPackageNote()`，需直接调用 YooAsset API 或后续补封装

---

### 6. obfuz 现状（issue #4 预研）

**包安装状态**：未安装
- `Packages/manifest.json` / `packages-lock.json` 无 obfuz 条目
- `Library/PackageCache` 无 obfuz 目录

**代码骨架已就位**：`Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs`
- `:21-89` 宏开关菜单 `ENABLE_OBFUZ`（`EnableObfuz` / `DisableObfuz`，但自身被 `#if ENABLE_OBFUZ` 包裹 → 鸡蛋问题）
- `:168-190` 混淆加密分支，全被 `#if ENABLE_HYBRIDCLR && ENABLE_OBFUZ` 门控

**配置资产存在**：`ProjectSettings/Obfuz.asset`
- `buildPipelineSettings.enable: 0` (当前禁用)
- `assembliesToObfuscate: [GameLogic, GameProto]`

**运行时引用**：`Assets/GameScripts/HotFix/GameLogic/GameApp.cs:5-7` `using Obfuz;` 全被 `#if ENABLE_OBFUZ` 门控

**当前宏状态**：`ProjectSettings/ProjectSettings.asset:768-782`  
所有平台均无 `ENABLE_OBFUZ`，只有 `ENABLE_HYBRIDCLR`。

---

## 关键文件索引

| 模块 | 文件 | 关键行号 | 说明 |
|------|------|---------|------|
| **运行时加载** | `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs` | :79-95, :196 | dll 加载入口，需改双参数 Assembly.Load |
| **Editor 拷贝** | `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs` | :149, :158, :359-372 | 控制 pdb 生成 + 拷贝逻辑 |
| **YooAsset 打包** | `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs` | :181-233 (BuildInternalWithConfig) | 写 PackageNote + pdb 检测弹窗 |
| **启动校验** | `Assets/GameScripts/Procedure/ProcedureInitResources.cs` | :86-194 (InitResources) | 清单更新后模式匹配 |
| **exe 模式标记** | `Assets/TEngine/Runtime/Core/UpdateSetting.cs` | :124-134 (Enable 范式) | 编译期宏烘焙 |
| **弹窗 UI** | `Assets/Launcher/Scripts/LauncherMgr.cs` | :120-126 (ShowMessageBox) | onCancel=null 隐藏取消按钮 |
| **YooAsset PackageNote** | `Library/PackageCache/.../PackageManifest.cs` | :72 | 包元数据字段 |
| **HybridCLR 编译** | `Library/PackageCache/.../CompileDllCommand.cs` | :15-28 | developmentBuild 控制 pdb 生成 |

---

## 实施要点

1. **PackageNote 是最佳包侧标记载体**：原生、每包独立、零配置改动
2. **编译期宏烘焙最防篡改**：exe 模式与实际编译状态强绑定
3. **pdb 生成受 `developmentBuild` 控制**：需显式传参给 `CompileDll`
4. **启动匹配在清单更新后**：`ProcedureInitResources` 的 `UpdatePackageManifestAsync` 成功后即可读 `PackageNote`
5. **obfuz 是半成品状态**：代码骨架齐全但包未装、宏未开，#4 接入时激活即可
