using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// SceneObjSpawn
/// 功能描述：
/// 创建时间：2026-04-01 14:26
/// 开发者：shaox
/// 最后修改：
/// 修改内容：
/// </summary>

namespace GameLogic
{
    public class SceneObjSpawn : MonoBehaviour
    {
        public List<GameObject> prefabe = new List<GameObject>();
        public List<string> prefabeLocation = new List<string>();
        // Start is called before the first frame update
        void Start()
        {
            for (int i = 0; i < prefabeLocation.Count; i++)
            {
                GameModule.Resource.LoadGameObjectAsync(prefabeLocation[i]);
            }
        }
#if UNITY_EDITOR
        [ContextMenu("同步 Prefab 路径")]
        private void SyncPrefabLocation()
        {
            // 1. 在修改数据前调用 Undo.RecordObject
            // 这会注册一个撤销操作（Ctrl+Z可用），并通知 Unity 该对象即将被修改（标记为 Dirty）
            Undo.RecordObject(this, "Sync Prefab Location");

            prefabeLocation.Clear();

            foreach (var go in prefabe)
            {
                if (go == null)
                {
                    prefabeLocation.Add(string.Empty);
                    continue;
                }

                string path = go.name;
                prefabeLocation.Add(path);
            }

            // 2. 如果这个脚本挂载在场景中的 Prefab 实例上，必须调用此方法
            // 否则 Unity 不会把这个 List 的变动作为 Prefab Override（预制体覆盖）保存到场景中
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);

            Debug.Log("同步完成！现在场景应该会出现未保存的星号(*)，请按 Ctrl+S 保存。");
        }
#endif
    }
}
