using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace TEngine.SceneTools
{
    /// <summary>
    /// 场景枚举自动生成配置（数据源 + 顺序注册表）。
    /// <para>每条目以 GUID 追踪场景身份：场景改名不丢失引用，资源地址自动跟随新文件名；枚举名/枚举值保持稳定（代码契约）。</para>
    /// <para>Inspector「同步场景资源」扫描目录追加新场景；「生成枚举代码」落盘
    /// SceneType.g.cs / SceneConstName.g.cs / SceneTypeMapping.g.cs。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "TEngine/场景枚举配置", fileName = "SceneEnumConfig")]
    public class SceneEnumConfig : ScriptableObject
    {
        public const string DefaultSceneFolder = "Assets/AssetRaw/Scenes";
        public const string DefaultOutputFolder = "Assets/GameScripts/HotFix/GameLogic/Module/GameScene/Generated";
        public const string CodeNamespace = "GameLogic";

        [FolderPath(RequireExistingPath = true)]
        [LabelText("场景资源目录")]
        [OnValueChanged(nameof(OnFolderChanged))]
        [InfoBox("同步时优先从 YooAsset Scenes Group 读取收集目录；读不到才回退到此目录。", InfoMessageType.Info)]
        public string SceneFolder = DefaultSceneFolder;

        [FolderPath(RequireExistingPath = true)]
        [LabelText("代码输出目录")]
        public string OutputFolder = DefaultOutputFolder;

        [ListDrawerSettings(ShowItemCount = true, ShowPaging = false, DraggableItems = true)]
        [LabelText("场景列表")]
        [PropertySpace(8)]
        [OnValueChanged(nameof(OnScenesChanged))]
        [ValidateInput(nameof(ValidateChineseNameUnique), "中文名重复，无法生成代码，请先修正后再生成。", InfoMessageType.Error)]
        public List<SceneEntry> Scenes = new List<SceneEntry>();

        [Button("同步场景资源", ButtonSizes.Large)]
        [GUIColor(0.7f, 0.8f, 1f)]
        public void SyncScenes() => SceneEnumSyncUtil.SyncScenes(this);

        [Button("生成枚举代码", ButtonSizes.Large)]
        [GUIColor(0.4f, 0.9f, 0.4f)]
        public void GenerateCode() => SceneEnumCodeGenerator.Generate(this);

        private void OnEnable()
        {
            InjectOwner();
        }

        private void OnFolderChanged()
        {
            Debug.Log("[场景枚举] 场景资源目录已变更，请点击「同步场景资源」刷新列表");
        }

        /// <summary>
        /// 列表级兜底回调（增删/移动/复制条目时触发）：注入宿主引用，
        /// 使条目内 <see cref="SceneEntry.OnSceneAssetChanged"/> 能正确 SetDirty 宿主配置。
        /// </summary>
        private void OnScenesChanged()
        {
            InjectOwner();
            EditorUtility.SetDirty(this);
        }

        /// <summary>向所有条目注入宿主引用（Owner 不序列化，加载与列表变更时补注入）。</summary>
        private void InjectOwner()
        {
            if (Scenes == null)
            {
                return;
            }

            foreach (SceneEntry entry in Scenes)
            {
                if (entry != null)
                {
                    entry.Owner = this;
                }
            }
        }

        /// <summary>
        /// Odin 校验：启用的 <see cref="SceneEntry"/> 中 <see cref="SceneEntry.ChineseName"/> 必须唯一（空值不参与冲突检测）。
        /// 重复时在 Inspector 直接警示并阻止 <see cref="GenerateCode"/> 推进（生成器内有同名兜底校验）。
        /// </summary>
        private bool ValidateChineseNameUnique(List<SceneEntry> scenes, ref string errorMessage)
        {
            if (scenes == null || scenes.Count == 0)
            {
                return true;
            }

            var duplicates = scenes
                .Where(e => e.Active && !string.IsNullOrWhiteSpace(e.ChineseName))
                .GroupBy(e => e.ChineseName)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Count == 0)
            {
                return true;
            }

            errorMessage = "中文名重复，无法生成代码：\n" + string.Join("\n",
                duplicates.Select(g => $"「{g.Key}」被 {g.Count()} 个场景占用：{string.Join("、", g.Select(e => string.IsNullOrEmpty(e.EnumName) ? "<未命名>" : e.EnumName))}"));
            return false;
        }

        /// <summary>
        /// 配置资产固定路径：与工具代码同目录，避免落入 Resources 被误打进包体
        /// （本资产仅 Editor 使用，且含 <see cref="UnityEditor.SceneAsset"/> 引用）。
        /// </summary>
        public const string ConfigAssetPath = "Assets/Editor/SceneTools/SceneEnumGenerator/SceneEnumConfig.asset";

        private const string ConfigFolderPath = "Assets/Editor/SceneTools/SceneEnumGenerator";

        /// <summary>
        /// 仅加载配置资产；不存在返回 null（只读场景用，不产生副作用）。
        /// </summary>
        public static SceneEnumConfig Load()
        {
            return AssetDatabase.LoadAssetAtPath<SceneEnumConfig>(ConfigAssetPath);
        }

        /// <summary>
        /// 加载配置资产，缺失时按固定路径创建。入口见
        /// <see cref="SceneEnumConfigWindow"/>（菜单 TEngine &gt; 场景枚举配置）。
        /// </summary>
        public static SceneEnumConfig LoadOrCreate()
        {
            SceneEnumConfig config = Load();
            if (config != null)
            {
                return config;
            }

            if (!AssetDatabase.IsValidFolder(ConfigFolderPath))
            {
                Directory.CreateDirectory(ConfigFolderPath);
                AssetDatabase.ImportAsset(ConfigFolderPath, ImportAssetOptions.ForceUpdate);
            }

            config = CreateInstance<SceneEnumConfig>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[场景枚举] 已创建配置资产：{ConfigAssetPath}");
            return config;
        }

        /// <summary>
        /// 场景条目：一个场景对应一条记录（每个条目多行展开显示）。
        /// <para>ChineseName：Editor 菜单显示用中文名（区别于 DisplayName 备注，后者写入枚举 XML 注释）。</para>
        /// </summary>
        [Serializable]
        [InlineProperty]
        public class SceneEntry
        {
            [HorizontalGroup("行1", 0.4f)]
            [LabelText("场景资源")]
            [LabelWidth(70)]
            [OnValueChanged(nameof(OnSceneAssetChanged))]
            public SceneAsset SceneAsset;

            [HorizontalGroup("行1", 0.4f)]
            [ShowInInspector]
            [ReadOnly]
            [LabelText("资源地址")]
            [LabelWidth(60)]
            [Tooltip("YooAsset location = 场景文件名，生成时自动读取，场景改名后重新生成会跟随。")]
            public string Address => SceneAsset != null
                ? System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(SceneAsset))
                : "<缺失>";

            [HorizontalGroup("行1", 0.2f)]
            [ShowInInspector]
            [LabelText("枚举值")]
            [LabelWidth(50)]
            [ReadOnly]
            [GUIColor("$EnumValueColor")]
            [Tooltip("由场景 GUID 确定性派生（非本地计数器）：多人协同各自新增场景不会撞值，且随场景身份固定、改名不变；无需占位。\n显示 0（红色）= 场景资源缺失，GUID 未初始化。")]
            public int EnumValue => SceneEnumIdentity.DeriveValue(EffectiveSceneGuid);

            /// <summary>枚举值显示 0（未初始化哨兵）时红色提示，便于及时发现场景资源缺失的条目。</summary>
            private Color EnumValueColor => EnumValue == 0 ? new Color(1f, 0.55f, 0.45f) : Color.white;

            /// <summary>
            /// 读路径兜底：<see cref="SceneGuid"/> 未落盘（手动拖入后尚未同步/保存）时，
            /// 实时按资源路径取 GUID，保证任何读路径（面板/体检/生成）都拿不到 0。
            /// </summary>
            private string EffectiveSceneGuid => string.IsNullOrEmpty(SceneGuid) && SceneAsset != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(SceneAsset))
                : SceneGuid;

            /// <summary>冗余存 GUID，SceneAsset 变 missing 时仍可追踪身份（删除/改名识别）。</summary>
            [HideInInspector]
            public string SceneGuid;

            /// <summary>宿主配置引用（不序列化）：条目级回调中 SetDirty 用，由配置在加载/列表变更时注入。</summary>
            [NonSerialized]
            public SceneEnumConfig Owner;

            /// <summary>
            /// 按当前 <see cref="SceneAsset"/> 实际路径刷新 GUID；资源为空时保留旧 GUID
            /// （用于识别删除/改名）。手动替换场景资源后调用可对齐真实身份。
            /// </summary>
            /// <returns>GUID 是否发生变化（调用方可据此 SetDirty）。</returns>
            public bool RefreshSceneGuid()
            {
                if (SceneAsset == null)
                {
                    return false;
                }

                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(SceneAsset));
                if (guid == SceneGuid)
                {
                    return false;
                }

                SceneGuid = guid;
                return true;
            }

            /// <summary>拖入/替换场景资源时立即补全 GUID，无需依赖「同步场景资源」。</summary>
            private void OnSceneAssetChanged()
            {
                bool changed = RefreshSceneGuid();
                SceneEnumConfig owner = Owner ?? SceneEnumConfig.Load();
                if (owner != null && (changed || owner != Owner))
                {
                    owner.InjectOwner();
                    EditorUtility.SetDirty(owner);
                }
            }

            [HorizontalGroup("行2", 0.4f)]
            [LabelText("枚举名")]
            [LabelWidth(60)]
            [Tooltip("代码中的枚举标识符，默认取场景文件名清洗结果。建议改为清晰英文名；场景改名不影响枚举名（代码契约稳定）。")]
            public string EnumName;

            [HorizontalGroup("行2", 0.4f)]
            [LabelText("中文名")]
            [LabelWidth(60)]
            [Tooltip("Editor 工具栏「注册场景」菜单显示用中文名（区别于下方中文备注）。可填中文场景名；为空则菜单回退显示枚举名。")]
            public string ChineseName;

            [HorizontalGroup("行2", 0.2f)]
            [LabelText("启用")]
            [LabelWidth(40)]
            [Tooltip("关闭后该场景不生成枚举；枚举值由 GUID 派生，无需保留占位。")]
            public bool Active = true;

            [HorizontalGroup("行3", 0.8f)]
            [LabelText("中文备注")]
            [LabelWidth(60)]
            [Tooltip("写入枚举的 XML 注释，便于查阅。可填中文场景名。")]
            public string DisplayName;
        }
    }
}
