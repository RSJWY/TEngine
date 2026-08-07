using System;
using System.Collections.Generic;
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
        public const string DefaultOutputFolder = "Assets/GameScripts/HotFix/GameLogic/Module/GameScene";
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
        public List<SceneEntry> Scenes = new List<SceneEntry>();

        [Button("同步场景资源", ButtonSizes.Large)]
        [GUIColor(0.7f, 0.8f, 1f)]
        public void SyncScenes() => SceneEnumSyncUtil.SyncScenes(this);

        [Button("生成枚举代码", ButtonSizes.Large)]
        [GUIColor(0.4f, 0.9f, 0.4f)]
        public void GenerateCode() => SceneEnumCodeGenerator.Generate(this);

        private void OnFolderChanged()
        {
            Debug.Log("[场景枚举] 场景资源目录已变更，请点击「同步场景资源」刷新列表");
        }

        private const string ConfigAssetPath = "Assets/Resources/SceneEnumConfig.asset";

        /// <summary>
        /// 菜单：创建或打开配置资产（统一存放在 Assets/Resources/ 下）。
        /// </summary>
        [MenuItem("TEngine/场景枚举配置")]
        public static void OpenConfig()
        {
            SceneEnumConfig config = AssetDatabase.LoadAssetAtPath<SceneEnumConfig>(ConfigAssetPath);
            if (config == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                {
                    AssetDatabase.CreateFolder("Assets", "Resources");
                }
                config = ScriptableObject.CreateInstance<SceneEnumConfig>();
                AssetDatabase.CreateAsset(config, ConfigAssetPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[场景枚举] 已创建配置资产：{ConfigAssetPath}");
            }
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        /// <summary>
        /// 场景条目：一个场景对应一条记录（每个条目多行展开显示）。
        /// </summary>
        [Serializable]
        [InlineProperty]
        public class SceneEntry
        {
            [HorizontalGroup("行1", 0.4f)]
            [LabelText("场景资源")]
            [LabelWidth(70)]
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
            [LabelText("枚举值")]
            [LabelWidth(50)]
            [ReadOnly]
            [Tooltip("固定整数值，保证顺序稳定：新增追加、删除保留空缺。")]
            public int EnumValue;

            /// <summary>冗余存 GUID，SceneAsset 变 missing 时仍可追踪身份（删除/改名识别）。</summary>
            [HideInInspector]
            public string SceneGuid;

            [HorizontalGroup("行2", 0.4f)]
            [LabelText("枚举名")]
            [LabelWidth(60)]
            [Tooltip("代码中的枚举标识符，默认取场景文件名清洗结果。建议改为清晰英文名；场景改名不影响枚举名（代码契约稳定）。")]
            public string EnumName;

            [HorizontalGroup("行2", 0.4f)]
            [LabelText("中文备注")]
            [LabelWidth(60)]
            [Tooltip("写入枚举的 XML 注释，便于查阅。可填中文场景名。")]
            public string DisplayName;

            [HorizontalGroup("行2", 0.2f)]
            [LabelText("启用")]
            [LabelWidth(40)]
            [Tooltip("关闭后该场景不生成枚举，但枚举值保留占位。")]
            public bool Active = true;
        }
    }
}
