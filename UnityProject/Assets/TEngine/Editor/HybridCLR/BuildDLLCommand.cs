#if ENABLE_HYBRIDCLR
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
#if OBFUZ_INSTALLED
using Obfuz.Settings;
using Obfuz4HybridCLR;
#endif
using HybridCLR.Editor.Installer;
using TEngine;
using TEngine.Editor;
using UnityEditor;
using UnityEngine;

public static class BuildDLLCommand
{
    private const string EnableHybridClrScriptingDefineSymbol = "ENABLE_HYBRIDCLR";
    private const string EnableObfuzScriptingDefineSymbol = "ENABLE_OBFUZ";
    private const string EnableReleaseScriptingDefineSymbol = "ENABLE_RELEASE";

    #region HybridCLR/Define Symbols
    /// <summary>
    /// 禁用HybridCLR宏定义。
    /// </summary>
    [MenuItem("HybridCLR/Define Symbols/Disable HybridCLR", false, 30)]
    public static void DisableHybridCLR()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableHybridClrScriptingDefineSymbol);
        HybridCLR.Editor.SettingsUtil.Enable = false;
#if ENABLE_HYBRIDCLR
        UpdateSettingEditor.ForceUpdateAssemblies();
#endif
    }

    /// <summary>
    /// 开启HybridCLR宏定义。
    /// </summary>
    [MenuItem("HybridCLR/Define Symbols/Enable HybridCLR", false, 31)]
    public static void EnableHybridCLR()
    {
        // 先去判断安装了没
        var controller = new InstallerController();
        if (!controller.HasInstalledHybridCLR())
        {
            controller.InstallDefaultHybridCLR();
        }

        if (!HybridCLR.Editor.SettingsUtil.Enable)
        {
            HybridCLR.Editor.SettingsUtil.Enable = true;
#if ENABLE_HYBRIDCLR
            UpdateSettingEditor.ForceUpdateAssemblies();
#endif
        }
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableHybridClrScriptingDefineSymbol);
        ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableHybridClrScriptingDefineSymbol);
        UpdateSettingEditor.ForceUpdateAssemblies();
    }
    #endregion
    
    #region 构建模式切换（菜单与 BuildModeWindow 共用）

    /// <summary>当前是否为 release 模式（按当前编译平台 define 判定）。</summary>
    public static bool IsReleaseModeActive =>
        ScriptingDefineSymbols.HasScriptingDefineSymbol(EditorUserBuildSettings.selectedBuildTargetGroup, EnableReleaseScriptingDefineSymbol);

    /// <summary>切换 dev/release 模式（全平台 define 同步）。</summary>
    public static void SetReleaseMode(bool release)
    {
        if (release)
        {
            ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableReleaseScriptingDefineSymbol);
            Debug.Log("[BuildMode] 已切换到 release 模式（不生成/不加载 pdb，PackageNote=release）");
        }
        else
        {
            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableReleaseScriptingDefineSymbol);
            Debug.Log("[BuildMode] 已切换到 dev 模式（pdb 有则加载，PackageNote=dev）");
        }
    }

    /// <summary>dev 模式 pdb 开关的当前配置值（仅配置项；实际生效还需 dev 模式，见 UpdateSetting.WillGeneratePdb）。</summary>
    public static bool IsPdbEnabled => Settings.UpdateSetting.GeneratePdb;

    /// <summary>切换 dev 模式 pdb 生成开关（UpdateSetting 序列化配置，不触发重编译）。</summary>
    public static void SetPdbEnabled(bool enable)
    {
        if (Settings.UpdateSetting.GeneratePdb == enable)
        {
            return;
        }
        Settings.UpdateSetting.GeneratePdb = enable;
        EditorUtility.SetDirty(Settings.UpdateSetting);
        AssetDatabase.SaveAssets();
        Debug.Log(enable
            ? "[BuildMode] 已开启 pdb 生成（dev 模式编译热更 dll 时产出 pdb）"
            : "[BuildMode] 已关闭 pdb 生成");
    }

