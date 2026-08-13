# Issue #3 阶段一：dev/release 模式标记与 pdb 调试符号加载

**日期**: 2026-08-13  
**Commit**: 52274358

## 实施内容

### 1. 新增编译期模式标记

**文件**: `UpdateSetting.cs`

```csharp
// 新增数据结构（运行时程序集）
[Serializable]
public class PackageMetadata
{
    public string mode; // "dev" / "release"
}

// 新增属性
public bool IsDevelopmentBuild
{
    get
    {
#if !ENABLE_OBFUZ
        return true;  // 未开混淆 = dev
#else
        return false; // 开了混淆 = release
#endif
    }
}

public string BuildMode => IsDevelopmentBuild ? "dev" : "release";
```

**核心逻辑**: 通过 `ENABLE_OBFUZ` 宏在编译期决定模式（未定义=dev，已定义=release）

---

### 2. YooAsset 打包写入 PackageNote

**文件**: `ReleaseTools.cs:272`

```csharp
buildParameters.PackageNote = JsonUtility.ToJson(new PackageMetadata { mode = Settings.UpdateSetting.BuildMode });
```

**数据格式**: `{"mode":"dev"}` 或 `{"mode":"release"}`

**扩展性**: 使用 JSON 格式预留后续扩展字段（buildTime、commitHash 等）

---

### 3. 启动时校验模式匹配

**文件**: `ProcedureInitResources.cs:193-216`

```csharp
string exeMode = _updateSetting.BuildMode;
foreach (var package in allPackages)
{
    string packageNote = package.GetPackageNote();
    if (string.IsNullOrEmpty(packageNote)) continue;

    var metadata = JsonUtility.FromJson<PackageMetadata>(packageNote);
    string packageMode = metadata.mode;
    
    if (!string.Equals(exeMode, packageMode, StringComparison.OrdinalIgnoreCase))
    {
        // 弹窗提示模式不匹配并退出
        Application.Quit();
    }
}
```

**保护机制**: dev exe 无法加载 release 资源包（反之亦然）

---

### 4. HybridCLR 编译时控制 pdb 生成

**文件**: `BuildDLLCommand.cs:148, 157`

```csharp
public static void BuildAndCopyDlls(BuildTarget target, bool developmentBuild)
{
    CompileDllCommand.CompileDll(target, developmentBuild); // 传入 developmentBuild 参数
    // ...
}

// 调用处
bool isDev = Settings.UpdateSetting.IsDevelopmentBuild;
BuildAndCopyDlls(target, isDev);
```

**效果**: dev 模式生成 .pdb 文件，release 模式不生成

---

### 5. Editor 拷贝 pdb 到资源目录

**状态**: ✅ 已在目录拆分会话中完成

**文件**: `BuildDLLCommand.cs` 中的 `CopyPdbToAssetPath()` 方法

**目标路径**: `Assets/AssetRaw/DLL/PDB/*.pdb.bytes`

---

### 6. 运行时加载 pdb

**文件**: `ProcedureLoadAssembly.cs`

**新增字段**:
```csharp
private readonly Dictionary<string, byte[]> _pdbBytesCache = new Dictionary<string, byte[]>();
```

**加载逻辑**:
```csharp
// 先加载所有 dll
foreach (string hotUpdateDllName in _setting.HotUpdateAssemblies)
{
    var result = await _resourceModule.LoadAssetAsync<TextAsset>(assetLocation, default, _assemblyPackageName);
    LoadAssetSuccess(result);
}

// 仅 dev 模式加载 pdb
if (_setting.IsDevelopmentBuild)
{
    foreach (string hotUpdateDllName in _setting.HotUpdateAssemblies)
    {
        string pdbAssetName = Path.GetFileNameWithoutExtension(hotUpdateDllName) + ".pdb";
        var result = await _resourceModule.LoadAssetAsync<TextAsset>(pdbAssetName, default, _assemblyPackageName);
        LoadAssetSuccess(result);
    }
}
```

