using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class SceneObjectReferenceFinderTool : EditorWindow
{
    private sealed class ReferenceInfo
    {
        public GameObject Owner;
        public Component Component;
        public string SceneName;
        public string OwnerPath;
        public string PropertyPath;
        public Object ReferencedObject;
    }

    private const string WindowMenuPath = "Tools/引用查找/查场景对象引用 - 谁在引用它";
    private const string GameObjectMenuPath = "GameObject/引用查找/查场景对象引用";

    private Object _targetObject;
    private bool _includeChildren;
    private bool _includeInactive = true;
    private bool _scanAllLoadedScenes = true;
    private readonly List<ReferenceInfo> _references = new List<ReferenceInfo>();
    private readonly HashSet<Object> _targetReferences = new HashSet<Object>();
    private Vector2 _scrollPosition;
    private string _lastScanSummary = "选择一个场景物体后点击“查找引用”。";

    [MenuItem(WindowMenuPath)]
    private static void Open()
    {
        SceneObjectReferenceFinderTool window = GetWindow<SceneObjectReferenceFinderTool>("场景对象引用");
        window.minSize = new Vector2(620f, 420f);
        window.UseSelection(false);
        window.Show();
    }

    [MenuItem(GameObjectMenuPath, false, 21)]
    private static void FindSelectedFromGameObjectMenu()
    {
        SceneObjectReferenceFinderTool window = GetWindow<SceneObjectReferenceFinderTool>("场景对象引用");
        window.minSize = new Vector2(620f, 420f);
        window.UseSelection(true);
        window.Show();
    }

    [MenuItem(GameObjectMenuPath, true)]
    private static bool ValidateFindSelectedFromGameObjectMenu()
    {
        return Selection.activeGameObject != null;
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawOptions();
        DrawResultSummary();
        DrawResults();
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("场景引用查找器", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("用于快速查找当前已加载场景里，哪些组件字段引用了选中的 GameObject、Transform 或它身上的组件。此工具只扫描场景对象，不扫描工程 Prefab/资源文件。", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            _targetObject = EditorGUILayout.ObjectField("目标物体", _targetObject, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                ClearResults("目标已变更，请重新查找。");
            }

            if (GUILayout.Button("使用当前选中", GUILayout.Width(110f)))
            {
                UseSelection(false);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(GetTargetGameObject(_targetObject) == null))
            {
                if (GUILayout.Button("查找引用", GUILayout.Height(28f)))
                {
                    FindReferences();
                }
            }

            if (GUILayout.Button("清空结果", GUILayout.Height(28f), GUILayout.Width(100f)))
            {
                ClearResults("结果已清空。");
            }
        }
    }

    private void DrawOptions()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("查找范围", EditorStyles.boldLabel);
        _includeChildren = EditorGUILayout.ToggleLeft("包含目标子物体和子物体组件", _includeChildren);
        _includeInactive = EditorGUILayout.ToggleLeft("包含未激活对象", _includeInactive);
        _scanAllLoadedScenes = EditorGUILayout.ToggleLeft("扫描所有已加载场景", _scanAllLoadedScenes);
    }

    private void DrawResultSummary()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(_lastScanSummary, _references.Count > 0 ? MessageType.Info : MessageType.None);
    }

    private void DrawResults()
    {
        if (_references.Count == 0)
        {
            return;
        }

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        foreach (ReferenceInfo info in _references)
        {
            if (info.Owner == null)
            {
                continue;
            }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField("引用者", info.Owner, typeof(GameObject), true);
                    if (GUILayout.Button("选中", GUILayout.Width(56f)))
                    {
                        Selection.activeGameObject = info.Owner;
                        EditorGUIUtility.PingObject(info.Owner);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField("组件", info.Component, typeof(Component), true);
                    EditorGUILayout.ObjectField("引用对象", info.ReferencedObject, typeof(Object), true);
                }

                EditorGUILayout.LabelField("字段路径", info.PropertyPath);
                EditorGUILayout.LabelField("层级路径", $"{info.SceneName}/{info.OwnerPath}");
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void UseSelection(bool scanImmediately)
    {
        _targetObject = Selection.activeObject;
        if (_targetObject == null && Selection.activeGameObject != null)
        {
            _targetObject = Selection.activeGameObject;
        }

        ClearResults(_targetObject == null ? "当前没有选中的场景物体。" : "已使用当前选中对象。");
        if (scanImmediately && GetTargetGameObject(_targetObject) != null)
        {
            FindReferences();
        }
    }

    private void FindReferences()
    {
        _references.Clear();
        _targetReferences.Clear();

        GameObject targetGameObject = GetTargetGameObject(_targetObject);
        if (targetGameObject == null)
        {
            _lastScanSummary = "请选择场景中的 GameObject 或 Component。";
            return;
        }

        BuildTargetReferenceSet(targetGameObject);
        int scannedComponentCount = 0;

        try
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                {
                    continue;
                }

                if (!_scanAllLoadedScenes && scene != targetGameObject.scene)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                foreach (GameObject root in roots)
                {
                    ScanGameObjectHierarchy(root, scene, ref scannedComponentCount);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        _references.RemoveAll(info => IsTargetObject(info.Owner));
        _lastScanSummary = $"扫描完成：检查 {scannedComponentCount} 个组件，找到 {_references.Count} 条引用。";
        Repaint();
    }

    private void ScanGameObjectHierarchy(GameObject root, Scene scene, ref int scannedComponentCount)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(_includeInactive);
        foreach (Transform transform in transforms)
        {
            if (transform == null)
            {
                continue;
            }

            Component[] components = transform.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null)
                {
                    continue;
                }

                scannedComponentCount++;
                if (scannedComponentCount % 100 == 0)
                {
                    EditorUtility.DisplayProgressBar("查找场景引用", $"正在扫描：{transform.name}", 0f);
                }

                ScanComponent(component, scene);
            }
        }
    }

    private void ScanComponent(Component component, Scene scene)
    {
        SerializedObject serializedObject;
        try
        {
            serializedObject = new SerializedObject(component);
        }
        catch (Exception)
        {
            return;
        }

        SerializedProperty iterator = serializedObject.GetIterator();
        while (iterator.NextVisible(true))
        {
            if (iterator.propertyType != SerializedPropertyType.ObjectReference)
            {
                continue;
            }

            Object referencedObject = iterator.objectReferenceValue;
            if (referencedObject == null || !_targetReferences.Contains(referencedObject))
            {
                continue;
            }

            GameObject owner = component.gameObject;
            _references.Add(new ReferenceInfo
            {
                Owner = owner,
                Component = component,
                SceneName = scene.name,
                OwnerPath = GetGameObjectPath(owner),
                PropertyPath = iterator.propertyPath,
                ReferencedObject = referencedObject
            });
        }
    }

    private void BuildTargetReferenceSet(GameObject targetGameObject)
    {
        AddTargetReferences(targetGameObject);
        if (!_includeChildren)
        {
            return;
        }

        Transform[] children = targetGameObject.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child == null || child.gameObject == targetGameObject)
            {
                continue;
            }

            AddTargetReferences(child.gameObject);
        }
    }

    private void AddTargetReferences(GameObject gameObject)
    {
        _targetReferences.Add(gameObject);
        _targetReferences.Add(gameObject.transform);

        Component[] components = gameObject.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component != null)
            {
                _targetReferences.Add(component);
            }
        }
    }

    private bool IsTargetObject(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return false;
        }

        return _targetReferences.Contains(gameObject) || _targetReferences.Contains(gameObject.transform);
    }

    private void ClearResults(string message)
    {
        _references.Clear();
        _targetReferences.Clear();
        _lastScanSummary = message;
        Repaint();
    }

    private static GameObject GetTargetGameObject(Object target)
    {
        if (target is GameObject gameObject && gameObject.scene.IsValid())
        {
            return gameObject;
        }

        if (target is Component component && component.gameObject.scene.IsValid())
        {
            return component.gameObject;
        }

        return null;
    }

    private static string GetGameObjectPath(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return "已销毁";
        }

        string path = gameObject.name;
        Transform current = gameObject.transform;
        while (current.parent != null)
        {
            current = current.parent;
            path = $"{current.name}/{path}";
        }

        return path;
    }
}
