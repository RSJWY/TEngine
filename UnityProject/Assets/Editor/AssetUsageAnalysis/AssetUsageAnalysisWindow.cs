using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AssetUsageAnalysisWindow
/// 功能描述：
/// 创建时间：2026-08-21 14:17
/// 开发者：Administrator
/// 最后修改：
/// 修改内容：
/// </summary>
using YooAsset.Editor;

/// <summary>
/// AB包资产使用分析工具。
/// 读取 YooAsset 构建报告（.report），与 Assets/AssetRaw 下的文件对比，
/// 列出已使用 / 未使用的资产，并可把分析结果保存为 AssetUsageReportData 资产以便提交跟踪。
/// （清理功能暂未实现）
/// </summary>
public class AssetUsageAnalysisWindow : EditorWindow
{
    /// <summary>热更资产扫描根目录</summary>
    private const string AssetRawRoot = "Assets/AssetRaw";
    /// <summary>分析结果资产保存目录</summary>
    private const string ReportSaveFolder = "Assets/AssetUsageReports";
    /// <summary>分页每页条数选项</summary>
    private static readonly string[] PageSizeLabels = { "100", "300", "500", "1000" };
    private static readonly int[] PageSizeValues = { 100, 300, 500, 1000 };

    private string _reportPath;
    private AssetUsageReportData _result;
    private int _tab; // 0=未使用 1=已使用
    private string _search = string.Empty;
    private int _page;
    private int _pageSize = 300;
    private Vector2 _scroll;
    private GUIStyle _pathStyle;

    [MenuItem("Tools/YooAssets/打包報告资产使用分析")]
    private static void Open()
    {
        var window = GetWindow<AssetUsageAnalysisWindow>("YooAssets-打包報告资产使用分析");
        window.minSize = new Vector2(700, 400);
        window.Show();
    }

    private void OnEnable()
    {
        if (string.IsNullOrEmpty(_reportPath))
            _reportPath = FindLatestReport();
    }

    /// <summary>
    /// 自动查找 Bundles 输出目录下最新的构建报告
    /// </summary>
    private static string FindLatestReport()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Bundles"));
        if (!Directory.Exists(root))
            return string.Empty;

