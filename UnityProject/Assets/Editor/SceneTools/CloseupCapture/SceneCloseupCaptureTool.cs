using System;
using System.IO;
using System.Text;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 场景特写拍摄工具：根据目标包围盒自动摆放临时相机，并输出 PNG。
/// 窗口打开期间在场景中维护一个可见的临时相机；关闭窗口后相机销毁。
/// 基于 Odin 特性声明式绘制，支持实时预览。
/// </summary>
public sealed class SceneCloseupCaptureTool : OdinEditorWindow
{
    public enum ViewPreset
    {
        Front,
        Back,
        Left,
        Right,
        Top,
        Bottom,
        ThreeQuarter,
        Custom
    }

    private const string MenuPath = "Tools/场景工具/特写拍摄工具";
    private const string OutputDirectoryPrefsKey = "SceneCloseupCaptureTool.OutputDirectory";
    private const string DefaultOutputDirectory = "Assets/AssetRaw/UIRaw/Raw/Screenshots/Closeups";
    private const string TemporaryCameraName = "__SceneCloseupCaptureCamera";

    private const float LivePreviewInterval = 0.1f; // 实时预览最小刷新间隔（秒）

    // ============================================================ 目标

    [BoxGroup("目标"), SceneObjectsOnly, LabelText("目标对象")]
    [OnValueChanged("MarkPreviewDirty")]
    public Transform target;

    [BoxGroup("目标"), LabelText("包含隐藏子节点 Renderer"), ToggleLeft]
    [OnValueChanged("MarkPreviewDirty")]
    public bool includeInactiveRenderers;

    [BoxGroup("目标"), Button("使用当前选中对象", ButtonSizes.Medium)]
    public void UseCurrentSelection()
    {
        BindTarget(Selection.activeTransform);
        MarkPreviewDirty();
    }

    [BoxGroup("目标"), Button("聚焦相机到 SceneView", ButtonSizes.Medium), GUIColor(0.6f, 1f, 0.6f)]
    public void FocusCameraButton()
    {
        FocusCameraInSceneView();
    }

    [BoxGroup("目标"), ShowIf("@target != null && HasBounds")]
    [ShowInInspector, ReadOnly, LabelText("包围盒中心")]
    private string BoundsCenterText => HasBounds ? FormatVector3(CurrentBounds.center) : "—";

    [BoxGroup("目标"), ShowIf("@target != null && HasBounds")]
    [ShowInInspector, ReadOnly, LabelText("包围盒尺寸")]
    private string BoundsSizeText => HasBounds ? FormatVector3(CurrentBounds.size) : "—";

    // ============================================================ 构图参数

    [BoxGroup("构图参数"), EnumToggleButtons, LabelText("视角")]
    [OnValueChanged("MarkPreviewDirty")]
    public ViewPreset viewPreset = ViewPreset.ThreeQuarter;

    [BoxGroup("构图参数"), LabelText("正交相机"), ToggleLeft]
    [OnValueChanged("OnUseOrthographicChanged")]
    public bool useOrthographic;

    [BoxGroup("构图参数"), Range(10f, 90f), LabelText("FOV"), DisableIf("useOrthographic")]
    [OnValueChanged("MarkPreviewDirty")]
    public float fieldOfView = 35f;

    [BoxGroup("构图参数"), PropertyRange(1f, 2.5f), LabelText("边距")]
    [OnValueChanged("MarkPreviewDirty")]
    public float padding = 1.15f;

    [BoxGroup("构图参数"), LabelText("距离微调")]
    [OnValueChanged("MarkPreviewDirty")]
    public float distanceOffset;

    [BoxGroup("构图参数"), LabelText("中心偏移")]
    [OnValueChanged("MarkPreviewDirty")]
    public Vector3 centerOffset;

    [BoxGroup("构图参数"), LabelText("旋转微调")]
    [OnValueChanged("MarkPreviewDirty")]
    public Vector3 rotationOffset;

    [BoxGroup("构图参数"), LabelText("透明背景"), ToggleLeft]
    [OnValueChanged("MarkPreviewDirty")]
    public bool transparentBackground = true;

