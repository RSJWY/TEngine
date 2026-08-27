using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace TEngine
{
    public static class UnityExtension
    {
        /// <summary>
        /// 为控件添加自定义事件监听器（EventTrigger 封装）。
        /// </summary>
        /// <param name="control">目标控件。</param>
        /// <param name="type">事件类型。</param>
        /// <param name="action">回调函数。</param>
        public static void AddCustomEventListener(this UIBehaviour control, EventTriggerType type, UnityAction<BaseEventData> action)
        {
            Utility.Unity.AddCustomEventListener(control, type, action);
        }

        /// <summary>
        /// 为控件移除自定义事件监听器。
        /// </summary>
        /// <param name="control">目标控件。</param>
        /// <param name="type">事件类型。</param>
        /// <param name="action">回调函数。</param>
        public static void RemoveCustomEventListener(this UIBehaviour control, EventTriggerType type, UnityAction<BaseEventData> action)
        {
            Utility.Unity.RemoveCustomEventListener(control, type, action);
        }
    }
}