**双参数加载**:
```csharp
private void LoadAssetSuccess(TextAsset textAsset)
{
    var assetName = textAsset.name;
    
    // 判断是 pdb 还是 dll
    if (assetName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
    {
        // 缓存 pdb（key = assembly 名，如 "GameLogic"）
        string assemblyName = Path.GetFileNameWithoutExtension(assetName);
        _pdbBytesCache[assemblyName] = textAsset.bytes;
        return;
    }
    
    // 加载 dll（尝试带 pdb）
    string dllName = Path.GetFileNameWithoutExtension(assetName);
    byte[] pdbBytes = _pdbBytesCache.ContainsKey(dllName) ? _pdbBytesCache[dllName] : null;
    
    Assembly assembly = pdbBytes != null
        ? Assembly.Load(dllBytes, pdbBytes)  // 带 pdb 加载
        : Assembly.Load(dllBytes);           // 无 pdb 时回退单参数
}
```

**关键设计**:
- 使用 addressable key 加载（不受目录变动影响）
- pdb 先缓存到字典，dll 回调时按名称匹配
- 兼容无 pdb 场景（release 模式或 pdb 加载失败时自动回退）

---

### 7. 打包前检测 pdb 残留

**文件**: `ReleaseTools.cs:193-235`

```csharp
private static YooAsset.Editor.BuildResult BuildInternalWithConfig(...)
{
    // pdb 残留检测（仅 release 模式且构建 CodePackage 时检查）
    bool isReleaseMode = !Settings.UpdateSetting.IsDevelopmentBuild;
    bool isCodePackage = IsAssemblyPackage(runtimePackage.PackageName);
    
    if (isReleaseMode && isCodePackage)
    {
        string pdbDir = Settings.UpdateSetting.GetPdbAssemblyAssetPath();
        if (Directory.Exists(pdbDir))
        {
            var pdbFiles = Directory.GetFiles(pdbDir, "*.pdb.bytes", SearchOption.TopDirectoryOnly);
            if (pdbFiles.Length > 0)
            {
                // 弹窗提示检测到 pdb，选择「清理并继续」或「取消打包」
                bool shouldContinue = EditorUtility.DisplayDialog(...);
                if (!shouldContinue)
                {
                    return new BuildResult { Success = false };
                }
                
                // 清理 pdb 和 .meta 文件
                foreach (var pdbFile in pdbFiles)
                {
                    File.Delete(pdbFile);
                    File.Delete(pdbFile + ".meta");
                }
                AssetDatabase.Refresh();
            }
        }
    }
    // ...
}
```

**保护机制**: 防止 release 包意外包含 pdb（泄露符号信息）

---

## 核心机制总结

### 模式标记框架

| 状态 | 宏 | IsDevelopmentBuild | BuildMode | pdb 生成 | PackageNote |
|------|----|--------------------|-----------|---------|-------------|
| 开发 | 未定义 `ENABLE_OBFUZ` | `true` | `"dev"` | ✅ | `{"mode":"dev"}` |
| 正式 | 已定义 `ENABLE_OBFUZ` | `false` | `"release"` | ❌ | `{"mode":"release"}` |

### pdb 完整流程

```
编译时                    Editor 时                运行时
────────────────────     ──────────────────      ──────────────────
CompileDll(target, true)  CopyPdbToAssetPath()    LoadAssetAsync("xxx.pdb")
       ↓                         ↓                        ↓
生成 .pdb 文件            拷贝到 AssetRaw/DLL/PDB/   缓存到 _pdbBytesCache
                                 ↓                        ↓
                          YooAsset 打包收集         Assembly.Load(dll, pdb)
```

### 三层保护

1. **编译期**: `ENABLE_OBFUZ` 宏决定是否生成 pdb
2. **打包期**: release 模式检测 pdb 残留并弹窗清理
3. **运行期**: 模式不匹配时弹窗并退出

---

## 技术细节

### PackageMetadata 定义位置