    [BoxGroup("构图参数"), ColorUsage(true, false), LabelText("背景颜色"), DisableIf("transparentBackground")]
    [OnValueChanged("MarkPreviewDirty")]
    public Color backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);

    [BoxGroup("构图参数"), LabelText("场景中显示相机"), ToggleLeft]
    public bool showCameraGizmo = true;

    [BoxGroup("构图参数"), Button("从 SceneView 取角度", ButtonSizes.Medium)]
    public void ApplySceneViewPoseButton()
    {
        ApplySceneViewPose();
        MarkPreviewDirty();
    }

    // ============================================================ 角度预设

    [BoxGroup("角度预设"), LabelText("预设资产")]
    [OnValueChanged("OnAnglePresetChanged")]
    public CloseupAnglePreset anglePreset;

    [BoxGroup("角度预设"), Button("应用预设", ButtonSizes.Medium), GUIColor(0.4f, 0.8f, 1f)]
    [EnableIf("@anglePreset != null && target != null")]
    public void ApplyAnglePreset()
    {
        if (anglePreset == null || target == null || _captureCamera == null)
        {
            return;
        }

        RefreshBoundsCache();
        Vector3 targetCenter = GetTargetCenter();
        float radius = GetTargetRadius();
        Quaternion targetRot = target.rotation;

        // 还原世界坐标位姿
        Vector3 worldDir = targetRot * anglePreset.relativeDirection.normalized;
        float distance = radius * anglePreset.distanceMultiplier;
        Vector3 camPos = targetCenter + worldDir * distance;
        Quaternion camRot = targetRot * anglePreset.relativeRotation;

        _captureCamera.transform.SetPositionAndRotation(camPos, camRot);
        _captureCamera.orthographic = anglePreset.isOrthographic;
        _captureCamera.orthographicSize = radius * anglePreset.orthographicSizeMultiplier;
        _captureCamera.fieldOfView = anglePreset.fieldOfView;

        // 同步面板参数
        useOrthographic = anglePreset.isOrthographic;
        fieldOfView = anglePreset.fieldOfView;
        padding = anglePreset.padding;

        // 切到 Custom 并记录位姿
        _customPosition = camPos;
        _customRotation = camRot;
        _customOrthographicSize = _captureCamera.orthographicSize;
        viewPreset = ViewPreset.Custom;

        MarkPreviewDirty();
    }

    [BoxGroup("角度预设"), Button("保存当前角度为预设", ButtonSizes.Medium), GUIColor(0.3f, 0.9f, 0.5f)]
    [EnableIf("@target != null")]
    public void SaveCurrentAngleAsPreset()
    {
        if (target == null || _captureCamera == null)
        {
            return;
        }

        RefreshBoundsCache();
        Vector3 targetCenter = GetTargetCenter();
        float radius = GetTargetRadius();
        Quaternion targetRot = target.rotation;

        Vector3 camPos = _captureCamera.transform.position;
        Quaternion camRot = _captureCamera.transform.rotation;

        // 计算相对数据
        Vector3 offset = camPos - targetCenter;
        Vector3 relDir = Quaternion.Inverse(targetRot) * offset.normalized;
        float distMul = offset.magnitude / Mathf.Max(radius, 0.001f);
        Quaternion relRot = Quaternion.Inverse(targetRot) * camRot;

        // 弹出保存对话框
        string defaultDir = "Assets/Editor/SceneTools/CloseupAnglePresets";
        if (!System.IO.Directory.Exists(ToAbsolutePath(defaultDir)))
        {
            System.IO.Directory.CreateDirectory(ToAbsolutePath(defaultDir));
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "保存角度预设", "NewAnglePreset", "asset", "选择保存位置", defaultDir);

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        CloseupAnglePreset preset = ScriptableObject.CreateInstance<CloseupAnglePreset>();
        preset.presetName = System.IO.Path.GetFileNameWithoutExtension(path);
        preset.relativeDirection = relDir;
        preset.distanceMultiplier = distMul;
        preset.relativeRotation = relRot;
        preset.fieldOfView = _captureCamera.fieldOfView;
        preset.isOrthographic = _captureCamera.orthographic;
        preset.orthographicSizeMultiplier = _captureCamera.orthographicSize / Mathf.Max(radius, 0.001f);
        preset.padding = padding;

        AssetDatabase.CreateAsset(preset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        anglePreset = preset;
        Debug.Log($"角度预设已保存：{path}");
    }

    [BoxGroup("角度预设"), Button("覆盖保存到当前预设", ButtonSizes.Small)]
    [EnableIf("@anglePreset != null && target != null")]
    public void OverwriteCurrentPreset()
    {
        if (anglePreset == null || target == null || _captureCamera == null)
        {
            return;
        }

        RefreshBoundsCache();
        Vector3 targetCenter = GetTargetCenter();
        float radius = GetTargetRadius();
        Quaternion targetRot = target.rotation;

        Vector3 camPos = _captureCamera.transform.position;
        Quaternion camRot = _captureCamera.transform.rotation;

        Vector3 offset = camPos - targetCenter;

        Undo.RecordObject(anglePreset, "覆盖角度预设");
        anglePreset.relativeDirection = (Quaternion.Inverse(targetRot) * offset.normalized);
        anglePreset.distanceMultiplier = offset.magnitude / Mathf.Max(radius, 0.001f);
        anglePreset.relativeRotation = Quaternion.Inverse(targetRot) * camRot;
        anglePreset.fieldOfView = _captureCamera.fieldOfView;
        anglePreset.isOrthographic = _captureCamera.orthographic;
        anglePreset.orthographicSizeMultiplier = _captureCamera.orthographicSize / Mathf.Max(radius, 0.001f);
        anglePreset.padding = padding;
        EditorUtility.SetDirty(anglePreset);
        AssetDatabase.SaveAssets();

        Debug.Log($"角度预设已覆盖保存：{anglePreset.presetName}");
    }

    private void OnAnglePresetChanged()
    {
        // 选择预设后自动应用
        if (anglePreset != null && target != null)
        {
            ApplyAnglePreset();
        }
    }

    // ============================================================ 相机

    [BoxGroup("相机"), Button("选中专用相机（在 Inspector 中编辑）", ButtonSizes.Medium), GUIColor(1f, 0.9f, 0.6f)]
    public void SelectCaptureCamera()
    {
        if (_captureCamera != null)
        {
            // 完全解除 hideFlags，确保 Inspector 中可自由编辑所有组件参数
            _captureCamera.gameObject.hideFlags = HideFlags.None;
            _captureCamera.hideFlags = HideFlags.None;
            Selection.activeGameObject = _captureCamera.gameObject;
            EditorGUIUtility.PingObject(_captureCamera.gameObject);
        }
    }

    [BoxGroup("相机"), LabelText("抗锯齿"), ValueDropdown("AntiAliasingOptions")]
    [OnValueChanged("MarkPreviewDirty")]
    public int antiAliasing = 8;

    private static ValueDropdownList<int> AntiAliasingOptions = new ValueDropdownList<int>
    {
        { "关闭 (1x)", 1 },
        { "2x", 2 },
        { "4x", 4 },
        { "8x", 8 },
    };

    // ============================================================ 输出

    [BoxGroup("输出"), LabelText("宽")]
    [OnValueChanged("MarkPreviewDirty")]
    public int width = 1920;

    [BoxGroup("输出"), LabelText("高")]
    [OnValueChanged("MarkPreviewDirty")]
    public int height = 1080;

    [BoxGroup("输出"), ButtonGroup("输出/Res"), Button("1024×1024")]
    public void SetResolution1024() => SetResolution(1024, 1024);

    [BoxGroup("输出"), ButtonGroup("输出/Res"), Button("1920×1080")]
    public void SetResolution1920() => SetResolution(1920, 1080);

    [BoxGroup("输出"), ButtonGroup("输出/Res"), Button("2048×2048")]
    public void SetResolution2048() => SetResolution(2048, 2048);

    [BoxGroup("输出"), LabelText("输出目录")]
    public string outputDirectory = DefaultOutputDirectory;

    [BoxGroup("输出"), Button("选择", ButtonSizes.Small)]
    public void ChooseOutputDirectoryButton()
    {
        ChooseOutputDirectory();
    }

    [BoxGroup("输出"), LabelText("自定义文件名")]
    [InfoBox("填写后直接使用该名称保存（不含扩展名），留空则使用下方规则自动生成。", InfoMessageType.None)]
    public string customFileName = string.Empty;

    [BoxGroup("输出"), LabelText("文件名前缀"), ShowIf("@string.IsNullOrWhiteSpace(customFileName)")]
    [OnValueChanged("OnFileNamePrefixChanged")]
    public string fileNamePrefix = string.Empty;

    [BoxGroup("输出"), LabelText("追加时间戳"), ToggleLeft, ShowIf("@string.IsNullOrWhiteSpace(customFileName)")]
    public bool appendTimestamp = true;

    [BoxGroup("输出"), LabelText("同名文件直接覆盖"), ToggleLeft]
    public bool overwriteExisting = false;

    [BoxGroup("输出"), ShowInInspector, ReadOnly, LabelText("最终文件名")]
    private string FinalFileNameText => GetFinalFileName() + ".png";

    // ============================================================ 预览

    [BoxGroup("预览"), LabelText("实时预览"), ToggleLeft]
    [OnValueChanged("OnLivePreviewChanged")]
    public bool livePreview = true;

    [BoxGroup("预览"), Button("刷新预览", ButtonSizes.Medium), GUIColor(0.4f, 0.8f, 1f)]
    [EnableIf("@target != null")]
    public void RefreshPreviewButton()
    {
        RefreshPreview(force: true);
    }

    [BoxGroup("预览"), Button("清除预览", ButtonSizes.Small), EnableIf("@previewTexture != null")]
    public void ClearPreviewButton()
    {
        ClearPreviewTexture();
        previewMessage = "点击 \"刷新预览\" 查看当前构图。";
    }

    // ============================================================ 拍摄

    [TitleGroup("操作")]
    [Button("拍摄并保存 PNG", ButtonSizes.Large), GUIColor(1f, 0.85f, 0.4f)]
    [EnableIf("@target != null")]
    public void CaptureAndSaveButton()
    {
        CaptureAndSave();
    }

    // ============================================================ 非序列化运行时状态

    [NonSerialized] private Camera _captureCamera;
    [NonSerialized] private Texture2D previewTexture;
    [NonSerialized] private string previewMessage = "点击 \"刷新预览\" 查看当前构图。";
    [NonSerialized] private bool _previewDirty = true;
    [NonSerialized] private double _lastLivePreviewTime;

    // 自定义模式位姿（仅 Custom 视角使用，非序列化）
    [NonSerialized] private Vector3 _customPosition;
    [NonSerialized] private Quaternion _customRotation = Quaternion.identity;
    [NonSerialized] private float _customOrthographicSize = 1f;

    // 为 Odin 只读显示提供当前包围盒
    private Bounds _currentBounds;
    private bool _hasBounds;

    // ============================================================ 窗口生命周期

    [MenuItem(MenuPath)]
    private static void Open()
    {
        var window = GetWindow<SceneCloseupCaptureTool>();
        window.titleContent = new GUIContent("特写拍摄工具", EditorGUIUtility.IconContent("Camera Icon").image);
        window.minSize = new Vector2(420, 560);
        window.Show();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        outputDirectory = EditorPrefs.GetString(OutputDirectoryPrefsKey, DefaultOutputDirectory);

        if (target == null && Selection.activeTransform != null)
        {
            BindTarget(Selection.activeTransform);
        }

        EnsureCaptureCamera();
        RefreshBoundsCache();
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= OnEditorUpdate;
        ClearPreviewTexture();
        DestroyCaptureCamera();
    }

    /// <summary>
    /// Odin 绘制入口：先绘制所有特性字段，再追加实时预览画面。
    /// </summary>
    protected override void OnImGUI()
    {
        base.OnImGUI();

        // 相机姿态始终跟随目标（即便不在预览中，SceneView 的 Gizmo 也要正确）
        if (target != null)
        {
            UpdateCameraPose();
        }
        else if (previewTexture != null)
        {
            // 目标被清空时清掉残留预览
            ClearPreviewTexture();
            previewMessage = "请选择一个场景对象。";
        }

        // 实时预览：仅在标记为脏时重渲染，避免每次鼠标移动 Repaint 都触发相机渲染。
        // 实时模式由 OnEditorUpdate 按固定节拍置脏驱动；参数编辑由 OnValueChanged 置脏。
        if (target != null && _previewDirty)
        {
            RefreshPreview(force: false);
            _previewDirty = false;
        }

        DrawPreviewImage();
    }

    // ---------------------------------------------------------------- 预览绘制

    private void DrawPreviewImage()
    {
        SirenixEditorGUI.DrawThickHorizontalSeparator();
        EditorGUILayout.Space(4);

        if (previewTexture == null)
        {
            EditorGUILayout.HelpBox(previewMessage, MessageType.Info);
            return;
        }

        // 自适应窗口宽度，居中绘制
        float availableWidth = EditorGUIUtility.currentViewWidth - 20f;
        float aspect = Mathf.Max(0.0001f, (float)previewTexture.width / previewTexture.height);
        float displayWidth = Mathf.Min(availableWidth, previewTexture.width);
        float displayHeight = displayWidth / aspect;

        // 限制最大高度，避免超长预览图把窗口撑爆
        float maxHeight = 400f;
        if (displayHeight > maxHeight)
        {
            displayHeight = maxHeight;
            displayWidth = displayHeight * aspect;
        }

        Rect rect = GUILayoutUtility.GetRect(displayWidth, displayHeight,
            GUILayout.ExpandWidth(true), GUILayout.Height(displayHeight));

        // 居中：如果可用宽度大于图片宽度，把 rect 居中偏移
        if (rect.width > displayWidth)
        {
            rect.x += (rect.width - displayWidth) * 0.5f;
            rect.width = displayWidth;
        }

        // 棋盘格背景（标识透明区域）
        if (transparentBackground)
        {
            DrawCheckerboard(rect, 8);
        }
        else
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));
        }

        GUI.DrawTexture(rect, previewTexture, ScaleMode.ScaleToFit, true);

        // 底部信息
        EditorGUILayout.LabelField(
            $"预览 {previewTexture.width}×{previewTexture.height}  |  输出 {width}×{height}",
            EditorStyles.centeredGreyMiniLabel);
    }

    /// <summary>绘制棋盘格背景，便于识别 PNG 透明区域。</summary>
    private static void DrawCheckerboard(Rect rect, int cellSize)
    {
        Color dark = new Color(0.25f, 0.25f, 0.25f, 1f);
        Color light = new Color(0.35f, 0.35f, 0.35f, 1f);

        // 先画深色底
        EditorGUI.DrawRect(rect, dark);

        // 再画浅色格子
        int cols = Mathf.CeilToInt(rect.width / cellSize);
        int rows = Mathf.CeilToInt(rect.height / cellSize);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                if ((x + y) % 2 != 0) continue;
                Rect cell = new Rect(
                    rect.x + x * cellSize,
                    rect.y + y * cellSize,
                    Mathf.Min(cellSize, rect.xMax - (rect.x + x * cellSize)),
                    Mathf.Min(cellSize, rect.yMax - (rect.y + y * cellSize)));
                EditorGUI.DrawRect(cell, light);
            }
        }
    }

    // ---------------------------------------------------------------- 编辑器轮询（实时预览节拍 + 跟随场景改动）

    private void OnEditorUpdate()
    {
        if (!livePreview || target == null)
        {
            return;
        }

        // 节流：避免每帧重渲染
        if (EditorApplication.timeSinceStartup - _lastLivePreviewTime < LivePreviewInterval)
        {
            return;
        }

        _lastLivePreviewTime = EditorApplication.timeSinceStartup;
        _previewDirty = true;
        Repaint();
    }

    private void OnLivePreviewChanged()
    {
        if (livePreview)
        {
            _previewDirty = true;
            Repaint();
        }
    }

    // ---------------------------------------------------------------- Odin 回调

    /// <summary>标记预览需要刷新（由各参数 OnValueChanged 触发，实现实时更新）。</summary>
    private void MarkPreviewDirty()
    {
        RefreshBoundsCache();
        _previewDirty = true;
        Repaint();
    }

    private void OnUseOrthographicChanged()
    {
        // 切到正交时记录当前自定义正交尺寸，便于 Custom 模式复用
        if (useOrthographic && _captureCamera != null)
        {
            _customOrthographicSize = Mathf.Max(_captureCamera.orthographicSize, 0.01f);
        }

        MarkPreviewDirty();
    }

    private void OnFileNamePrefixChanged()
    {
        fileNamePrefix = SanitizeFileName(fileNamePrefix);
    }

    // ---------------------------------------------------------------- 持久相机管理

    private void EnsureCaptureCamera()
    {
        if (_captureCamera != null)
        {
            return;
        }

        // 尝试找回上次残留的临时相机
        GameObject existing = GameObject.Find(TemporaryCameraName);
        if (existing != null)
        {
            _captureCamera = existing.GetComponent<Camera>();
            if (_captureCamera != null)
            {
                return;
            }

            DestroyImmediate(existing);
        }

        GameObject cameraObject = new GameObject(TemporaryCameraName)
        {
            hideFlags = HideFlags.DontSave
        };

        _captureCamera = cameraObject.AddComponent<Camera>();
        _captureCamera.enabled = false; // 不参与正常渲染
        _captureCamera.clearFlags = CameraClearFlags.SolidColor;
        _captureCamera.backgroundColor = Color.clear;
        _captureCamera.nearClipPlane = 0.01f;
        _captureCamera.farClipPlane = 5000f;
    }

    private void DestroyCaptureCamera()
    {
        if (_captureCamera == null)
        {
            return;
        }

        DestroyImmediate(_captureCamera.gameObject);
        _captureCamera = null;
    }

    private void UpdateCameraPose()
    {
        if (_captureCamera == null)
        {
            EnsureCaptureCamera();
        }

        if (_captureCamera == null || target == null)
        {
            return;
        }

        // 每帧刷新包围盒缓存
        RefreshBoundsCache();

        // Custom 模式：不覆写相机任何属性，让用户在 Inspector / SceneView 中自由调整
        if (viewPreset == ViewPreset.Custom)
        {
            return;
        }

        if (!TryBuildCameraPose(out Vector3 position, out Quaternion rotation, out float orthographicSize))
        {
            return;
        }

        // 预设模式：只同步位姿和构图直接相关的参数
        _captureCamera.transform.SetPositionAndRotation(position, rotation);
        _captureCamera.orthographic = useOrthographic;
        _captureCamera.orthographicSize = orthographicSize;
        _captureCamera.fieldOfView = fieldOfView;
    }

    private void FocusCameraInSceneView()
    {
        if (_captureCamera == null)
        {
            return;
        }

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            return;
        }

        sceneView.AlignViewToObject(_captureCamera.transform);
        sceneView.Repaint();
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (_captureCamera == null || !showCameraGizmo)
        {
            return;
        }

        // 即使无目标也可拖动：自定义模式下手动调整相机的好场景
        bool canDrag = target != null;

        // ----- 拖动控制器：自由移动 / 自由旋转相机 -----
        if (canDrag)
        {
            Vector3 camPos = _captureCamera.transform.position;
            Quaternion camRot = _captureCamera.transform.rotation;
            float handleSize = HandleUtility.GetHandleSize(camPos);

            // 位置拖动：拖动后切到 Custom 模式，避免被自动构图覆盖
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(camPos, camRot);
            if (EditorGUI.EndChangeCheck() && newPos != camPos)
            {
                Undo.RecordObject(_captureCamera.transform, "拖动特写相机位置");
                _captureCamera.transform.position = newPos;
                SyncCustomFromCamera();
                MarkPreviewDirty();
            }

            // 旋转拖动
            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.RotationHandle(camRot, camPos);
            if (EditorGUI.EndChangeCheck() && newRot != camRot)
            {
                Undo.RecordObject(_captureCamera.transform, "旋转特写相机朝向");
                _captureCamera.transform.rotation = newRot;
                SyncCustomFromCamera();
                MarkPreviewDirty();
            }
        }

        if (target == null)
        {
            return;
        }

        // 绘制相机视锥线框
        Handles.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Matrix4x4 oldMatrix = Handles.matrix;
        Handles.matrix = _captureCamera.transform.localToWorldMatrix;

        float aspect = Mathf.Max(0.0001f, (float)width / height);

        if (_captureCamera.orthographic)
        {
            float orthoSize = _captureCamera.orthographicSize;
            float halfWidth = orthoSize * aspect;
            float near = _captureCamera.nearClipPlane;
            float far = Mathf.Min(_captureCamera.farClipPlane, 100f);

            Vector3 nTL = new Vector3(-halfWidth, orthoSize, near);
            Vector3 nTR = new Vector3(halfWidth, orthoSize, near);
            Vector3 nBL = new Vector3(-halfWidth, -orthoSize, near);
            Vector3 nBR = new Vector3(halfWidth, -orthoSize, near);
            Vector3 fTL = new Vector3(-halfWidth, orthoSize, far);
            Vector3 fTR = new Vector3(halfWidth, orthoSize, far);
            Vector3 fBL = new Vector3(-halfWidth, -orthoSize, far);
            Vector3 fBR = new Vector3(halfWidth, -orthoSize, far);

            // 近平面
            Handles.DrawLine(nTL, nTR);
            Handles.DrawLine(nTR, nBR);
            Handles.DrawLine(nBR, nBL);
            Handles.DrawLine(nBL, nTL);
            // 远平面
            Handles.DrawLine(fTL, fTR);
            Handles.DrawLine(fTR, fBR);
            Handles.DrawLine(fBR, fBL);
            Handles.DrawLine(fBL, fTL);
            // 连线
            Handles.DrawLine(nTL, fTL);
            Handles.DrawLine(nTR, fTR);
            Handles.DrawLine(nBL, fBL);
            Handles.DrawLine(nBR, fBR);
        }
        else
        {
            float near = _captureCamera.nearClipPlane;
            float far = Mathf.Min(_captureCamera.farClipPlane, 100f);
            float halfFovRad = _captureCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float nearH = near * Mathf.Tan(halfFovRad);
            float nearW = nearH * aspect;
            float farH = far * Mathf.Tan(halfFovRad);
            float farW = farH * aspect;

            Vector3 nTL = new Vector3(-nearW, nearH, near);
            Vector3 nTR = new Vector3(nearW, nearH, near);
            Vector3 nBL = new Vector3(-nearW, -nearH, near);
            Vector3 nBR = new Vector3(nearW, -nearH, near);
            Vector3 fTL = new Vector3(-farW, farH, far);
            Vector3 fTR = new Vector3(farW, farH, far);
            Vector3 fBL = new Vector3(-farW, -farH, far);
            Vector3 fBR = new Vector3(farW, -farH, far);

            // 近平面
            Handles.DrawLine(nTL, nTR);
            Handles.DrawLine(nTR, nBR);
            Handles.DrawLine(nBR, nBL);
            Handles.DrawLine(nBL, nTL);
            // 远平面
            Handles.DrawLine(fTL, fTR);
            Handles.DrawLine(fTR, fBR);
            Handles.DrawLine(fBR, fBL);
            Handles.DrawLine(fBL, fTL);
            // 连线
            Handles.DrawLine(nTL, fTL);
            Handles.DrawLine(nTR, fTR);
            Handles.DrawLine(nBL, fBL);
            Handles.DrawLine(nBR, fBR);
        }

        Handles.matrix = oldMatrix;

        // 在相机位置画一个小标签
        Handles.Label(_captureCamera.transform.position, "特写相机", EditorStyles.whiteBoldLabel);
    }

    // ---------------------------------------------------------------- 逻辑

    private void BindTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null && string.IsNullOrWhiteSpace(fileNamePrefix))
        {
            fileNamePrefix = SanitizeFileName(target.name);
        }

        RefreshBoundsCache();
    }

    private void SetResolution(int newWidth, int newHeight)
    {
        width = Mathf.Max(1, newWidth);
        height = Mathf.Max(1, newHeight);
        MarkPreviewDirty();
    }

    private void ChooseOutputDirectory()
    {
        string absoluteStartDirectory = ToAbsolutePath(outputDirectory);
        if (!Directory.Exists(absoluteStartDirectory))
        {
            absoluteStartDirectory = Application.dataPath;
        }

        string selected = EditorUtility.OpenFolderPanel("选择输出目录", absoluteStartDirectory, string.Empty);
        if (string.IsNullOrEmpty(selected))
        {
            return;
        }

        outputDirectory = ToProjectRelativePath(selected);
        EditorPrefs.SetString(OutputDirectoryPrefsKey, outputDirectory);
    }

    private void CaptureAndSave()
    {
        if (target == null)
        {
            return;
        }

        string dir = string.IsNullOrWhiteSpace(outputDirectory) ? DefaultOutputDirectory : outputDirectory;
        string absoluteOutputDirectory = ToAbsolutePath(dir);
        Directory.CreateDirectory(absoluteOutputDirectory);
        EditorPrefs.SetString(OutputDirectoryPrefsKey, dir);

        string filePath = overwriteExisting
            ? Path.Combine(absoluteOutputDirectory, GetFinalFileName() + ".png")
            : GetUniqueAbsoluteFilePath(absoluteOutputDirectory, GetFinalFileName());
        Texture2D texture = RenderCloseup(width, height);

        if (texture == null)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(filePath, texture.EncodeToPNG());
        }
        finally
        {
            DestroyImmediate(texture);
        }

        string projectRelativePath = ToProjectRelativePath(filePath);
        AssetDatabase.Refresh();

        // 将导入的纹理类型设置为 Sprite
        if (projectRelativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            TextureImporter importer = AssetImporter.GetAtPath(projectRelativePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(projectRelativePath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        Debug.Log($"特写截图已保存：{projectRelativePath}");
    }

    /// <summary>
    /// 刷新预览。force=true 强制重渲染；force=false 时若已有纹理且非脏则跳过以省性能。
    /// </summary>
    private void RefreshPreview(bool force)
    {
        if (target == null)
        {
            ClearPreviewTexture();
            previewMessage = "请选择一个场景对象。";
            return;
        }

        GetPreviewResolution(out int previewWidth, out int previewHeight);
        Texture2D newTexture = RenderCloseup(previewWidth, previewHeight);

        if (newTexture == null)
        {
            previewMessage = "预览生成失败。";
            return;
        }

        ClearPreviewTexture();
        previewTexture = newTexture;
        previewMessage = string.Empty;

        if (force)
        {
            Repaint();
        }
    }

    /// <summary>
    /// 使用持久相机渲染一帧并返回 Texture2D。调用方负责销毁返回的贴图。
    /// </summary>
    private Texture2D RenderCloseup(int renderWidth, int renderHeight)
    {
        if (_captureCamera == null)
        {
            EnsureCaptureCamera();
        }

        if (_captureCamera == null)
        {
            return null;
        }

        UpdateCameraPose();

        // 渲染时临时覆写背景色，结束后恢复，不干扰用户在 Inspector 中的编辑
        Color originalBg = _captureCamera.backgroundColor;
        CameraClearFlags originalClearFlags = _captureCamera.clearFlags;
        if (transparentBackground)
        {
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = Color.clear;
        }
        else
        {
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = backgroundColor;
        }

        RenderTexture renderTexture = null;
        Texture2D texture = null;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            renderTexture = new RenderTexture(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = Mathf.Clamp(antiAliasing, 1, 8)
            };

            texture = new Texture2D(renderWidth, renderHeight, TextureFormat.RGBA32, false);
            _captureCamera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            _captureCamera.Render();
            texture.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
            texture.Apply(false);
        }
        catch
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
                texture = null;
            }
        }
        finally
        {
            RenderTexture.active = previousActive;
            _captureCamera.targetTexture = null;

            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyImmediate(renderTexture);
            }

            // 恢复相机原始状态，不干扰用户在 Inspector 中的设置
            _captureCamera.backgroundColor = originalBg;
            _captureCamera.clearFlags = originalClearFlags;
        }

        return texture;
    }

    private void GetPreviewResolution(out int previewWidth, out int previewHeight)
    {
        const int maxPreviewSize = 512;
        float aspect = Mathf.Max(0.0001f, (float)width / height);

        if (aspect >= 1f)
        {
            previewWidth = maxPreviewSize;
            previewHeight = Mathf.Max(1, Mathf.RoundToInt(maxPreviewSize / aspect));
        }
        else
        {
            previewHeight = maxPreviewSize;
            previewWidth = Mathf.Max(1, Mathf.RoundToInt(maxPreviewSize * aspect));
        }
    }

    private void ClearPreviewTexture()
    {
        if (previewTexture == null)
        {
            return;
        }

        DestroyImmediate(previewTexture);
        previewTexture = null;
    }

    // ---------------------------------------------------------------- 相机姿态计算

    private bool TryBuildCameraPose(out Vector3 position, out Quaternion rotation, out float orthographicSize)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        orthographicSize = 1f;

        if (target == null)
        {
            return false;
        }

        // 自定义模式：直接使用手动记录的位姿，不再按预设自动计算
        if (viewPreset == ViewPreset.Custom)
        {
            position = _customPosition;
            rotation = _customRotation;
            orthographicSize = Mathf.Max(_customOrthographicSize, 0.01f);
            return true;
        }

        Vector3 center = GetTargetCenter() + centerOffset;
        float radius = GetTargetRadius();
        Vector3 direction = GetViewDirection();

        if (direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        direction.Normalize();

        float aspect = Mathf.Max(0.0001f, (float)width / height);
        float verticalFov = fieldOfView * Mathf.Deg2Rad;
        float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f) * aspect);
        float verticalDistance = radius / Mathf.Tan(verticalFov * 0.5f);
        float horizontalDistance = radius / Mathf.Tan(horizontalFov * 0.5f);
        float distance = Mathf.Max(verticalDistance, horizontalDistance) * padding + distanceOffset;
        distance = Mathf.Max(0.01f, distance);

        orthographicSize = Mathf.Max(radius * padding, 0.01f);
        position = center - direction * distance;
        rotation = GetCameraRotation(direction) * Quaternion.Euler(rotationOffset);
        return true;
    }

    private static Quaternion GetCameraRotation(Vector3 direction)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.95f
            ? Vector3.forward
            : Vector3.up;
        return Quaternion.LookRotation(direction, up);
    }

    private Vector3 GetViewDirection()
    {
        switch (viewPreset)
        {
            case ViewPreset.Front:
                return Vector3.back;
            case ViewPreset.Back:
                return Vector3.forward;
            case ViewPreset.Left:
                return Vector3.right;
            case ViewPreset.Right:
                return Vector3.left;
            case ViewPreset.Top:
                return Vector3.down;
            case ViewPreset.Bottom:
                return Vector3.up;
            case ViewPreset.ThreeQuarter:
            default:
                return new Vector3(-1f, -0.45f, -1f).normalized;
        }
    }

    private Vector3 GetTargetCenter()
    {
        return HasBounds ? CurrentBounds.center : target.position;
    }

    private float GetTargetRadius()
    {
        if (!HasBounds)
        {
            return 1f;
        }

        return Mathf.Max(CurrentBounds.extents.magnitude, 0.01f);
    }

    private bool TryGetTargetBounds(out Bounds bounds)
    {
        bounds = default;

        if (target == null)
        {
            return false;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(includeInactiveRenderers);
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    /// <summary>缓存当前包围盒，供 Odin 只读字段与姿态计算共用，避免每帧重复遍历。</summary>
    private void RefreshBoundsCache()
    {
        _hasBounds = TryGetTargetBounds(out _currentBounds);
    }

    private bool HasBounds => _hasBounds;
    private Bounds CurrentBounds => _currentBounds;

    private void ApplySceneViewPose()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null || _captureCamera == null)
        {
            return;
        }

        _captureCamera.transform.SetPositionAndRotation(
            sceneView.camera.transform.position,
            sceneView.camera.transform.rotation);
        _captureCamera.fieldOfView = sceneView.camera.fieldOfView;
        _captureCamera.orthographic = sceneView.camera.orthographic;
        _captureCamera.orthographicSize = sceneView.camera.orthographicSize;

        useOrthographic = _captureCamera.orthographic;
        fieldOfView = _captureCamera.fieldOfView;

        SyncCustomFromCamera();
        Repaint();
    }

    /// <summary>
    /// 将当前持久相机的位姿回写到自定义模式字段并切换视角为 Custom，
    /// 使后续 OnImGUI 的 UpdateCameraPose 不会再按预设自动构图覆盖手动调整。
    /// </summary>
    private void SyncCustomFromCamera()
    {
        if (_captureCamera == null)
        {
            return;
        }

        _customPosition = _captureCamera.transform.position;
        _customRotation = _captureCamera.transform.rotation;
        _customOrthographicSize = Mathf.Max(_captureCamera.orthographicSize, 0.01f);
        viewPreset = ViewPreset.Custom;
    }

    // ---------------------------------------------------------------- 文件名与路径工具

    private string GetFinalFileName()
    {
        // 有自定义文件名时直接使用
        if (!string.IsNullOrWhiteSpace(customFileName))
        {
            return SanitizeFileName(customFileName);
        }

        return GetBaseFileName();
    }

    private string GetBaseFileName()
    {
        string prefix = string.IsNullOrWhiteSpace(fileNamePrefix)
            ? (target != null ? target.name : "Closeup")
            : fileNamePrefix;

        StringBuilder builder = new StringBuilder();
        builder.Append(SanitizeFileName(prefix));
        builder.Append('_');
        builder.Append(viewPreset);

        if (appendTimestamp)
        {
            builder.Append('_');
            builder.Append(DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        }

        return builder.ToString();
    }

    private static string GetUniqueAbsoluteFilePath(string absoluteDirectory, string baseFileName)
    {
        string filePath = Path.Combine(absoluteDirectory, baseFileName + ".png");
        if (!File.Exists(filePath))
        {
            return filePath;
        }

        for (int index = 1; index < 1000; index++)
        {
            string candidate = Path.Combine(absoluteDirectory, $"{baseFileName}_{index:000}.png");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(absoluteDirectory, $"{baseFileName}_{Guid.NewGuid():N}.png");
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Closeup";
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new StringBuilder(fileName.Length);
        foreach (char character in fileName)
        {
            builder.Append(Array.IndexOf(invalidChars, character) >= 0 ? '_' : character);
        }

        return builder.ToString().Trim();
    }

    private static string ToAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Application.dataPath;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }

    private static string ToProjectRelativePath(string absolutePath)
    {
        string fullPath = Path.GetFullPath(absolutePath).Replace('\\', '/');
        string projectPath = Path.GetFullPath(Directory.GetCurrentDirectory()).Replace('\\', '/');

        if (fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath.Substring(projectPath.Length).TrimStart('/');
        }

        return fullPath;
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"{value.x:F3}, {value.y:F3}, {value.z:F3}";
    }
}
