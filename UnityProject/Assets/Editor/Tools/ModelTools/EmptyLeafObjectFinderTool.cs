using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class EmptyLeafObjectFinderTool : EditorWindow
{
    private sealed class EmptyLeafInfo
    {
        public GameObject GameObject;
        public string Path;
    }

    private const string WindowMenuPath = "Tools/模型工具/筛选空叶子 - 只有 Transform 的空物体";
    private const string GameObjectMenuPath = "GameObject/模型工具/筛选空叶子";

    private GameObject _parentObject;
    private bool _includeInactive = true;
    private bool _includeParent;
    private bool _selectResultsAfterScan = true;
    private readonly List<EmptyLeafInfo> _results = new List<EmptyLeafInfo>();
    private Vector2 _scrollPosition;
    private string _lastScanSummary = "选择一个父物体后点击“开始筛选”。";

    [MenuItem(WindowMenuPath)]
    private static void Open()
    {
        EmptyLeafObjectFinderTool window = GetWindow<EmptyLeafObjectFinderTool>("筛选空叶子");
        window.minSize = new Vector2(560f, 420f);
        window.UseSelection(false);
        window.Show();
    }

    [MenuItem(GameObjectMenuPath, false, 22)]
    private static void ScanSelectedFromGameObjectMenu()
    {
        EmptyLeafObjectFinderTool window = GetWindow<EmptyLeafObjectFinderTool>("筛选空叶子");
        window.minSize = new Vector2(560f, 420f);
        window.UseSelection(true);
        window.Show();
    }

    [MenuItem(GameObjectMenuPath, true)]
    private static bool ValidateScanSelectedFromGameObjectMenu()
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
        EditorGUILayout.LabelField("空叶子物体筛选", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("筛选父物体层级下没有子物体，且除了 Transform/RectTransform 外没有其它组件的物体。", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            _parentObject = (GameObject)EditorGUILayout.ObjectField("父物体", _parentObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                ClearResults("父物体已变更，请重新筛选。");
            }

            if (GUILayout.Button("使用当前选中", GUILayout.Width(110f)))
            {
                UseSelection(false);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_parentObject == null))
            {
                if (GUILayout.Button("开始筛选", GUILayout.Height(28f)))
                {
                    FindEmptyLeafObjects();
                }
            }

            using (new EditorGUI.DisabledScope(_results.Count == 0))
            {
                if (GUILayout.Button("选中全部结果", GUILayout.Height(28f), GUILayout.Width(110f)))
                {
                    SelectAllResults();
                }
            }

            if (GUILayout.Button("清空", GUILayout.Height(28f), GUILayout.Width(70f)))
            {
                ClearResults("结果已清空。");
            }
        }
    }

    private void DrawOptions()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("筛选选项", EditorStyles.boldLabel);
        _includeInactive = EditorGUILayout.ToggleLeft("包含未激活物体", _includeInactive);
        _includeParent = EditorGUILayout.ToggleLeft("包含父物体本身", _includeParent);
        _selectResultsAfterScan = EditorGUILayout.ToggleLeft("筛选完成后自动选中结果", _selectResultsAfterScan);
    }

    private void DrawResultSummary()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(_lastScanSummary, _results.Count > 0 ? MessageType.Info : MessageType.None);
    }

    private void DrawResults()
    {
        RemoveDestroyedResults();
        if (_results.Count == 0)
        {
            return;
        }

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        foreach (EmptyLeafInfo info in _results)
        {
            if (info.GameObject == null)
            {
                continue;
            }

            using (new EditorGUILayout.HorizontalScope("box"))
            {
                EditorGUILayout.ObjectField(info.GameObject, typeof(GameObject), true, GUILayout.Width(220f));
                EditorGUILayout.LabelField(info.Path, EditorStyles.miniLabel);

                if (GUILayout.Button("选中", GUILayout.Width(56f)))
                {
                    Selection.activeGameObject = info.GameObject;
                    EditorGUIUtility.PingObject(info.GameObject);
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void UseSelection(bool scanImmediately)
    {
        _parentObject = Selection.activeGameObject;
        ClearResults(_parentObject == null ? "当前没有选中的 GameObject。" : "已使用当前选中物体。");

        if (scanImmediately && _parentObject != null)
        {
            FindEmptyLeafObjects();
        }
    }

    private void FindEmptyLeafObjects()
    {
        _results.Clear();

        if (_parentObject == null)
        {
            _lastScanSummary = "请先选择父物体。";
            return;
        }

        Transform parent = _parentObject.transform;
        Transform[] transforms = parent.GetComponentsInChildren<Transform>(_includeInactive);
        int scannedCount = 0;
        int skippedMissingComponentCount = 0;

        try
        {
            foreach (Transform current in transforms)
            {
                if (current == null)
                {
                    continue;
                }

                if (!_includeParent && current == parent)
                {
                    continue;
                }

                scannedCount++;
                if (scannedCount % 100 == 0)
                {
                    EditorUtility.DisplayProgressBar("筛选空叶子物体", $"正在检查: {current.name}", (float)scannedCount / transforms.Length);
                }

                if (current.childCount > 0)
                {
                    continue;
                }

                if (!HasOnlyTransformComponent(current.gameObject, out bool hasMissingComponent))
                {
                    if (hasMissingComponent)
                    {
                        skippedMissingComponentCount++;
                    }

                    continue;
                }

                _results.Add(new EmptyLeafInfo
                {
                    GameObject = current.gameObject,
                    Path = GetGameObjectPath(parent, current)
                });
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        _lastScanSummary = $"筛选完成：检查 {scannedCount} 个物体，找到 {_results.Count} 个空叶子物体。";
        if (skippedMissingComponentCount > 0)
        {
            _lastScanSummary += $" 另有 {skippedMissingComponentCount} 个物体存在缺失脚本槽，未计入结果。";
        }

        if (_selectResultsAfterScan && _results.Count > 0)
        {
            SelectAllResults();
        }

        Repaint();
    }

    private void SelectAllResults()
    {
        RemoveDestroyedResults();

        Object[] objects = new Object[_results.Count];
        for (int i = 0; i < _results.Count; i++)
        {
            objects[i] = _results[i].GameObject;
        }

        Selection.objects = objects;
        if (objects.Length > 0)
        {
            EditorGUIUtility.PingObject(objects[0]);
        }
    }

    private void ClearResults(string message)
    {
        _results.Clear();
        _lastScanSummary = message;
        Repaint();
    }

    private void RemoveDestroyedResults()
    {
        _results.RemoveAll(info => info.GameObject == null);
    }

    private static bool HasOnlyTransformComponent(GameObject gameObject, out bool hasMissingComponent)
    {
        hasMissingComponent = false;
        Component[] components = gameObject.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component == null)
            {
                hasMissingComponent = true;
                return false;
            }

            if (component is Transform)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string GetGameObjectPath(Transform root, Transform target)
    {
        if (target == null)
        {
            return "已销毁";
        }

        if (target == root)
        {
            return target.name;
        }

        string path = target.name;
        Transform current = target;
        while (current.parent != null && current.parent != root)
        {
            current = current.parent;
            path = $"{current.name}/{path}";
        }

        return root == null || current == root ? path : $"{root.name}/{path}";
    }
}
