using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using GameLogic;

/// <summary>
/// DynamicSceneSpawner 自定义 Inspector
/// 功能：默认字段绘制 + 一键批量放置预览 / 一键批量卸载（编辑器调试用，不污染场景文件）。
/// 对所有子类生效（editorForChildClasses: true）。
/// 创建时间：2026-06-27
/// </summary>
[CustomEditor(typeof(DynamicSceneSpawner), editorForChildClasses: true)]
public class DynamicSceneSpawnerInspector : Editor
{
    private DynamicSceneSpawner _target;

    // location -> prefab 资源路径缓存
    private static Dictionary<string, string> _prefabPathCache;
    private static bool _cacheInitialized;

    private void OnEnable()
    {
        _target = (DynamicSceneSpawner)target;
        // 重连丢失的预览引用（NonSerialized 列表在失焦/重编译后清空）
        TryReconnectPreviewInstances();
    }

    /// <summary>
    /// 扫描所有子 SpawnPoint 的子节点，恢复 Spawner 级和 SpawnPoint 级的预览引用。
    /// </summary>
    private void TryReconnectPreviewInstances()
    {
        if (_target == null) return;

        if (_target.editorPreviewInstances == null)
            _target.editorPreviewInstances = new List<GameObject>();

        // 先清除列表中已销毁的引用
        _target.editorPreviewInstances.RemoveAll(go => go == null);

        // 扫描子 SpawnPoint 的子节点，找到 [Preview] 开头 + DontSave 的对象
        var points = _target.GetComponentsInChildren<DynamicSpawnPoint>(true);
        foreach (var point in points)
        {
            if (point == null) continue;

            foreach (Transform child in point.transform)
            {
                if (child == null) continue;
                if (child.gameObject.name.StartsWith("[Preview] ") &&
                    (child.gameObject.hideFlags & HideFlags.DontSave) != 0)
                {
                    // 恢复 SpawnPoint 级引用
                    if (point.previewInstance == null)
                        point.previewInstance = child.gameObject;

                    // 恢复 Spawner 级列表引用（避免重复添加）
                    if (!_target.editorPreviewInstances.Contains(child.gameObject))
                        _target.editorPreviewInstances.Add(child.gameObject);

                    break; // 每个 SpawnPoint 只取一个预览
                }
            }
        }
    }

