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
        /// 编辑器辅助：当前放置的预览实例（不序列化）。
        /// </summary>
        [System.NonSerialized]
        public GameObject previewInstance;

        /// <summary>
        /// 编辑器辅助属性：基于 <see cref="prefabRef"/> 的 GUID 解析/写回预制体引用。
        /// get 时按 GUID 现查现加载资源；set 时把资源转成 GUID 存储。
        /// 全程<b>不在序列化数据中保留任何 GameObject 引用</b>，因此不产生 Bundle 依赖。
        /// </summary>
        public GameObject EditorPrefab
        {
            get
            {
                var guid = prefabRef?.AssetGUID;
                if (string.IsNullOrEmpty(guid)) return null;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                return string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            set
            {
                if (prefabRef == null) prefabRef = new AssetReferenceGameObject();

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
