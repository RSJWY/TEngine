using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;

/// <summary>
/// DynamicSpawnPoint 可视化管理面板
/// 功能：总览列表、定位聚焦、批量编辑、有效性检查、快速添加、一键转换、Gizmo 预览
/// 创建时间：2026-06-25
/// </summary>
public class DynamicSpawnPointManager : OdinEditorWindow
{
    private const string MenuPath = "Tools/场景工具/动态加载点管理器";

    #region 数据模型

    [Serializable]
    public class SpawnPointEntry
    {
        [HideInInspector] public GameLogic.DynamicSpawnPoint Component;
        [HideInInspector] public GameObject GameObject;

        [TableColumnWidth(180, Resizable = true)]
        [ReadOnly, LabelText("节点名")]
        public string Name;

        [TableColumnWidth(160, Resizable = true)]
        [LabelText("Location")]
        public string Location;

        [TableColumnWidth(120)]
        [LabelText("对齐模式")]
        public GameLogic.SpawnAlignMode AlignMode;

        [TableColumnWidth(60)]
        [ReadOnly, LabelText("状态")]
        public string Status;

        [TableColumnWidth(50)]
        [ReadOnly, LabelText("预览")]
        public string PreviewStatus;

        [HideInInspector] public bool HasError;
        [HideInInspector] public bool HasPreview;
    }

    #endregion

    #region 面板状态

    [TitleGroup("筛选与操作")]
    [HorizontalGroup("筛选与操作/Filter")]
    [LabelText("搜索"), LabelWidth(40)]
    [OnValueChanged("RefreshList")]
    public string searchFilter = "";

    [HorizontalGroup("筛选与操作/Filter")]
    [LabelText("仅显示异常"), LabelWidth(70)]
    [OnValueChanged("RefreshList")]
    public bool showErrorsOnly = false;

    [TitleGroup("SpawnPoint 列表")]
    [TableList(ShowIndexLabels = true, AlwaysExpanded = true, IsReadOnly = false)]
    [OnValueChanged("OnTableChanged")]
    public List<SpawnPointEntry> entries = new List<SpawnPointEntry>();

    [TitleGroup("快速添加")]
    [LabelText("目标 Spawner 根节点")]
    [SceneObjectsOnly]
    public GameObject spawnerRoot;

    [TitleGroup("快速添加")]
    [LabelText("预制体")]
    [AssetsOnly]
    [AssetSelector(Paths = "Assets", Filter = "t:Prefab")]
    public GameObject prefabToAdd;

    [TitleGroup("设置")]
    [LabelText("Scene 视图 Gizmo 预览")]
    public bool enableGizmoPreview = true;

    [TitleGroup("设置")]
    [LabelText("Gizmo 图标大小")]
    [Range(0.2f, 3f)]
    [ShowIf("enableGizmoPreview")]
    public float gizmoIconSize = 1f;

    // 预制体缓存（用于有效性检查）
    private HashSet<string> _prefabNameCache;
    private bool _cacheInitialized;

    // 选中项索引
    [HideInInspector] public int selectedIndex = -1;

    #endregion

    #region 窗口生命周期

    [MenuItem(MenuPath)]
    public static void ShowWindow()
    {
        var window = GetWindow<DynamicSpawnPointManager>();
        window.titleContent = new GUIContent("动态加载点管理", EditorGUIUtility.IconContent("d_SceneViewTools").image);
        window.minSize = new Vector2(700, 500);
        window.Show();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        RefreshPrefabCache();
        ReconnectAllPreviewInstances();
        RefreshList();
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
    }

    private void OnHierarchyChanged()
    {
        RefreshList();
        Repaint();
    }

    #endregion

    #region 刷新逻辑

