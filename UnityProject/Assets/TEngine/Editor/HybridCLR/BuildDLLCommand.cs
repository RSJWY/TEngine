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
#if ENABLE_OBFUZ
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
    
#if ENABLE_OBFUZ
    #region Obfuz/Define Symbols
    /// <summary>
    /// 禁用Obfuz宏定义。
    /// </summary>
    [MenuItem("Obfuz/Define Symbols/Disable Obfuz", false, 30)]
    public static void DisableObfuz()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
        ObfuzSettings.Instance.buildPipelineSettings.enable = false;
    }

    /// <summary>
    /// 开启Obfuz宏定义。
    /// </summary>
    [MenuItem("Obfuz/Define Symbols/Enable Obfuz", false, 31)]
    public static void EnableObfuz()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
        ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
        ObfuzSettings.Instance.buildPipelineSettings.enable = true;
    }
    #endregion
#endif

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

        Debug.Log($"[AOTMetadata] 同步完成：{GetAOTMetadataManifestAssetPath()}\n" +
                  $"  生成项：{generatedAssemblies.Count} 个 [{string.Join(", ", generatedAssemblies)}]\n" +
                  $"  保留额外项：{extra.Count} 个 [{string.Join(", ", extra)}]\n" +
                  $"  最终：{newList.Count} 个");
#else
        Debug.LogWarning("[AOTMetadata] 同步跳过：需启用 ENABLE_HYBRIDCLR 宏定义");
#endif
    }

    [MenuItem("HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath")]
    public static void BuildAndCopyDlls()
    {
#if ENABLE_HYBRIDCLR
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
        CompileDllCommand.CompileDll(target);
        CopyAOTHotUpdateDlls(target);
#endif
    }

    public static void BuildAndCopyDlls(BuildTarget target)
    {
#if ENABLE_HYBRIDCLR
        CompileDllCommand.CompileDll(target);
        CopyAOTHotUpdateDlls(target);
#endif
    }

    public static void CopyAOTHotUpdateDlls(BuildTarget target)
    {
        CopyAOTAssembliesToAssetPath(target);
        CopyHotUpdateAssembliesToAssetPath(target);

#if ENABLE_HYBRIDCLR && ENABLE_OBFUZ
        CompileDllCommand.CompileDll(target);

        string obfuscatedHotUpdateDllPath = PrebuildCommandExt.GetObfuscatedHotUpdateAssemblyOutputPath(target);
        ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath);

        Directory.CreateDirectory(Application.streamingAssetsPath);

        string hotUpdateDllPath = $"{SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)}";
        List<string> obfuscationRelativeAssemblyNames = ObfuzSettings.Instance.assemblySettings.GetObfuscationRelativeAssemblyNames();

        foreach (string assName in SettingsUtil.HotUpdateAssemblyNamesIncludePreserved)
        {
            string srcDir = obfuscationRelativeAssemblyNames.Contains(assName) ? obfuscatedHotUpdateDllPath : hotUpdateDllPath;
            string srcFile = $"{srcDir}/{assName}.dll";
            string dstFile = Application.dataPath +"/"+ TEngine.Settings.UpdateSetting.AssemblyTextAssetPath  + $"/{assName}.dll.bytes";
            if (File.Exists(srcFile))
            {
                File.Copy(srcFile, dstFile, true);
                Debug.Log($"[CompileAndObfuscate] Copy {srcFile} to {dstFile}");
            }
        }
#endif
        
        AssetDatabase.Refresh();
    }

    public static void CopyAOTAssembliesToAssetPath()
    {
        CopyAOTAssembliesToAssetPath(EditorUserBuildSettings.activeBuildTarget);
    }

    public static void CopyAOTAssembliesToAssetPath(BuildTarget target)
    {
#if ENABLE_HYBRIDCLR
        string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
        string aotAssembliesDstDir = Application.dataPath + "/" + TEngine.Settings.UpdateSetting.AssemblyTextAssetPath;
        var resolvedAssemblies = GetResolvedAOTMetaAssemblies();

        Debug.Log($"[AOTMetadata] 开始拷贝AOT补充元数据DLL。Target:{target}, SrcDir:{aotAssembliesSrcDir}, DstDir:{aotAssembliesDstDir}, Count:{resolvedAssemblies.Count}");
        foreach (var dll in resolvedAssemblies)
        {
            string srcDllPath = $"{aotAssembliesSrcDir}/{dll}";
            string dllBytesPath = $"{aotAssembliesDstDir}/{dll}.bytes";
            Debug.Log($"[AOTMetadata] 准备拷贝：{dll}, Src:{srcDllPath}, Dst:{dllBytesPath}");
            if (!File.Exists(srcDllPath))
            {
                string errorMsg =
                    $"[AOTMetadata] 校验失败：AOT 源 DLL 不存在：{srcDllPath}（程序集：{dll}）\n" +
                    $"裁剪后的 AOT DLL 仅在 BuildPlayer 时生成，需先完整构建一次游戏 App 后再打 AssetBundle。\n" +
                    $"若已构建过 App 仍报错，请检查 AOTMetadataManifest.asset 是否配置了不存在的程序集。";
                Debug.LogError(errorMsg);
                throw new BuildFailedException(errorMsg);
            }

            File.Copy(srcDllPath, dllBytesPath, true);
            Debug.Log($"[AOTMetadata] 拷贝完成：{dll}, Size:{new FileInfo(dllBytesPath).Length} bytes");
        }
        Debug.Log("[AOTMetadata] AOT补充元数据DLL拷贝流程结束。");
#endif
    }