#if OBFUZ_INSTALLED
    /// <summary>当前混淆开关状态（按当前编译平台 define 判定）。</summary>
    public static bool IsObfuzActive =>
        ScriptingDefineSymbols.HasScriptingDefineSymbol(EditorUserBuildSettings.selectedBuildTargetGroup, EnableObfuzScriptingDefineSymbol);

    /// <summary>切换 Obfuz 混淆（全平台 define + ObfuzSettings.enable 同步）。</summary>
    public static void SetObfuz(bool enable)
    {
        if (enable)
        {
            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
            ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
            ObfuzSettings.Instance.buildPipelineSettings.enable = true;
            Debug.Log("[BuildMode] 已开启 Obfuz 混淆");
        }
        else
        {
            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
            ObfuzSettings.Instance.buildPipelineSettings.enable = false;
            Debug.Log("[BuildMode] 已关闭 Obfuz 混淆");
        }
    }
#endif

    /// <summary>
    /// Obfuz 包是否已安装。OBFUZ_INSTALLED 宏仅在 TEngine.Editor 程序集内可见，
    /// 其它程序集（如工具栏扩展所在的 Assembly-CSharp-Editor）请通过本属性安全判断。
    /// </summary>
    public static bool IsObfuzInstalled =>
#if OBFUZ_INSTALLED
        true;
#else
        false;
#endif

    /// <summary>混淆状态安全查询：包未安装时恒为 false。</summary>
    public static bool IsObfuzActiveSafe =>
#if OBFUZ_INSTALLED
        IsObfuzInstalled && IsObfuzActive;
#else
        false;
#endif

    /// <summary>混淆开关安全切换：包未安装时不做任何事。</summary>
    public static void SetObfuzSafe(bool enable)
    {
#if OBFUZ_INSTALLED
        SetObfuz(enable);
#endif
    }

    #endregion

#if OBFUZ_INSTALLED
    #region Obfuz/Define Symbols
    /// <summary>
    /// 禁用Obfuz宏定义。
    /// </summary>
    [MenuItem("Obfuz/Define Symbols/Disable Obfuz", false, 30)]
    public static void DisableObfuz() => SetObfuz(false);

    /// <summary>
    /// 开启Obfuz宏定义。
    /// </summary>
    [MenuItem("Obfuz/Define Symbols/Enable Obfuz", false, 31)]
    public static void EnableObfuz() => SetObfuz(true);
    #endregion
