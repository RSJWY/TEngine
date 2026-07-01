using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Editor.Tools.DataBinding
{
    /// <summary>
    /// 自定义数据绑定生成器面板。
    /// </summary>
    /// <remarks>
    /// 该面板只管理 DataBinding 生成流程，不处理 UI 绑定。
    /// </remarks>
    public sealed class DataBindingGeneratorWindow : OdinEditorWindow
    {
        private const string MenuPath = "Tools/数据绑定/生成器面板";
        private const float ModelListMinWidth = 1120f;
        private const float ModelListMinHeight = 100f;
        private const float ModelListMaxHeight = 420f;
        private const float ModelListHeaderHeight = 26f;
        private const float ModelListRowHeight = 44f;
        private const float ModelActionButtonHeight = 22f;

        private Vector2 _modelListScroll;

        [SerializeField]
        [BoxGroup("设置")]
        [LabelText("输出目录")]
        [FolderPath]
        [DelayedProperty]
        [InlineButton(nameof(ResetOutputDirectory), "默认")]
        [InlineButton(nameof(CreateOutputDirectory), "创建")]
        [InlineButton(nameof(OpenOutputDirectory), "打开")]
        [OnValueChanged(nameof(RefreshModels))]
        private string _outputDirectory = DataBindingGenerator.DefaultOutputDirectory;

        [SerializeField]
        [BoxGroup("状态")]
        [ReadOnly]
        [LabelText("最近结果")]
        private string _lastResult = "尚未生成。";

        [ShowInInspector]
        [BoxGroup("状态")]
        [ReadOnly]
        [LabelText("模型数量")]
        private int ModelCount => _models.Count;

        [SerializeField]
        [HideInInspector]
        private List<ModelEntry> _models = new List<ModelEntry>();

        [MenuItem(MenuPath)]
        public static void Open()
        {
            DataBindingGeneratorWindow window = GetWindow<DataBindingGeneratorWindow>();
            window.titleContent = new GUIContent("数据绑定生成器");
            window.minSize = new Vector2(920f, 520f);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshModels();
        }

        protected override void OnImGUI()
        {
            DrawHeader();
            DrawModelList();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("输出目录", GUILayout.Width(64f));

            EditorGUI.BeginChangeCheck();
            string outputDirectory = EditorGUILayout.TextField(_outputDirectory);
            if (EditorGUI.EndChangeCheck())
            {
                _outputDirectory = outputDirectory;
                RefreshModels();
            }

            if (GUILayout.Button("默认", GUILayout.Width(52f)))
            {
                ResetOutputDirectory();
            }

            if (GUILayout.Button("创建", GUILayout.Width(52f)))
            {
                CreateOutputDirectory();
            }

            if (GUILayout.Button("打开", GUILayout.Width(52f)))
            {
                OpenOutputDirectory();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成全部", GUILayout.Height(28f)))
            {
                GenerateAll();
            }

            if (GUILayout.Button("刷新模型列表", GUILayout.Height(28f)))
            {
                RefreshModels();
            }

            if (GUILayout.Button("清理生成文件", GUILayout.Height(28f)))
            {
                CleanGeneratedFiles();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("最近结果", EditorStyles.boldLabel, GUILayout.Width(64f));
            EditorGUILayout.LabelField(_lastResult, EditorStyles.wordWrappedLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"模型：{_models.Count}", GUILayout.Width(72f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        [Button("生成全部", ButtonSizes.Large)]
        [GUIColor(0.35f, 0.75f, 0.35f)]
        [PropertyOrder(-10)]
        private void GenerateAll()
        {
            CreateOutputDirectory();
            DataBindingGenerator.GenerateResult result = DataBindingGenerator.GenerateAll(_outputDirectory);
            _lastResult = $"已生成 {result.GeneratedCount}/{result.ModelCount}。变更：{result.ChangedCount}。跳过：{result.SkippedCount}。";
            RefreshModels();
        }

        [Button("刷新模型列表", ButtonSizes.Medium)]
        [PropertyOrder(-9)]
        private void RefreshModels()
        {
            try
            {
                _models = DataBindingGenerator.CollectModelInfos(_outputDirectory)
                    .Select(info => ModelEntry.From(info, _outputDirectory, this))
                    .ToList();
            }
            catch (Exception exception)
            {
                _lastResult = exception.Message;
                Debug.LogException(exception);
            }
        }

        [Button("清理生成文件", ButtonSizes.Medium)]
        [GUIColor(0.9f, 0.65f, 0.25f)]
        [PropertyOrder(-8)]
        private void CleanGeneratedFiles()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "清理数据绑定生成文件",
                $"将删除以下目录中的数据绑定生成文件：\n{_outputDirectory}",
                "删除",
                "取消");

            if (!confirmed)
            {
                return;
            }

            int deletedCount = DataBindingGenerator.CleanGeneratedFiles(_outputDirectory);
            _lastResult = $"已删除 {deletedCount} 个数据绑定生成文件。";
            RefreshModels();
        }

        private void ResetOutputDirectory()
        {
            _outputDirectory = DataBindingGenerator.DefaultOutputDirectory;
            RefreshModels();
        }

        private void CreateOutputDirectory()
        {
            if (string.IsNullOrWhiteSpace(_outputDirectory))
            {
                _outputDirectory = DataBindingGenerator.DefaultOutputDirectory;
            }

            Directory.CreateDirectory(_outputDirectory);
            AssetDatabase.Refresh();
        }

        private void OpenOutputDirectory()
        {
            CreateOutputDirectory();
            EditorUtility.RevealInFinder(ToAbsolutePath(_outputDirectory));
        }

        private static string ToAbsolutePath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
            {
                return assetPath;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private void DrawModelList()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("模型列表", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{_models.Count} 个模型", GUILayout.Width(72f));
            EditorGUILayout.EndHorizontal();

            if (_models.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到数据绑定模型。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            float contentHeight = ModelListHeaderHeight + _models.Count * ModelListRowHeight;
            float listHeight = Mathf.Clamp(contentHeight + 4f, ModelListMinHeight, ModelListMaxHeight);
            float tableWidth = Mathf.Max(ModelListMinWidth, position.width - 28f);

            _modelListScroll = EditorGUILayout.BeginScrollView(
                _modelListScroll,
                false,
                false,
                GUILayout.Height(listHeight));

            Rect tableRect = GUILayoutUtility.GetRect(
                tableWidth,
                contentHeight,
                GUILayout.Width(tableWidth),
                GUILayout.Height(contentHeight));
            DrawModelTable(tableRect);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawModelTable(Rect tableRect)
        {
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };

            GUIStyle cellStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };

            Rect headerRect = new Rect(tableRect.x, tableRect.y, tableRect.width, ModelListHeaderHeight);
            EditorGUI.DrawRect(headerRect, new Color(0.22f, 0.22f, 0.22f, 0.32f));
            DrawModelTableCells(headerRect, headerStyle, "#", "状态", "模型类型", "绑定器", "成员", "信号", "容差", "定义源", "操作");

            for (int index = 0; index < _models.Count; index++)
            {
                Rect rowRect = new Rect(
                    tableRect.x,
                    tableRect.y + ModelListHeaderHeight + index * ModelListRowHeight,
                    tableRect.width,
                    ModelListRowHeight);
                DrawModelTableRow(rowRect, _models[index], index, cellStyle);
            }
        }

        private static void DrawModelTableRow(Rect rowRect, ModelEntry entry, int index, GUIStyle cellStyle)
        {
            Color rowColor = index % 2 == 0
                ? new Color(1f, 1f, 1f, 0.035f)
                : new Color(0f, 0f, 0f, 0.035f);
            EditorGUI.DrawRect(rowRect, rowColor);

            DrawModelTableCells(
                rowRect,
                cellStyle,
                (index + 1).ToString(),
                entry.Status,
                entry.ModelType,
                entry.BinderType,
                entry.MemberCount.ToString(),
                entry.SignalCount.ToString(),
                entry.ToleranceCount.ToString(),
                string.IsNullOrEmpty(entry.SourceFile) ? "-" : entry.SourceFile,
                string.Empty);

            DrawModelActions(GetActionCellRect(rowRect), entry);
        }

        private static void DrawModelTableCells(Rect rowRect, GUIStyle style, params string[] values)
        {
            float sourceWidth = Mathf.Max(200f, rowRect.width - 920f);
            float[] widths =
            {
                42f,
                64f,
                230f,
                160f,
                54f,
                54f,
                54f,
                sourceWidth,
                262f
            };

            float x = rowRect.x;
            for (int i = 0; i < values.Length && i < widths.Length; i++)
            {
                Rect cellRect = new Rect(x, rowRect.y, widths[i], rowRect.height);
                GUI.Label(
                    new Rect(cellRect.x + 4f, cellRect.y, cellRect.width - 8f, cellRect.height),
                    new GUIContent(values[i], values[i]),
                    style);

                EditorGUI.DrawRect(new Rect(cellRect.xMax - 1f, cellRect.y + 4f, 1f, cellRect.height - 8f), new Color(0f, 0f, 0f, 0.12f));
                x += widths[i];
            }
        }

        private static Rect GetActionCellRect(Rect rowRect)
        {
            float sourceWidth = Mathf.Max(200f, rowRect.width - 920f);
            float actionX = rowRect.x + 42f + 64f + 230f + 160f + 54f + 54f + 54f + sourceWidth;
            return new Rect(actionX, rowRect.y, 262f, rowRect.height);
        }

        private static void DrawModelActions(Rect rect, ModelEntry entry)
        {
            const float regenerateWidth = 78f;
            const float cleanWidth = 48f;
            const float pingWidth = 48f;
            const float pingSourceWidth = 58f;
            const float gap = 6f;
            float totalWidth = regenerateWidth + cleanWidth + pingWidth + pingSourceWidth + gap * 3f;
            float x = rect.x + (rect.width - totalWidth) * 0.5f;
            float y = rect.y + (rect.height - ModelActionButtonHeight) * 0.5f;

            if (GUI.Button(new Rect(x, y, regenerateWidth, ModelActionButtonHeight), "重新生成"))
            {
                entry.Regenerate();
            }

            x += regenerateWidth + gap;
            using (new EditorGUI.DisabledScope(!entry.GeneratedFileExists))
            {
                if (GUI.Button(new Rect(x, y, cleanWidth, ModelActionButtonHeight), "清理"))
                {
                    entry.CleanGeneratedFile();
                }
            }

            x += cleanWidth + gap;
            if (GUI.Button(new Rect(x, y, pingWidth, ModelActionButtonHeight), "定位"))
            {
                entry.Ping();
            }

            x += pingWidth + gap;
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(entry.SourceFile)))
            {
                if (GUI.Button(new Rect(x, y, pingSourceWidth, ModelActionButtonHeight), "定位源"))
                {
                    entry.PingSource();
                }
            }
        }

        [Serializable]
        private sealed class ModelEntry
        {
            [TableColumnWidth(220, Resizable = true)]
            [ReadOnly]
            [LabelText("模型类型")]
            public string ModelType;

            [TableColumnWidth(240, Resizable = true)]
            [ReadOnly]
            [DisplayAsString(false)]
            [LabelText("定义源")]
            public string SourceFile;

            [TableColumnWidth(170, Resizable = true)]
            [ReadOnly]
            [LabelText("绑定器类型")]
            public string BinderType;

            [TableColumnWidth(70)]
            [ReadOnly]
            [LabelText("成员数")]
            public int MemberCount;

            [TableColumnWidth(65)]
            [ReadOnly]
            [LabelText("信号数")]
            public int SignalCount;

            [TableColumnWidth(80)]
            [ReadOnly]
            [LabelText("容差数")]
            public int ToleranceCount;

            [TableColumnWidth(80)]
            [ReadOnly]
            [LabelText("状态")]
            public string Status;

            [HideInInspector]
            public bool GeneratedFileExists;

            [TableColumnWidth(80)]
            [Button("重新生成", ButtonSizes.Small)]
            public void Regenerate()
            {
                DataBindingGenerator.GenerateModelResult result = DataBindingGenerator.GenerateOne(ModelType, OutputDirectory);
                if (result.IsSkipped)
                {
                    Status = "跳过";
                    if (_owner != null)
                    {
                        _owner._lastResult = $"跳过 {ModelType}：{result.Message}";
                    }

                    return;
                }

                Status = "已生成";
                GeneratedFileExists = true;
                if (_owner != null)
                {
                    string state = result.Changed ? "已更新" : "无变化";
                    _owner._lastResult = $"已重新生成 {ModelType}。{state}。";
                    _owner.RefreshModels();
                }
            }

            [TableColumnWidth(55)]
            [Button("清理", ButtonSizes.Small)]
            [EnableIf(nameof(GeneratedFileExists))]
            public void CleanGeneratedFile()
            {
                bool deleted = DataBindingGenerator.CleanGeneratedFile(OutputPath);
                if (deleted)
                {
                    Status = "缺失";
                    GeneratedFileExists = false;
                }

                if (_owner != null)
                {
                    _owner._lastResult = deleted
                        ? $"已删除 {ModelType} 的生成文件。"
                        : $"未删除 {ModelType}：生成文件不存在或不是数据绑定生成文件。";
                    _owner.RefreshModels();
                }
            }

            [TableColumnWidth(55)]
            [Button("定位", ButtonSizes.Small)]
            public void Ping()
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(OutputPath);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                    return;
                }

                string directory = Path.GetDirectoryName(OutputPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(directory))
                {
                    UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(directory);
                    if (folder != null)
                    {
                        EditorGUIUtility.PingObject(folder);
                        Selection.activeObject = folder;
                    }
                }
            }

            [TableColumnWidth(70)]
            [Button("定位源", ButtonSizes.Small)]
            [EnableIf(nameof(HasSourceFile))]
            public void PingSource()
            {
                if (string.IsNullOrEmpty(SourceFile))
                {
                    return;
                }

                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SourceFile);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }
            }

            private bool HasSourceFile => !string.IsNullOrEmpty(SourceFile);

            [HideInInspector]
            public string OutputPath;

            [HideInInspector]
            public string OutputDirectory;

            [NonSerialized]
            private DataBindingGeneratorWindow _owner;

            public static ModelEntry From(DataBindingGenerator.ModelInfo info, string outputDirectory, DataBindingGeneratorWindow owner)
            {
                return new ModelEntry
                {
                    ModelType = info.ModelType,
                    SourceFile = info.SourceFile,
                    BinderType = info.BinderType,
                    MemberCount = info.MemberCount,
                    SignalCount = info.SignalCount,
                    ToleranceCount = info.ToleranceCount,
                    Status = info.GeneratedFileExists ? "已生成" : "缺失",
                    GeneratedFileExists = info.GeneratedFileExists,
                    OutputPath = info.OutputPath,
                    OutputDirectory = outputDirectory,
                    _owner = owner
                };
            }
        }
    }
}
