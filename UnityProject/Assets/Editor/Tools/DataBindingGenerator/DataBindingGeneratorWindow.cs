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
        [BoxGroup("模型列表")]
        [TableList(AlwaysExpanded = true, IsReadOnly = true, ShowIndexLabels = true)]
        [LabelText("数据绑定模型")]
        private List<ModelEntry> _models = new List<ModelEntry>();

        [MenuItem(MenuPath)]
        public static void Open()
        {
            DataBindingGeneratorWindow window = GetWindow<DataBindingGeneratorWindow>();
            window.titleContent = new GUIContent("数据绑定生成器");
            window.minSize = new Vector2(820f, 520f);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshModels();
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

        [Serializable]
        private sealed class ModelEntry
        {
            [TableColumnWidth(220, Resizable = true)]
            [ReadOnly]
            [LabelText("模型类型")]
            public string ModelType;

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

            [TableColumnWidth(70)]
            [ReadOnly]
            [LabelText("格式化数")]
            public int FormattedCount;

            [TableColumnWidth(80)]
            [ReadOnly]
            [LabelText("容差数")]
            public int ToleranceCount;

            [TableColumnWidth(80)]
            [ReadOnly]
            [LabelText("状态")]
            public string Status;

            [TableColumnWidth(80)]
            [Button("重新生成", ButtonSizes.Small)]
            private void Regenerate()
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
                if (_owner != null)
                {
                    string state = result.Changed ? "已更新" : "无变化";
                    _owner._lastResult = $"已重新生成 {ModelType}。{state}。";
                    _owner.RefreshModels();
                }
            }

            [TableColumnWidth(55)]
            [Button("定位", ButtonSizes.Small)]
            private void Ping()
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
                    BinderType = info.BinderType,
                    MemberCount = info.MemberCount,
                    SignalCount = info.SignalCount,
                    FormattedCount = info.FormattedCount,
                    ToleranceCount = info.ToleranceCount,
                    Status = info.GeneratedFileExists ? "已生成" : "缺失",
                    OutputPath = info.OutputPath,
                    OutputDirectory = outputDirectory,
                    _owner = owner
                };
            }
        }
    }
}