    public override void OnInspectorGUI()
    {
        int previewCount = _target.editorPreviewInstances != null
            ? CountAlive(_target.editorPreviewInstances)
            : 0;

        if (previewCount > 0)
        {
            var warnStyle = new GUIStyle(EditorStyles.helpBox)
            {
                richText = true,
                fontSize = 12,
                normal = { textColor = new Color(1f, 0.85f, 0.3f) }
            };
            EditorGUILayout.LabelField(
                $"<b><color=#FFD23F>⚠ 托管的 {previewCount} 个物体不会保存到场景中</color></b>\n" +
                "<color=#FFD23F>这些是编辑器预览实例（HideFlags.DontSave），仅供布局调试。\n" +
                "运行 / 切场景 / 保存前请先点「一键卸载全部预览」。</color>",
                warnStyle);
            EditorGUILayout.Space(6);
        }

        // 默认字段（batchSize / initMode 等）
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        DrawSeparator();
        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("编辑器调试（不参与打包）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "一键放置：收集本 Spawner 的所有加载项，从工程内按文件名查找预制体并实例化预览。\n" +
            "预览实例标记为 HideFlags.DontSave，不会保存到场景文件，运行/切场景前请先卸载。",
            MessageType.Info);

        if (Application.isPlaying)
        {
            EditorGUILayout.Space(4);
            DrawRuntimeStatus();
            EditorGUILayout.HelpBox("运行模式下请使用真实加载流程，此处批量预览仅供非运行态布局调试。", MessageType.Warning);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        {
            var defColor = GUI.backgroundColor;

            GUI.backgroundColor = new Color(0.5f, 1f, 0.6f);
            var loadLabel = previewCount > 0 ? "重新放置全部预览" : "一键放置全部预览";
            if (GUILayout.Button(new GUIContent(loadLabel, "收集加载项并实例化全部预制体预览"), GUILayout.Height(26)))
            {
                PlaceAllPreviews();
            }

            EditorGUI.BeginDisabledGroup(previewCount == 0);
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button(new GUIContent("一键卸载全部预览", "销毁所有预览实例"), GUILayout.Height(26)))
            {
                RemoveAllPreviews();
            }
            EditorGUI.EndDisabledGroup();

            GUI.backgroundColor = defColor;
        }
        EditorGUILayout.EndHorizontal();

        if (previewCount > 0)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var style = new GUIStyle(EditorStyles.miniLabel) { richText = true };
            EditorGUILayout.LabelField($"<color=#80FF80>● 已放置 {previewCount} 个预览实例</color>", style);
            EditorGUILayout.LabelField("标记 HideFlags.DontSave，不会写入场景文件。", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
    }

    #region 批量放置 / 卸载

    private void PlaceAllPreviews()
    {
        RemoveAllPreviews();

        var items = _target.EditorCollectSpawnItems();
        if (items == null || items.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "未收集到任何加载项（检查占位节点或子类 CollectSpawnItems）。", "确定");
            return;
        }

        EnsureCache();

        if (_target.editorPreviewInstances == null)
            _target.editorPreviewInstances = new List<GameObject>();

        int success = 0, missing = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Location)) continue;

            if (!_prefabPathCache.TryGetValue(item.Location, out var path))
            {
                Debug.LogWarning($"[DynamicSceneSpawner] 找不到预制体: \"{item.Location}\"");
                missing++;
                continue;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                missing++;
                continue;
            }

            var parent = item.Parent != null ? item.Parent : _target.transform;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = $"[Preview] {item.Location}";

            if (item.AlignMode == SpawnAlignMode.AlignToPlaceholder)
            {
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
            }
            // KeepPrefabLocal：保留预制体自带 local TRS

            SetHideFlagsRecursive(instance, HideFlags.DontSave);
            _target.editorPreviewInstances.Add(instance);

            // 同步写入对应 SpawnPoint 的 previewInstance
            if (item.Parent != null)
            {
                var spawnPoint = item.Parent.GetComponent<DynamicSpawnPoint>();
                if (spawnPoint != null)
                    spawnPoint.previewInstance = instance;
            }

            success++;
        }

        SceneView.RepaintAll();
        Debug.Log($"[DynamicSceneSpawner] {_target.GetType().Name}: 批量放置完成，成功 {success}，缺失 {missing}");
    }

    private void RemoveAllPreviews()
    {
        if (_target.editorPreviewInstances == null) return;

        int count = 0;
        foreach (var instance in _target.editorPreviewInstances)
        {
            if (instance != null)
            {
                // 同步清理对应 SpawnPoint 的 previewInstance 引用
                var parentPoint = instance.transform.parent != null
                    ? instance.transform.parent.GetComponent<DynamicSpawnPoint>()
                    : null;
                if (parentPoint != null && parentPoint.previewInstance == instance)
                    parentPoint.previewInstance = null;

                DestroyImmediate(instance);
                count++;
            }
        }
        _target.editorPreviewInstances.Clear();

        if (count > 0)
        {
            SceneView.RepaintAll();
            Debug.Log($"[DynamicSceneSpawner] {_target.GetType().Name}: 已卸载 {count} 个预览实例");
        }
    }

    #endregion

    #region 辅助

    private static int CountAlive(List<GameObject> list)
    {
        int n = 0;
        foreach (var go in list)
            if (go != null) n++;
        return n;
    }

    private static void EnsureCache()
    {
        if (_cacheInitialized && _prefabPathCache != null) return;

        _prefabPathCache = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        var guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            // 同名取第一个，避免覆盖
            if (!_prefabPathCache.ContainsKey(fileName))
                _prefabPathCache[fileName] = path;
        }
        _cacheInitialized = true;
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

    private static void DrawSeparator()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1f);
        rect.height = 1f;
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
    }

    /// <summary>
    /// 运行时状态面板：显示加载进度、注册表条目、各 SpawnPoint 的子物体加载状态。
    /// </summary>
    private void DrawRuntimeStatus()
    {
        DrawSeparator();
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("运行时加载状态", EditorStyles.boldLabel);

        // 加载完成状态
        var statusLabel = _target.IsSpawnCompleted ? "✓ 加载完成" : "⏳ 加载中...";
        var statusColor = _target.IsSpawnCompleted ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.9f, 0.4f);
        var richStyle = new GUIStyle(EditorStyles.label) { richText = true };
        EditorGUILayout.LabelField($"<color=#{ColorUtility.ToHtmlStringRGB(statusColor)}><b>{statusLabel}</b></color>", richStyle);

        // 注册表信息
        EditorGUILayout.LabelField($"注册表条目数: {_target.RegisteredCount}", EditorStyles.miniLabel);

        if (_target.RegisteredCount > 0)
        {
            EditorGUI.indentLevel++;
            foreach (var key in _target.RegisteredKeys)
            {
                var go = _target.GetRegistered(key);
                var goName = go != null ? go.name : "(已销毁)";
                EditorGUILayout.LabelField($"[{key}] → {goName}", EditorStyles.miniLabel);
            }
            EditorGUI.indentLevel--;
        }

        // 各 SpawnPoint 加载状态
        EditorGUILayout.Space(4);
        var points = _target.GetComponentsInChildren<DynamicSpawnPoint>(true);
        if (points.Length > 0)
        {
            EditorGUILayout.LabelField($"占位节点 ({points.Length})", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            foreach (var point in points)
            {
                if (point == null) continue;
                // 运行时加载的实例是 SpawnPoint 子节点中非 DontSave 的对象
                bool hasLoadedChild = false;
                foreach (Transform child in point.transform)
                {
                    if (child == null) continue;
                    if ((child.gameObject.hideFlags & HideFlags.DontSave) == 0)
                    {
                        hasLoadedChild = true;
                        break;
                    }
                }
                var icon = hasLoadedChild ? "<color=#80FF80>●</color>" : "<color=#FF6666>○</color>";
                var loc = !string.IsNullOrEmpty(point.location) ? point.location : "(空)";
                EditorGUILayout.LabelField($"{icon} {point.name}  →  {loc}", richStyle);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(4);

        // 运行时刷新按钮
        if (GUILayout.Button("刷新运行时状态", GUILayout.Height(20)))
        {
            Repaint();
        }
    }

    #endregion
}