**初次错误**: 定义在 `ReleaseTools.cs`（Editor 程序集）  
**报错**: `ProcedureInitResources.cs` 无法访问 Editor 程序集的类  
**解决**: 移到 `UpdateSetting.cs`（运行时程序集）

### pdb 加载使用 addressable key

**不依赖路径**: `LoadAssetAsync<TextAsset>("GameLogic.pdb")` 而非硬编码 `Path.Combine("Assets", _setting.AssemblyTextAssetPath, ...)`

**好处**: 目录结构变动时无需修改代码

### pdb 缓存机制

**为什么需要缓存**:  
- YooAsset 异步加载，dll 和 pdb 的回调顺序不确定
- 必须先缓存所有 pdb，dll 回调时再从字典匹配

**缓存 key**: assembly 名（去除 `.bytes` 扩展名），如 `"GameLogic"`

---

## TEngine 已有 Obfuz 集成

### 现有菜单（需要先有宏才显示）

**位置**: `BuildDLLCommand.cs:66-89`

```csharp
#if ENABLE_OBFUZ
    [MenuItem("Obfuz/Define Symbols/Enable Obfuz")]
    public static void EnableObfuz()
    {
        ScriptingDefineSymbols.AddScriptingDefineSymbol("ENABLE_OBFUZ");
        ObfuzSettings.Instance.buildPipelineSettings.enable = true;
    }
    
    [MenuItem("Obfuz/Define Symbols/Disable Obfuz")]
    public static void DisableObfuz()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol("ENABLE_OBFUZ");
        ObfuzSettings.Instance.buildPipelineSettings.enable = false;
    }
#endif
```

### 发现的问题

**悖论**: 
- 菜单需要 `ENABLE_OBFUZ` 宏才显示
- 启用宏需要菜单操作
- Disable 后菜单消失，无法通过菜单重新 Enable

**潜在方案**（待用户决策）:
- 把菜单移出 `#if ENABLE_OBFUZ`，保持 `ObfuzSettings` 调用在宏保护内
- 让菜单始终可见，解决首次启用和关闭后无法重开的问题

---

## 验证清单（待执行）

### 1. dev 模式 pdb 真机调试
1. 确认 `ENABLE_OBFUZ` 宏未定义
2. 菜单 `HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath`
3. 检查 `Assets/AssetRaw/DLL/PDB/` 下有 `GameLogic.pdb.bytes`
4. YooAsset 打包，确认日志显示 PackageNote = `{"mode":"dev"}`
5. 出 exe 真机运行，触发断点查看是否有行号

### 2. release 模式打包拦截
1. 手动在 `Assets/AssetRaw/DLL/PDB/` 放一个假的 `test.pdb.bytes`
2. 在 `ProjectSettings/Player` 的 Scripting Define Symbols 加 `ENABLE_OBFUZ`
3. 触发 YooAsset 打包（构建 CodePackage）
4. 应弹窗提示检测到 pdb，验证「清理并继续」和「取消打包」两个分支

### 3. 模式不匹配拦截
1. 移除 `ENABLE_OBFUZ`，打一个 dev 资源包
2. 加上 `ENABLE_OBFUZ`，出一个 release exe
3. 启动游戏，应在资源初始化阶段弹「资源模式不匹配」并退出

---

## 后续 Issue #4 接入路径

本期框架已就绪，#4 只需：
1. 安装 obfuz 包
2. release 打包前通过菜单或手动开启 `ENABLE_OBFUZ` 宏
3. 模式框架自动生效（dev exe 拒绝加载 release 混淆包）

---

## 相关文件清单

- `Assets/TEngine/Runtime/Core/UpdateSetting.cs` — PackageMetadata 结构 + 模式属性
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs` — 写入 PackageNote + pdb 残留检测
- `Assets/GameScripts/Procedure/ProcedureInitResources.cs` — 启动时模式校验
- `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs` — 编译时控制 pdb 生成
- `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs` — 运行时加载 pdb