#endif

    #region TEngine/Define Symbols
    /// <summary>
    /// 切换到 release 发布模式（不生成/不加载 pdb）。
    /// </summary>
    [MenuItem("TEngine/Define Symbols/Enable Release Mode", false, 40)]
    public static void EnableReleaseMode() => SetReleaseMode(true);

    /// <summary>
    /// 切换到 dev 开发模式（pdb 有则加载）。
    /// </summary>
    [MenuItem("TEngine/Define Symbols/Disable Release Mode (dev)", false, 41)]
    public static void DisableReleaseMode() => SetReleaseMode(false);
    #endregion

    /// <summary>
    /// 同步 AOT 元数据清单：从 HybridCLR 生成的 AOTGenericReferences.PatchedAOTAssemblyList 更新 AOTMetadataManifest.asset。
    /// 保留 manifest 中手动添加的额外项。
    /// </summary>
    [MenuItem("HybridCLR/Build/Sync AOT Metadata Manifest", false, 89)]
    public static void SyncAOTMetadataManifest()
    {
#if ENABLE_HYBRIDCLR
        var manifest = LoadAOTMetadataManifest();
        if (manifest == null)
        {
            // manifest 不存在时，警告并明确告知创建方式与路径
            string manifestPath = GetAOTMetadataManifestAssetPath();
            Debug.LogWarning(
                $"[AOTMetadata] 同步失败：未找到 AOTMetadataManifest。\n" +
                $"  期望路径：{manifestPath}\n" +
                $"  创建方式一（推荐）：Project 窗口右键 → Create → TEngine → AOT Metadata Manifest，" +
                $"并将生成的资产移动/重命名到上述路径（文件名须为 {AOTMetadataManifest.ManifestAssetName}.asset）。\n" +
                $"  创建方式二：直接在 Assets/{TEngine.Settings.UpdateSetting.AssemblyTextAssetPath}/ 目录下创建，" +
                $"该目录会被 YooAsset 的 CodePackage 收集以支持热更。");
            return;
        }

        var generatedAssemblies = GetGeneratedPatchedAOTAssemblies();
        if (generatedAssemblies.Count == 0)
        {
            Debug.LogWarning("[AOTMetadata] 同步跳过：HybridCLR 未生成 AOTGenericReferences 或 PatchedAOTAssemblyList 为空。请先运行 HybridCLR → Generate → AOT Generic References");
            return;
        }

        var oldNormalized = NormalizeAssemblyList(manifest.AOTMetaAssemblies ?? new List<string>());

        // 保留 manifest 中手动添加的额外项（不在生成列表中的）
        var extra = oldNormalized.Where(a => !generatedAssemblies.Contains(a)).ToList();

        // 新列表 = 生成列表 + 手动额外项，去重排序
        var newList = NormalizeAssemblyList(generatedAssemblies.Concat(extra));
        newList.Sort(StringComparer.Ordinal);

        manifest.AOTMetaAssemblies = newList;
        EditorUtility.SetDirty(manifest);
        AssetDatabase.SaveAssets();

        // 同步写入 .json.bytes 打包资产：归档管线与非归档管线统一从 JSON 字节流加载 manifest。
        WriteAOTMetadataManifestJson(newList);

        Debug.Log($"[AOTMetadata] 同步完成：{GetAOTMetadataManifestAssetPath()}\n" +
                  $"  生成项：{generatedAssemblies.Count} 个 [{string.Join(", ", generatedAssemblies)}]\n" +
                  $"  保留额外项：{extra.Count} 个 [{string.Join(", ", extra)}]\n" +
                  $"  最终：{newList.Count} 个\n" +
                  $"  JSON 资产已写入：{GetAOTMetadataManifestBytesAssetPath()}");
#else
        Debug.LogWarning("[AOTMetadata] 同步跳过：需启用 ENABLE_HYBRIDCLR 宏定义");
#endif
    }

    [MenuItem("HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath")]
    public static void BuildAndCopyDlls()
    {
#if ENABLE_HYBRIDCLR
        SyncAOTMetadataManifest();
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
        bool developmentBuild = Settings.UpdateSetting.WillGeneratePdb;
        CompileDllCommand.CompileDll(target, developmentBuild);
        CopyAOTHotUpdateDlls(target);
#endif
    }

    public static void BuildAndCopyDlls(BuildTarget target)
    {
#if ENABLE_HYBRIDCLR
        SyncAOTMetadataManifest();
        bool developmentBuild = Settings.UpdateSetting.WillGeneratePdb;
        CompileDllCommand.CompileDll(target, developmentBuild);
        CopyAOTHotUpdateDlls(target);
#endif
    }

    public static void CopyAOTHotUpdateDlls(BuildTarget target)
    {
        CopyAOTAssembliesToAssetPath(target);
        CopyHotUpdateAssembliesToAssetPath(target);

#if ENABLE_HYBRIDCLR && ENABLE_OBFUZ
        CompileDllCommand.CompileDll(target, Settings.UpdateSetting.WillGeneratePdb);

        string obfuscatedHotUpdateDllPath = PrebuildCommandExt.GetObfuscatedHotUpdateAssemblyOutputPath(target);
        ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath);

        Directory.CreateDirectory(Application.streamingAssetsPath);

        string hotUpdateDllPath = $"{SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)}";
        List<string> obfuscationRelativeAssemblyNames = ObfuzSettings.Instance.assemblySettings.GetObfuscationRelativeAssemblyNames();
        bool polymorphicEnabled = ObfuzSettings.Instance.polymorphicDllSettings.enable;
        string polymorphicHotUpdateDllPath = GetPolymorphicHotUpdateAssemblyOutputPath(target);

        foreach (string assName in SettingsUtil.HotUpdateAssemblyNamesIncludePreserved)
        {
            string srcDir = obfuscationRelativeAssemblyNames.Contains(assName) ? obfuscatedHotUpdateDllPath : hotUpdateDllPath;
            string srcFile = $"{srcDir}/{assName}.dll";
            string dstFile = Application.dataPath + "/" + TEngine.Settings.UpdateSetting.AssemblyTextAssetPath + "/" + TEngine.Settings.UpdateSetting.HotUpdateAssemblySubPath + $"/{assName}.dll.bytes";
            if (!File.Exists(srcFile))
            {
                continue;
            }
            if (polymorphicEnabled)
            {
                srcFile = GeneratePolymorphicHotUpdateDll(srcFile, polymorphicHotUpdateDllPath, assName);
            }
            File.Copy(srcFile, dstFile, true);
            Debug.Log($"[CompileAndObfuscate] Copy {srcFile} to {dstFile}");
        }
