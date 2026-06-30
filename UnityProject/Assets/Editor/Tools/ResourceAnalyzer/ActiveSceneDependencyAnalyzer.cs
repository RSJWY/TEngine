using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class ActiveSceneDependencyAnalyzer : EditorWindow
{
    // 数据结构：资源 -> 被场景中的哪些 GameObject 引用
    private class ReverseSceneAssetInfo
    {
        public string Path;
        public Object AssetObj;
        public long SizeBytes;
        public float SizeMB => SizeBytes / (1024f * 1024f);
        public List<GameObject> ReferencedByGameObjects = new List<GameObject>();
        public bool IsExpanded = false;
    }

    private List<ReverseSceneAssetInfo> assetDataList = new List<ReverseSceneAssetInfo>();
    private Vector2 scrollPosition;
    private float minSizeFilterMB = 1f; // 默认过滤掉1MB以下的资源

    [MenuItem("Tools/资源分析器/查当前场景大资源 - 哪些物体在用")]
    public static void ShowWindow()
    {
        var window = GetWindow<ActiveSceneDependencyAnalyzer>("当前场景大资源");
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
        Scene activeScene = SceneManager.GetActiveScene();
        GUILayout.Label($"当前分析场景: {activeScene.name} {(activeScene.isDirty ? "(未保存)" : "")}", EditorStyles.boldLabel);
        
        GUILayout.Space(5);
        GUILayout.BeginHorizontal();
        GUILayout.Label("过滤小于该大小的资源 (MB):", GUILayout.Width(180));
        minSizeFilterMB = EditorGUILayout.FloatField(minSizeFilterMB, GUILayout.Width(50));
        
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("扫描当前场景", GUILayout.Width(120), GUILayout.Height(25)))
        {
            AnalyzeActiveScene();
        }
        GUILayout.EndHorizontal();

        if (assetDataList.Count > 0)
        {
            GUILayout.Space(5);
            GUILayout.Label($"扫描结果: 场景中找到了 {assetDataList.Count} 个符合条件的外部依赖资源。", EditorStyles.helpBox);
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
            GUILayout.Label("点击“扫描当前场景”开始分析。", EditorStyles.centeredGreyMiniLabel);
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
            GUILayout.Label($"被场景中 {assetInfo.ReferencedByGameObjects.Count} 个物体引用", EditorStyles.miniBoldLabel);
            GUILayout.EndHorizontal();

            // 展开引用了该资源的场景物体列表
            if (assetInfo.IsExpanded)
            {
                EditorGUI.indentLevel++;
                GUILayout.Label("场景中直接引用该资源的 GameObject (点击可在 Hierarchy 中高亮):", EditorStyles.miniLabel);
                foreach (var go in assetInfo.ReferencedByGameObjects)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(30); // 缩进
                    EditorGUILayout.ObjectField(go, typeof(GameObject), true, GUILayout.Width(250));
                    
                    // 显示该物体在场景中的层级路径，方便查找
                    GUILayout.Label(GetGameObjectPath(go), EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
                GUILayout.Space(5);
            }
            GUILayout.EndVertical();
        }

        GUILayout.EndScrollView();
    }

    private void AnalyzeActiveScene()
    {
        assetDataList.Clear();
        Scene activeScene = SceneManager.GetActiveScene();

        // 获取当前场景中的所有 GameObject（包括隐藏的物体）
        GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(go => go.scene == activeScene).ToArray();

        Dictionary<string, ReverseSceneAssetInfo> assetDict = new Dictionary<string, ReverseSceneAssetInfo>();

        int count = 0;
        foreach (GameObject go in allGameObjects)
        {
            count++;
            // 每分析20个物体刷新一次进度条，避免频繁刷新导致界面卡顿
            if (count % 20 == 0)
            {
                EditorUtility.DisplayProgressBar("扫描场景中", $"正在分析物体: {go.name}...", (float)count / allGameObjects.Length);
            }

            // 获取该 GameObject 及其组件上引用的所有资产
            Object[] dependencies = EditorUtility.CollectDependencies(new Object[] { go });

            foreach (Object dep in dependencies)
            {
                if (dep == null) continue;

                string depPath = AssetDatabase.GetAssetPath(dep);

                // 排除：空路径（可能是内存生成的网格/材质）、脚本、以及非 Assets 目录下的 Unity 内置资源
                if (string.IsNullOrEmpty(depPath) || 
                    depPath.EndsWith(".cs") || 
                    depPath.EndsWith(".dll") || 
                    !depPath.StartsWith("Assets/"))
                    continue;

                // 如果字典里还没有这个资源，添加并计算磁盘大小
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
                            assetDict[depPath] = new ReverseSceneAssetInfo
                            {
                                Path = depPath,
                                AssetObj = AssetDatabase.LoadAssetAtPath<Object>(depPath),
                                SizeBytes = size
                            };
                        }
                    }
                }

                // 将当前的场景 GameObject 绑定到该资源上
                if (assetDict.ContainsKey(depPath) && !assetDict[depPath].ReferencedByGameObjects.Contains(go))
                {
                    assetDict[depPath].ReferencedByGameObjects.Add(go);
                }
            }
        }

        // 转换为List并按资源单体大小降序排列
        assetDataList = assetDict.Values.ToList();
        assetDataList.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        
        EditorUtility.ClearProgressBar();
    }

    // 辅助方法：获取 GameObject 在 Hierarchy 中的完整路径
    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform current = obj.transform;
        while (current.parent != null)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }
        return path;
    }
}
