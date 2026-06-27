using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// DynamicSpawnPoint
/// 功能描述：场景内占位组件——标记一个动态加载点，运行时由 DynamicSceneSpawner 加载真实预制体并对齐。
/// 创建时间：2026-06-25
/// 开发者：lzx
/// </summary>

namespace GameLogic
{
    /// <summary>
    /// 对齐模式：决定加载的预制体实例如何对齐到占位节点。
    /// </summary>
    public enum SpawnAlignMode
    {
        /// <summary>
        /// 实例作为占位节点子物体，localPosition/localRotation/localScale 归零（用占位节点的世界 TRS）。
        /// </summary>
        [InspectorName("对齐占位节点（TRS 归零）")]
        AlignToPlaceholder,

        /// <summary>
        /// 实例作为占位节点子物体，但保留预制体自带的 localPosition/localRotation/localScale。
        /// 适合预制体根节点本身有偏移的情况。
        /// </summary>
        [InspectorName("保留预制体偏移")]
        KeepPrefabLocal
    }

    /// <summary>
    /// 挂在场景空节点上的占位组件。美术在编辑器里摆好空节点的 TRS，
    /// 运行时 <see cref="DynamicSceneSpawner"/> 按 <see cref="location"/> 加载预制体并对齐到此节点。
    /// </summary>
    /// <remarks>
    /// 节点本身只有 Transform + 本组件，序列化体积极小，可大幅减少 .unity 场景文件大小。
    /// </remarks>
    public class DynamicSpawnPoint : MonoBehaviour
    {
        /// <summary>
        /// YooAsset 资源地址（文件名，不含路径和扩展名）。
        /// </summary>
        [Tooltip("YooAsset 资源地址（文件名，不含路径和扩展名）")]
        public string location;

        /// <summary>
        /// 对齐模式。
        /// </summary>
        [Tooltip("AlignToPlaceholder：实例 TRS 归零对齐占位节点；KeepPrefabLocal：保留预制体自带偏移")]
        public SpawnAlignMode alignMode = SpawnAlignMode.AlignToPlaceholder;

        /// <summary>
        /// 可选的唯一标识键（为将来注册表/延迟绑定预留，当前静态装饰物无需设置）。
        /// </summary>
        [Tooltip("可选唯一标识键，用于运行时注册表查找（静态装饰物无需设置）")]
        public string registerKey;

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器辅助：预制体资源 GUID（字符串）。
        /// 仅编辑器下存在，且以<b>字符串</b>形式保存——不会产生 PPtr 对象引用，
        /// 因此场景<b>不会</b>与预制体建立 Bundle 依赖。打包后该字段不存在。
        /// 不要直接读写它，请统一使用 <see cref="EditorPrefab"/>。
        /// </summary>
        [HideInInspector]
        [SerializeField]
        private string prefabGuid;

        /// <summary>
        /// 旧版直接引用字段（已废弃，仅用于迁移）。
        /// 历史场景里已经把 GameObject PPtr 序列化进了 .unity 文件——正是这个 PPtr
        /// 导致打包时场景对预制体产生 Bundle 依赖。<see cref="MigrateLegacyReferenceIfNeeded"/>
        /// 会把它转成 <see cref="prefabGuid"/> 并清空，从而解开依赖。
        /// 字段名必须保持 <c>prefabReference</c> 才能匹配旧序列化数据，切勿赋值。
        /// </summary>
        [HideInInspector]
        [SerializeField]
        private GameObject prefabReference;

        /// <summary>
        /// 编辑器辅助：当前放置的预览实例（不序列化）。
        /// </summary>
        [System.NonSerialized]
        public GameObject previewInstance;

        /// <summary>
        /// 编辑器辅助属性：基于 <see cref="prefabGuid"/> 解析/写回预制体引用。
        /// get 时按 GUID 现查现加载资源；set 时把资源转成 GUID 存储。
        /// 全程<b>不在序列化数据中保留任何 GameObject 引用</b>，因此不产生 Bundle 依赖。
        /// </summary>
        public GameObject EditorPrefab
        {
            get
            {
                if (string.IsNullOrEmpty(prefabGuid)) return null;
                var path = AssetDatabase.GUIDToAssetPath(prefabGuid);
                return string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            set
            {
                if (value == null)
                {
                    prefabGuid = string.Empty;
                    return;
                }
                var path = AssetDatabase.GetAssetPath(value);
                prefabGuid = string.IsNullOrEmpty(path)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(path);
            }
        }

        /// <summary>
        /// 把历史遗留的 <see cref="prefabReference"/>（GameObject PPtr）迁移为 GUID 字符串，
        /// 并清空 PPtr 引用——这是真正"解开" Bundle 依赖的关键一步。
        /// 调用方在返回 true 时需对组件 <c>SetDirty</c> 并保存场景，磁盘上的 PPtr 才会消失。
        /// </summary>
        /// <returns>发生迁移返回 true。</returns>
        public bool MigrateLegacyReferenceIfNeeded()
        {
            if (prefabReference == null) return false;

            // 仅在尚无 GUID 时用旧引用回填，避免覆盖已有新数据
            if (string.IsNullOrEmpty(prefabGuid))
            {
                var path = AssetDatabase.GetAssetPath(prefabReference);
                prefabGuid = string.IsNullOrEmpty(path)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(path);
            }

            prefabReference = null; // 断开 PPtr —— 消除场景对预制体的 Bundle 依赖
            return true;
        }

        [ContextMenu("从预制体引用填充 Location")]
        private void FillLocationFromPrefab()
        {
            var prefab = EditorPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[DynamicSpawnPoint] {name}: 预制体引用为空，无法填充 location。");
                return;
            }

            Undo.RecordObject(this, "Fill Location From Prefab");
            location = prefab.name;
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            Debug.Log($"[DynamicSpawnPoint] {name}: location 已填充为 \"{location}\"");
        }

        [ContextMenu("从预制体引用对齐节点名")]
        private void RenameToMatchPrefab()
        {
            var prefab = EditorPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[DynamicSpawnPoint] {name}: 预制体引用为空，无法重命名。");
                return;
            }

            Undo.RecordObject(gameObject, "Rename To Match Prefab");
            gameObject.name = $"[Spawn] {prefab.name}";
            Debug.Log($"[DynamicSpawnPoint] 节点已重命名为 \"{gameObject.name}\"");
        }
#endif
    }
}
