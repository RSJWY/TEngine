using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TortoiseSVNMenu
/// 功能描述：在 Unity 编辑器中唤起 TortoiseSVN（TortoiseProc.exe）常用命令。
///           Project 窗口有选中资产时对选中项操作（自动带上 .meta），否则对整个工程根目录操作。
/// 创建时间：2026-08-11 17:28
/// 开发者：Administrator
/// 最后修改：
/// 修改内容：
/// </summary>
public static class TortoiseSVNMenu
{
    private const string MenuRoot = "Tools/SVN/";

    /// <summary>TortoiseProc.exe 自定义路径（EditorPrefs，按机器保存）。</summary>
    private const string PrefKeyExePath = "DroneSimulation.TortoiseProcPath";

    /// <summary>常见安装位置，找不到时用菜单里的"设置 TortoiseProc 路径"手动指定。</summary>
    private static readonly string[] FallbackExePaths =
    {
        @"D:\Program Files\TortoiseSVN\bin\TortoiseProc.exe",
        @"C:\Program Files\TortoiseSVN\bin\TortoiseProc.exe",
        @"C:\Program Files (x86)\TortoiseSVN\bin\TortoiseProc.exe",
    };

    [MenuItem(MenuRoot + "更新 Update", false, 1)]
    private static void Update()
    {
        SaveDirtyBeforeSvn();
        RunCommand("update");
    }

    [MenuItem(MenuRoot + "提交 Commit", false, 2)]
    private static void Commit()
    {
        SaveDirtyBeforeSvn();
        RunCommand("commit");
    }

    [MenuItem(MenuRoot + "查看日志 Log", false, 3)]
    private static void Log() => RunCommand("log");

    [MenuItem(MenuRoot + "对比差异 Diff", false, 4)]
    private static void Diff() => RunCommand("diff");

    [MenuItem(MenuRoot + "还原 Revert", false, 5)]
    private static void Revert() => RunCommand("revert");

    [MenuItem(MenuRoot + "加入版本控制 Add", false, 20)]
    private static void Add() => RunCommand("add");

    [MenuItem(MenuRoot + "检查修改 Check Modifications", false, 21)]
    private static void CheckModifications() => RunCommand("repostatus");

    [MenuItem(MenuRoot + "版本库浏览器 Repo Browser", false, 22)]
    private static void RepoBrowser() => RunCommand("repobrowser");

    [MenuItem(MenuRoot + "清理 Cleanup", false, 23)]
    private static void Cleanup() => RunCommand("cleanup");

    [MenuItem(MenuRoot + "显示锁定/解锁 Lock", false, 24)]
    private static void Lock() => RunCommand("lock");

    [MenuItem(MenuRoot + "编辑冲突 Edit Conflicts", false, 30)]
    private static void EditConflicts() => RunCommand("conflicteditor");

    [MenuItem(MenuRoot + "标记已解决 Resolved", false, 31)]
    private static void Resolved() => RunCommand("resolve");

    [MenuItem(MenuRoot + "合并 Merge", false, 32)]
    private static void Merge() => RunCommand("merge");

    [MenuItem(MenuRoot + "TortoiseSVN 设置", false, 40)]
    private static void Settings() => RunCommand("settings", useTargets: false);

    [MenuItem(MenuRoot + "设置 TortoiseProc 路径...", false, 41)]
    private static void PickExePath()
    {
        string current = EditorPrefs.GetString(PrefKeyExePath, ResolveExePath() ?? string.Empty);
        string startDir = string.IsNullOrEmpty(current) ? @"C:\" : Path.GetDirectoryName(current);
        string picked = EditorUtility.OpenFilePanel("选择 TortoiseProc.exe", startDir, "exe");
        if (string.IsNullOrEmpty(picked))
        {
            return;
        }

        picked = picked.Replace('/', '\\');
        EditorPrefs.SetString(PrefKeyExePath, picked);
        UnityEngine.Debug.Log($"[SVN] TortoiseProc 路径已设置为：{picked}");
    }

    [MenuItem(MenuRoot + "在资源管理器中打开选中项", false, 42)]
    private static void RevealSelection()
    {
        List<string> targets = GetTargetPaths(includeMeta: false);
        EditorUtility.RevealInFinder(targets.Count > 0 ? targets[0] : GetProjectRoot());
    }

    /// <summary>
    /// 调起 TortoiseProc。多个路径用 '*' 分隔，这是 TortoiseSVN 命令行的约定写法。
    /// </summary>
    private static void RunCommand(string command, bool useTargets = true)
    {
        string exe = ResolveExePath();
        if (string.IsNullOrEmpty(exe))
        {
            EditorUtility.DisplayDialog("找不到 TortoiseSVN",
                "未找到 TortoiseProc.exe。\n\n请先安装 TortoiseSVN（勾选 command line client tools），" +
                "或用菜单 Tools/SVN/设置 TortoiseProc 路径... 手动指定。", "好");
            return;
        }

        string args = $"/command:{command} /closeonend:0";
        if (useTargets)
        {
            List<string> targets = GetTargetPaths(includeMeta: true);
            if (targets.Count == 0)
            {
                targets.Add(GetProjectRoot());
            }

            args = $"/command:{command} /path:\"{string.Join("*", targets)}\" /closeonend:0";
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[SVN] 调起 TortoiseProc 失败：{e.Message}\n{exe} {args}");
        }
    }

    /// <summary>
    /// 取当前 Project 窗口选中资产的绝对路径；includeMeta 时把同名 .meta 一起带上，
    /// 否则容易出现资产已提交但 meta 没提交（GUID 丢失）的情况。
    /// </summary>
    private static List<string> GetTargetPaths(bool includeMeta)
    {
        List<string> result = new List<string>();
        string root = GetProjectRoot();

        foreach (Object obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            string relative = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(relative))
            {
                continue;
            }

            string absolute = Path.GetFullPath(Path.Combine(root, relative));
            if (!result.Contains(absolute))
            {
                result.Add(absolute);
            }

            if (!includeMeta)
            {
                continue;
            }

            string meta = absolute + ".meta";
            if (File.Exists(meta) && !result.Contains(meta))
            {
                result.Add(meta);
            }
        }

        return result;
    }

    /// <summary>工程根目录（Assets 的上一级，也就是 .svn 所在目录）。</summary>
    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string ResolveExePath()
    {
        string saved = EditorPrefs.GetString(PrefKeyExePath, string.Empty);
        if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
        {
            return saved;
        }

        foreach (string path in FallbackExePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>提交/更新前落盘，避免内存里的场景和资产改动没写进文件。</summary>
    private static void SaveDirtyBeforeSvn()
    {
        UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        AssetDatabase.SaveAssets();
    }
}

