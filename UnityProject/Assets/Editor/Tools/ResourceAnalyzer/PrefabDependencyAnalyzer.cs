using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class PrefabDependencyAnalyzer : EditorWindow
{
    // 用于反向查找的数据结构：资源 -> 被哪些根对象(预制体/资源)引用
    private class ReverseAssetInfo
    {
        public string Path;
        public Object AssetObj;
        public long SizeBytes;
        public float SizeMB => SizeBytes / (1024f * 1024f);
        public List<Object> ReferencedByObjects = new List<Object>();
        public bool IsExpanded = false;
    }

    // 将 DefaultAsset 改为 Object，以支持单个文件或文件夹
    private Object targetObject; 
    private List<ReverseAssetInfo> assetDataList = new List<ReverseAssetInfo>();
    private Vector2 scrollPosition;
    private float minSizeFilterMB = 0f; // 可以过滤掉太小的文件

    [MenuItem("Tools/资源分析器/查资源依赖大小 - 文件或文件夹")]
    public static void ShowWindow()
    {
        var window = GetWindow<PrefabDependencyAnalyzer>("资源依赖大小");
        window.minSize = new Vector2(600, 500);
        window.Show();
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawSeparator();
        DrawAssetList();
    }

    private void DrawHeader()
    {
        GUILayout.Space(10);
        GUILayout.Label("1. 选择要扫描的文件夹 或 单个资源:", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        // 允许拖入任何类型的 Object
        targetObject = EditorGUILayout.ObjectField(targetObject, typeof(Object), false, GUILayout.Width(300));
        GUILayout.EndHorizontal();

        GUILayout.Space(5);
        GUILayout.BeginHorizontal();
        GUILayout.Label("2. 过滤小于该大小的资源 (MB):", GUILayout.Width(180));
        minSizeFilterMB = EditorGUILayout.FloatField(minSizeFilterMB, GUILayout.Width(50));
        
        GUILayout.FlexibleSpace();
        GUI.enabled = targetObject != null;
        if (GUILayout.Button("开始扫描大资源", GUILayout.Width(120), GUILayout.Height(25)))
        {
            AnalyzeReverseDependencies();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (assetDataList.Count > 0)
        {
            GUILayout.Space(5);
            GUILayout.Label($"扫描结果: 找到 {assetDataList.Count} 个符合条件的外部依赖资源。", EditorStyles.helpBox);
        }
    }

    private void DrawSeparator()
    {
        GUILayout.Space(5);
        Rect rect = EditorGUILayout.GetControlRect(false, 2);
        rect.height = 1;
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
        GUILayout.Space(5);
    }

    private void DrawAssetList()
    {
        if (assetDataList.Count == 0)
        {
            GUILayout.Label("请选择目标并点击“开始扫描”。", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        foreach (var assetInfo in assetDataList)
        {
            GUILayout.BeginVertical("box");
            
            // 资源标题行
            GUILayout.BeginHorizontal();
            assetInfo.IsExpanded = EditorGUILayout.Foldout(assetInfo.IsExpanded, "", true);
            EditorGUILayout.ObjectField(assetInfo.AssetObj, typeof(Object), false, GUILayout.Width(250));
            
            // 大资源高亮
            GUIStyle sizeStyle = new GUIStyle(EditorStyles.label);
            if (assetInfo.SizeMB > 10f) sizeStyle.normal.textColor = new Color(1f, 0.4f, 0.4f); // 大于10MB标红
            else if (assetInfo.SizeMB > 2f) sizeStyle.normal.textColor = new Color(1f, 0.7f, 0.2f); // 大于2MB标黄
            
            GUILayout.Label($"[ {assetInfo.SizeMB:F2} MB ]", sizeStyle, GUILayout.Width(100));
            GUILayout.Label($"被 {assetInfo.ReferencedByObjects.Count} 个对象引用", EditorStyles.miniBoldLabel);
            GUILayout.EndHorizontal();

            // 展开引用了该资源的预制体/资源列表
            if (assetInfo.IsExpanded)
            {
                EditorGUI.indentLevel++;
                GUILayout.Label("引用该资源的根对象 (点击定位):", EditorStyles.miniLabel);
                foreach (var refObj in assetInfo.ReferencedByObjects)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(30); // 缩进
                    EditorGUILayout.ObjectField(refObj, typeof(Object), false, GUILayout.Width(250));
                    GUILayout.Label(AssetDatabase.GetAssetPath(refObj), EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
                GUILayout.Space(5);
            }
            GUILayout.EndVertical();
        }

        GUILayout.EndScrollView();
    }

    /*private void AnalyzeReverseDependencies()
    {
        assetDataList.Clear();
        string targetPath = AssetDatabase.GetAssetPath(targetObject);
        
        List<string> rootPathsToAnalyze = new List<string>();

        // 判断选中的是文件夹还是单个文件
        if (AssetDatabase.IsValidFolder(targetPath))
        {
            // 如果是文件夹，找出里面所有的预制体
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { targetPath });
            foreach (string guid in prefabGuids)
            {
                rootPathsToAnalyze.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
        }
        else
        {
            // 如果是单个文件，直接把它加入分析列表
            rootPathsToAnalyze.Add(targetPath);
        }

        Dictionary<string, ReverseAssetInfo> assetDict = new Dictionary<string, ReverseAssetInfo>();

        int count = 0;
        foreach (string rootPath in rootPathsToAnalyze)
        {
            count++;
            EditorUtility.DisplayProgressBar("扫描中", $"正在分析 {Path.GetFileName(rootPath)}...", (float)count / rootPathsToAnalyze.Count);

            Object rootObj = AssetDatabase.LoadAssetAtPath<Object>(rootPath);
            string[] dependencies = AssetDatabase.GetDependencies(rootPath, true);

            foreach (string depPath in dependencies)
            {
                // 排除自己、代码和非 Assets 目录的内置资源
                if (depPath == rootPath || depPath.EndsWith(".cs") || depPath.EndsWith(".dll") || !depPath.StartsWith("Assets/"))
                    continue;

                // 如果字典里还没有这个资源，就添加它并计算大小
                if (!assetDict.ContainsKey(depPath))
                {
                    string fullPath = Path.GetFullPath(depPath);
                    if (File.Exists(fullPath))
                    {
                        long size = new FileInfo(fullPath).Length;
                        float sizeMB = size / (1024f * 1024f);

                        // 过滤掉太小的文件
                        if (sizeMB >= minSizeFilterMB)
                        {
                            assetDict[depPath] = new ReverseAssetInfo
                            {
                                Path = depPath,
                                AssetObj = AssetDatabase.LoadAssetAtPath<Object>(depPath),
                                SizeBytes = size
                            };
                        }
                    }
                }

                // 记录引用关系
                if (assetDict.ContainsKey(depPath) && !assetDict[depPath].ReferencedByObjects.Contains(rootObj))
                {
                    assetDict[depPath].ReferencedByObjects.Add(rootObj);
                }
            }
        }

        // 转换为List并按资源单体大小降序排列
        assetDataList = assetDict.Values.ToList();
        assetDataList.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        
        EditorUtility.ClearProgressBar();
    }*/
    private void AnalyzeReverseDependencies()
    {
        assetDataList.Clear();
        string targetPath = AssetDatabase.GetAssetPath(targetObject);
        
        List<string> rootPathsToAnalyze = new List<string>();

        // 判断选中的是文件夹还是单个文件
        if (AssetDatabase.IsValidFolder(targetPath))
        {
            // 【修改点】：不再局限于 "t:Prefab"，而是查找文件夹下的所有资源
            string[] allGuids = AssetDatabase.FindAssets("", new[] { targetPath });
            foreach (string guid in allGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                
                // 排除子文件夹本身，只把真实的文件加入分析列表
                if (!AssetDatabase.IsValidFolder(path) && !rootPathsToAnalyze.Contains(path))
                {
                    rootPathsToAnalyze.Add(path);
                }
            }
        }
        else
        {
            // 如果是单个文件，直接把它加入分析列表
            rootPathsToAnalyze.Add(targetPath);
        }

        Dictionary<string, ReverseAssetInfo> assetDict = new Dictionary<string, ReverseAssetInfo>();

        int count = 0;
        foreach (string rootPath in rootPathsToAnalyze)
        {
            count++;
            EditorUtility.DisplayProgressBar("扫描中", $"正在分析 {Path.GetFileName(rootPath)}...", (float)count / rootPathsToAnalyze.Count);

            Object rootObj = AssetDatabase.LoadAssetAtPath<Object>(rootPath);
            string[] dependencies = AssetDatabase.GetDependencies(rootPath, true);

            foreach (string depPath in dependencies)
            {
                // 排除自己、代码和非 Assets 目录的内置资源
                if (depPath == rootPath || depPath.EndsWith(".cs") || depPath.EndsWith(".dll") || !depPath.StartsWith("Assets/"))
                    continue;

                // 如果字典里还没有这个资源，就添加它并计算大小
                if (!assetDict.ContainsKey(depPath))
                {
                    string fullPath = Path.GetFullPath(depPath);
                    if (File.Exists(fullPath))
                    {
                        long size = new FileInfo(fullPath).Length;
                        float sizeMB = size / (1024f * 1024f);

                        // 过滤掉太小的文件
                        if (sizeMB >= minSizeFilterMB)
                        {
                            assetDict[depPath] = new ReverseAssetInfo
                            {
                                Path = depPath,
                                AssetObj = AssetDatabase.LoadAssetAtPath<Object>(depPath),
                                SizeBytes = size
                            };
                        }
                    }
                }

                // 记录引用关系
                if (assetDict.ContainsKey(depPath) && !assetDict[depPath].ReferencedByObjects.Contains(rootObj))
                {
                    assetDict[depPath].ReferencedByObjects.Add(rootObj);
                }
            }
        }

        // 转换为List并按资源单体大小降序排列
        assetDataList = assetDict.Values.ToList();
        assetDataList.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        
        EditorUtility.ClearProgressBar();
    }
}