        string newest = null;
        DateTime newestTime = DateTime.MinValue;
        foreach (string file in Directory.GetFiles(root, "*.report", SearchOption.AllDirectories))
        {
            DateTime time = File.GetLastWriteTime(file);
            if (time > newestTime)
            {
                newestTime = time;
                newest = file;
            }
        }
        return newest ?? string.Empty;
    }

    private void OnGUI()
    {
        if (_pathStyle == null)
            _pathStyle = new GUIStyle(EditorStyles.label) { richText = false };

        DrawReportPicker();

        if (_result != null)
        {
            DrawSummary();
            DrawToolbar();
            DrawList();
        }
    }

    private void DrawReportPicker()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("构建报告 (.report)");
        EditorGUILayout.SelectableLabel(_reportPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        if (GUILayout.Button("浏览", GUILayout.Width(50)))
        {
            string selected = EditorUtility.OpenFilePanel("选择 YooAsset 构建报告", Path.GetFullPath(Path.Combine(Application.dataPath, "../Bundles")), "report");
            if (!string.IsNullOrEmpty(selected))
                _reportPath = selected;
        }
        if (GUILayout.Button("分析", GUILayout.Width(50)))
            Analyze();
        EditorGUILayout.EndHorizontal();

        // 加载已保存的分析结果资产
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("已保存的分析结果");
        var loaded = (AssetUsageReportData)EditorGUILayout.ObjectField(null, typeof(AssetUsageReportData), false);
        if (loaded != null)
        {
            _result = loaded;
            _page = 0;
            _scroll = Vector2.zero;
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawSummary()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"包名：{_result.PackageName}    版本：{_result.PackageVersion}    构建时间：{_result.BuildDate}");
        EditorGUILayout.LabelField($"已使用：{_result.UsedCount} 个（{FormatSize(_result.UsedTotalSize)}）    未使用：{_result.UnusedCount} 个（{FormatSize(_result.UnusedTotalSize)}）");

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(_result.UnusedCount == 0 && _result.UsedCount == 0);
        if (GUILayout.Button("保存为资产（提交跟踪）", GUILayout.Width(200)))
            SaveAsAsset();
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        string[] tabs = { $"未使用 ({_result.UnusedCount})", $"已使用 ({_result.UsedCount})" };
        int newTab = GUILayout.Toolbar(_tab, tabs, EditorStyles.toolbarButton, GUILayout.Width(220));
        if (newTab != _tab)
        {
            _tab = newTab;
            _page = 0;
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label("搜索:", GUILayout.Width(35));
        string newSearch = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(220));
        if (newSearch != _search)
        {
            _search = newSearch;
            _page = 0;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawList()
    {
        List<AssetUsageReportData.AssetEntry> source = _tab == 0 ? _result.UnusedAssets : _result.UsedAssets;

        IEnumerable<AssetUsageReportData.AssetEntry> filtered = source;
        if (!string.IsNullOrEmpty(_search))
            filtered = filtered.Where(e => e.AssetPath.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        List<AssetUsageReportData.AssetEntry> display = filtered.ToList();

        // 分页
        int totalCount = display.Count;
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)_pageSize));
        _page = Mathf.Clamp(_page, 0, pageCount - 1);
        int start = _page * _pageSize;
        int end = Mathf.Min(start + _pageSize, totalCount);

        DrawPager(totalCount, pageCount);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        for (int i = start; i < end; i++)
            DrawEntry(display[i]);
        EditorGUILayout.EndScrollView();
    }

    private void DrawPager(int totalCount, int pageCount)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUI.BeginDisabledGroup(_page <= 0);
        if (GUILayout.Button("首页", EditorStyles.toolbarButton, GUILayout.Width(40)))
            _page = 0;
        if (GUILayout.Button("上一页", EditorStyles.toolbarButton, GUILayout.Width(50)))
            _page--;
        EditorGUI.EndDisabledGroup();

        GUILayout.Label($"第 {_page + 1} / {pageCount} 页（共 {totalCount} 条）", EditorStyles.miniLabel, GUILayout.Width(160));

        EditorGUI.BeginDisabledGroup(_page >= pageCount - 1);
        if (GUILayout.Button("下一页", EditorStyles.toolbarButton, GUILayout.Width(50)))
            _page++;
        if (GUILayout.Button("末页", EditorStyles.toolbarButton, GUILayout.Width(40)))
            _page = pageCount - 1;
        EditorGUI.EndDisabledGroup();

        GUILayout.FlexibleSpace();
        GUILayout.Label("每页:", GUILayout.Width(35));
        int newPageSize = EditorGUILayout.IntPopup(_pageSize, PageSizeLabels, PageSizeValues, EditorStyles.toolbarPopup, GUILayout.Width(60));
        if (newPageSize != _pageSize)
        {
            _pageSize = newPageSize;
            _page = 0;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawEntry(AssetUsageReportData.AssetEntry entry)
    {
        EditorGUILayout.BeginHorizontal("box");

        string label = _tab == 1 && entry.IsMainAsset ? $"[主] {entry.AssetPath}" : entry.AssetPath;
        // 双击定位到 Project 窗口
        if (GUILayout.Button(label, _pathStyle))
        {
            if (Event.current.clickCount == 2)
                PingAsset(entry.AssetPath);
        }
        GUILayout.Label(FormatSize(entry.FileSize), GUILayout.Width(80));
        if (GUILayout.Button("定位", GUILayout.Width(40)))
            PingAsset(entry.AssetPath);

        EditorGUILayout.EndHorizontal();
    }

    private static void PingAsset(string assetPath)
    {
        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (obj != null)
            EditorGUIUtility.PingObject(obj);
    }

    /// <summary>
    /// 解析报告并与 AssetRaw 磁盘文件对比
    /// </summary>
    private void Analyze()
    {
        if (string.IsNullOrEmpty(_reportPath) || !File.Exists(_reportPath))
        {
            EditorUtility.DisplayDialog("错误", "请选择有效的 .report 构建报告文件。", "确定");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("AB资产使用分析", "解析构建报告...", 0.2f);
            BuildReport report = BuildReport.Deserialize(File.ReadAllText(_reportPath));

            // 已使用集合：主资产 + 依赖资产
            var usedMap = new Dictionary<string, AssetUsageReportData.AssetEntry>(report.Summary.AssetFileTotalCount);
            foreach (var assetInfo in report.AssetInfos)
            {
                AddUsed(usedMap, assetInfo.AssetPath, assetInfo.AssetGUID, true);
                foreach (var depend in assetInfo.DependAssets)
                    AddUsed(usedMap, depend.AssetPath, depend.AssetGUID, false);
            }

            // 未使用集合：AssetRaw 下存在但不在已使用集合中
            EditorUtility.DisplayProgressBar("AB资产使用分析", "扫描 AssetRaw 目录...", 0.6f);
            var unusedList = new List<AssetUsageReportData.AssetEntry>();
            foreach (string file in Directory.GetFiles(AssetRawRoot, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                string assetPath = file.Replace('\\', '/');
                if (usedMap.ContainsKey(assetPath))
                    continue;

                var entry = new AssetUsageReportData.AssetEntry
                {
                    AssetPath = assetPath,
                    AssetGUID = AssetDatabase.AssetPathToGUID(assetPath),
                    FileSize = new FileInfo(file).Length,
                };
                unusedList.Add(entry);
            }

            // 未使用按大小降序，方便优先关注大文件
            unusedList.Sort((a, b) => b.FileSize.CompareTo(a.FileSize));
            var usedList = usedMap.Values.OrderBy(e => e.AssetPath, StringComparer.Ordinal).ToList();

            _result = CreateInstance<AssetUsageReportData>();
            _result.ReportFilePath = _reportPath;
            _result.PackageName = report.Summary.BuildPackageName;
            _result.PackageVersion = report.Summary.BuildPackageVersion;
            _result.BuildDate = report.Summary.BuildDate;
            _result.AnalyzeDate = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            _result.UsedAssets = usedList;
            _result.UnusedAssets = unusedList;
            _result.UsedCount = usedList.Count;
            _result.UsedTotalSize = usedList.Sum(e => e.FileSize);
            _result.UnusedCount = unusedList.Count;
            _result.UnusedTotalSize = unusedList.Sum(e => e.FileSize);

            _page = 0;
            _scroll = Vector2.zero;

            Debug.Log($"[AB资产使用分析] 完成：已使用 {_result.UsedCount}，未使用 {_result.UnusedCount}（{FormatSize(_result.UnusedTotalSize)}）");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AB资产使用分析] 分析失败：{e}");
            EditorUtility.DisplayDialog("分析失败", e.Message, "确定");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void AddUsed(Dictionary<string, AssetUsageReportData.AssetEntry> usedMap, string assetPath, string guid, bool isMain)
    {
        if (string.IsNullOrEmpty(assetPath))
            return;
        if (usedMap.TryGetValue(assetPath, out var existing))
        {
            existing.IsMainAsset |= isMain;
            return;
        }

        long size = 0;
        if (File.Exists(assetPath))
            size = new FileInfo(assetPath).Length;

        usedMap.Add(assetPath, new AssetUsageReportData.AssetEntry
        {
            AssetPath = assetPath,
            AssetGUID = guid,
            FileSize = size,
            IsMainAsset = isMain,
        });
    }

    /// <summary>
    /// 保存分析结果为 .asset，同名覆盖，便于 SVN 提交跟踪
    /// </summary>
    private void SaveAsAsset()
    {
        if (!AssetDatabase.IsValidFolder(ReportSaveFolder))
            AssetDatabase.CreateFolder("Assets", "AssetUsageReports");

        string assetPath = $"{ReportSaveFolder}/AssetUsageReport_{_result.PackageVersion}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<AssetUsageReportData>(assetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(_result, existing);
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(_result, assetPath);
        }
        AssetDatabase.SaveAssets();

        Debug.Log($"[AB资产使用分析] 结果已保存：{assetPath}");
        PingAsset(assetPath);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (float)(1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{bytes / (float)(1L << 20):F2} MB";
        if (bytes >= 1L << 10) return $"{bytes / (float)(1L << 10):F1} KB";
        return $"{bytes} B";
    }
}