    [TitleGroup("筛选与操作")]
    [HorizontalGroup("筛选与操作/Buttons")]
    [Button("刷新列表", ButtonSizes.Medium)]
    [GUIColor(0.4f, 0.8f, 1f)]
    public void RefreshList()
    {
        ReconnectAllPreviewInstances();

        var allPoints = GameObject.FindObjectsOfType<GameLogic.DynamicSpawnPoint>(true);
        entries.Clear();

        foreach (var point in allPoints)
        {
            // 顺手迁移历史遗留的 PPtr 引用，标记场景为脏（保存后即解开 Bundle 依赖）
            if (point.MigrateLegacyReferenceIfNeeded())
            {
                EditorUtility.SetDirty(point);
            }

            var entry = new SpawnPointEntry
            {
                Component = point,
                GameObject = point.gameObject,
                Name = point.gameObject.name,
                Location = point.location ?? "",
                AlignMode = point.alignMode,
                HasPreview = point.previewInstance != null,
                PreviewStatus = point.previewInstance != null ? "●" : "—"
            };

            // 有效性检查
            ValidateEntry(entry);

            // 筛选
            if (showErrorsOnly && !entry.HasError) continue;
            if (!string.IsNullOrEmpty(searchFilter))
            {
                var filter = searchFilter.ToLower();
                if (!entry.Name.ToLower().Contains(filter) &&
                    !entry.Location.ToLower().Contains(filter))
                    continue;
            }

            entries.Add(entry);
        }

        // 异常项排前面
        entries.Sort((a, b) =>
        {
            if (a.HasError != b.HasError) return a.HasError ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
    }

    private void ValidateEntry(SpawnPointEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Location))
        {
            entry.Status = "空";
            entry.HasError = true;
            return;
        }

        if (!IsPrefabLocationValid(entry.Location))
        {
            entry.Status = "找不到";
            entry.HasError = true;
            return;
        }

        entry.Status = "OK";
        entry.HasError = false;
    }