#endif
        
    }

#if ENABLE_HYBRIDCLR && ENABLE_OBFUZ
    /// <summary>多态热更 dll 输出目录，与混淆产物目录平行，避免标准/多态两种格式混写。</summary>
    private static string GetPolymorphicHotUpdateAssemblyOutputPath(BuildTarget target)
    {
        return $"{ObfuzSettings.Instance.ObfuzRootDir}/{target}/PolymorphicHotUpdateAssemblies";
    }

    /// <summary>
    /// 将混淆/原始 dll 转为多态格式（结构由 polymorphicDllSettings.codeGenerationSecretKey 决定）。
    /// 转换失败直接抛异常中断构建，避免静默回退标准格式产物。
    /// </summary>
    private static string GeneratePolymorphicHotUpdateDll(string srcFile, string outputDir, string assemblyName)
    {
        Directory.CreateDirectory(outputDir);
        string dstFile = $"{outputDir}/{assemblyName}.dll";
        ObfuscateUtil.GeneratePolymorphicDll(srcFile, dstFile);
        return dstFile;
    }
#endif

    public static void CopyAOTAssembliesToAssetPath()
    {
        CopyAOTAssembliesToAssetPath(EditorUserBuildSettings.activeBuildTarget);
    }

    public static void CopyAOTAssembliesToAssetPath(BuildTarget target)
    {
#if ENABLE_HYBRIDCLR
        string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
        string aotAssembliesDstDir = Application.dataPath + "/" + TEngine.Settings.UpdateSetting.AssemblyTextAssetPath + "/" + TEngine.Settings.UpdateSetting.AOTAssemblySubPath;
        var resolvedAssemblies = GetResolvedAOTMetaAssemblies();

        Debug.Log($"[AOTMetadata] 开始拷贝AOT补充元数据DLL。Target:{target}, SrcDir:{aotAssembliesSrcDir}, DstDir:{aotAssembliesDstDir}, Count:{resolvedAssemblies.Count}");
        foreach (var dll in resolvedAssemblies)
        {
            string srcDllPath = $"{aotAssembliesSrcDir}/{dll}";
            string dllBytesPath = $"{aotAssembliesDstDir}/{dll}.bytes";
            Debug.Log($"[AOTMetadata] 准备拷贝：{dll}, Src:{srcDllPath}, Dst:{dllBytesPath}");
            if (!File.Exists(srcDllPath))
            {
                Debug.LogWarning(
                    $"[AOTMetadata] AOT 源 DLL 不存在，跳过拷贝：{srcDllPath}（程序集：{dll}）。\n" +
                    $"裁剪后的 AOT DLL 仅在 BuildPlayer 时生成，需先完整构建一次游戏 App 后再打 AssetBundle。\n" +
                    $"若已构建过 App 仍报错误，请检查 AOTMetadataManifest.asset / UpdateSetting.AOTMetaAssemblies 是否配置了不存在的程序集。");
                continue;
            }

            File.Copy(srcDllPath, dllBytesPath, true);
            Debug.Log($"[AOTMetadata] 拷贝完成：{dll}, Size:{new FileInfo(dllBytesPath).Length} bytes");
        }
        Debug.Log("[AOTMetadata] AOT补充元数据DLL拷贝流程结束。");
#endif
        
        AssetDatabase.Refresh();
    }

