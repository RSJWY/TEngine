// RockeyUIBase.cs
// 作用：零框架依赖的轻量 UI 基类（仿 Assets/Launcher/Scripts/UIBase.cs，可独立拷进任何工程）。
// 约定：UI 类为纯 C# 类（不挂 prefab），由 RockeyUIMgr 实例化后绑定 gameObject，
//       组件查找用层级路径（FindChildComponent），与 TEngine 的 UIBindComponent/生成代码体系无关。

using UnityEngine;

namespace RockeyArm
{
    /// <summary>轻量 UI 基类（零框架依赖）。</summary>
    public class RockeyUIBase
    {
        public GameObject gameObject;

        public Transform transform => gameObject != null ? gameObject.transform : null;

        public RectTransform rectTransform => transform as RectTransform;

        protected object m_param;

        /// <summary>实例化后由管理器调用：按层级路径绑定组件、挂事件。</summary>
        protected virtual void ScriptGenerator() { }

        /// <summary>关闭前由管理器调用：解事件、停协程等清理。</summary>
        protected virtual void OnClose() { }

        public void CallScriptGenerator() => ScriptGenerator();

        public void CallOnClose() => OnClose();

        /// <summary>显示时由管理器调用：应用传入参数。</summary>
        public virtual void OnInit(object param) => m_param = param;

        public void Show() => gameObject?.SetActive(true);

        public void Hide() => gameObject?.SetActive(false);

        public void Close() => RockeyUIMgr.CloseUI(this);

        #region FindChildComponent

        public Transform FindChild(string path) => FindChildImp(rectTransform, path);

        public Transform FindChild(Transform trans, string path) => FindChildImp(trans, path);

        public T FindChildComponent<T>(string path) where T : Component => FindChildComponentImp<T>(rectTransform, path);

        public T FindChildComponent<T>(Transform trans, string path) where T : Component => FindChildComponentImp<T>(trans, path);

        static Transform FindChildImp(Transform trans, string path)
        {
            if (trans == null)
            {
                return null;
            }

            var findTrans = trans.Find(path);
            return findTrans == null ? null : findTrans;
        }

        static T FindChildComponentImp<T>(Transform trans, string path) where T : Component
        {
            var findTrans = FindChildImp(trans, path);
            return findTrans == null ? null : findTrans.GetComponent<T>();
        }

        #endregion
    }
}