    private void RefreshPrefabCache()
    {
        _prefabNameCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            _prefabNameCache.Add(fileName);
        }
        _cacheInitialized = true;
    }

    private bool IsPrefabLocationValid(string location)
    {
        if (!_cacheInitialized) RefreshPrefabCache();
        return _prefabNameCache.Contains(location);
    }

    #endregion

    #region 表格操作回调

    private void OnTableChanged()
    {
        // 同步修改回 Component
        foreach (var entry in entries)
        {
            if (entry.Component == null) continue;
            if (entry.Location != entry.Component.location ||
                entry.AlignMode != entry.Component.alignMode)
            {
                Undo.RecordObject(entry.Component, "修改 SpawnPoint 属性");
                entry.Component.location = entry.Location;
                entry.Component.alignMode = entry.AlignMode;
                EditorUtility.SetDirty(entry.Component);
                ValidateEntry(entry);
            }
        }
    }

    #endregion

    #region 按钮操作

    [TitleGroup("筛选与操作")]
    [HorizontalGroup("筛选与操作/Buttons")]
    [Button("刷新预制体缓存", ButtonSizes.Medium)]
    public void ForceRefreshPrefabCache()
    {
        RefreshPrefabCache();
        RefreshList();
    }

    [TitleGroup("筛选与操作")]
    [InfoBox("若历史场景里仍有 prefabReference 直接引用（导致打包依赖预制体），点此把它们迁移成 GUID 字符串并保存，依赖即被解开。", InfoMessageType.Warning)]
    [Button("迁移并清理旧引用（解开打包依赖）", ButtonSizes.Medium)]
    [GUIColor(1f, 0.7f, 0.3f)]
    public void MigrateAndSaveAllScenes()
    {
        int migrated = 0;
        var dirtyScenes = new HashSet<Scene>();

        var allPoints = GameObject.FindObjectsOfType<GameLogic.DynamicSpawnPoint>(true);
        foreach (var point in allPoints)
        {
            if (point.MigrateLegacyReferenceIfNeeded())
            {
                EditorUtility.SetDirty(point);
                if (point.gameObject.scene.IsValid())
                    dirtyScenes.Add(point.gameObject.scene);
                migrated++;
            }
        }

        if (migrated > 0)
        {
            foreach (var scene in dirtyScenes)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }
            RefreshList();
            Debug.Log($"[SpawnPointManager] 已迁移并保存 {migrated} 个占位点（{dirtyScenes.Count} 个场景），prefabReference 依赖已解开。");
        }
        else
        {
            Debug.Log("[SpawnPointManager] 未发现遗留的 prefabReference 引用，无需迁移。");
        }
    }

    [TitleGroup("筛选与操作")]
    [HorizontalGroup("筛选与操作/Buttons")]
    [Button("定位选中项", ButtonSizes.Medium)]
    [GUIColor(0.6f, 1f, 0.6f)]
    public void PingSelected()
    {
        var selected = Selection.activeGameObject;
        if (selected == null) return;

        var point = selected.GetComponent<GameLogic.DynamicSpawnPoint>();
        if (point == null) return;

        var entry = entries.FirstOrDefault(e => e.Component == point);
        if (entry != null)
        {
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }

    [TitleGroup("SpawnPoint 列表")]
    [Button("聚焦到场景节点", ButtonSizes.Small)]
    [HorizontalGroup("SpawnPoint 列表/TableActions")]
    public void FocusOnSceneNode()
    {
        // 聚焦 Unity Selection 中的第一个 SpawnPoint entry
        if (Selection.activeGameObject != null)
        {
            SceneView.lastActiveSceneView?.FrameSelected();
            return;
        }

        // 或者聚焦列表中第一个异常项
        var errorEntry = entries.FirstOrDefault(e => e.HasError);
        if (errorEntry != null && errorEntry.GameObject != null)
        {
            Selection.activeGameObject = errorEntry.GameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }

    [TitleGroup("SpawnPoint 列表")]
    [Button("批量修复节点名", ButtonSizes.Small)]
    [HorizontalGroup("SpawnPoint 列表/TableActions")]
    public void BatchRenameNodes()
    {
        int count = 0;
        foreach (var entry in entries)
        {
            if (entry.Component == null || string.IsNullOrEmpty(entry.Location)) continue;

            var expectedName = $"[Spawn] {entry.Location}";
            if (entry.GameObject.name != expectedName)
            {
                Undo.RecordObject(entry.GameObject, "批量重命名 SpawnPoint 节点");
                entry.GameObject.name = expectedName;
                entry.Name = expectedName;
                count++;
            }
        }

        if (count > 0)
            Debug.Log($"[SpawnPointManager] 已重命名 {count} 个节点");
        else
            Debug.Log("[SpawnPointManager] 所有节点名称已正确，无需修改");
    }

    [TitleGroup("SpawnPoint 列表")]
    [Button("批量放置预览", ButtonSizes.Small)]
    [HorizontalGroup("SpawnPoint 列表/TableActions2")]
    [GUIColor(0.5f, 1f, 0.6f)]
    public void BatchPlacePreview()
    {
        int count = 0;
        foreach (var entry in entries)
        {
            if (entry.Component == null) continue;
            if (entry.Component.EditorPrefab == null) continue;
            if (entry.Component.previewInstance != null) continue; // 已有预览则跳过

            PlacePreviewFor(entry.Component);
            count++;
        }

        RefreshList();
        SceneView.RepaintAll();
        Debug.Log($"[SpawnPointManager] 已批量放置 {count} 个预览实例");
    }

    [TitleGroup("SpawnPoint 列表")]
    [Button("批量移除预览", ButtonSizes.Small)]
    [HorizontalGroup("SpawnPoint 列表/TableActions2")]
    [GUIColor(1f, 0.5f, 0.5f)]
    public void BatchRemovePreview()
    {
        int count = 0;
        foreach (var entry in entries)
        {
            if (entry.Component == null) continue;
            if (entry.Component.previewInstance == null) continue;

            DestroyImmediate(entry.Component.previewInstance);
            entry.Component.previewInstance = null;
            count++;
        }

        RefreshList();
        SceneView.RepaintAll();
        Debug.Log($"[SpawnPointManager] 已批量移除 {count} 个预览实例");
    }

    [TitleGroup("SpawnPoint 列表")]
    [Button("重连预览引用", ButtonSizes.Small)]
    [HorizontalGroup("SpawnPoint 列表/TableActions2")]
    public void ManualReconnectPreview()
    {
        ReconnectAllPreviewInstances();
        RefreshList();
        Repaint();
        Debug.Log("[SpawnPointManager] 已重连所有预览实例引用");
    }

    [TitleGroup("快速添加")]
    [Button("添加占位节点", ButtonSizes.Large)]
    [GUIColor(0.3f, 0.9f, 0.5f)]
    public void AddSpawnPoint()
    {
        if (spawnerRoot == null)
        {
            EditorUtility.DisplayDialog("提示", "请先指定「目标 Spawner 根节点」", "确定");
            return;
        }
        if (prefabToAdd == null)
        {
            EditorUtility.DisplayDialog("提示", "请先选择要添加的预制体", "确定");
            return;
        }

        var prefabName = prefabToAdd.name;

        // 创建空节点
        var go = new GameObject($"[Spawn] {prefabName}");
        Undo.RegisterCreatedObjectUndo(go, "添加 SpawnPoint");
        go.transform.SetParent(spawnerRoot.transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        // 添加组件
        var point = Undo.AddComponent<GameLogic.DynamicSpawnPoint>(go);
        point.location = prefabName;
        point.EditorPrefab = prefabToAdd;

        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();

        RefreshList();
        Debug.Log($"[SpawnPointManager] 已添加占位节点: [Spawn] {prefabName}");
    }

    [TitleGroup("一键转换")]
    [InfoBox("选中场景中已有的预制体实例（可多选），点击按钮将其转换为占位节点（保留 TRS，删除原物体）。")]
    [Button("将选中物体转换为占位节点", ButtonSizes.Large)]
    [GUIColor(1f, 0.7f, 0.3f)]
    public void ConvertSelectedToSpawnPoints()
    {
        var selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先在场景中选中要转换的物体", "确定");
            return;
        }

        // 确定目标父节点
        var targetParent = spawnerRoot != null ? spawnerRoot.transform : null;

        int convertedCount = 0;
        var newSelections = new List<GameObject>();

        foreach (var obj in selectedObjects)
        {
            if (obj == null) continue;

            // 获取预制体来源
            var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(obj);
            string prefabName = prefabSource != null ? prefabSource.name : obj.name;

            // 记录 TRS
            var worldPos = obj.transform.position;
            var worldRot = obj.transform.rotation;
            var worldScale = obj.transform.lossyScale;
            var parent = targetParent != null ? targetParent : obj.transform.parent;

            // 创建占位节点
            var placeholder = new GameObject($"[Spawn] {prefabName}");
            Undo.RegisterCreatedObjectUndo(placeholder, "转换为 SpawnPoint");
            placeholder.transform.SetParent(parent);
            placeholder.transform.position = worldPos;
            placeholder.transform.rotation = worldRot;

            // 尽量保留 scale（考虑父节点的 lossyScale）
            if (parent != null)
            {
                var parentScale = parent.lossyScale;
                placeholder.transform.localScale = new Vector3(
                    parentScale.x != 0 ? worldScale.x / parentScale.x : 1f,
                    parentScale.y != 0 ? worldScale.y / parentScale.y : 1f,
                    parentScale.z != 0 ? worldScale.z / parentScale.z : 1f
                );
            }
            else
            {
                placeholder.transform.localScale = worldScale;
            }

            // 添加组件
            var point = Undo.AddComponent<GameLogic.DynamicSpawnPoint>(placeholder);
            point.location = prefabName;

            // 尝试找到原始预制体资源引用
            if (prefabSource != null)
            {
                var prefabAssetPath = AssetDatabase.GetAssetPath(prefabSource);
                if (!string.IsNullOrEmpty(prefabAssetPath))
                {
                    point.EditorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
                }
            }

            newSelections.Add(placeholder);
            convertedCount++;

            // 删除原物体
            Undo.DestroyObjectImmediate(obj);
        }

        if (newSelections.Count > 0)
        {
            Selection.objects = newSelections.ToArray();
        }

        RefreshList();
        Debug.Log($"[SpawnPointManager] 已将 {convertedCount} 个物体转换为占位节点");
    }

    #endregion

    #region 预览实例管理

    /// <summary>
    /// 全局重连：扫描场景中所有 DynamicSpawnPoint，对 previewInstance 丢失引用的进行恢复。
    /// NonSerialized 字段在 Inspector 失焦/重编译/切场景后会丢失，但实际子 GameObject 仍存在。
    /// </summary>
    private void ReconnectAllPreviewInstances()
    {
        var allPoints = GameObject.FindObjectsOfType<GameLogic.DynamicSpawnPoint>(true);
        foreach (var point in allPoints)
        {
            if (point == null || point.previewInstance != null) continue;

            foreach (Transform child in point.transform)
            {
                if (child == null) continue;
                if (child.gameObject.name.StartsWith("[Preview] ") &&
                    (child.gameObject.hideFlags & HideFlags.DontSave) != 0)
                {
                    point.previewInstance = child.gameObject;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 为指定 SpawnPoint 放置预览实例（与 Inspector 中逻辑一致）。
    /// </summary>
    private void PlacePreviewFor(GameLogic.DynamicSpawnPoint point)
    {
        var prefab = point.EditorPrefab;
        if (prefab == null) return;

        // 先移除旧的
        if (point.previewInstance != null)
        {
            DestroyImmediate(point.previewInstance);
            point.previewInstance = null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, point.transform);
        instance.name = $"[Preview] {prefab.name}";

        if (point.alignMode == GameLogic.SpawnAlignMode.AlignToPlaceholder)
        {
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }

        instance.hideFlags = HideFlags.DontSave;
        SetHideFlagsRecursive(instance, HideFlags.DontSave);

        point.previewInstance = instance;
    }

    private static void SetHideFlagsRecursive(GameObject go, HideFlags flags)
    {
        go.hideFlags = flags;
        foreach (var component in go.GetComponents<Component>())
        {
            if (component != null)
                component.hideFlags = flags;
        }
        foreach (Transform child in go.transform)
        {
            SetHideFlagsRecursive(child.gameObject, flags);
        }
    }

    #endregion

    #region 自定义绘制（列表项点击定位）

    protected override void OnImGUI()
    {
        // 在 Odin 绘制前，检测列表中的点击事件
        base.OnImGUI();

        // 底部状态栏
        GUILayout.FlexibleSpace();
        SirenixEditorGUI.DrawThickHorizontalSeparator();
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            var totalCount = GameObject.FindObjectsOfType<GameLogic.DynamicSpawnPoint>(true).Length;
            var errorCount = entries.Count(e => e.HasError);
            var previewCount = entries.Count(e => e.HasPreview);

            GUILayout.Label($"场景: {SceneManager.GetActiveScene().name}  |  " +
                          $"总计: {totalCount}  |  " +
                          $"显示: {entries.Count}  |  " +
                          $"预览: {previewCount}  |  " +
                          $"异常: {errorCount}",
                          EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("选中→聚焦", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                if (Selection.activeGameObject != null)
                    SceneView.lastActiveSceneView?.FrameSelected();
            }
        }
        GUILayout.EndHorizontal();
    }

    #endregion

    #region Scene 视图 Gizmo

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!enableGizmoPreview) return;

        var allPoints = GameObject.FindObjectsOfType<GameLogic.DynamicSpawnPoint>(true);
        if (allPoints == null || allPoints.Length == 0) return;

        foreach (var point in allPoints)
        {
            if (point == null) continue;

            var pos = point.transform.position;
            var size = gizmoIconSize * HandleUtility.GetHandleSize(pos) * 0.15f;
            bool hasError = string.IsNullOrEmpty(point.location) || !IsPrefabLocationValid(point.location);
            bool isSelected = Selection.activeGameObject == point.gameObject;

            // 颜色：异常红色、选中黄色、正常绿色
            Color color;
            if (hasError) color = new Color(1f, 0.3f, 0.3f, 0.9f);
            else if (isSelected) color = new Color(1f, 1f, 0.2f, 0.9f);
            else color = new Color(0.3f, 1f, 0.5f, 0.7f);

            Handles.color = color;

            // 绘制实心球 + 标签
            Handles.SphereHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);

            // 绘制方向指示线（forward 方向）
            Handles.color = new Color(color.r, color.g, color.b, 0.5f);
            Handles.DrawLine(pos, pos + point.transform.forward * size * 3f);

            // 绘制标签
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = color },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9
            };

            var label = !string.IsNullOrEmpty(point.location) ? point.location : "(空)";
            Handles.Label(pos + Vector3.up * size * 2f, label, labelStyle);

            // 可点击选中
            if (Handles.Button(pos, Quaternion.identity, size * 0.8f, size * 1.2f, Handles.SphereHandleCap))
            {
                Selection.activeGameObject = point.gameObject;
                EditorGUIUtility.PingObject(point.gameObject);
            }
        }
    }

    #endregion
}
