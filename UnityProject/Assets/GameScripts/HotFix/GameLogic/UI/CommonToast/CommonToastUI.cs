using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 通用Toast弹窗UI - 支持四种模式：
    /// 1. Toast纯提示（自动上浮消失，不拦截背景点击）
    /// 2. 确认框（全屏遮罩拦截背景点击，单按钮）
    /// 3. 确认取消框（全屏遮罩拦截背景点击，双按钮，点击遮罩等同取消）
    /// 4. 确认取消附加框（全屏遮罩拦截背景点击，三按钮：确认 + 取消 + 附加，
    ///    点击遮罩等同取消。附加按钮由调用方通过 showExtra 控制是否显示，
    ///    onExtra 为附加按钮回调，用于需要三选一的业务场景。）
    /// </summary>
    [Window(UILayer.Tips, location: "CommonToastUI")]
    public class CommonToastUI : UIWindow
    {
        #region 脚本工具生成的代码
        private GameObject m_go_ToastRoot;
        private TextMeshProUGUI m_tmp_ToastMessage;

        private GameObject m_go_DialogRoot;
        private CanvasGroup m_cg_DialogRoot;
        private Image m_img_DialogMask;
        private TextMeshProUGUI m_tmp_DialogMessage;
        private Button m_btn_Confirm;
        private TextMeshProUGUI m_tmp_ConfirmText;
        private Button m_btn_Cancel;
        private GameObject m_go_CancelButton;
        private Button m_btn_Extra;
        private GameObject m_go_ExtraButton;
        private TextMeshProUGUI m_tmp_ExtraText;

        protected override void ScriptGenerator()
        {
            m_go_ToastRoot = FindChild("ToastRoot")?.gameObject;
            m_tmp_ToastMessage = FindChildComponent<TextMeshProUGUI>("ToastRoot/ToastMessage");

            m_go_DialogRoot = FindChild("DialogRoot")?.gameObject;
            m_cg_DialogRoot = FindChildComponent<CanvasGroup>("DialogRoot");
            m_img_DialogMask = FindChildComponent<Image>("DialogRoot/Mask");
            m_tmp_DialogMessage = FindChildComponent<TextMeshProUGUI>("DialogRoot/Panel/DialogMessage");
            m_btn_Confirm = FindChildComponent<Button>("DialogRoot/Panel/ButtonGroup/ConfirmButton");
            m_tmp_ConfirmText = FindChildComponent<TextMeshProUGUI>("DialogRoot/Panel/ButtonGroup/ConfirmButton/Text");
            m_btn_Cancel = FindChildComponent<Button>("DialogRoot/Panel/ButtonGroup/CancelButton");
            m_go_CancelButton = FindChild("DialogRoot/Panel/ButtonGroup/CancelButton")?.gameObject;
            m_btn_Extra = FindChildComponent<Button>("DialogRoot/Panel/ButtonGroup/ExtraButton");
            m_go_ExtraButton = FindChild("DialogRoot/Panel/ButtonGroup/ExtraButton")?.gameObject;
            m_tmp_ExtraText = FindChildComponent<TextMeshProUGUI>("DialogRoot/Panel/ButtonGroup/ExtraButton/Text");

            m_btn_Confirm?.onClick.AddListener(OnClickConfirm);
            m_btn_Cancel?.onClick.AddListener(OnClickCancel);
            m_btn_Extra?.onClick.AddListener(OnClickExtra);
        }
        #endregion

        private ToastData _current;
        private int _toastTimerId;
        private System.Threading.CancellationTokenSource _animationCts;

        protected override void OnCreate()
        {
            base.OnCreate();
            _animationCts = new System.Threading.CancellationTokenSource();

            // 给遮罩挂一个按钮，点击遮罩等同取消（仅在 ConfirmCancel 模式生效）
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

            HideAll();
        }

        protected override void OnRefresh()
        {
            base.OnRefresh();
            // 通过 UserData 驱动，避免外部异步回调竞态
            var data = UserData as ToastData;
            if (data == null)
            {
                return;
            }
            _current = data;

            switch (data.mode)
            {
                case ToastMode.Toast:
                    ShowToast(data);
                    break;
                case ToastMode.Confirm:
                    ShowDialog(data, false);
                    break;
                case ToastMode.ConfirmCancel:
                    ShowDialog(data, true);
                    break;
                case ToastMode.ConfirmCancelExtra:
                    ShowDialog(data, true, showExtra: true);
                    break;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CancelToastTimer();
            _animationCts?.Cancel();
            _animationCts?.Dispose();
            _animationCts = null;
            _current = null;
        }

        private void CancelToastTimer()
        {
            if (_toastTimerId > 0)
            {
                GameModule.Timer.RemoveTimer(_toastTimerId);
                _toastTimerId = 0;
            }
        }

        private void HideAll()
        {
            m_go_ToastRoot?.SetActive(false);
            m_go_DialogRoot?.SetActive(false);
        }

        #region Toast模式
        private void ShowToast(ToastData data)
        {
            HideAll();
            CancelToastTimer();

            ResetAnimationCts();

            if (m_go_ToastRoot == null || m_tmp_ToastMessage == null)
            {
                Debug.LogError("[CommonToastUI] Toast组件未正确绑定");
                return;
            }

            m_tmp_ToastMessage.text = data.message;
            m_go_ToastRoot.SetActive(true);

            var rectTransform = m_go_ToastRoot.GetComponent<RectTransform>();
            var canvasGroup = m_go_ToastRoot.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = m_go_ToastRoot.AddComponent<CanvasGroup>();
            }
            // Toast 不拦截任何点击
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            rectTransform.anchoredPosition = Vector2.zero;
            canvasGroup.alpha = 0f;

            PlayToastAnimation(canvasGroup, rectTransform, data.duration, data.moveDistance, _animationCts.Token).Forget();
        }

        private async UniTaskVoid PlayToastAnimation(CanvasGroup canvasGroup, RectTransform rectTransform, float duration, float moveDistance, System.Threading.CancellationToken ct)
        {
            try
            {
                float fadeInTime = 0.15f;
                float elapsed = 0f;
                while (elapsed < fadeInTime)
                {
                    if (ct.IsCancellationRequested || canvasGroup == null) return;
                    elapsed += Time.deltaTime;
                    canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInTime);
                    await UniTask.Yield(ct);
                }
                if (canvasGroup != null) canvasGroup.alpha = 1f;

                await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0.1f, duration)), cancellationToken: ct);
                if (ct.IsCancellationRequested || canvasGroup == null || rectTransform == null) return;

                float fadeOutTime = 0.5f;
                elapsed = 0f;
                Vector2 startPos = rectTransform.anchoredPosition;
                Vector2 endPos = startPos + new Vector2(0, moveDistance);
                while (elapsed < fadeOutTime)
                {
                    if (ct.IsCancellationRequested || canvasGroup == null || rectTransform == null) return;
                    elapsed += Time.deltaTime;
                    float t = elapsed / fadeOutTime;
                    float easeT = 1f - (1f - t) * (1f - t);
                    rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, easeT);
                    canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
                    await UniTask.Yield(ct);
                }

                if (m_go_ToastRoot != null) m_go_ToastRoot.SetActive(false);
                Close();
            }
            catch (System.OperationCanceledException)
            {
                // 正常取消
            }
        }
        #endregion

        #region Dialog模式
        private void ShowDialog(ToastData data, bool showCancel, bool showExtra = false)
        {
            HideAll();
            CancelToastTimer();
            ResetAnimationCts();

            if (m_go_DialogRoot == null || m_tmp_DialogMessage == null)
            {
                Debug.LogError("[CommonToastUI] Dialog组件未正确绑定");
                return;
            }

            m_tmp_DialogMessage.text = data.message;
            if (m_tmp_ConfirmText != null) m_tmp_ConfirmText.text = data.confirmText;
            if (m_go_CancelButton != null) m_go_CancelButton.SetActive(showCancel);
            // 附加按钮：仅 ConfirmCancelExtra 模式 + 调用方显式开启时显示
            bool extraVisible = showExtra && data.showExtra;
            if (m_go_ExtraButton != null) m_go_ExtraButton.SetActive(extraVisible);
            if (extraVisible && m_tmp_ExtraText != null && !string.IsNullOrEmpty(data.extraText))
            {
                m_tmp_ExtraText.text = data.extraText;
            }

            // 遮罩拦截开关：ConfirmCancel / ConfirmCancelExtra 模式下点击遮罩可取消；Confirm 模式遮罩不可点击
            bool maskCanCancel = data.maskClickable && showCancel;
            if (m_img_DialogMask != null)
            {
                m_img_DialogMask.raycastTarget = true;
                var maskBtn = m_img_DialogMask.GetComponent<Button>();
                if (maskBtn != null)
                {
                    maskBtn.interactable = maskCanCancel;
                }
            }

            // 确保 DialogRoot 的 CanvasGroup 拦截射线
            if (m_cg_DialogRoot != null)
            {
                m_cg_DialogRoot.interactable = true;
                m_cg_DialogRoot.blocksRaycasts = true;
            }

            m_go_DialogRoot.SetActive(true);

            // 渐显动画
            var cg = m_cg_DialogRoot;
            if (cg == null)
            {
                cg = m_go_DialogRoot.GetComponent<CanvasGroup>();
                if (cg == null) cg = m_go_DialogRoot.AddComponent<CanvasGroup>();
            }
            cg.alpha = 0f;
            PlayDialogFadeIn(cg).Forget();
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
            _current = null;
            cb?.Invoke();
            Close();
        }

        private void OnClickCancel()
        {
            var cb = _current?.onCancel;
            _current = null;
            cb?.Invoke();
            Close();
        }

        private void OnClickExtra()
        {
            var cb = _current?.onExtra;
            _current = null;
            cb?.Invoke();
            Close();
        }
        #endregion

        private void ResetAnimationCts()
        {
            _animationCts?.Cancel();
            _animationCts?.Dispose();
            _animationCts = new System.Threading.CancellationTokenSource();
        }
    }

    /// <summary>
    /// Toast显示参数，通过 UIModule ShowUI 的 UserData 传入
    /// </summary>
    public class ToastData
    {
        public ToastMode mode;
        public string message;
        public float duration = 2f;
        public float moveDistance = 100f;
        public string confirmText = "确认";
        public string cancelText = "取消";
        public bool maskClickable = true; // 点击遮罩是否触发取消（仅 ConfirmCancel/ConfirmCancelExtra 生效）
        public Action onConfirm;
        public Action onCancel;

        // ===== 三按钮扩展（ConfirmCancelExtra 模式）=====
        // 附加按钮由调用方决定是否显示：showExtra=false 时即使 ConfirmCancelExtra 模式也只显示双按钮
        public bool showExtra; // 是否显示第三（附加）按钮
        public string extraText = "附加"; // 附加按钮文本
        public Action onExtra; // 附加按钮回调
    }

    public enum ToastMode
    {
        Toast,
        Confirm,
        ConfirmCancel,
        ConfirmCancelExtra,
    }
}
