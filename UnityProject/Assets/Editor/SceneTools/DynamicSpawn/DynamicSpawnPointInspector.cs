using UnityEngine;
using UnityEditor;
using GameLogic;

/// <summary>
/// DynamicSpawnPoint 自定义 Inspector
/// 功能：预制体引用辅助（不参与打包）、一键放置预览实例、一键移除、快速调试。
/// 创建时间：2026-06-26
/// </summary>
[CustomEditor(typeof(DynamicSpawnPoint))]
public class DynamicSpawnPointInspector : Editor
{
    private DynamicSpawnPoint _target;
    private Editor _prefabPreviewEditor;

    private SerializedProperty _locationProp;
    private SerializedProperty _alignModeProp;
    private SerializedProperty _registerKeyProp;

    private void OnEnable()
    {
        _target = (DynamicSpawnPoint)target;
        _locationProp = serializedObject.FindProperty("location");
        _alignModeProp = serializedObject.FindProperty("alignMode");
        _registerKeyProp = serializedObject.FindProperty("registerKey");

        // 迁移历史遗留的 PPtr 引用（prefabReference → prefabGuid），解开 Bundle 依赖
        if (_target != null && _target.MigrateLegacyReferenceIfNeeded())
        {
            EditorUtility.SetDirty(_target);
        }

        // 重连预览实例：NonSerialized 字段在 Inspector 失焦/重编译后会丢失引用，
        // 但实际 GameObject 仍存在于子节点中，通过命名模式恢复。
        TryReconnectPreviewInstance();
    }

    /// <summary>
    /// 扫描子节点，找到名为 [Preview] 开头且带 DontSave 标记的对象，恢复 previewInstance 引用。
    /// </summary>
    private void TryReconnectPreviewInstance()
    {
        if (_target == null || _target.previewInstance != null) return;

        foreach (Transform child in _target.transform)
        {
            if (child == null) continue;
            if (child.gameObject.name.StartsWith("[Preview] ") &&
                (child.gameObject.hideFlags & HideFlags.DontSave) != 0)
            {
                _target.previewInstance = child.gameObject;
                break;
            }
        }
    }

