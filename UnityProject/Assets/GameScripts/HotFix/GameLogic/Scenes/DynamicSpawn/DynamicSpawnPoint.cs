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
    /// 挂在场景空节点上的占位组件。美术在编辑器里摆好空节点的 TRS 并拖入预制体，
    /// 运行时 <see cref="DynamicSceneSpawner"/> 优先按 <see cref="prefabRef"/>（GUID 弱引用）寻址加载，
    /// 未设置时回落 <see cref="location"/> 地址字符串，实例化后对齐到此节点。
    /// </summary>
    /// <remarks>
    /// 节点本身只有 Transform + 本组件，序列化体积极小，可大幅减少 .unity 场景文件大小。
    /// </remarks>
    public class DynamicSpawnPoint : MonoBehaviour
    {
        /// <summary>
        /// 预制体弱引用（包裹名 + GUID，非对象引用，不产生 Bundle 依赖）。
        /// 运行时优先按 GUID 寻址，预制体改名/移动目录不受影响；未设置时回落 <see cref="location"/>。
        /// </summary>
        [Tooltip("预制体弱引用（GUID 寻址，改名/移动不断）；未设置时回落 location 字符串")]
        [SerializeField]
        private AssetReferenceGameObject prefabRef = new AssetReferenceGameObject();

        /// <summary>
        /// YooAsset 资源地址（文件名，不含路径和扩展名）。
        /// 代码列表法/配置表驱动等运行时动态寻址场景使用；静态摆点优先用 <see cref="prefabRef"/>。
        /// </summary>
        [Tooltip("YooAsset 资源地址（文件名，不含路径和扩展名）；静态摆点建议用上面的弱引用，此处留空")]
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

        /// <summary>
        /// 预制体弱引用（只读访问，编辑器下通过 Inspector 拖拽赋值）。
        /// </summary>
        public AssetReferenceGameObject PrefabRef => prefabRef;

#if UNITY_EDITOR
        /// <summary>
        /// 旧版编辑器辅助字段：预制体资源 GUID（字符串）。
        /// 已废弃——新数据统一存到 <see cref="prefabRef"/>，本字段仅用于迁移存量场景，
        /// 由 <see cref="MigrateToAssetReferenceIfNeeded"/> 搬空后保持为空。切勿直接读写。
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
        /// 编辑器辅助属性：基于 <see cref="prefabRef"/> 的 GUID 解析/写回预制体引用
        /// （未迁移的旧数据回落 <see cref="prefabGuid"/>）。
        /// get 时按 GUID 现查现加载资源；set 时把资源转成 GUID 存储。
        /// 全程<b>不在序列化数据中保留任何 GameObject 引用</b>，因此不产生 Bundle 依赖。
        /// </summary>
        public GameObject EditorPrefab
        {
            get
            {
                var guid = prefabRef != null && !string.IsNullOrEmpty(prefabRef.AssetGUID)
                    ? prefabRef.AssetGUID
                    : prefabGuid;
                if (string.IsNullOrEmpty(guid)) return null;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                return string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            set
            {
                if (prefabRef == null) prefabRef = new AssetReferenceGameObject();
                prefabGuid = string.Empty;

                if (value == null)
                {
                    prefabRef.EditorSetAssetGUID(string.Empty);
                    return;
                }
                var path = AssetDatabase.GetAssetPath(value);
                prefabRef.EditorSetAssetGUID(string.IsNullOrEmpty(path)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(path));
            }
        }

        /// <summary>
        /// 把旧版 <see cref="prefabGuid"/> 字段迁移进 <see cref="prefabRef"/> 弱引用。
        /// 调用方在返回 true 时需对组件 <c>SetDirty</c> 并保存场景。
        /// </summary>
        /// <returns>发生迁移返回 true。</returns>
        public bool MigrateToAssetReferenceIfNeeded()
        {
            if (string.IsNullOrEmpty(prefabGuid)) return false;

            if (prefabRef == null) prefabRef = new AssetReferenceGameObject();
            if (string.IsNullOrEmpty(prefabRef.AssetGUID))
            {
                prefabRef.EditorSetAssetGUID(prefabGuid);
            }

            prefabGuid = string.Empty; // 单一数据源：GUID 统一由 prefabRef 持有
            return true;
        }

        /// <summary>
        /// 把历史遗留的 <see cref="prefabReference"/>（GameObject PPtr）迁移为 GUID 字符串，
        /// 并清空 PPtr 引用——这是真正"解开" Bundle 依赖的关键一步。
        /// 迁移目标为 <see cref="prefabRef"/> 弱引用（经由 <see cref="prefabGuid"/> 中转）。
        /// 调用方在返回 true 时需对组件 <c>SetDirty</c> 并保存场景，磁盘上的 PPtr 才会消失。
        /// </summary>
        /// <returns>发生迁移返回 true。</returns>
        public bool MigrateLegacyReferenceIfNeeded()
        {
            if (prefabReference == null) return false;

            // 仅在尚无 GUID 时用旧引用回填，避免覆盖已有新数据
            if (string.IsNullOrEmpty(prefabGuid) && (prefabRef == null || string.IsNullOrEmpty(prefabRef.AssetGUID)))
            {
                var path = AssetDatabase.GetAssetPath(prefabReference);
                prefabGuid = string.IsNullOrEmpty(path)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(path);
            }

            prefabReference = null; // 断开 PPtr —— 消除场景对预制体的 Bundle 依赖
            MigrateToAssetReferenceIfNeeded(); // 顺带搬进 prefabRef
            return true;
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
