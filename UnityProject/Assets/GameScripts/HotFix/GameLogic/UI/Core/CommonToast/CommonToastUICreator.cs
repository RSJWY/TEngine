using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// CommonToastUICreator
/// 功能描述：CommonToastUI预制体一键创建工具
/// 创建时间：2026-08-05 10:22
/// 开发者：Administrator
/// 最后修改：
/// 修改内容：修正根节点 RectTransform 必须全屏拉伸，否则遮罩无法覆盖屏幕、无法拦截点击
/// </summary>
public class CommonToastUICreator : MonoBehaviour
    {
#if UNITY_EDITOR
        [Header("点击下方按钮创建预制体")]
        [Tooltip("预制体保存路径")]
        public string savePath = "Assets/AssetRaw/UI/CommonToastUI.prefab";

        /// <summary>
        /// 创建CommonToastUI预制体
        /// </summary>
        public void CreatePrefab()
        {
            // 创建根节点
            GameObject root = new GameObject("CommonToastUI");

            // 添加Canvas
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999; // 确保在最上层

            // 添加CanvasScaler
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // 添加GraphicRaycaster
            root.AddComponent<GraphicRaycaster>();

            // 关键：根节点 RectTransform 必须全屏拉伸，否则子节点 Mask 无法覆盖屏幕
            RectTransform rootRT = root.GetComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;
            rootRT.pivot = new Vector2(0.5f, 0.5f);

            // 创建ToastRoot
            CreateToastRoot(root.transform);

            // 创建DialogRoot
            CreateDialogRoot(root.transform);

            // 确保目录存在
            string directory = System.IO.Path.GetDirectoryName(savePath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            // 保存为预制体
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, savePath);

            // 删除场景中的临时对象
            DestroyImmediate(root);

            // 选中预制体
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"<color=green>CommonToastUI预制体创建成功: {savePath}</color>");
        }

        /// <summary>
        /// 创建Toast模式的UI
        /// </summary>
        private void CreateToastRoot(Transform parent)
        {
            GameObject toastRoot = new GameObject("ToastRoot");
            RectTransform toastRT = toastRoot.AddComponent<RectTransform>();
            toastRT.SetParent(parent, false);

            // 居中锚点
            toastRT.anchorMin = new Vector2(0.5f, 0.5f);
            toastRT.anchorMax = new Vector2(0.5f, 0.5f);
            toastRT.pivot = new Vector2(0.5f, 0.5f);
            toastRT.anchoredPosition = Vector2.zero;
            toastRT.sizeDelta = new Vector2(600, 100);

            // 添加背景
            Image toastBg = toastRoot.AddComponent<Image>();
            toastBg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            toastBg.raycastTarget = false;

            // 添加CanvasGroup
            CanvasGroup toastCG = toastRoot.AddComponent<CanvasGroup>();
            toastCG.alpha = 0;
            // Toast 不拦截点击
            toastCG.interactable = false;
            toastCG.blocksRaycasts = false;

            // 创建消息文本
            GameObject toastMsg = new GameObject("ToastMessage");
            RectTransform msgRT = toastMsg.AddComponent<RectTransform>();
            msgRT.SetParent(toastRT, false);
            msgRT.anchorMin = Vector2.zero;
            msgRT.anchorMax = Vector2.one;
            msgRT.offsetMin = new Vector2(20, 10);
            msgRT.offsetMax = new Vector2(-20, -10);

            TextMeshProUGUI msgText = toastMsg.AddComponent<TextMeshProUGUI>();
            msgText.text = "Toast消息示例";
            msgText.fontSize = 28;
            msgText.color = Color.white;
            msgText.alignment = TextAlignmentOptions.Center;
            msgText.raycastTarget = false;

            toastRoot.SetActive(false);
        }

        /// <summary>
        /// 创建Dialog模式的UI
        /// </summary>
        private void CreateDialogRoot(Transform parent)
        {
            GameObject dialogRoot = new GameObject("DialogRoot");
            RectTransform dialogRT = dialogRoot.AddComponent<RectTransform>();
            dialogRT.SetParent(parent, false);
            // 全屏拉伸
            dialogRT.anchorMin = Vector2.zero;
            dialogRT.anchorMax = Vector2.one;
            dialogRT.offsetMin = Vector2.zero;
            dialogRT.offsetMax = Vector2.zero;

            // 添加CanvasGroup
            CanvasGroup dialogCG = dialogRoot.AddComponent<CanvasGroup>();
            dialogCG.alpha = 0;
            // 遮罩需要拦截点击
            dialogCG.interactable = true;
            dialogCG.blocksRaycasts = true;

            // 创建遮罩
            GameObject mask = new GameObject("Mask");
            RectTransform maskRT = mask.AddComponent<RectTransform>();
            maskRT.SetParent(dialogRT, false);
            maskRT.anchorMin = Vector2.zero;
            maskRT.anchorMax = Vector2.one;
            maskRT.offsetMin = Vector2.zero;
            maskRT.offsetMax = Vector2.zero;

            Image maskImg = mask.AddComponent<Image>();
            maskImg.color = new Color(0, 0, 0, 0.7f);
            maskImg.raycastTarget = true; // 遮罩需要拦截点击事件

            // 遮罩挂一个 Button，用于点击遮罩触发取消（在 CommonToastUI.OnCreate 中也会兜底添加）
            Button maskBtn = mask.AddComponent<Button>();
            maskBtn.transition = Selectable.Transition.None;

            // 创建面板
            GameObject panel = new GameObject("Panel");
            RectTransform panelRT = panel.AddComponent<RectTransform>();
            panelRT.SetParent(dialogRT, false);
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.anchoredPosition = Vector2.zero;
            panelRT.sizeDelta = new Vector2(600, 300);

            Image panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            panelBg.raycastTarget = false;

            // 创建消息文本
            GameObject dialogMsg = new GameObject("DialogMessage");
            RectTransform dialogMsgRT = dialogMsg.AddComponent<RectTransform>();
            dialogMsgRT.SetParent(panelRT, false);
            dialogMsgRT.anchorMin = new Vector2(0, 0.4f);
            dialogMsgRT.anchorMax = new Vector2(1, 1);
            dialogMsgRT.offsetMin = new Vector2(40, 20);
            dialogMsgRT.offsetMax = new Vector2(-40, -20);

            TextMeshProUGUI dialogMsgText = dialogMsg.AddComponent<TextMeshProUGUI>();
            dialogMsgText.text = "对话框消息内容";
            dialogMsgText.fontSize = 32;
            dialogMsgText.color = Color.white;
            dialogMsgText.alignment = TextAlignmentOptions.Center;
            dialogMsgText.raycastTarget = false;

            // 创建按钮组
            CreateButtonGroup(panelRT);

            dialogRoot.SetActive(false);
        }

        /// <summary>
        /// 创建按钮组
        /// </summary>
        private void CreateButtonGroup(RectTransform panelRT)
        {
            GameObject btnGroup = new GameObject("ButtonGroup");
            RectTransform btnGroupRT = btnGroup.AddComponent<RectTransform>();
            btnGroupRT.SetParent(panelRT, false);
            btnGroupRT.anchorMin = new Vector2(0, 0);
            btnGroupRT.anchorMax = new Vector2(1, 0.4f);
            btnGroupRT.offsetMin = new Vector2(40, 20);
            btnGroupRT.offsetMax = new Vector2(-40, -20);

            HorizontalLayoutGroup layout = btnGroup.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            // 创建取消按钮
            CreateButton(btnGroupRT, "CancelButton", "取消", new Color(0.3f, 0.3f, 0.3f));

            // 创建附加按钮（默认隐藏，运行时由 ConfirmCancelExtra 模式启用）
            CreateButton(btnGroupRT, "ExtraButton", "附加", new Color(0.5f, 0.3f, 0.3f));

            // 创建确认按钮
            CreateButton(btnGroupRT, "ConfirmButton", "确认", new Color(0.2f, 0.5f, 0.8f));
        }

        /// <summary>
        /// 创建单个按钮
        /// </summary>
        private void CreateButton(RectTransform parent, string name, string text, Color color)
        {
            GameObject btn = new GameObject(name);
            RectTransform btnRT = btn.AddComponent<RectTransform>();
            btnRT.SetParent(parent, false);
            btnRT.sizeDelta = new Vector2(200, 60);

            Image btnImg = btn.AddComponent<Image>();
            btnImg.color = color;

            Button btnComp = btn.AddComponent<Button>();
            btnComp.targetGraphic = btnImg;

            // 按钮文本
            GameObject btnText = new GameObject("Text");
            RectTransform btnTextRT = btnText.AddComponent<RectTransform>();
            btnTextRT.SetParent(btnRT, false);
            btnTextRT.anchorMin = Vector2.zero;
            btnTextRT.anchorMax = Vector2.one;
            btnTextRT.offsetMin = Vector2.zero;
            btnTextRT.offsetMax = Vector2.zero;

            TextMeshProUGUI btnTMP = btnText.AddComponent<TextMeshProUGUI>();
            btnTMP.text = text;
            btnTMP.fontSize = 28;
            btnTMP.color = Color.white;
            btnTMP.alignment = TextAlignmentOptions.Center;
            btnTMP.raycastTarget = false;
        }
#endif
    }

#if UNITY_EDITOR
[CustomEditor(typeof(CommonToastUICreator))]
public class CommonToastUICreatorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);

        CommonToastUICreator creator = (CommonToastUICreator)target;

        if (GUILayout.Button("Create CommonToastUI Prefab", GUILayout.Height(40)))
        {
            creator.CreatePrefab();
        }

        EditorGUILayout.HelpBox(
            "点击按钮将自动创建CommonToastUI预制体到指定路径。\n" +
            "创建完成后可以删除此脚本组件。\n\n" +
            "注意：根节点 RectTransform 必须全屏拉伸，遮罩才能覆盖屏幕并拦截背景点击。",
            MessageType.Info
        );
    }
}
#endif