#if ENABLE_HYBRIDCLR
    private static List<string> GetResolvedAOTMetaAssemblies()
    {
        var assemblies = new List<string>();
        var manifest = LoadAOTMetadataManifest();
        if (manifest != null && manifest.AOTMetaAssemblies != null && manifest.AOTMetaAssemblies.Count > 0)
        {
            assemblies.AddRange(manifest.AOTMetaAssemblies);
            Debug.Log($"[AOTMetadata] 使用 AOTMetadataManifest 配置列表，Count:{manifest.AOTMetaAssemblies.Count}, List:{string.Join(", ", NormalizeAssemblyList(manifest.AOTMetaAssemblies))}");
        }
        else
        {
            assemblies.AddRange(TEngine.Settings.UpdateSetting.AOTMetaAssemblies);
            Debug.Log($"[AOTMetadata] 使用 UpdateSetting.AOTMetaAssemblies 回退列表，Count:{TEngine.Settings.UpdateSetting.AOTMetaAssemblies.Count}, List:{string.Join(", ", NormalizeAssemblyList(TEngine.Settings.UpdateSetting.AOTMetaAssemblies))}");
        }

        var generatedAssemblies = GetGeneratedPatchedAOTAssemblies();
        Debug.Log($"[AOTMetadata] HybridCLR AOTGenericReferences 生成列表，Count:{generatedAssemblies.Count}, List:{string.Join(", ", generatedAssemblies)}");

        var normalizedAssemblies = NormalizeAssemblyList(assemblies);

        // 【校验点 1】单向校验：manifest 必须包含 HybridCLR 生成的全部程序集，缺失则中断构建
        var missingGeneratedAssemblies = generatedAssemblies
            .Where(assembly => !normalizedAssemblies.Contains(assembly))
            .ToList();
        if (missingGeneratedAssemblies.Count > 0)
        {
            string errorMsg =
                $"[AOTMetadata] 校验失败：AOTMetadataManifest 缺少 HybridCLR 生成的以下补充元数据程序集：\n" +
                $"  {string.Join("\n  ", missingGeneratedAssemblies)}\n" +
                $"缺失会导致运行时 ExecutionEngineException。请运行菜单 HybridCLR → Build → Sync AOT Metadata Manifest 同步后重试。";
            Debug.LogError(errorMsg);
            throw new BuildFailedException(errorMsg);
        }

        // manifest 含额外项（手动补充）时仅警告，不中断
        var extraAssemblies = normalizedAssemblies
            .Where(assembly => !generatedAssemblies.Contains(assembly))
            .ToList();
        if (extraAssemblies.Count > 0)
        {
            Debug.LogWarning($"[AOTMetadata] 注意：AOTMetadataManifest 包含 HybridCLR 未生成的程序集（可能是手动添加，将一并拷贝）：{string.Join(", ", extraAssemblies)}");
        }

        var resolvedAssemblies = NormalizeAssemblyList(assemblies);
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
            Debug.LogWarning($"[AOTMetadata] 未找到 AOTMetadataManifest：{manifestPath}，回退使用 UpdateSetting.AOTMetaAssemblies。");
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
        return $"Assets/{TEngine.Settings.UpdateSetting.AssemblyTextAssetPath}/{AOTMetadataManifest.ManifestAssetName}.asset";
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
        string hotfixAssembliesDstDir = Application.dataPath +"/"+ TEngine.Settings.UpdateSetting.AssemblyTextAssetPath;
        foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
        {
            string dllPath = $"{hotfixDllSrcDir}/{dll}";
            string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
            System.IO.File.Copy(dllPath, dllBytesPath, true);
            Debug.Log($"[拷贝热更新dll代码] copy hotfix dll {dllPath} -> {dllBytesPath}");
        }
#endif
    }
}

/// <summary>
/// 构建失败异常。用于 AOT 元数据校验失败、源 DLL 缺失等场景中断构建流程。
/// </summary>
public class BuildFailedException : System.Exception
{
    public BuildFailedException(string message) : base(message) { }
}