    private void OnDisable()
    {
        if (_prefabPreviewEditor != null)
        {
            DestroyImmediate(_prefabPreviewEditor);
            _prefabPreviewEditor = null;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // === 运行时字段 ===
        EditorGUILayout.LabelField("运行时配置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_locationProp, new GUIContent("Location", "YooAsset 资源地址（文件名，不含路径和扩展名）"));
        EditorGUILayout.PropertyField(_alignModeProp, new GUIContent("对齐模式"));
        EditorGUILayout.PropertyField(_registerKeyProp, new GUIContent("注册键", "可选唯一标识键，用于运行时注册表查找"));

        EditorGUILayout.Space(10);
        DrawSeparator();
        EditorGUILayout.Space(4);

        // === 编辑器辅助区域 ===
        EditorGUILayout.LabelField("编辑器辅助（不参与打包）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "预制体引用以 GUID 字符串形式保存（非对象引用），不会序列化为 PPtr，" +
            "因此场景与预制体之间不产生任何 Bundle 依赖。",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        var newPrefab = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("预制体引用", "拖入预制体以便快速填充 location / registerKey 和放置预览（仅存 GUID，不产生依赖）"),
            _target.EditorPrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Set Prefab Reference");
            _target.EditorPrefab = newPrefab;
            // 自动填充 location 和 registerKey
            if (newPrefab != null)
            {
                if (string.IsNullOrEmpty(_target.location))
                {
                    _target.location = newPrefab.name;
                }
                if (string.IsNullOrEmpty(_target.registerKey))
                {
                    _target.registerKey = newPrefab.name;
                }
            }
            EditorUtility.SetDirty(_target);
        }

        EditorGUILayout.Space(6);

        // === 操作按钮 ===
        DrawActionButtons();

        EditorGUILayout.Space(6);

        // === 预览实例状态 ===
        DrawPreviewStatus();

        // === 预制体缩略图预览 ===
        DrawPrefabThumbnail();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawActionButtons()
    {
        bool hasPrefabRef = _target.EditorPrefab != null;
        bool hasPreview = _target.previewInstance != null;

        EditorGUILayout.BeginHorizontal();
        {
            // 放置预览
            EditorGUI.BeginDisabledGroup(!hasPrefabRef);
            var placeColor = GUI.backgroundColor;
            GUI.backgroundColor = hasPreview ? new Color(1f, 0.9f, 0.5f) : new Color(0.5f, 1f, 0.6f);
            var placeLabel = hasPreview ? "重新放置" : "放置预览";
            if (GUILayout.Button(new GUIContent(placeLabel, "在占位节点下实例化预制体预览（不会保存到场景）"), GUILayout.Height(24)))
            {
                PlacePreview();
            }
            GUI.backgroundColor = placeColor;
            EditorGUI.EndDisabledGroup();

            // 移除预览
            EditorGUI.BeginDisabledGroup(!hasPreview);
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button(new GUIContent("移除预览", "销毁当前预览实例"), GUILayout.Height(24)))
            {
                RemovePreview();
            }
            GUI.backgroundColor = placeColor;
            EditorGUI.EndDisabledGroup();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        EditorGUILayout.BeginHorizontal();
        {
            // 填充 location
            EditorGUI.BeginDisabledGroup(!hasPrefabRef);
            if (GUILayout.Button(new GUIContent("填充 Location", "从预制体名称填充 location 字段"), GUILayout.Height(20)))
            {
                Undo.RecordObject(_target, "Fill Location");
                _target.location = _target.EditorPrefab.name;
                EditorUtility.SetDirty(_target);
            }
            EditorGUI.EndDisabledGroup();

            // 填充注册键
            bool canFillKey = !string.IsNullOrEmpty(_target.location);
            EditorGUI.BeginDisabledGroup(!canFillKey);
            if (GUILayout.Button(new GUIContent("填充注册键", "将 registerKey 设为当前 location 值"), GUILayout.Height(20)))
            {
                Undo.RecordObject(_target, "Fill RegisterKey");
                _target.registerKey = _target.location;
                EditorUtility.SetDirty(_target);
            }
            EditorGUI.EndDisabledGroup();

            // 对齐节点名
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_target.location));
            if (GUILayout.Button(new GUIContent("对齐节点名", "将 GameObject 名称设为 [Spawn] {location}"), GUILayout.Height(20)))
            {
                Undo.RecordObject(_target.gameObject, "Rename SpawnPoint");
                _target.gameObject.name = $"[Spawn] {_target.location}";
                EditorUtility.SetDirty(_target.gameObject);
            }
            EditorGUI.EndDisabledGroup();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPreviewStatus()
    {
        if (_target.previewInstance != null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var style = new GUIStyle(EditorStyles.miniLabel) { richText = true };
            EditorGUILayout.LabelField(
                $"<color=#80FF80>● 预览已放置</color>  —  {_target.previewInstance.name}",
                style);
            EditorGUILayout.LabelField(
                "预览实例标记为 HideFlags.DontSave，不会保存到场景文件。",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
    }

    private void DrawPrefabThumbnail()
    {
        var prefab = _target.EditorPrefab;
        if (prefab == null) return;

        EditorGUILayout.Space(6);
        var texture = AssetPreview.GetAssetPreview(prefab);
        if (texture != null)
        {
            EditorGUILayout.LabelField("预制体预览", EditorStyles.miniLabel);
            var rect = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
        }
    }

    #region 放置/移除逻辑

    private void PlacePreview()
    {
        // 先移除旧的
        RemovePreview();

        var prefab = _target.EditorPrefab;
        if (prefab == null) return;

        // 实例化
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _target.transform);
        instance.name = $"[Preview] {prefab.name}";

        // 根据对齐模式设置 TRS
        if (_target.alignMode == SpawnAlignMode.AlignToPlaceholder)
        {
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }
        // KeepPrefabLocal 模式下保留预制体自身的 local TRS，不做修改

        // 标记为不保存——不会污染场景文件
        instance.hideFlags = HideFlags.DontSave;
        SetHideFlagsRecursive(instance, HideFlags.DontSave);

        _target.previewInstance = instance;

        // 自动填充 location（如果为空）
        if (string.IsNullOrEmpty(_target.location))
        {
            Undo.RecordObject(_target, "Auto Fill Location on Place");
            _target.location = prefab.name;
            EditorUtility.SetDirty(_target);
        }

        SceneView.RepaintAll();
        Debug.Log($"[DynamicSpawnPoint] 已放置预览: {instance.name}");
    }

    private void RemovePreview()
    {
        if (_target.previewInstance != null)
        {
            var instanceName = _target.previewInstance.name;
            DestroyImmediate(_target.previewInstance);
            _target.previewInstance = null;
            SceneView.RepaintAll();
            Debug.Log($"[DynamicSpawnPoint] 已移除预览: {instanceName}");
        }
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

    #region 辅助绘制

    private static void DrawSeparator()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1f);
        rect.height = 1f;
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
    }

    #endregion
}
