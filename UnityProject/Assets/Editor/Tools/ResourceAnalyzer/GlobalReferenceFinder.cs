using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class GlobalReferenceFinder : EditorWindow
{
    private Object targetAsset;
    
    private List<GameObject> referencingPrefabs = new List<GameObject>();
    private List<SceneAsset> referencingSceneFiles = new List<SceneAsset>(); // 新增：引用的场景文件
    private List<GameObject> referencingSceneObjects = new List<GameObject>();
    
    private Vector2 prefabScroll;
    private Vector2 sceneFileScroll; // 新增：场景文件滚动条
    private Vector2 sceneScroll;
    
    private bool showPrefabs = true;
    private bool showSceneFiles = true; // 新增：场景文件折叠状态
    private bool showSceneObjects = true;

    [MenuItem("Tools/资源分析器/查资源被谁用 - Prefab 和场景")]
    public static void ShowWindow()
    {
        var window = GetWindow<GlobalReferenceFinder>("资源反向引用");
        window.minSize = new Vector2(500, 600);
        window.Show();
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawSeparator();
        
        if (referencingPrefabs.Count > 0 || referencingSceneFiles.Count > 0 || referencingSceneObjects.Count > 0)
        {
            DrawResults();
        }
        else if (targetAsset != null)
        {
            GUILayout.Space(10);
            GUILayout.Label("没有找到任何预制体、场景文件或当前场景物体引用此资源。", EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void DrawHeader()
    {
        GUILayout.Space(10);
        GUILayout.Label("请拖入你要排查的目标资源 (如贴图、材质、FBX等):", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        targetAsset = EditorGUILayout.ObjectField(targetAsset, typeof(Object), false, GUILayout.Height(20));
        
        GUI.enabled = targetAsset != null;
        if (GUILayout.Button("开始全盘查找", GUILayout.Width(120), GUILayout.Height(20)))
        {
            FindReferences();
        }
        GUI.enabled = true;
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
        // 1. 预制体引用列表
        if (referencingPrefabs.Count > 0)
        {
            GUILayout.BeginHorizontal();
            showPrefabs = EditorGUILayout.Foldout(showPrefabs, $"项目预制体引用 ({referencingPrefabs.Count})", true, EditorStyles.foldoutHeader);
            GUILayout.EndHorizontal();

            if (showPrefabs)
            {
                prefabScroll = GUILayout.BeginScrollView(prefabScroll, GUILayout.MaxHeight(200));
                foreach (var prefab in referencingPrefabs)
                {
                    GUILayout.BeginHorizontal("box");
                    EditorGUILayout.ObjectField(prefab, typeof(GameObject), false, GUILayout.Width(250));
                    GUILayout.Label(AssetDatabase.GetAssetPath(prefab), EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
            GUILayout.Space(10);
        }

        // 2. 场景文件引用列表 (新增)
        if (referencingSceneFiles.Count > 0)
        {
            GUILayout.BeginHorizontal();
            showSceneFiles = EditorGUILayout.Foldout(showSceneFiles, $"项目场景文件引用 ({referencingSceneFiles.Count})", true, EditorStyles.foldoutHeader);
            GUILayout.EndHorizontal();

            if (showSceneFiles)
            {
                sceneFileScroll = GUILayout.BeginScrollView(sceneFileScroll, GUILayout.MaxHeight(150));
                foreach (var sceneFile in referencingSceneFiles)
                {
                    GUILayout.BeginHorizontal("box");
                    EditorGUILayout.ObjectField(sceneFile, typeof(SceneAsset), false, GUILayout.Width(250));
                    GUILayout.Label(AssetDatabase.GetAssetPath(sceneFile), EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
            GUILayout.Space(10);
        }

        // 3. 当前场景物体引用列表
        if (referencingSceneObjects.Count > 0)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            GUILayout.BeginHorizontal();
            showSceneObjects = EditorGUILayout.Foldout(showSceneObjects, $"当前已打开场景 [{activeScene.name}] 内部引用 ({referencingSceneObjects.Count})", true, EditorStyles.foldoutHeader);
            GUILayout.EndHorizontal();

            if (showSceneObjects)
            {
                sceneScroll = GUILayout.BeginScrollView(sceneScroll);
                foreach (var go in referencingSceneObjects)
                {
                    GUILayout.BeginHorizontal("box");
                    EditorGUILayout.ObjectField(go, typeof(GameObject), true, GUILayout.Width(250));
                    GUILayout.Label(GetGameObjectPath(go), EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
        }
    }

    private void FindReferences()
    {
        referencingPrefabs.Clear();
        referencingSceneFiles.Clear();
        referencingSceneObjects.Clear();
        
        string targetPath = AssetDatabase.GetAssetPath(targetAsset);
        if (string.IsNullOrEmpty(targetPath))
        {
            Debug.LogWarning("选中的目标不是有效的工程资产。");
            return;
        }

        FindInPrefabs(targetPath);
        FindInAllSceneFiles(targetPath); // 新增：扫描所有场景文件
        FindInActiveScene(targetAsset);
        
        EditorUtility.ClearProgressBar();
    }

    private void FindInPrefabs(string targetPath)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        int count = 0;
        
        foreach (string guid in prefabGuids)
        {
            count++;
            if (count % 50 == 0)
                EditorUtility.DisplayProgressBar("扫描工程预制体", $"正在扫描... {count}/{prefabGuids.Length}", (float)count / prefabGuids.Length);

            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            string[] dependencies = AssetDatabase.GetDependencies(prefabPath, true);
            
            if (dependencies.Contains(targetPath))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null) referencingPrefabs.Add(prefab);
            }
        }
    }

    // 新增：扫描全工程所有的场景文件
    private void FindInAllSceneFiles(string targetPath)
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
        int count = 0;

        foreach (string guid in sceneGuids)
        {
            count++;
            if (count % 10 == 0)
                EditorUtility.DisplayProgressBar("扫描项目场景文件", $"正在扫描... {count}/{sceneGuids.Length}", (float)count / sceneGuids.Length);

            string scenePath = AssetDatabase.GUIDToAssetPath(guid);
            
            // 扫描场景文件的依赖关系
            string[] dependencies = AssetDatabase.GetDependencies(scenePath, true);
            
            if (dependencies.Contains(targetPath))
            {
                SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                if (sceneAsset != null) referencingSceneFiles.Add(sceneAsset);
            }
        }
    }

    private void FindInActiveScene(Object targetObj)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(go => go.scene == activeScene).ToArray();

        int count = 0;
        foreach (GameObject go in allGameObjects)
        {
            count++;
            if (count % 20 == 0)
                EditorUtility.DisplayProgressBar("扫描当前场景内部", $"正在分析... {count}/{allGameObjects.Length}", (float)count / allGameObjects.Length);

            Object[] dependencies = EditorUtility.CollectDependencies(new Object[] { go });
            
            if (dependencies.Contains(targetObj))
            {
                referencingSceneObjects.Add(go);
            }
        }
    }

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
