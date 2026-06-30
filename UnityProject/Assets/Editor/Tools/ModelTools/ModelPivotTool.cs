using UnityEditor;
using UnityEngine;

/// <summary>
/// 模型原点工具：重新设置物体的原点（位置 + 旋转），先测试验证再生成新原点。
/// 工作流：1) 设置新原点  2) 旋转测试验证轴心/轴向  3) 测试通过后生成新原点父节点。
/// </summary>
public sealed class ModelPivotTool : EditorWindow
{
    private enum ParentMode
    {
        KeepWorldParent,
        UseParentOverride
    }

    private enum PickMode
    {
        None,
        Surface,
        PointA,
        PointB,
        PointC
    }

    private enum AlignAxis
    {
        PositiveZ,
        NegativeZ,
        PositiveY,
        NegativeY,
        PositiveX,
        NegativeX
    }

    private const string MenuPath = "Tools/模型工具/模型原点 - 新建父节点改轴心";

    private static readonly GUIContent[] ParentModeLabels =
    {
        new GUIContent("保持原父级", "新原点父节点放在目标当前父级下。"),
        new GUIContent("指定新父级", "手动指定新原点父节点的父级。")
    };

    private static readonly GUIContent UseSpecifiedTargetLabel =
        new GUIContent("指定目标物体（开启后不跟随当前选中）", "目标由下方字段指定，切换场景/层级选中物体不会自动替换目标。");

    private static readonly string[] AlignAxisLabels =
    {
        "+Z (前方)", "-Z (后方)", "+Y (上方)", "-Y (下方)", "+X (右方)", "-X (左方)"
    };

    // 目标与新原点姿态
    private Transform _target;
    private Vector3 _pivotPosition;
    private Vector3 _pivotEuler;
    private Transform _referenceTransform;

    // 拾取状态
    private PickMode _pickMode = PickMode.None;
    private AlignAxis _surfaceAlignAxis = AlignAxis.PositiveZ;
    private AlignAxis _primaryAxis = AlignAxis.PositiveZ;
    private bool _snapToVertex = true;
    private Vector3 _pickPointA;
    private Vector3 _pickPointB;
    private Vector3 _pickPointC;
    private bool _hasPointA;
    private bool _hasPointB;
    private bool _hasPointC;
    private Vector3 _pickPreviewPoint;
    private Vector3 _pickPreviewNormal;
    private bool _hasPickPreview;

    // 测试（旋转预览）状态
    private bool _testing;
    private bool _testPassed;
    private Transform _testTarget;
    private Vector3 _testOriginalPosition;
    private Quaternion _testOriginalRotation = Quaternion.identity;
    private Vector3 _testOriginalScale = Vector3.one;
    private Vector3 _testMoveOffset;
    private Vector3 _testEuler;
    private Vector3 _testPivotPosition;
    private Quaternion _testPivotRotation = Quaternion.identity;

    // 生成选项
    private ParentMode _parentMode = ParentMode.KeepWorldParent;
    private Transform _parentOverride;
    private bool _selectCreatedRoot = true;

