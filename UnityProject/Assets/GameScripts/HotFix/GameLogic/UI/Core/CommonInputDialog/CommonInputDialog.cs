using System;
using Cysharp.Threading.Tasks;
using TEngine;
using TMPro;
using UnityEngine;

using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 通用输入弹窗UI - 支持三种模式：
    /// 1. SingleLine（单行输入：title、名字等）
    /// 2. MultiLine（多行输入：描述信息等）
    /// 3. TitleAndDescription（单行 + 多行，同时输入标题与描述）
    ///
    /// 结构参考 CommonToastUI：全屏遮罩拦截背景点击，点击遮罩等同取消；
    /// 通过 UserData 传入 InputDialogData 驱动显示，确认时通过 Action&lt;InputDialogResult&gt; 回调。
    /// </summary>
    [Window(UILayer.Tips, location: "CommonInputDialogUI")]
    public class CommonInputDialogUI : UIWindow
    {
        #region 脚本工具生成的代码
        private GameObject m_go_DialogRoot;
        private CanvasGroup m_cg_DialogRoot;
        private Image m_img_DialogMask;
        private RectTransform m_rt_Panel;
        private TextMeshProUGUI m_tmp_Title;
        private TextMeshProUGUI m_tmp_Message;

        private GameObject m_go_SingleLineRoot;
        private TMP_InputField m_input_SingleLine;
        private TextMeshProUGUI m_tmp_SingleLinePlaceholder;

        private GameObject m_go_MultiLineRoot;
        private TMP_InputField m_input_MultiLine;
        private TextMeshProUGUI m_tmp_MultiLinePlaceholder;

        private RawImage m_rimg_Preview;
        private GameObject m_go_PreviewRoot;

        private Button m_btn_Confirm;
        private TextMeshProUGUI m_tmp_ConfirmText;
        private Button m_btn_Cancel;
        private GameObject m_go_CancelButton;
        private TextMeshProUGUI m_tmp_CancelText;
        #endregion

        protected override void ScriptGenerator()
        {
            m_cg_DialogRoot = FindChildComponent<CanvasGroup>("DialogRoot");
            m_go_DialogRoot = FindChild("DialogRoot")?.gameObject;
            m_img_DialogMask = FindChildComponent<Image>("DialogRoot/Mask");
            m_rt_Panel = FindChildComponent<RectTransform>("DialogRoot/Panel");
            m_tmp_Title = FindChildComponent<TextMeshProUGUI>("DialogRoot/Panel/Title");
            m_tmp_Message = FindChildComponent<TextMeshProUGUI>("DialogRoot/Panel/Message");

            m_go_SingleLineRoot = FindChild("DialogRoot/Panel/SingleLineRoot")?.gameObject;
            m_input_SingleLine = FindChildComponent<TMP_InputField>("DialogRoot/Panel/SingleLineRoot/SingleLineInput");
            m_tmp_SingleLinePlaceholder = FindChildComponent<TextMeshProUGUI>("DialogRoot/Panel/SingleLineRoot/SingleLineInput/Text Area/Placeholder");

            m_go_MultiLineRoot = FindChild("DialogRoot/Panel/MultiLineRoot")?.gameObject;
            m_input_MultiLine = FindChildComponent<TMP_InputField>("DialogRoot/Panel/MultiLineRoot/MultiLineInput");
            m_tmp_MultiLinePlaceholder = FindChildComponent<TextMeshProUGUI>("DialogRoot/Panel/MultiLineRoot/MultiLineInput/Text Area/Placeholder");

            m_go_PreviewRoot = FindChild("DialogRoot/Panel/PreviewImage")?.gameObject;
            m_rimg_Preview = FindChildComponent<RawImage>("DialogRoot/Panel/PreviewImage");

            m_btn_Confirm = FindChildComponent<Button>("DialogRoot/Panel/ButtonGroup/ConfirmButton");
            m_tmp_ConfirmText = FindChildComponent<TextMeshProUGUI>("DialogRoot/Panel/ButtonGroup/ConfirmButton/Text");
            m_btn_Cancel = FindChildComponent<Button>("DialogRoot/Panel/ButtonGroup/CancelButton");
            m_go_CancelButton = FindChild("DialogRoot/Panel/ButtonGroup/CancelButton")?.gameObject;
            m_tmp_CancelText = FindChildComponent<TextMeshProUGUI>("DialogRoot/Panel/ButtonGroup/CancelButton/Text");

            m_btn_Confirm?.onClick.AddListener(OnClickConfirm);
            m_btn_Cancel?.onClick.AddListener(OnClickCancel);
        }

        private InputDialogData _current;
        private System.Threading.CancellationTokenSource _animationCts;

        protected override void OnCreate()
        {
            base.OnCreate();
            _animationCts = new System.Threading.CancellationTokenSource();

            // 给遮罩挂一个按钮，点击遮罩等同取消
            if (m_img_DialogMask != null)
            {
                var maskBtn = m_img_DialogMask.gameObject.GetComponent<Button>();
                if (maskBtn == null)
                {
                    maskBtn = m_img_DialogMask.gameObject.AddComponent<Button>();
                }
                maskBtn.transition = Selectable.Transition.None;
                maskBtn.onClick.AddListener(OnClickCancel);
            }
        }

        protected override void OnRefresh()
        {
            base.OnRefresh();
            var data = UserData as InputDialogData;
            if (data == null)
            {
                return;
            }
            _current = data;

            // 标题
            if (m_tmp_Title != null)
            {
                m_tmp_Title.text = data.title;
                m_tmp_Title.gameObject.SetActive(!string.IsNullOrEmpty(data.title));
            }

            // 副标题/提示
            if (m_tmp_Message != null)
            {
                m_tmp_Message.text = data.message;
                m_tmp_Message.gameObject.SetActive(!string.IsNullOrEmpty(data.message));
            }

            // 按钮文本
            if (m_tmp_ConfirmText != null) m_tmp_ConfirmText.text = data.confirmText;
            if (m_tmp_CancelText != null) m_tmp_CancelText.text = data.cancelText;
            if (m_go_CancelButton != null) m_go_CancelButton.SetActive(data.showCancel);

            // 单行输入
            if (m_go_SingleLineRoot != null)
            {
                bool showSingle = data.mode == InputDialogMode.SingleLine || data.mode == InputDialogMode.TitleAndDescription;
                m_go_SingleLineRoot.SetActive(showSingle);
                if (showSingle && m_input_SingleLine != null)
                {
                    m_input_SingleLine.text = data.singleLineDefault ?? string.Empty;
                    m_input_SingleLine.characterLimit = Mathf.Max(0, data.singleLineLimit);
                    if (m_tmp_SingleLinePlaceholder != null)
                    {
                        m_tmp_SingleLinePlaceholder.text = data.singleLinePlaceholder ?? string.Empty;
                    }
                }
            }

            // 多行输入
            if (m_go_MultiLineRoot != null)
            {
                bool showMulti = data.mode == InputDialogMode.MultiLine || data.mode == InputDialogMode.TitleAndDescription;
                m_go_MultiLineRoot.SetActive(showMulti);
                if (showMulti && m_input_MultiLine != null)
                {
                    m_input_MultiLine.text = data.multiLineDefault ?? string.Empty;
                    m_input_MultiLine.characterLimit = Mathf.Max(0, data.multiLineLimit);
                    if (m_tmp_MultiLinePlaceholder != null)
                    {
                        m_tmp_MultiLinePlaceholder.text = data.multiLinePlaceholder ?? string.Empty;
                    }
                }
            }

            // 遮罩拦截
            if (m_img_DialogMask != null)
            {
                m_img_DialogMask.raycastTarget = true;
                var maskBtn = m_img_DialogMask.GetComponent<Button>();
                if (maskBtn != null)
                {
                    maskBtn.interactable = data.maskClickable && data.showCancel;
                }
            }

            // 预览图（可选）：传入 Texture2D 时显示，否则隐藏
            if (m_go_PreviewRoot != null)
            {
                bool showPreview = data.previewImage != null;
                m_go_PreviewRoot.SetActive(showPreview);
                if (showPreview && m_rimg_Preview != null)
                {
                    m_rimg_Preview.texture = data.previewImage;
                    // 按图片真实宽高比 + Panel 可用宽度，动态计算预览图高度
                    // Panel 的 VerticalLayoutGroup childControlHeight=true，通过 LayoutElement.preferredHeight
                    // 驱动预览图高度，ContentSizeFitter 会自动让 Panel 背景随之适配。
                    var le = m_rimg_Preview.GetComponent<LayoutElement>();
                    if (le != null && data.previewImage != null)
                    {
                        float w = data.previewImage.width;
                        float h = data.previewImage.height;
                        float aspect = (h > 0) ? (w / h) : (16f / 9f);
                        // Panel 内容可用宽度 = Panel 宽 - 左右 padding
                        float panelWidth = 620f;
                        var panelRT = m_rimg_Preview.transform.parent as RectTransform;
                        if (panelRT != null)
                        {
                            float pw = panelRT.rect.width;
                            var vlg = panelRT.GetComponent<VerticalLayoutGroup>();
                            if (vlg != null) pw -= vlg.padding.left + vlg.padding.right;
                            if (pw > 0) panelWidth = pw;
                        }
                        float previewHeight = panelWidth / aspect;
                        // 通过 LayoutElement 控制高度，Panel 的 VLG childControlHeight=true 会自动应用
                        le.preferredHeight = previewHeight;
                    }
                }
            }

            if (m_cg_DialogRoot != null)
            {
                m_cg_DialogRoot.interactable = true;
                m_cg_DialogRoot.blocksRaycasts = true;
            }

            // 激活 DialogRoot（prefab 默认 inactive，由 UI 脚本按需激活）
            if (m_go_DialogRoot != null) m_go_DialogRoot.SetActive(true);

            // 强制重建 Panel 布局，使 ContentSizeFitter 根据当前显隐内容立刻得到正确高度，避免留白
            if (m_rt_Panel != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(m_rt_Panel);
            }

            // 渐显
            var cg = m_cg_DialogRoot;
            if (cg != null)
            {
                cg.alpha = 0f;
                PlayDialogFadeIn(cg).Forget();
            }

            // 自动聚焦输入框（若可见），延迟一帧等布局完成，便于直接输入
            DelayFocusInput().Forget();
        }

        private async UniTaskVoid DelayFocusInput()
        {
            try
            {
                await UniTask.Yield(_animationCts?.Token ?? System.Threading.CancellationToken.None);

                if (m_go_SingleLineRoot != null && m_go_SingleLineRoot.activeSelf && m_input_SingleLine != null)
                {
                    m_input_SingleLine.ActivateInputField();
                }
                else if (m_go_MultiLineRoot != null && m_go_MultiLineRoot.activeSelf && m_input_MultiLine != null)
                {
                    m_input_MultiLine.ActivateInputField();
                }
            }
            catch (System.OperationCanceledException)
            {
                // 正常取消
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _animationCts?.Cancel();
            _animationCts?.Dispose();
            _animationCts = null;
            _current = null;
            // 预览图 Texture 由调用方管理生命周期，这里只清引用
            if (m_rimg_Preview != null) m_rimg_Preview.texture = null;
        }

        private async UniTaskVoid PlayDialogFadeIn(CanvasGroup canvasGroup)
        {
            try
            {
                float fadeTime = 0.15f;
                float elapsed = 0f;
                while (elapsed < fadeTime)
                {
                    if (_animationCts == null || _animationCts.IsCancellationRequested || canvasGroup == null)
                        return;
                    elapsed += Time.deltaTime;
                    canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeTime);
                    await UniTask.Yield(_animationCts.Token);
                }
                if (canvasGroup != null) canvasGroup.alpha = 1f;
            }
            catch (System.OperationCanceledException)
            {
                // 正常取消
            }
        }

        private void OnClickConfirm()
        {
            var cb = _current?.onConfirm;
            var data = _current;
            _current = null;

            var result = new InputDialogResult
            {
                mode = data != null ? data.mode : InputDialogMode.SingleLine,
                singleLineText = m_input_SingleLine != null ? m_input_SingleLine.text : string.Empty,
                multiLineText = m_input_MultiLine != null ? m_input_MultiLine.text : string.Empty,
            };
            cb?.Invoke(result);
            Close();
        }

        private void OnClickCancel()
        {
            var cb = _current?.onCancel;
            _current = null;
            cb?.Invoke();
            Close();
        }
    }

    /// <summary>
    /// 输入弹窗显示参数，通过 UIModule ShowUI 的 UserData 传入。
    /// </summary>
    public class InputDialogData
    {
        public InputDialogMode mode = InputDialogMode.SingleLine;

        public string title = string.Empty;
        public string message = string.Empty;

        public string singleLinePlaceholder = "请输入...";
        public string singleLineDefault = string.Empty;
        public int singleLineLimit = 0; // 0 表示不限制（TMP 默认上限 32767）

        public string multiLinePlaceholder = "请输入描述...";
        public string multiLineDefault = string.Empty;
        public int multiLineLimit = 0;

        public string confirmText = "确认";
        public string cancelText = "取消";
        public bool showCancel = true;
        public bool maskClickable = true; // 点击遮罩是否触发取消（仅 showCancel=true 生效）

        /// <summary>预览图（可选）：传入则显示 RawImage 供用户确认，如截图预览。Texture 生命周期由调用方管理。</summary>
        public Texture2D previewImage;

        public Action<InputDialogResult> onConfirm;
        public Action onCancel;
    }

    /// <summary>
    /// 输入弹窗结果，回传给 onConfirm 回调。
    /// </summary>
    public class InputDialogResult
    {
        public InputDialogMode mode;
        public string singleLineText;
        public string multiLineText;
    }

    public enum InputDialogMode
    {
        SingleLine,
        MultiLine,
        TitleAndDescription,
        /// <summary>纯确认模式：不显示输入框，仅标题+预览图+按钮（如截图确认）。</summary>
        None,
    }
}
