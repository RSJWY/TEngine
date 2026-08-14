using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class GlobalReferenceFinder : EditorWindow
{
    private List<Object> targetAssets = new List<Object>();

    private class PrefabRefResult
    {
        public GameObject Prefab;
        public List<string> HitTargetPaths = new List<string>();
        public bool IsExpanded;
    }
    private class SceneFileRefResult
    {
        public SceneAsset SceneAsset;
        public List<string> HitTargetPaths = new List<string>();
        public bool IsExpanded;
    }
    private class SceneObjRefResult
    {
        public string GameObjectName;
        public string GameObjectPath;
        public List<Object> HitTargetObjects = new List<Object>();
        public bool IsExpanded;
    }

    private List<PrefabRefResult> referencingPrefabs = new List<PrefabRefResult>();
    private List<SceneFileRefResult> referencingSceneFiles = new List<SceneFileRefResult>();
    private List<SceneObjRefResult> referencingSceneObjects = new List<SceneObjRefResult>();

    private HashSet<string> targetPaths = new HashSet<string>();
    private List<Object> targetObjectsForScene = new List<Object>();
    private bool hasSearched;
    private int lastSearchedSceneHandle = -1;

    private static readonly Dictionary<string, DepCacheEntry> _depCache = new Dictionary<string, DepCacheEntry>();

    private struct DepCacheEntry
    {
        public string[] Dependencies;
        public string LastWriteTime;
    }

    private static string[] GetCachedDependencies(string assetPath)
    {
        string writeTime = File.GetLastWriteTime(assetPath).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_depCache.TryGetValue(assetPath, out DepCacheEntry entry) && entry.LastWriteTime == writeTime)
            return entry.Dependencies;

        string[] deps = AssetDatabase.GetDependencies(assetPath, true);
        _depCache[assetPath] = new DepCacheEntry { Dependencies = deps, LastWriteTime = writeTime };
        return deps;
    }

    private Vector2 prefabScroll;
    private Vector2 sceneFileScroll;
    private Vector2 sceneScroll;
    private Vector2 targetListScroll;

    private bool showPrefabs = true;
    private bool showSceneFiles = true;
    private bool showSceneObjects = true;

    [MenuItem("Tools/资源分析器/全局反向引用查找器 (全盘排查)")]
    public static void ShowWindow()
    {
        var window = GetWindow<GlobalReferenceFinder>("反向引用查找");
        window.minSize = new Vector2(520, 620);
        window.Show();
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawSeparator();

        Scene activeScene = SceneManager.GetActiveScene();
        if (hasSearched && lastSearchedSceneHandle != -1 && lastSearchedSceneHandle != activeScene.handle)
        {
            referencingSceneObjects.Clear();
            GUILayout.Space(10);
            EditorGUILayout.HelpBox($"当前场景已切换为 [{activeScene.name}]，上次扫描的当前场景内引用结果已失效，请重新扫描。", MessageType.Warning);
            if (GUILayout.Button("重新扫描当前场景"))
                FindInActiveScene();
            GUILayout.Space(5);
        }

        if (referencingPrefabs.Count > 0 || referencingSceneFiles.Count > 0 || referencingSceneObjects.Count > 0)
        {
            DrawResults();
        }
        else if (targetAssets.Count > 0 && hasSearched)
        {
            GUILayout.Space(10);
            GUILayout.Label("没有找到任何预制体、场景文件或当前场景物体引用这些目标资源。", EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void DrawHeader()
    {
        GUILayout.Space(10);
        GUILayout.Label("请添加要排查的目标资源 (支持文件夹/多选/拖拽):", EditorStyles.boldLabel);

        if (targetAssets.Count > 0)
        {
            targetListScroll = GUILayout.BeginScrollView(targetListScroll, GUILayout.MaxHeight(120));
            for (int i = 0; i < targetAssets.Count; i++)
            {
                GUILayout.BeginHorizontal("box");
                EditorGUILayout.ObjectField(targetAssets[i], typeof(Object), false, GUILayout.Width(260));
                GUILayout.Label(AssetDatabase.GetAssetPath(targetAssets[i]), EditorStyles.miniLabel);
                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    targetAssets.RemoveAt(i);
                    i--;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        GUILayout.BeginHorizontal();
        Object newObj = EditorGUILayout.ObjectField(null, typeof(Object), false, GUILayout.Height(20));
        if (newObj != null && !targetAssets.Contains(newObj))
        {
            targetAssets.Add(newObj);
        }
        if (GUILayout.Button("从选中项载入", GUILayout.Width(100), GUILayout.Height(20)))
        {
            foreach (Object sel in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(sel);
                if (!string.IsNullOrEmpty(p) && !targetAssets.Contains(sel))
                    targetAssets.Add(sel);
            }
        }
        if (GUILayout.Button("清空", GUILayout.Width(60), GUILayout.Height(20)))
        {
            targetAssets.Clear();
        }
        GUILayout.EndHorizontal();

        Rect dragRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
        GUI.Box(dragRect, "将资源或文件夹拖拽到此处添加 (支持多选)", GUI.skin.GetStyle("HelpBox"));
        Event evt = Event.current;
        if (evt.type == EventType.DragUpdated && dragRect.Contains(evt.mousePosition))
        {
            if (DragAndDrop.objectReferences.Any(o => AssetDatabase.Contains(o)))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform && dragRect.Contains(evt.mousePosition))
        {
            DragAndDrop.AcceptDrag();
            foreach (Object dragged in DragAndDrop.objectReferences)
            {
                if (AssetDatabase.Contains(dragged) && !targetAssets.Contains(dragged))
                    targetAssets.Add(dragged);
            }
            evt.Use();
        }

        GUILayout.Space(5);
        GUILayout.BeginHorizontal();
        GUI.enabled = targetAssets.Count > 0;
        if (GUILayout.Button("开始全盘查找", GUILayout.Height(24)))
        {
            FindReferences();
        }
        GUI.enabled = true;
        if (GUILayout.Button("清空依赖缓存", GUILayout.Width(110), GUILayout.Height(24)))
        {
            _depCache.Clear();
            Debug.Log("[全局反向引用查找] 依赖缓存已清空");
        }
        GUILayout.EndHorizontal();
    }

    private void DrawSeparator()
    {
        GUILayout.Space(10);
        Rect rect = EditorGUILayout.GetControlRect(false, 2);
        rect.height = 1;
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
        GUILayout.Space(5);
    }

    private void DrawResults()
    {
        GUILayout.Label($"目标资源: {targetPaths.Count} 个 | 预制体 {referencingPrefabs.Count}、场景文件 {referencingSceneFiles.Count}、当前场景物体 {referencingSceneObjects.Count}",
            EditorStyles.boldLabel);

        if (referencingPrefabs.Count > 0)
        {
            showPrefabs = EditorGUILayout.Foldout(showPrefabs, $"项目预制体引用 ({referencingPrefabs.Count})", true, EditorStyles.foldoutHeader);
            if (showPrefabs)
            {
                prefabScroll = GUILayout.BeginScrollView(prefabScroll, GUILayout.MaxHeight(220));
                foreach (var r in referencingPrefabs)
                {
                    GUILayout.BeginHorizontal("box");
                    EditorGUILayout.ObjectField(r.Prefab, typeof(GameObject), false, GUILayout.Width(240));
                    GUILayout.Label(AssetDatabase.GetAssetPath(r.Prefab), EditorStyles.miniLabel);
                    GUILayout.Label($"命中 {r.HitTargetPaths.Count}", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                    r.IsExpanded = GUILayout.Toggle(r.IsExpanded, r.IsExpanded ? "\u25BC" : "\u25B6", EditorStyles.miniButton, GUILayout.Width(24));
                    GUILayout.EndHorizontal();
                    if (r.IsExpanded) DrawHitPaths(r.HitTargetPaths);
                }
                GUILayout.EndScrollView();
            }
            GUILayout.Space(8);
        }

        if (referencingSceneFiles.Count > 0)
        {
            showSceneFiles = EditorGUILayout.Foldout(showSceneFiles, $"项目场景文件引用 ({referencingSceneFiles.Count})", true, EditorStyles.foldoutHeader);
            if (showSceneFiles)
            {
                sceneFileScroll = GUILayout.BeginScrollView(sceneFileScroll, GUILayout.MaxHeight(180));
                foreach (var r in referencingSceneFiles)
                {
                    GUILayout.BeginHorizontal("box");
                    EditorGUILayout.ObjectField(r.SceneAsset, typeof(SceneAsset), false, GUILayout.Width(240));
                    GUILayout.Label(AssetDatabase.GetAssetPath(r.SceneAsset), EditorStyles.miniLabel);
                    GUILayout.Label($"命中 {r.HitTargetPaths.Count}", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                    r.IsExpanded = GUILayout.Toggle(r.IsExpanded, r.IsExpanded ? "\u25BC" : "\u25B6", EditorStyles.miniButton, GUILayout.Width(24));
                    GUILayout.EndHorizontal();
                    if (r.IsExpanded) DrawHitPaths(r.HitTargetPaths);
                }
                GUILayout.EndScrollView();
            }
            GUILayout.Space(8);
        }

        if (referencingSceneObjects.Count > 0)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            showSceneObjects = EditorGUILayout.Foldout(showSceneObjects, $"当前已打开场景 [{activeScene.name}] 内部引用 ({referencingSceneObjects.Count})", true, EditorStyles.foldoutHeader);
            if (showSceneObjects)
            {
                sceneScroll = GUILayout.BeginScrollView(sceneScroll);
                foreach (var r in referencingSceneObjects)
                {
                    GUILayout.BeginHorizontal("box");
                    GUILayout.Label(r.GameObjectName ?? "<missing>", EditorStyles.objectField, GUILayout.Width(240));
                    GUILayout.Label(string.IsNullOrEmpty(r.GameObjectPath) ? r.GameObjectName : r.GameObjectPath, EditorStyles.miniLabel);
                    GUILayout.Label($"命中 {r.HitTargetObjects.Count}", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                    r.IsExpanded = GUILayout.Toggle(r.IsExpanded, r.IsExpanded ? "\u25BC" : "\u25B6", EditorStyles.miniButton, GUILayout.Width(24));
                    GUILayout.EndHorizontal();
                    if (r.IsExpanded) DrawHitObjects(r.HitTargetObjects);
                }
                GUILayout.EndScrollView();
            }
        }
    }

    private void DrawHitPaths(List<string> paths)
    {
        EditorGUI.indentLevel++;
        foreach (var tp in paths)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(30);
            EditorGUILayout.ObjectField(AssetDatabase.LoadAssetAtPath<Object>(tp), typeof(Object), false, GUILayout.Width(240));
            GUILayout.Label(tp, EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }
        EditorGUI.indentLevel--;
    }

    private void DrawHitObjects(List<Object> objs)
    {
        EditorGUI.indentLevel++;
        objs.RemoveAll(o => o == null || o.Equals(null));
        foreach (var to in objs)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(30);
            EditorGUILayout.ObjectField(to, typeof(Object), false, GUILayout.Width(240));
            GUILayout.Label(AssetDatabase.GetAssetPath(to), EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }
        EditorGUI.indentLevel--;
    }

    private void FindReferences()
    {
        referencingPrefabs.Clear();
        referencingSceneFiles.Clear();
        referencingSceneObjects.Clear();
        targetPaths.Clear();
        targetObjectsForScene.Clear();
        hasSearched = true;

        if (targetAssets.Count == 0)
        {
            Debug.LogWarning("请先添加目标资源。");
            return;
        }

        foreach (Object obj in targetAssets)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;

            if (Directory.Exists(path))
            {
                string[] folder = { path };
                string[] guids = AssetDatabase.FindAssets(null, folder);
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (Directory.Exists(assetPath)) continue;
                    if (targetPaths.Add(assetPath))
                    {
                        Object loaded = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                        if (loaded != null) targetObjectsForScene.Add(loaded);
                    }
                }
            }
            else
            {
                if (targetPaths.Add(path))
                {
                    targetObjectsForScene.Add(obj);
                }
            }
        }

        if (targetPaths.Count == 0)
        {
            Debug.LogWarning("目标资源中没有有效的工程资产。");
            return;
        }

        FindInPrefabs();
        FindInAllSceneFiles();
        FindInActiveScene();

        lastSearchedSceneHandle = SceneManager.GetActiveScene().handle;
        EditorUtility.ClearProgressBar();
        Debug.Log($"[全局反向引用查找] 完成: 目标 {targetPaths.Count} 个 | 预制体命中 {referencingPrefabs.Count} | 场景文件命中 {referencingSceneFiles.Count} | 当前场景物体命中 {referencingSceneObjects.Count}");
    }

    private void FindInPrefabs()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        int count = 0;

        foreach (string guid in prefabGuids)
        {
            count++;
            if (count % 50 == 0)
                EditorUtility.DisplayProgressBar("扫描工程预制体", $"正在扫描... {count}/{prefabGuids.Length}", (float)count / prefabGuids.Length);

            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            string[] dependencies = GetCachedDependencies(prefabPath);

            var hits = new List<string>();
            foreach (string dep in dependencies)
            {
                if (targetPaths.Contains(dep) && !hits.Contains(dep))
                    hits.Add(dep);
            }

            if (hits.Count > 0)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                    referencingPrefabs.Add(new PrefabRefResult { Prefab = prefab, HitTargetPaths = hits });
            }
        }
    }

    private void FindInAllSceneFiles()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
        int count = 0;

        foreach (string guid in sceneGuids)
        {
            count++;
            if (count % 10 == 0)
                EditorUtility.DisplayProgressBar("扫描项目场景文件", $"正在扫描... {count}/{sceneGuids.Length}", (float)count / sceneGuids.Length);

            string scenePath = AssetDatabase.GUIDToAssetPath(guid);
            string[] dependencies = GetCachedDependencies(scenePath);

            var hits = new List<string>();
            foreach (string dep in dependencies)
            {
                if (targetPaths.Contains(dep) && !hits.Contains(dep))
                    hits.Add(dep);
            }

            if (hits.Count > 0)
            {
                SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                if (sceneAsset != null)
                    referencingSceneFiles.Add(new SceneFileRefResult { SceneAsset = sceneAsset, HitTargetPaths = hits });
            }
        }
    }

    private void FindInActiveScene()
    {
        referencingSceneObjects.Clear();
        Scene activeScene = SceneManager.GetActiveScene();
        lastSearchedSceneHandle = activeScene.handle;
        GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(go => go.scene == activeScene).ToArray();

        var targetObjSet = new HashSet<Object>(targetObjectsForScene);
        int count = 0;
        foreach (GameObject go in allGameObjects)
        {
            count++;
            if (count % 20 == 0)
                EditorUtility.DisplayProgressBar("扫描当前场景内部", $"正在分析... {count}/{allGameObjects.Length}", (float)count / allGameObjects.Length);

            Object[] dependencies = EditorUtility.CollectDependencies(new Object[] { go });

            var hits = new List<Object>();
            foreach (Object dep in dependencies)
            {
                if (targetObjSet.Contains(dep) && !hits.Contains(dep))
                    hits.Add(dep);
            }

            if (hits.Count > 0)
            {
                referencingSceneObjects.Add(new SceneObjRefResult
                {
                    GameObjectName = go.name,
                    GameObjectPath = GetGameObjectPath(go),
                    HitTargetObjects = hits
                });
            }
        }
    }

    private string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return string.Empty;
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
