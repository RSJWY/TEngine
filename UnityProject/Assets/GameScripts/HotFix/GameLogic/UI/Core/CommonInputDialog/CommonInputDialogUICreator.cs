using UnityEngine;

using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 挂载到 CommonInputDialogUI 预制体根节点（带 Canvas/CanvasScaler/GraphicRaycaster）上，
/// 点击 Inspector 按钮：清空旧子节点 -> 重建 DialogRoot -> 回写 prefab。
/// 可重复执行，不会新建根节点。
/// </summary>
public class CommonInputDialogUICreator : MonoBehaviour
    {
#if UNITY_EDITOR
        [Header("点击下方按钮更新预制体（原地重建子节点）")]
        [Tooltip("预制体保存路径（默认即本预制体路径）")]
        public string savePath = "Assets/AssetRaw/UI/CommonInputDialogUI.prefab";

        public void CreatePrefab()
        {
            // 更新模式：直接使用当前挂载物体的根节点（自身），不新建根
            GameObject root = gameObject;

            // 确保根节点具备 Canvas/CanvasScaler/GraphicRaycaster
            EnsureRootComponents(root);

            // 根节点 RectTransform 全屏拉伸
            RectTransform rootRT = root.GetComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;
            rootRT.pivot = new Vector2(0.5f, 0.5f);

            // 清空旧子节点（DialogRoot 等全部移除），避免重复/残留
            ClearAllChildren(root.transform);

            // 重建 DialogRoot
            CreateDialogRoot(root.transform);

            // 确保目录存在
            string directory = System.IO.Path.GetDirectoryName(savePath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            // 回写 prefab 到指定路径（若该路径已是本 prefab，则原地更新）
            // 注意：当前打开的是预制体资源实例时，直接 Apply；否则 SaveAsPrefabAsset
            bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(root);
            GameObject prefab;
            if (isPrefabAsset)
            {
                // 已是预制体资源：直接 Apply 修改
                PrefabUtility.SavePrefabAsset(root);
                prefab = root;
            }
            else
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(root, savePath);
            }

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"<color=green>CommonInputDialogUI预制体更新成功: {(isPrefabAsset ? AssetDatabase.GetAssetPath(root) : savePath)}</color>");
        }

        /// <summary>
        /// 确保根节点有 Canvas/CanvasScaler/GraphicRaycaster，缺则补齐。
        /// 不覆盖已有配置（如 sortingOrder、referenceResolution）。
        /// </summary>
        private void EnsureRootComponents(GameObject root)
        {
            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 9999;
            }

            if (root.GetComponent<CanvasScaler>() == null)
            {
                CanvasScaler scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (root.GetComponent<GraphicRaycaster>() == null)
            {
                root.AddComponent<GraphicRaycaster>();
            }
        }

        /// <summary>
        /// 清空父节点下所有子物体（仅 DestroyImmediate 直接子节点）。
        /// </summary>
        private void ClearAllChildren(Transform parent)
        {
            // 倒序删除避免索引变动
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                DestroyImmediate(child.gameObject);
            }
        }

        private void CreateDialogRoot(Transform parent)
        {
            GameObject dialogRoot = new GameObject("DialogRoot");
            RectTransform dialogRT = dialogRoot.AddComponent<RectTransform>();
            dialogRT.SetParent(parent, false);
            dialogRT.anchorMin = Vector2.zero;
            dialogRT.anchorMax = Vector2.one;
            dialogRT.offsetMin = Vector2.zero;
            dialogRT.offsetMax = Vector2.zero;

            CanvasGroup dialogCG = dialogRoot.AddComponent<CanvasGroup>();
            dialogCG.alpha = 1;
            dialogCG.interactable = true;
            dialogCG.blocksRaycasts = true;

            // 遮罩
            GameObject mask = new GameObject("Mask");
            RectTransform maskRT = mask.AddComponent<RectTransform>();
            maskRT.SetParent(dialogRT, false);
            maskRT.anchorMin = Vector2.zero;
            maskRT.anchorMax = Vector2.one;
            maskRT.offsetMin = Vector2.zero;
            maskRT.offsetMax = Vector2.zero;

            Image maskImg = mask.AddComponent<Image>();
            maskImg.color = new Color(0, 0, 0, 0.7f);
            maskImg.raycastTarget = true;

            Button maskBtn = mask.AddComponent<Button>();
            maskBtn.transition = Selectable.Transition.None;

            // 面板
            GameObject panel = new GameObject("Panel");
            RectTransform panelRT = panel.AddComponent<RectTransform>();
            panelRT.SetParent(dialogRT, false);
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.anchoredPosition = Vector2.zero;
            panelRT.sizeDelta = new Vector2(700, 600);

            Image panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            panelBg.raycastTarget = false;

            // Panel 用垂直布局管理标题/预览图/输入区/按钮组
            VerticalLayoutGroup panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(40, 40, 30, 30);
            panelLayout.spacing = 16;
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true; // 让 VLG 根据子元素 preferred 高度控制高度
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            // Panel 高度根据内容自适应（标题/消息/预览图/输入框显隐时背景高度自动变化）
            ContentSizeFitter panelCsf = panel.AddComponent<ContentSizeFitter>();
            panelCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // 宽度保持固定
            panelCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // 标题
            GameObject title = new GameObject("Title");
            RectTransform titleRT = title.AddComponent<RectTransform>();
            titleRT.SetParent(panelRT, false);
            titleRT.sizeDelta = new Vector2(0, 50);
            TextMeshProUGUI titleTmp = title.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "标题";
            titleTmp.fontSize = 36;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.raycastTarget = false;
            ContentSizeFitter titleCsf = title.AddComponent<ContentSizeFitter>();
            titleCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 副标题/提示消息
            GameObject message = new GameObject("Message");
            RectTransform messageRT = message.AddComponent<RectTransform>();
            messageRT.SetParent(panelRT, false);
            messageRT.sizeDelta = new Vector2(0, 30);
            TextMeshProUGUI messageTmp = message.AddComponent<TextMeshProUGUI>();
            messageTmp.text = "请输入内容";
            messageTmp.fontSize = 24;
            messageTmp.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            messageTmp.alignment = TextAlignmentOptions.Center;
            messageTmp.raycastTarget = false;
            ContentSizeFitter messageCsf = message.AddComponent<ContentSizeFitter>();
            messageCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 预览图（RawImage + AspectRatioFitter），默认隐藏，由 UI 脚本按需显示
            CreatePreviewImage(panelRT);

            // 单行输入区
            CreateSingleLineRoot(panelRT);

            // 多行输入区
            CreateMultiLineRoot(panelRT);

            // 按钮组
            CreateButtonGroup(panelRT);

            // 默认隐藏，由 UI 脚本控制
            dialogRoot.SetActive(false);
        }

        /// <summary>
        /// 创建预览图节点：PreviewImage(RawImage + LayoutElement)。
        /// 默认 inactive，UI 脚本根据 InputDialogData.previewImage 是否为空决定显示。
        /// 高度由 UI 脚本运行时按图片真实宽高比 + Panel 实际宽度动态计算（设置 LayoutElement.preferredHeight）。
        /// 不使用 AspectRatioFitter/ContentSizeFitter，避免与 VerticalLayoutGroup 冲突导致高度被压成 0。
        /// </summary>
        private void CreatePreviewImage(RectTransform panelRT)
        {
            GameObject root = new GameObject("PreviewImage");
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.SetParent(panelRT, false);
            rootRT.anchorMin = new Vector2(0.5f, 0.5f);
            rootRT.anchorMax = new Vector2(0.5f, 0.5f);
            rootRT.pivot = new Vector2(0.5f, 0.5f);
            rootRT.anchoredPosition = Vector2.zero;
            rootRT.sizeDelta = new Vector2(0, 280);

            RawImage raw = root.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;

            // 用 LayoutElement.preferredHeight 占位，UI 脚本运行时按图片宽高比重算
            LayoutElement le = root.AddComponent<LayoutElement>();
            le.preferredWidth = -1;
            le.preferredHeight = 280;
            le.flexibleWidth = 1;
            le.flexibleHeight = 0;

            // 默认隐藏
            root.SetActive(false);
        }

        private void CreateSingleLineRoot(RectTransform panelRT)
        {
            GameObject root = new GameObject("SingleLineRoot");
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.SetParent(panelRT, false);
            rootRT.sizeDelta = new Vector2(0, 70);

            // 固定高度，避免 ContentSizeFitter 与 VLG 冲突导致输入框被压扁
            LayoutElement le = root.AddComponent<LayoutElement>();
            le.preferredWidth = -1;
            le.preferredHeight = 70;
            le.flexibleWidth = 1;
            le.flexibleHeight = 0;

            // 输入框使用 TMP 默认结构（InputField/Text Area/Placeholder + Text）
            GameObject inputObj = CreateTMPInputField("SingleLineInput", rootRT, false);
            RectTransform inputRT = inputObj.GetComponent<RectTransform>();
            inputRT.anchorMin = Vector2.zero;
            inputRT.anchorMax = Vector2.one;
            inputRT.offsetMin = Vector2.zero;
            inputRT.offsetMax = Vector2.zero;
            inputRT.sizeDelta = Vector2.zero;

            Image inputBg = inputObj.GetComponent<Image>();
            if (inputBg != null)
            {
                inputBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            }

            var input = inputObj.GetComponent<TMP_InputField>();
            if (input != null)
            {
                input.contentType = TMP_InputField.ContentType.Standard;
                input.lineType = TMP_InputField.LineType.SingleLine;
            }
        }

        private void CreateMultiLineRoot(RectTransform panelRT)
        {
            GameObject root = new GameObject("MultiLineRoot");
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.SetParent(panelRT, false);
            rootRT.sizeDelta = new Vector2(0, 160);

            // 固定高度，避免 ContentSizeFitter 与 VLG 冲突导致输入框被压扁
            LayoutElement le = root.AddComponent<LayoutElement>();
            le.preferredWidth = -1;
            le.preferredHeight = 160;
            le.flexibleWidth = 1;
            le.flexibleHeight = 0;

            GameObject inputObj = CreateTMPInputField("MultiLineInput", rootRT, true);
            RectTransform inputRT = inputObj.GetComponent<RectTransform>();
            inputRT.anchorMin = Vector2.zero;
            inputRT.anchorMax = Vector2.one;
            inputRT.offsetMin = Vector2.zero;
            inputRT.offsetMax = Vector2.zero;
            inputRT.sizeDelta = Vector2.zero;

            Image inputBg = inputObj.GetComponent<Image>();
            if (inputBg != null)
            {
                inputBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            }

            var input = inputObj.GetComponent<TMP_InputField>();
            if (input != null)
            {
                input.contentType = TMP_InputField.ContentType.Standard;
                input.lineType = TMP_InputField.LineType.MultiLineNewline;
            }
        }

        /// <summary>
        /// 创建一个 TMP_InputField 完整节点结构：
        ///   root(Image+TMP_InputField)
        ///     └─ Text Area(RectMask2D)
        ///          ├─ Placeholder(TextMeshProUGUI)
        ///          └─ Text(TextMeshProUGUI)
        /// </summary>
        private GameObject CreateTMPInputField(string name, Transform parent, bool multiLine)
        {
            GameObject root = new GameObject(name);
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.SetParent(parent, false);
            rootRT.anchorMin = new Vector2(0.5f, 0.5f);
            rootRT.anchorMax = new Vector2(0.5f, 0.5f);
            rootRT.pivot = new Vector2(0.5f, 0.5f);
            rootRT.anchoredPosition = Vector2.zero;
            rootRT.sizeDelta = new Vector2(0, 0);

            Image bg = root.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            TMP_InputField input = root.AddComponent<TMP_InputField>();

            // Text Area
            GameObject textArea = new GameObject("Text Area");
            RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
            textAreaRT.SetParent(rootRT, false);
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(10, 6);
            textAreaRT.offsetMax = new Vector2(-10, -7);
            textAreaRT.sizeDelta = Vector2.zero;
            RectMask2D rectMask = textArea.AddComponent<RectMask2D>();
            rectMask.padding = new Vector4(-8, -5, -8, -5);

            // Placeholder
            GameObject placeholder = new GameObject("Placeholder");
            RectTransform placeholderRT = placeholder.AddComponent<RectTransform>();
            placeholderRT.SetParent(textAreaRT, false);
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.offsetMin = Vector2.zero;
            placeholderRT.offsetMax = Vector2.zero;
            placeholderRT.sizeDelta = Vector2.zero;
            TextMeshProUGUI placeholderTmp = placeholder.AddComponent<TextMeshProUGUI>();
            placeholderTmp.text = multiLine ? "请输入描述..." : "请输入...";
            placeholderTmp.fontSize = 28;
            placeholderTmp.fontStyle = FontStyles.Italic;
            placeholderTmp.enableWordWrapping = true;
            placeholderTmp.alignment = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
            placeholderTmp.color = new Color(1f, 1f, 1f, 0.5f);
            placeholderTmp.raycastTarget = false;
            placeholder.AddComponent<LayoutElement>().ignoreLayout = true;

            // Text
            GameObject text = new GameObject("Text");
            RectTransform textRT = text.AddComponent<RectTransform>();
            textRT.SetParent(textAreaRT, false);
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            textRT.sizeDelta = Vector2.zero;
            TextMeshProUGUI textTmp = text.AddComponent<TextMeshProUGUI>();
            textTmp.text = "";
            textTmp.fontSize = 28;
            textTmp.enableWordWrapping = true;
            textTmp.extraPadding = true;
            textTmp.color = Color.white;
            textTmp.alignment = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
            textTmp.raycastTarget = false;

            input.textViewport = textAreaRT;
            input.textComponent = textTmp;
            input.placeholder = placeholderTmp;

            return root;
        }

        private void CreateButtonGroup(RectTransform panelRT)
        {
            GameObject btnGroup = new GameObject("ButtonGroup");
            RectTransform btnGroupRT = btnGroup.AddComponent<RectTransform>();
            btnGroupRT.SetParent(panelRT, false);
            btnGroupRT.sizeDelta = new Vector2(0, 60);

            ContentSizeFitter csf = btnGroup.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            HorizontalLayoutGroup layout = btnGroup.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            CreateButton(btnGroupRT, "CancelButton", "取消", new Color(0.3f, 0.3f, 0.3f));
            CreateButton(btnGroupRT, "ConfirmButton", "确认", new Color(0.2f, 0.5f, 0.8f));
        }

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
[CustomEditor(typeof(CommonInputDialogUICreator))]
public class CommonInputDialogUICreatorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);

        CommonInputDialogUICreator creator = (CommonInputDialogUICreator)target;

        if (GUILayout.Button("Update CommonInputDialogUI Prefab (In-Place)", GUILayout.Height(40)))
        {
            creator.CreatePrefab();
        }

        EditorGUILayout.HelpBox(
            "使用方式：\n" +
            "1. 打开 CommonInputDialogUI.prefab（双击进入预制体编辑模式）\n" +
            "2. 选中根节点 CommonInputDialogUI，Add Component -> CommonInputDialogUICreator\n" +
            "3. 点击上方按钮，将清空旧子节点并原地重建 DialogRoot 结构\n" +
            "4. 保存预制体后可移除本组件\n\n" +
            "注意：本脚本只重建子节点，不新建根节点；Canvas/CanvasScaler/GraphicRaycaster 保留在根上。",
            MessageType.Info
        );
    }
}
#endif
