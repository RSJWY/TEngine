using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// GameObject 对象池根节点标记组件。
    /// <para>挂在不销毁的根节点上，用于场景切换后保留对象池层级。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameObjectPoolRoot : MonoBehaviour
    {
    }
}
