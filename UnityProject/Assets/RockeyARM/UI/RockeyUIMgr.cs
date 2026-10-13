// RockeyUIMgr.cs
// 作用：零框架依赖的轻量 UI 管理器（仿 Assets/Launcher/Scripts/LauncherMgr.cs，可独立拷进任何工程）。
// 约定：prefab 放在 Resources/UIWindow/<UI类名>，用 Resources.Load 加载（不依赖任何资源框架）。
//       若场景存在 "UIRoot/UICanvas" 节点则挂到其下，否则以 prefab 自带 Canvas 独立显示。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace RockeyArm
{
    /// <summary>轻量 UI 管理器（零框架依赖）。</summary>
    public static class RockeyUIMgr
    {
        const string UI_ROOT_PATH = "UIRoot/UICanvas";
        const string UI_WINDOW_PATH = "UIWindow/";
        static Transform m_uiRoot;
        static readonly Dictionary<string, RockeyUIBase> m_uiMapDict = new Dictionary<string, RockeyUIBase>(4);

        /// <summary>查找场景 UI 根节点（可选；找不到时 UI 以 prefab 自带 Canvas 独立显示）。</summary>
        public static void Initialize()
        {
            m_uiRoot = GameObject.Find(UI_ROOT_PATH)?.transform;
        }

        /// <summary>空场景兜底：没有 EventSystem 时按钮无法点击，自动补一个。</summary>
        static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        public static void ShowUI<T>(object param = null) where T : RockeyUIBase, new()
        {
            string uiName = typeof(T).Name;
            if (string.IsNullOrEmpty(uiName))
            {
                Debug.LogWarning("======== RockeyUIMgr.ShowUI UIName 为空 ========");
                return;
            }

            if (!m_uiMapDict.TryGetValue(uiName, out var uiBase))
            {
                Object obj = Resources.Load(UI_WINDOW_PATH + uiName);
                if (obj == null)
                {
                    Debug.LogWarning($"======== 未找到 UI prefab：Resources/{UI_WINDOW_PATH}{uiName} ========");
                    return;
                }

                EnsureEventSystem();

                var uiWindow = Object.Instantiate(obj) as GameObject;
                if (uiWindow == null)
                {
                    return;
                }

                if (m_uiRoot != null)
                {
                    uiWindow.transform.SetParent(m_uiRoot, false);
                    uiWindow.transform.localScale = Vector3.one;
                    uiWindow.transform.localPosition = Vector3.zero;
                    uiWindow.transform.localRotation = Quaternion.identity;
                    RectTransform rectTransform = uiWindow.GetComponent<RectTransform>();
                    if (rectTransform != null)
                    {
                        rectTransform.sizeDelta = Vector2.zero;
                    }
                }

                uiWindow.name = uiName;

                uiBase = new T();
                uiBase.gameObject = uiWindow;
                uiBase.CallScriptGenerator();
                m_uiMapDict[uiName] = uiBase;
            }

            uiBase?.Show();
            uiBase?.OnInit(param);
        }

        public static void CloseUI(RockeyUIBase uiWindow) => CloseUI(uiWindow.GetType().Name);

        public static void CloseUI(string uiName)
        {
            if (string.IsNullOrEmpty(uiName))
            {
                return;
            }

            if (!m_uiMapDict.TryGetValue(uiName, out var uiWindow))
            {
                return;
            }

            uiWindow?.Hide();
            uiWindow?.CallOnClose();
            DestroyObject(uiWindow?.gameObject);
            m_uiMapDict.Remove(uiName);
        }

        public static T GetActiveUI<T>() where T : RockeyUIBase => GetActiveUI(typeof(T).Name) as T;

        public static RockeyUIBase GetActiveUI(string uiName) => m_uiMapDict.GetValueOrDefault(uiName);

        public static void HideAllUI()
        {
            foreach (var ui in m_uiMapDict.Values)
            {
                ui?.Hide();
                ui?.CallOnClose();
                DestroyObject(ui?.gameObject);
            }

            m_uiMapDict.Clear();
        }

        /// <summary>便捷入口：弹授权错误框（数据为 ExitPanelData）。退出动作由 ExitErrorUI 内置，onConfirm 仅为退出前附加钩子。</summary>
        public static void ShowError(string content, Action onConfirm = null)
        {
            ShowUI<ExitErrorUI>(new ExitPanelData
            {
                title = "授权验证失败",
                contenet = content,
                buttonText = "退出",
                onConfirm = onConfirm
            });
        }

        static void DestroyObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }

#if UNITY_EDITOR
            Object.DestroyImmediate(go);
#else
            Object.Destroy(go);
#endif
        }
    }
}