#if ENABLE_HYBRIDCLR
    private static List<string> GetResolvedAOTMetaAssemblies()
    {
        // 最终拷贝列表 = manifest ∪ HybridCLR 生成列表 ∪ UpdateSetting.AOTMetaAssemblies。
        // manifest 缺失 generated 的项视为配置遗漏：仅警告并说明系统已补上，不中断构建。
        var manifest = LoadAOTMetadataManifest();
        var manifestAssemblies = manifest != null && manifest.AOTMetaAssemblies != null
            ? manifest.AOTMetaAssemblies
            : new List<string>();
        if (manifest != null)
        {
            Debug.Log($"[AOTMetadata] 使用 AOTMetadataManifest 配置列表，Count:{manifestAssemblies.Count}, List:{string.Join(", ", NormalizeAssemblyList(manifestAssemblies))}");
        }
        else
        {
            Debug.Log("[AOTMetadata] 未找到 AOTMetadataManifest，仅使用生成列表 + UpdateSetting。");
        }

        var generatedAssemblies = GetGeneratedPatchedAOTAssemblies();
        Debug.Log($"[AOTMetadata] HybridCLR AOTGenericReferences 生成列表，Count:{generatedAssemblies.Count}, List:{string.Join(", ", generatedAssemblies)}");

        var normalizedManifest = NormalizeAssemblyList(manifestAssemblies);

        // 【缺失补齐】generated 里有但 manifest 里没有：警告并说明系统已补上，不中断
        var missingGeneratedAssemblies = generatedAssemblies
            .Where(assembly => !normalizedManifest.Contains(assembly))
            .ToList();
        if (missingGeneratedAssemblies.Count > 0)
        {
            Debug.LogWarning(
                $"[AOTMetadata] AOTMetadataManifest 缺少 HybridCLR 生成的以下补充元数据程序集，系统已补上：\n" +
                $"  {string.Join("\n  ", missingGeneratedAssemblies)}\n" +
                $"缺失会导致运行时 ExecutionEngineException。建议运行菜单 HybridCLR → Build → Sync AOT Metadata Manifest 同步至 manifest 以持久化。");
        }

        // manifest 含 generated 未包含的项（可能是手动添加，将一并拷贝）
        var extraAssemblies = normalizedManifest
            .Where(assembly => !generatedAssemblies.Contains(assembly))
            .ToList();
        if (extraAssemblies.Count > 0)
        {
            Debug.LogWarning($"[AOTMetadata] 注意：AOTMetadataManifest 包含 HybridCLR 未生成的程序集（可能是手动添加，将一并拷贝）：{string.Join(", ", extraAssemblies)}");
        }

        var updateSettingAssemblies = TEngine.Settings.UpdateSetting.AOTMetaAssemblies;
        Debug.Log($"[AOTMetadata] UpdateSetting.AOTMetaAssemblies 列表，Count:{updateSettingAssemblies.Count}, List:{string.Join(", ", NormalizeAssemblyList(updateSettingAssemblies))}");

        // 三源合并去重
        var resolvedAssemblies = NormalizeAssemblyList(
            normalizedManifest
                .Concat(generatedAssemblies)
                .Concat(updateSettingAssemblies));
        Debug.Log($"[AOTMetadata] 最终AOT补充元数据列表，Count:{resolvedAssemblies.Count}, List:{string.Join(", ", resolvedAssemblies)}");
        return resolvedAssemblies;
    }

    private static AOTMetadataManifest LoadAOTMetadataManifest()
    {
        string manifestPath = GetAOTMetadataManifestAssetPath();
        Debug.Log($"[AOTMetadata] 查找 AOTMetadataManifest：{manifestPath}");
        var manifest = AssetDatabase.LoadAssetAtPath<AOTMetadataManifest>(manifestPath);
        if (manifest == null)
        {
            Debug.LogWarning($"[AOTMetadata] 未找到 AOTMetadataManifest：{manifestPath}，将使用生成列表 + UpdateSetting.AOTMetaAssemblies 合并。");
        }
        else
        {
            int count = manifest.AOTMetaAssemblies == null ? 0 : manifest.AOTMetaAssemblies.Count;
            Debug.Log($"[AOTMetadata] 找到 AOTMetadataManifest：{manifestPath}, Count:{count}");
        }
        return manifest;
    }

    private static string GetAOTMetadataManifestAssetPath()
    {
        return TEngine.Settings.UpdateSetting.GetAOTMetadataManifestAssetPath();
    }

    private static string GetAOTMetadataManifestBytesAssetPath()
    {
        return TEngine.Settings.UpdateSetting.GetAOTMetadataManifestBytesAssetPath();
    }

    /// <summary>
    /// 将 manifest 列表写入 .json.bytes 打包资产。
    /// 归档管线（RawFileObject.GetBytes）与非归档管线（TextAsset.bytes）统一读取此文件。
    /// </summary>
    private static void WriteAOTMetadataManifestJson(List<string> assemblies)
    {
        string assetPath = GetAOTMetadataManifestBytesAssetPath();
        string fullDir = Path.GetDirectoryName(Application.dataPath + "/" + assetPath.Substring("Assets/".Length));
        Directory.CreateDirectory(fullDir);

        var manifest = LoadAOTMetadataManifest();
        string json = manifest != null ? manifest.ToJson() : JsonUtility.ToJson(new { AOTMetaAssemblies = assemblies }, true);
        string fullPath = Application.dataPath + "/" + assetPath.Substring("Assets/".Length);
        File.WriteAllText(fullPath, json);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[AOTMetadata] JSON 资产已写入：{assetPath}");
    }

    private static List<string> GetGeneratedPatchedAOTAssemblies()
    {
        string referenceFile = GetAOTGenericReferenceFilePath();
        Debug.Log($"[AOTMetadata] 查找 HybridCLR AOTGenericReferences：{referenceFile}");
        if (string.IsNullOrEmpty(referenceFile) || !File.Exists(referenceFile))
        {
            Debug.LogWarning("[AOTMetadata] 未找到 HybridCLR AOTGenericReferences 文件，跳过生成列表合并。");
            return new List<string>();
        }

        var content = File.ReadAllText(referenceFile);
        var match = Regex.Match(content, @"PatchedAOTAssemblyList\s*=\s*new\s+List<string>\s*\{(?<body>[\s\S]*?)\};");
        if (!match.Success)
        {
            Debug.LogWarning($"[AOTMetadata] 未能解析 PatchedAOTAssemblyList：{referenceFile}");
            return new List<string>();
        }

        var assemblies = Regex.Matches(match.Groups["body"].Value, @"""(?<assembly>[^""\r\n]+\.dll)""")
            .Cast<Match>()
            .Select(item => item.Groups["assembly"].Value)
            .ToList();
        Debug.Log($"[AOTMetadata] 解析 HybridCLR AOTGenericReferences 完成：{referenceFile}, Count:{assemblies.Count}");
        return assemblies;
    }

    private static string GetAOTGenericReferenceFilePath()
    {
        string referenceFile = HybridCLRSettings.Instance.outputAOTGenericReferenceFile;
        if (string.IsNullOrEmpty(referenceFile))
        {
            return string.Empty;
        }

        if (File.Exists(referenceFile))
        {
            return referenceFile;
        }

        string assetsRelativePath = Path.Combine("Assets", referenceFile);
        return File.Exists(assetsRelativePath) ? assetsRelativePath : referenceFile;
    }

    private static List<string> NormalizeAssemblyList(IEnumerable<string> assemblies)
    {
        return assemblies
            .Where(assembly => !string.IsNullOrWhiteSpace(assembly))
            .Select(assembly => assembly.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
#endif

    public static void CopyHotUpdateAssembliesToAssetPath()
    {
        CopyHotUpdateAssembliesToAssetPath(EditorUserBuildSettings.activeBuildTarget);
    }

    public static void CopyHotUpdateAssembliesToAssetPath(BuildTarget target)
    {
#if ENABLE_HYBRIDCLR
        string hotfixDllSrcDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
        string hotfixAssembliesDstDir = Application.dataPath + "/" + TEngine.Settings.UpdateSetting.AssemblyTextAssetPath + "/" + TEngine.Settings.UpdateSetting.HotUpdateAssemblySubPath;
        foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
        {
            string dllPath = $"{hotfixDllSrcDir}/{dll}";
            string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
            System.IO.File.Copy(dllPath, dllBytesPath, true);
            Debug.Log($"[拷贝热更新dll代码] copy hotfix dll {dllPath} -> {dllBytesPath}");
        }

        CopyPdbToAssetPath(hotfixDllSrcDir, target);
#endif
        
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 拷贝热更程序集的 pdb 符号文件到 PDB 子目录（仅 development 构建产物存在时）。
    /// 当前配置不生成 pdb（release 模式或 pdb 开关关闭）时改为清理两处残留：
    /// HybridCLR 输出目录的旧 pdb 与 PDB 子目录的旧 pdb.bytes，防止过期符号被误拷贝/误打包。
    /// </summary>
    private static void CopyPdbToAssetPath(string hotfixDllSrcDir, BuildTarget target)
    {
        string pdbDstDir = Application.dataPath + "/" + TEngine.Settings.UpdateSetting.AssemblyTextAssetPath + "/" + TEngine.Settings.UpdateSetting.PdbAssemblySubPath;
        if (!TEngine.Settings.UpdateSetting.WillGeneratePdb)
        {
            CleanStalePdbFiles(hotfixDllSrcDir, pdbDstDir);
            return;
        }

        foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
        {
            string assemblyName = Path.GetFileNameWithoutExtension(dll);
            string pdbPath = $"{hotfixDllSrcDir}/{assemblyName}.pdb";
            if (!File.Exists(pdbPath))
            {
                continue;
            }

            string pdbBytesPath = $"{pdbDstDir}/{assemblyName}.pdb.bytes";
            File.Copy(pdbPath, pdbBytesPath, true);
            Debug.Log($"[拷贝热更新pdb符号] copy pdb {pdbPath} -> {pdbBytesPath}");
        }
    }

    /// <summary>
    /// 清理不产 pdb 配置下的残留符号文件（HybridCLR 输出目录的 .pdb 与资产目录的 .pdb.bytes）。
    /// </summary>
    private static void CleanStalePdbFiles(string hotfixDllSrcDir, string pdbDstDir)
    {
        foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
        {
            string assemblyName = Path.GetFileNameWithoutExtension(dll);

            string pdbPath = $"{hotfixDllSrcDir}/{assemblyName}.pdb";
            if (File.Exists(pdbPath))
            {
                File.Delete(pdbPath);
                Debug.Log($"[拷贝热更新pdb符号] 当前配置不生成 pdb，清理残留：{pdbPath}");
            }

            string pdbBytesPath = $"{pdbDstDir}/{assemblyName}.pdb.bytes";
            if (File.Exists(pdbBytesPath))
            {
                File.Delete(pdbBytesPath);
                Debug.Log($"[拷贝热更新pdb符号] 当前配置不生成 pdb，清理残留：{pdbBytesPath}");
            }
        }
    }
}

/// <summary>
/// 构建失败异常。用于 AOT 元数据校验失败、源 DLL 缺失等场景中断构建流程。
/// </summary>
public class BuildFailedException : System.Exception
{
    public BuildFailedException(string message) : base(message) { }
}