    // 显示
    private bool _useSpecifiedTarget = true;
    private bool _showSceneHandle = true;
    private bool _hasHiddenUnityTool;
    private bool _previousToolsHidden;
    private Vector2 _scroll;
    private GUIStyle _labelStyle;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        GetWindow<ModelPivotTool>("模型原点");
    }

    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
        SceneView.duringSceneGui += OnSceneGUI;

        if (_target == null && Selection.activeTransform != null)
        {
            BindTarget(Selection.activeTransform);
        }
    }

    private void OnDisable()
    {
        CancelPick();
        StopTest(true);
        SetUnityTransformHandleHidden(false);
        Selection.selectionChanged -= OnSelectionChanged;
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // ---------------------------------------------------------------- GUI

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawTargetSection();

        using (new EditorGUI.DisabledScope(_target == null))
        {
            DrawStep1SetPivot();
            DrawStep2Test();
            DrawStep3Generate();
        }

        EditorGUILayout.EndScrollView();

        UpdateUnityTransformHandleVisibility();
    }

    private void DrawTargetSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("目标对象", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _useSpecifiedTarget = EditorGUILayout.Toggle(UseSpecifiedTargetLabel, _useSpecifiedTarget);
        if (EditorGUI.EndChangeCheck() && !_useSpecifiedTarget && Selection.activeTransform != null && Selection.activeTransform != _target)
        {
            StopTest(true);
            BindTarget(Selection.activeTransform);
        }

        using (new EditorGUI.DisabledScope(!_useSpecifiedTarget))
        {
            EditorGUI.BeginChangeCheck();
            Transform next = (Transform)EditorGUILayout.ObjectField("目标", _target, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck() && next != _target)
            {
                StopTest(true);
                BindTarget(next);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("使用当前选中对象"))
            {
                StopTest(true);
                BindTarget(Selection.activeTransform);
            }

            using (new EditorGUI.DisabledScope(_target == null))
            {
                if (GUILayout.Button("取消使用当前使用对象"))
                {
                    StopTest(true);
                    CancelPick();
                    BindTarget(null);
                }
            }
        }
    }

    private void DrawStep1SetPivot()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("① 设置新原点", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _showSceneHandle = EditorGUILayout.Toggle("显示场景手柄", _showSceneHandle);
        Vector3 nextPosition = EditorGUILayout.Vector3Field("世界位置", _pivotPosition);
        Vector3 nextEuler = EditorGUILayout.Vector3Field("世界旋转", _pivotEuler);
        if (EditorGUI.EndChangeCheck())
        {
            SetPivotPose(nextPosition, nextEuler, true);
        }

        EditorGUILayout.LabelField("快捷定位", EditorStyles.miniBoldLabel);

        bool hasBounds = TryGetRendererBounds(_target, out Bounds bounds);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("目标位置"))
            {
                SetPivotPosition(GetStablePosition(_target));
            }

            if (GUILayout.Button("当前轴心"))
            {
                SetPivotPosition(Tools.handlePosition);
            }

            using (new EditorGUI.DisabledScope(!hasBounds))
            {
                if (GUILayout.Button("包围盒中心"))
                {
                    SetPivotPosition(bounds.center);
                }

                if (GUILayout.Button("底部中心"))
                {
                    SetPivotPosition(GetBoundsPoint(bounds, 0.5f, 0f, 0.5f));
                }
            }
        }

        using (new EditorGUI.DisabledScope(!hasBounds))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("顶部"))
                {
                    SetPivotPosition(GetBoundsPoint(bounds, 0.5f, 1f, 0.5f));
                }

                if (GUILayout.Button("左侧"))
                {
                    SetPivotPosition(GetBoundsPoint(bounds, 0f, 0.5f, 0.5f));
                }

                if (GUILayout.Button("右侧"))
                {
                    SetPivotPosition(GetBoundsPoint(bounds, 1f, 0.5f, 0.5f));
                }

                if (GUILayout.Button("后侧"))
                {
                    SetPivotPosition(GetBoundsPoint(bounds, 0.5f, 0.5f, 0f));
                }

                if (GUILayout.Button("前侧"))
                {
                    SetPivotPosition(GetBoundsPoint(bounds, 0.5f, 0.5f, 1f));
                }
            }
        }

        if (!hasBounds)
        {
            EditorGUILayout.HelpBox("目标层级下没有 Renderer，包围盒快捷定位不可用。", MessageType.None);
        }

        EditorGUILayout.LabelField("旋转", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("对齐目标旋转"))
            {
                SetPivotRotation(GetStableRotation(_target));
            }

            _referenceTransform = (Transform)EditorGUILayout.ObjectField(_referenceTransform, typeof(Transform), true);
            using (new EditorGUI.DisabledScope(_referenceTransform == null))
            {
                if (GUILayout.Button("对齐参考姿态"))
                {
                    SetPivotPose(GetStablePosition(_referenceTransform), GetStableRotation(_referenceTransform).eulerAngles, true);
                }
            }

            if (GUILayout.Button("旋转清零"))
            {
                SetPivotRotation(Quaternion.identity);
            }
        }

        DrawSurfacePick();
        DrawPointOrientation();
    }

    private void DrawSurfacePick()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("表面拾取", EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox("点击模型表面，自动将位置设为命中点，旋转对齐到表面法线。", MessageType.None);

        _surfaceAlignAxis = (AlignAxis)EditorGUILayout.Popup("法线对齐轴", (int)_surfaceAlignAxis, AlignAxisLabels);

        bool isSurfacePicking = _pickMode == PickMode.Surface;
        if (isSurfacePicking)
        {
            GUI.color = new Color(1f, 0.9f, 0.3f);
        }

        if (GUILayout.Button(isSurfacePicking ? "拾取中… (Esc 取消)" : "拾取表面"))
        {
            if (isSurfacePicking)
            {
                CancelPick();
            }
            else
            {
                _pickMode = PickMode.Surface;
                _hasPickPreview = false;
            }
        }

        GUI.color = Color.white;
    }

    private void DrawPointOrientation()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("采点定向", EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox(
            "A = 原点位置\nA→B = 主轴方向\nC = 确定第二轴（可选，不填则自动补）",
            MessageType.None);

        _primaryAxis = (AlignAxis)EditorGUILayout.Popup("主轴", (int)_primaryAxis, AlignAxisLabels);
        _snapToVertex = EditorGUILayout.Toggle("吸附顶点", _snapToVertex);

        DrawPointSlot("点 A", ref _pickPointA, ref _hasPointA, PickMode.PointA);
        DrawPointSlot("点 B", ref _pickPointB, ref _hasPointB, PickMode.PointB);
        DrawPointSlot("点 C (可选)", ref _pickPointC, ref _hasPointC, PickMode.PointC);

        using (new EditorGUI.DisabledScope(!_hasPointA || !_hasPointB))
        {
            if (GUILayout.Button("应用定向"))
            {
                ApplyPointOrientation();
            }
        }

        if (GUILayout.Button("清除采点"))
        {
            _hasPointA = _hasPointB = _hasPointC = false;
            CancelPick();
        }
    }

    private void DrawPointSlot(string label, ref Vector3 point, ref bool hasPoint, PickMode slotMode)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (hasPoint)
            {
                EditorGUILayout.Vector3Field(label, point);
            }
            else
            {
                EditorGUILayout.LabelField(label, "(未设置)");
            }

            bool isPicking = _pickMode == slotMode;
            if (isPicking)
            {
                GUI.color = new Color(1f, 0.9f, 0.3f);
            }

            if (GUILayout.Button(isPicking ? "拾取中…" : "拾取", GUILayout.Width(60f)))
            {
                if (isPicking)
                {
                    CancelPick();
                }
                else
                {
                    _pickMode = slotMode;
                    _hasPickPreview = false;
                }
            }

            GUI.color = Color.white;
        }
    }

    private void DrawStep2Test()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("② 移动/旋转测试", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "按当前新原点临时移动/旋转模型，用来验证轴心和轴向是否正确。测试只是预览，不会修改模型；结束测试、切换目标或关闭窗口都会恢复原状。",
            MessageType.None);

        EditorGUI.BeginChangeCheck();
        _testMoveOffset = EditorGUILayout.Vector3Field("测试移动", _testMoveOffset);
        _testEuler = EditorGUILayout.Vector3Field("测试旋转", _testEuler);
        if (EditorGUI.EndChangeCheck())
        {
            ApplyTest();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("X +90"))
            {
                AddTestEuler(new Vector3(90f, 0f, 0f));
            }

            if (GUILayout.Button("Y +90"))
            {
                AddTestEuler(new Vector3(0f, 90f, 0f));
            }

            if (GUILayout.Button("Z +90"))
            {
                AddTestEuler(new Vector3(0f, 0f, 90f));
            }

            if (GUILayout.Button("清零"))
            {
                _testMoveOffset = Vector3.zero;
                _testEuler = Vector3.zero;
                ApplyTest();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (!_testing)
            {
                if (GUILayout.Button("开始测试", GUILayout.Height(26f)))
                {
                    StartTest();
                }
            }
            else
            {
                if (GUILayout.Button("测试通过 ✓", GUILayout.Height(26f)))
                {
                    StopTest(true);
                    _testPassed = true;
                }

                if (GUILayout.Button("放弃测试", GUILayout.Height(26f)))
                {
                    StopTest(true);
                }
            }
        }

        if (_testPassed)
        {
            EditorGUILayout.HelpBox("测试已通过，可在下方生成新原点。", MessageType.Info);
        }
    }

    private void DrawStep3Generate()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("③ 生成新原点", EditorStyles.boldLabel);

        if (_testing)
        {
            EditorGUILayout.HelpBox("测试进行中，请先结束测试再生成。", MessageType.Warning);
            return;
        }

        if (!_testPassed)
        {
            EditorGUILayout.HelpBox("请先在第 ② 步完成旋转测试并点“测试通过”，再生成新原点。", MessageType.Warning);
        }

        EditorGUILayout.HelpBox("创建一个位于新原点的父节点，并保持模型在场景中的视觉位置不变。", MessageType.None);

        _parentMode = (ParentMode)EditorGUILayout.Popup(new GUIContent("父级模式"), (int)_parentMode, ParentModeLabels);
        if (_parentMode == ParentMode.UseParentOverride)
        {
            _parentOverride = (Transform)EditorGUILayout.ObjectField("指定父级", _parentOverride, typeof(Transform), true);
        }

        _selectCreatedRoot = EditorGUILayout.Toggle("生成后选中新父节点", _selectCreatedRoot);

        using (new EditorGUI.DisabledScope(!_testPassed))
        {
            if (GUILayout.Button("生成新原点父节点", GUILayout.Height(30f)))
            {
                CreatePivotRoot(_target, _pivotPosition, Quaternion.Euler(_pivotEuler), GetRootParent(), _selectCreatedRoot);
                _testPassed = false;
            }
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("跳过测试直接生成"))
        {
            if (EditorUtility.DisplayDialog("跳过测试", "确定不经过旋转测试，直接按当前新原点生成父节点？", "生成", "取消"))
            {
                CreatePivotRoot(_target, _pivotPosition, Quaternion.Euler(_pivotEuler), GetRootParent(), _selectCreatedRoot);
                _testPassed = false;
            }
        }
    }

    // ------------------------------------------------------------- Scene GUI

    private void OnSceneGUI(SceneView sceneView)
    {
        if (_target == null)
        {
            SetUnityTransformHandleHidden(false);
            return;
        }

        UpdateUnityTransformHandleVisibility();

        Color oldColor = Handles.color;

        if (_pickMode != PickMode.None)
        {
            HandleScenePicking(sceneView);
        }

        if (_showSceneHandle && !_testing && _pickMode == PickMode.None)
        {
            DrawPivotHandle();
        }

        if (_testing)
        {
            DrawTestHandle();
        }

        DrawPickedPointsGizmos();

        Handles.color = oldColor;
    }

    private void HandleScenePicking(SceneView sceneView)
    {
        Event evt = Event.current;
        int controlId = GUIUtility.GetControlID(FocusType.Passive);

        if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
        {
            CancelPick();
            evt.Use();
            Repaint();
            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
        bool hit = RaycastTarget(_target, ray, out Vector3 hitPoint, out Vector3 hitNormal);

        if (hit && _snapToVertex && _pickMode != PickMode.Surface)
        {
            hitPoint = SnapToNearestVertex(_target, hitPoint);
        }

        if (hit)
        {
            _hasPickPreview = true;
            _pickPreviewPoint = hitPoint;
            _pickPreviewNormal = hitNormal;

            float size = HandleUtility.GetHandleSize(hitPoint) * 0.08f;
            Handles.color = new Color(1f, 1f, 0f, 0.9f);
            Handles.SphereHandleCap(0, hitPoint, Quaternion.identity, size, EventType.Repaint);
            Handles.DrawLine(hitPoint, hitPoint + hitNormal * HandleUtility.GetHandleSize(hitPoint) * 0.5f);
            sceneView.Repaint();
        }
        else
        {
            _hasPickPreview = false;
        }

        if (evt.type == EventType.MouseDown && evt.button == 0 && hit)
        {
            GUIUtility.hotControl = controlId;
            evt.Use();
            ApplyPick(hitPoint, hitNormal);
            Repaint();
        }

        if (evt.type == EventType.MouseUp && evt.button == 0)
        {
            GUIUtility.hotControl = 0;
        }

        if (evt.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(controlId);
        }
    }

    private void DrawPickedPointsGizmos()
    {
        if (_hasPointA)
        {
            DrawPointGizmo(_pickPointA, Color.red, "A");
        }

        if (_hasPointB)
        {
            DrawPointGizmo(_pickPointB, Color.green, "B");
            if (_hasPointA)
            {
                Handles.color = Color.green;
                Handles.DrawLine(_pickPointA, _pickPointB);
            }
        }

        if (_hasPointC)
        {
            DrawPointGizmo(_pickPointC, Color.blue, "C");
            if (_hasPointA)
            {
                Handles.color = Color.blue;
                Handles.DrawDottedLine(_pickPointA, _pickPointC, 3f);
            }
        }
    }

    private void DrawPointGizmo(Vector3 point, Color color, string label)
    {
        float size = HandleUtility.GetHandleSize(point) * 0.06f;
        Handles.color = color;
        Handles.SphereHandleCap(0, point, Quaternion.identity, size, EventType.Repaint);
        Handles.Label(point + Vector3.up * size * 2f, label, GetLabelStyle());
    }

    private void DrawPivotHandle()
    {
        Vector3 position = _pivotPosition;
        Quaternion rotation = Quaternion.Euler(_pivotEuler);
        float size = HandleUtility.GetHandleSize(position) * 0.14f;

        Handles.color = new Color(0f, 0.9f, 1f, 0.95f);
        Handles.DrawDottedLine(GetStablePosition(_target), position, 4f);
        Handles.SphereHandleCap(0, position, rotation, size * 1.35f, EventType.Repaint);
        Handles.Label(position + Vector3.up * size * 1.8f, "新原点", GetLabelStyle());

        EditorGUI.BeginChangeCheck();
        Vector3 nextPosition = Handles.PositionHandle(position, rotation);
        Quaternion nextRotation = Handles.RotationHandle(rotation, position);
        if (EditorGUI.EndChangeCheck())
        {
            SetPivotPose(nextPosition, nextRotation.eulerAngles, true);
        }
    }

    private void DrawTestHandle()
    {
        Vector3 handlePosition = _testPivotPosition + _testMoveOffset;
        float size = HandleUtility.GetHandleSize(handlePosition) * 0.14f;
        Quaternion handleRotation = _testPivotRotation * Quaternion.Euler(_testEuler);

        Handles.color = new Color(1f, 0.75f, 0.1f, 0.95f);
        Handles.Label(handlePosition + Vector3.up * size * 3.2f, "移动/旋转测试", GetLabelStyle());

        EditorGUI.BeginChangeCheck();
        Vector3 nextPosition = Handles.PositionHandle(handlePosition, handleRotation);
        Quaternion nextRotation = Handles.RotationHandle(handleRotation, handlePosition);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(this, "调整测试移动/旋转");
            _testMoveOffset = nextPosition - _testPivotPosition;
            _testEuler = (Quaternion.Inverse(_testPivotRotation) * nextRotation).eulerAngles;
            ApplyTest();
            Repaint();
        }
    }

    private void OnSelectionChanged()
    {
        if (_useSpecifiedTarget || Selection.activeTransform == null || Selection.activeTransform == _target)
        {
            return;
        }

        StopTest(true);
        BindTarget(Selection.activeTransform);
        Repaint();
        RepaintSceneViews();
    }

    // ------------------------------------------------------------- Pivot state

    private void BindTarget(Transform target)
    {
        _target = target;
        _testPassed = false;
        if (_target != null)
        {
            _pivotPosition = _target.position;
            _pivotEuler = _target.rotation.eulerAngles;
        }

        UpdateUnityTransformHandleVisibility();
        RepaintSceneViews();
    }

    private void SetPivotPosition(Vector3 position)
    {
        SetPivotPose(position, _pivotEuler, true);
    }

    private void SetPivotRotation(Quaternion rotation)
    {
        SetPivotPose(_pivotPosition, rotation.eulerAngles, true);
    }

    private void SetPivotPose(Vector3 position, Vector3 euler, bool changed)
    {
        if (!changed)
        {
            return;
        }

        Undo.RecordObject(this, "设置新原点");
        _pivotPosition = position;
        _pivotEuler = euler;
        _testPassed = false; // 改动原点后需重新测试

        if (_testing)
        {
            _testPivotPosition = _pivotPosition;
            _testPivotRotation = Quaternion.Euler(_pivotEuler);
            ApplyTest();
        }

        Repaint();
        RepaintSceneViews();
    }

    // ------------------------------------------------------------- Test

    private void StartTest()
    {
        if (_target == null)
        {
            return;
        }

        StopTest(true);

        _testTarget = _target;
        _testOriginalPosition = _target.position;
        _testOriginalRotation = _target.rotation;
        _testOriginalScale = _target.localScale;
        _testPivotPosition = _pivotPosition;
        _testPivotRotation = Quaternion.Euler(_pivotEuler);
        _testing = true;
        ApplyTest();
    }

    private void AddTestEuler(Vector3 delta)
    {
        _testEuler += delta;
        ApplyTest();
    }

    private void ApplyTest()
    {
        if (!_testing || _testTarget == null)
        {
            return;
        }

        Quaternion delta = _testPivotRotation * Quaternion.Euler(_testEuler) * Quaternion.Inverse(_testPivotRotation);
        Vector3 position = _testPivotPosition + _testMoveOffset + delta * (_testOriginalPosition - _testPivotPosition);
        Quaternion rotation = delta * _testOriginalRotation;

        _testTarget.SetPositionAndRotation(position, rotation);
        _testTarget.localScale = _testOriginalScale;
        EditorUtility.SetDirty(_testTarget);
        RepaintSceneViews();
    }

    private void StopTest(bool clear)
    {
        if (_testing && _testTarget != null)
        {
            _testTarget.SetPositionAndRotation(_testOriginalPosition, _testOriginalRotation);
            _testTarget.localScale = _testOriginalScale;
            EditorUtility.SetDirty(_testTarget);
            RepaintSceneViews();
        }

        _testing = false;
        _testTarget = null;
        if (clear)
        {
            _testMoveOffset = Vector3.zero;
            _testEuler = Vector3.zero;
        }

        UpdateUnityTransformHandleVisibility();
    }

    // ------------------------------------------------------------- Picking

    private void CancelPick()
    {
        _pickMode = PickMode.None;
        _hasPickPreview = false;
        UpdateUnityTransformHandleVisibility();
    }

    private void ApplyPick(Vector3 point, Vector3 normal)
    {
        switch (_pickMode)
        {
            case PickMode.Surface:
                SetPivotPosition(point);
                SetPivotRotation(BuildRotationFromAxis(_surfaceAlignAxis, normal));
                CancelPick();
                break;

            case PickMode.PointA:
                _pickPointA = point;
                _hasPointA = true;
                CancelPick();
                break;

            case PickMode.PointB:
                _pickPointB = point;
                _hasPointB = true;
                CancelPick();
                break;

            case PickMode.PointC:
                _pickPointC = point;
                _hasPointC = true;
                CancelPick();
                break;
        }
    }

    private void ApplyPointOrientation()
    {
        if (!_hasPointA || !_hasPointB)
        {
            return;
        }

        Vector3 primaryDir = (_pickPointB - _pickPointA).normalized;
        if (primaryDir == Vector3.zero)
        {
            EditorUtility.DisplayDialog("采点定向", "A 和 B 距离太近，无法确定方向。", "确定");
            return;
        }

        Vector3 secondaryDir;
        if (_hasPointC)
        {
            Vector3 rawSecondary = (_pickPointC - _pickPointA).normalized;
            secondaryDir = Vector3.Cross(primaryDir, Vector3.Cross(rawSecondary, primaryDir)).normalized;
            if (secondaryDir == Vector3.zero)
            {
                EditorUtility.DisplayDialog("采点定向", "三点共线，无法确定第二轴。", "确定");
                return;
            }
        }
        else
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(primaryDir, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            secondaryDir = Vector3.Cross(primaryDir, Vector3.Cross(up, primaryDir)).normalized;
        }

        Quaternion rotation = BuildOrientationFromPrimarySecondary(_primaryAxis, primaryDir, secondaryDir);
        SetPivotPosition(_pickPointA);
        SetPivotRotation(rotation);
    }

    private static Quaternion BuildRotationFromAxis(AlignAxis axis, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        direction.Normalize();
        Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;

        Quaternion lookRotation = Quaternion.LookRotation(direction, up);

        switch (axis)
        {
            case AlignAxis.PositiveZ:
                return lookRotation;
            case AlignAxis.NegativeZ:
                return lookRotation * Quaternion.Euler(0f, 180f, 0f);
            case AlignAxis.PositiveY:
                return lookRotation * Quaternion.Euler(90f, 0f, 0f);
            case AlignAxis.NegativeY:
                return lookRotation * Quaternion.Euler(-90f, 0f, 0f);
            case AlignAxis.PositiveX:
                return lookRotation * Quaternion.Euler(0f, -90f, 0f);
            case AlignAxis.NegativeX:
                return lookRotation * Quaternion.Euler(0f, 90f, 0f);
            default:
                return lookRotation;
        }
    }

    private static Quaternion BuildOrientationFromPrimarySecondary(AlignAxis primaryAxisSlot, Vector3 primaryDir, Vector3 secondaryDir)
    {
        Vector3 third = Vector3.Cross(primaryDir, secondaryDir).normalized;
        secondaryDir = Vector3.Cross(third, primaryDir).normalized;

        Vector3 forward, up;
        switch (primaryAxisSlot)
        {
            case AlignAxis.PositiveZ:
                forward = primaryDir;
                up = secondaryDir;
                break;
            case AlignAxis.NegativeZ:
                forward = -primaryDir;
                up = secondaryDir;
                break;
            case AlignAxis.PositiveY:
                forward = third;
                up = primaryDir;
                break;
            case AlignAxis.NegativeY:
                forward = third;
                up = -primaryDir;
                break;
            case AlignAxis.PositiveX:
                forward = -third;
                up = secondaryDir;
                break;
            case AlignAxis.NegativeX:
                forward = third;
                up = secondaryDir;
                break;
            default:
                forward = primaryDir;
                up = secondaryDir;
                break;
        }

        if (forward.sqrMagnitude < 0.0001f || up.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(forward, up);
    }

    private static bool RaycastTarget(Transform target, Ray ray, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = Vector3.zero;
        hitNormal = Vector3.up;

        MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
        SkinnedMeshRenderer[] skinnedRenderers = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        float closestDist = float.MaxValue;
        bool didHit = false;

        foreach (MeshFilter mf in meshFilters)
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            Transform meshTransform = mf.transform;
            RaycastHit rHit;
            if (IntersectRayMesh(ray, mesh, meshTransform.localToWorldMatrix, out rHit))
            {
                if (rHit.distance < closestDist)
                {
                    closestDist = rHit.distance;
                    hitPoint = rHit.point;
                    hitNormal = rHit.normal;
                    didHit = true;
                }
            }
        }

        foreach (SkinnedMeshRenderer smr in skinnedRenderers)
        {
            Mesh bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);
            Transform smrTransform = smr.transform;
            RaycastHit rHit;
            if (IntersectRayMesh(ray, bakedMesh, smrTransform.localToWorldMatrix, out rHit))
            {
                if (rHit.distance < closestDist)
                {
                    closestDist = rHit.distance;
                    hitPoint = rHit.point;
                    hitNormal = rHit.normal;
                    didHit = true;
                }
            }

            Object.DestroyImmediate(bakedMesh);
        }

        return didHit;
    }

    private static bool IntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
    {
        var method = typeof(HandleUtility).GetMethod(
            "IntersectRayMesh",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        if (method != null)
        {
            object[] parameters = new object[] { ray, mesh, matrix, null };
            bool result = (bool)method.Invoke(null, parameters);
            hit = (RaycastHit)parameters[3];
            return result;
        }

        hit = default;
        return false;
    }

    private static Vector3 SnapToNearestVertex(Transform target, Vector3 worldPoint)
    {
        float closestDist = float.MaxValue;
        Vector3 closest = worldPoint;

        MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter mf in meshFilters)
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                continue;
            }

            Vector3[] vertices = mesh.vertices;
            Transform meshTransform = mf.transform;

            foreach (Vector3 vertex in vertices)
            {
                Vector3 worldVertex = meshTransform.TransformPoint(vertex);
                float dist = (worldVertex - worldPoint).sqrMagnitude;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = worldVertex;
                }
            }
        }

        return closest;
    }

    // ------------------------------------------------------------- Generate

    private Transform GetRootParent()
    {
        if (_target == null)
        {
            return null;
        }

        return _parentMode == ParentMode.KeepWorldParent ? _target.parent : _parentOverride;
    }

    private static void CreatePivotRoot(Transform target, Vector3 pivotPosition, Quaternion pivotRotation, Transform parent, bool selectCreatedRoot)
    {
        if (target == null)
        {
            return;
        }

        if (parent != null && (parent == target || parent.IsChildOf(target)))
        {
            EditorUtility.DisplayDialog("生成新原点", "指定父级不能是目标自身，也不能是目标的子物体。", "确定");
            return;
        }

        int siblingIndex = target.GetSiblingIndex();
        GameObject root = new GameObject($"{target.name}_PivotRoot");
        Undo.RegisterCreatedObjectUndo(root, "生成新原点");

        Transform rootTransform = root.transform;
        Undo.RecordObject(rootTransform, "生成新原点");
        rootTransform.SetPositionAndRotation(pivotPosition, pivotRotation);
        rootTransform.localScale = Vector3.one;

        Undo.SetTransformParent(rootTransform, parent, "设置新原点父级");
        if (parent == target.parent)
        {
            rootTransform.SetSiblingIndex(siblingIndex);
        }

        Undo.SetTransformParent(target, rootTransform, "挂载目标到新原点");

        if (selectCreatedRoot)
        {
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
        }
    }

    // ------------------------------------------------------------- Helpers

    private Vector3 GetStablePosition(Transform transform)
    {
        if (transform == null)
        {
            return Vector3.zero;
        }

        if (_testing && transform == _testTarget)
        {
            return _testOriginalPosition;
        }

        return transform.position;
    }

    private Quaternion GetStableRotation(Transform transform)
    {
        if (transform == null)
        {
            return Quaternion.identity;
        }

        if (_testing && transform == _testTarget)
        {
            return _testOriginalRotation;
        }

        return transform.rotation;
    }

    private static Vector3 GetBoundsPoint(Bounds bounds, float nx, float ny, float nz)
    {
        Vector3 min = bounds.min;
        Vector3 size = bounds.size;
        return new Vector3(min.x + size.x * nx, min.y + size.y * ny, min.z + size.z * nz);
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
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

    private void UpdateUnityTransformHandleVisibility()
    {
        bool shouldHide = _target != null && (_showSceneHandle || _testing || _pickMode != PickMode.None);
        SetUnityTransformHandleHidden(shouldHide);
    }

    private void SetUnityTransformHandleHidden(bool hidden)
    {
        if (hidden)
        {
            if (!_hasHiddenUnityTool)
            {
                _previousToolsHidden = Tools.hidden;
                _hasHiddenUnityTool = true;
            }

            Tools.hidden = true;
            return;
        }

        if (!_hasHiddenUnityTool)
        {
            return;
        }

        Tools.hidden = _previousToolsHidden;
        _hasHiddenUnityTool = false;
    }

    private static void RepaintSceneViews()
    {
        foreach (SceneView sceneView in SceneView.sceneViews)
        {
            sceneView.Repaint();
        }
    }

    private GUIStyle GetLabelStyle()
    {
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            _labelStyle.normal.textColor = Color.cyan;
        }

        return _labelStyle;
    }
}
