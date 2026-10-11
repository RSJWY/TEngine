using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace TEngine.SceneTools
{
    /// <summary>
    /// 场景枚举配置窗口（Odin）：直接在面板上编辑 <see cref="SceneEnumConfig"/>，
    /// 无需再去 Assets 里找 .asset 资源。
    /// <para>菜单：TEngine &gt; 场景枚举配置。</para>
    /// </summary>
    public class SceneEnumConfigWindow : OdinEditorWindow
    {
        private const string MenuPath = "TEngine/场景枚举配置";

        [MenuItem(MenuPath, false, 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<SceneEnumConfigWindow>();
            window.titleContent = new GUIContent("场景枚举配置");
            window.minSize = new Vector2(760, 620);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            InitConfig();
            EnsureHealth(true);
        }

        #region 配置面板

        [TabGroup("Pages", "配置")]
        [InfoBox(
            "场景枚举数据源。推荐流程：\n" +
            "1. 点「同步场景资源」扫描目录，追加/停用场景条目\n" +
            "2. 校对枚举名与中文名\n" +
            "3. 点「生成枚举代码」落盘到代码输出目录",
            InfoMessageType.None)]
        [ShowInInspector]
        [HideLabel]
        [InlineEditor(InlineEditorModes.GUIAndHeader, InlineEditorObjectFieldModes.Hidden)]
        [OnInspectorInit(nameof(InitConfig))]
        private SceneEnumConfig _config;

        private void InitConfig()
        {
            if (_config == null)
            {
                _config = SceneEnumConfig.LoadOrCreate();
            }
        }

        #endregion

        #region 体检

        [TabGroup("Pages", "体检")]
        [BoxGroup("Pages/体检/汇总")]
        [ShowInInspector, ReadOnly, HideLabel, MultiLineProperty(6)]
        private string Summary
        {
            get
            {
                EnsureHealth();
                return _summary;
            }
        }

        [TabGroup("Pages", "体检")]
        [BoxGroup("Pages/体检/汇总")]
        [HorizontalGroup("Pages/体检/汇总/Buttons")]
        [Button("重新体检", ButtonSizes.Medium)]
        [GUIColor(0.4f, 0.8f, 1f)]
        private void RefreshHealth()
        {
            EnsureHealth(true);
            Repaint();
        }

        [TabGroup("Pages", "体检")]
        [BoxGroup("Pages/体检/汇总")]
        [HorizontalGroup("Pages/体检/汇总/Buttons")]
        [Button("定位配置资产", ButtonSizes.Medium)]
        private void PingConfigAsset()
        {
            var config = _config ?? SceneEnumConfig.LoadOrCreate();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        [TabGroup("Pages", "体检")]
        [BoxGroup("Pages/体检/汇总")]
        [HorizontalGroup("Pages/体检/汇总/Buttons")]
        [Button("修复重复枚举名", ButtonSizes.Medium)]
        [GUIColor(0.95f, 0.75f, 0.35f)]
        [PropertyTooltip("对重复的枚举名统一追加 _<GUID4> 后缀。规则只依赖冲突与自身 GUID，各分支合并后重新执行会收敛到同一结果。")]
        private void RepairDuplicateNames()
        {
            var config = _config ?? SceneEnumConfig.LoadOrCreate();
            if (SceneEnumIdentity.TryRepairDuplicateNames(config.Scenes, out int repaired))
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                Debug.Log($"[场景枚举] 已修复 {repaired} 个重复枚举名（追加 GUID 短后缀）");
            }
            else
            {
                Debug.Log("[场景枚举] 未发现重复枚举名，无需修复");
            }

            EnsureHealth(true);
            Repaint();
        }

        [TabGroup("Pages", "体检")]
        [BoxGroup("Pages/体检/问题")]
        [InfoBox("空列表表示无问题。所有「错误」项都会阻止代码生成，请先修正。", InfoMessageType.None)]
        [ShowInInspector, ReadOnly]
        [TableList(ShowIndexLabels = false, AlwaysExpanded = true, IsReadOnly = true)]
        private List<HealthItem> Issues
        {
            get
            {
                EnsureHealth();
                return _issues;
            }
        }

        // 体检结果缓存：Odin 的 [ShowInInspector] 在重绘时会反复求值，
        // 而体检要读取 YooAsset 收集配置 + 遍历条目，必须避免每帧重算。
        private string _summary = string.Empty;
        private List<HealthItem> _issues = new List<HealthItem>();
        private double _healthNextRefreshTime;

        /// <summary>
        /// 按需重建体检结果：命中节流窗口则直接返回缓存（默认 1 秒最多重建一次）。
        /// </summary>
        private void EnsureHealth(bool force = false)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!force && now < _healthNextRefreshTime)
            {
                return;
            }

            _healthNextRefreshTime = now + 1.0;
            _issues = BuildIssues();
            _summary = BuildSummary(_issues);
        }

        private sealed class HealthItem
        {
            [TableColumnWidth(70, Resizable = false)]
            [LabelText("级别")]
            [GUIColor("$Color")]
            public string Level;

            [TableColumnWidth(150, Resizable = true)]
            [LabelText("枚举名")]
            public string EnumName;

            [HideLabel]
            public string Detail;

            public Color Color => Level == "错误" ? new Color(0.95f, 0.42f, 0.35f)
                : Level == "提示" ? new Color(0.62f, 0.72f, 0.85f)
                : new Color(0.95f, 0.72f, 0.25f);
        }

        private string BuildSummary(List<HealthItem> issues)
        {
            var config = _config ?? SceneEnumConfig.Load();
            if (config == null)
            {
                return $"配置资产缺失：{SceneEnumConfig.ConfigAssetPath}";
            }

            var entries = config.Scenes ?? new List<SceneEnumConfig.SceneEntry>();
            int errorCount = 0;
            int warnCount = 0;
            foreach (var item in issues)
            {
                if (item.Level == "错误") errorCount++;
                else if (item.Level == "警告") warnCount++;
            }

            return
                $"配置资产：{SceneEnumConfig.ConfigAssetPath}\n" +
                $"场景目录：{config.SceneFolder}\n" +
                $"输出目录：{config.OutputFolder}\n" +
                $"条目：{entries.Count}（启用 {entries.Count(e => e.Active)}，停用 {entries.Count(e => !e.Active)}）\n" +
                $"体检：错误 {errorCount}，警告 {warnCount}";
        }

        private List<HealthItem> BuildIssues()
        {
            var result = new List<HealthItem>();
            var config = _config ?? SceneEnumConfig.Load();
            if (config == null)
            {
                result.Add(new HealthItem { Level = "错误", EnumName = "-", Detail = "配置资产缺失，请重新打开本窗口以自动创建。" });
                return result;
            }

            var entries = config.Scenes ?? new List<SceneEnumConfig.SceneEntry>();
            var active = entries.Where(e => e.Active).ToList();

            foreach (var entry in entries.Where(e => !e.Active))
            {
                result.Add(new HealthItem
                {
                    Level = "提示",
                    EnumName = DisplayNameOf(entry),
                    Detail = "已停用（不参与代码生成；枚举值随场景身份固定，无需占位）",
                });
            }

            foreach (var entry in active.Where(e => e.SceneAsset == null))
            {
                result.Add(new HealthItem
                {
                    Level = "错误",
                    EnumName = DisplayNameOf(entry),
                    Detail = "场景资源引用缺失（SceneAsset 为空），请「同步场景资源」或手动重新指定",
                });
            }

            foreach (var entry in active.Where(e => string.IsNullOrWhiteSpace(e.EnumName)))
            {
                result.Add(new HealthItem
                {
                    Level = "错误",
                    EnumName = DisplayNameOf(entry),
                    Detail = "枚举名为空，无法生成代码",
                });
            }

            foreach (var entry in active.Where(e => !string.IsNullOrWhiteSpace(e.EnumName) &&
                                                    !SceneEnumCodeGenerator.IsValidEnumName(e.EnumName)))
            {
                result.Add(new HealthItem
                {
                    Level = "错误",
                    EnumName = entry.EnumName,
                    Detail = "枚举名不是合法 C# 标识符（需字母/数字/下划线，且不以数字开头）",
                });
            }

            AddDuplicateIssues(result, active, e => e.EnumName, "枚举名");
            AddDuplicateIssues(result, active, e => e.ChineseName, "中文名");
            AddDuplicateEnumValueIssues(result, active);

            AddCollectRangeIssues(result, active);

            return result;
        }

        private static void AddDuplicateIssues(
            List<HealthItem> result,
            List<SceneEnumConfig.SceneEntry> active,
            System.Func<SceneEnumConfig.SceneEntry, string> selector,
            string label)
        {
            var duplicates = active
                .Where(e => !string.IsNullOrWhiteSpace(selector(e)))
                .GroupBy(selector)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicates)
            {
                result.Add(new HealthItem
                {
                    Level = "错误",
                    EnumName = group.Key,
                    Detail = $"{label}重复：被 {group.Count()} 个条目占用（{string.Join("、", group.Select(DisplayNameOf))}）",
                });
            }
        }

        private static void AddDuplicateEnumValueIssues(List<HealthItem> result, List<SceneEnumConfig.SceneEntry> active)
        {
            var duplicates = active.GroupBy(e => e.EnumValue).Where(g => g.Count() > 1);
            foreach (var group in duplicates)
            {
                result.Add(new HealthItem
                {
                    Level = "错误",
                    EnumName = string.Join("、", group.Select(DisplayNameOf)),
                    Detail = $"枚举值哈希碰撞（极罕见）：{group.Key}。请改动其中一个场景的资源重新生成 GUID，或联系维护者。",
                });
            }
        }

        private static void AddCollectRangeIssues(List<HealthItem> result, List<SceneEnumConfig.SceneEntry> active)
        {
            if (YooAssetCollectorReader.GetCollectPaths().Count == 0)
            {
                result.Add(new HealthItem
                {
                    Level = "警告",
                    EnumName = "-",
                    Detail = "未读取到 YooAsset Scenes Group 收集配置，生成时的收集范围校验已跳过",
                });
                return;
            }

            foreach (var entry in active.Where(e => e.SceneAsset != null &&
                                                    !YooAssetCollectorReader.IsSceneCollected(AssetDatabase.GetAssetPath(e.SceneAsset))))
            {
                result.Add(new HealthItem
                {
                    Level = "警告",
                    EnumName = DisplayNameOf(entry),
                    Detail = "不在 YooAsset Scenes Group 收集范围内，打包后无法加载",
                });
            }
        }

        private static string DisplayNameOf(SceneEnumConfig.SceneEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.EnumName))
            {
                return entry.EnumName;
            }
            return entry.SceneAsset != null ? entry.SceneAsset.name : "<未命名>";
        }

        #endregion
    }
}
