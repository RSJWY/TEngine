using UnityEngine;
using UnityEngine.UI;
using System;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Launcher
{
    public class LoadTipsUI : UIBase
    {
        #region 脚本工具生成的代码

        private Text m_textDesc;
        private Button m_btnConfirm;
        private Text m_textConfirm;
        private Button m_btnUpdate;
        private Text m_textUpdate;
        private Button m_btnCancel;
        private Text m_textCancel;

        protected override void ScriptGenerator()
        {
            m_textDesc = FindChildComponent<Text>("BgImage/m_textDesc");
            m_btnConfirm = FindChildComponent<Button>("BgImage/ButtonGroup/m_btnConfirm");
            m_textConfirm = FindChildComponent<Text>("BgImage/ButtonGroup/m_btnConfirm/m_textConfirm");
            m_btnUpdate = FindChildComponent<Button>("BgImage/ButtonGroup/m_btnUpdate");
            m_textUpdate = FindChildComponent<Text>("BgImage/ButtonGroup/m_btnUpdate/m_textUpdate");
            m_btnCancel = FindChildComponent<Button>("BgImage/ButtonGroup/m_btnCancel");
            m_textCancel = FindChildComponent<Text>("BgImage/ButtonGroup/m_btnCancel/m_textCancel");
            m_btnConfirm.onClick.AddListener(OnClickConfirmBtn);
            m_btnUpdate.onClick.AddListener(OnClickUpdateBtn);
            m_btnCancel.onClick.AddListener(OnClickCancelBtn);
        }

        #endregion

        private const string m_cancelText = "Cancel";
        private const string m_confirmText = "Confirm";
        private const string m_updateText = "Update";

        public Action OnConfirmClick { get; set; }
        public Action OnUpdateClick { get; set; }
        public Action OnCancelClick { get; set; }

        private CancellationTokenSource _autoConfirmCts;
        private bool _handled;
        private string _baseDesc;
        private float _autoConfirmDelay;
        private float _remainingSeconds;
        private bool _autoConfirmUsesCancel;

        public override void OnInit(object data)
        {
            base.OnInit(data);
            _handled = false;
            CancelAutoConfirm();
            _baseDesc = data?.ToString() ?? string.Empty;
            _autoConfirmDelay = 0f;
            _remainingSeconds = 0f;

            m_textCancel.text = m_cancelText;
            m_textUpdate.text = m_updateText;
            m_textConfirm.text = m_confirmText;

            m_btnUpdate.gameObject.SetActive(false);
            m_btnCancel.gameObject.SetActive(false);
            m_btnConfirm.gameObject.SetActive(false);

            RefreshDesc();
        }

        public void SetAllCallback(Action onConfirm, Action onUpdate, Action onCancel, float autoConfirmDelay = 0f,
            bool autoConfirmUsesCancel = false)
        {
            CancelAutoConfirm();
            _handled = false;
            _autoConfirmDelay = Mathf.Max(0f, autoConfirmDelay);
            _remainingSeconds = _autoConfirmDelay;
            _autoConfirmUsesCancel = autoConfirmUsesCancel;

            m_btnUpdate.gameObject.SetActive(false);
            m_btnCancel.gameObject.SetActive(false);
            m_btnConfirm.gameObject.SetActive(false);
            OnConfirmClick = null;
            OnUpdateClick = null;
            OnCancelClick = null;

            if (onConfirm != null)
            {
                OnConfirmClick = onConfirm;
                m_btnConfirm.gameObject.SetActive(true);
            }
            if (onUpdate != null)
            {
                OnUpdateClick = onUpdate;
                m_btnUpdate.gameObject.SetActive(true);
            }
            if (onCancel != null)
            {
                OnCancelClick = onCancel;
                m_btnCancel.gameObject.SetActive(true);
            }

            RefreshDesc();

            if (_autoConfirmDelay > 0f && (OnConfirmClick != null || (_autoConfirmUsesCancel && OnCancelClick != null)))
            {
                StartAutoConfirm().Forget();
            }
        }

        protected override void OnClose()
        {
            CancelAutoConfirm();
        }

        private void OnClickUpdateBtn()
        {
            HandleAction(OnUpdateClick);
        }

        private void OnClickCancelBtn()
        {
            HandleAction(OnCancelClick);
        }

        private void OnClickConfirmBtn()
        {
            HandleAction(OnConfirmClick);
        }

        private void HandleAction(Action callback)
        {
            if (_handled)
            {
                return;
            }

            _handled = true;
            CancelAutoConfirm();
            callback?.Invoke();
            Close();
        }

        private async UniTaskVoid StartAutoConfirm()
        {
            _autoConfirmCts = new CancellationTokenSource();
            var token = _autoConfirmCts.Token;

            try
            {
                while (_remainingSeconds > 0f)
                {
                    RefreshDesc();
                    await UniTask.Delay(TimeSpan.FromSeconds(1f), cancellationToken: token);
                    _remainingSeconds -= 1f;
                }

                RefreshDesc();
                HandleAction(_autoConfirmUsesCancel ? OnCancelClick : OnConfirmClick);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void RefreshDesc()
        {
            if (_autoConfirmDelay > 0f && !_handled && (OnConfirmClick != null || (_autoConfirmUsesCancel && OnCancelClick != null)))
            {
                var remainSeconds = Mathf.CeilToInt(Mathf.Max(0f, _remainingSeconds));
                string actionText = _autoConfirmUsesCancel ? "自动继续进入游戏" : "自动继续更新";
                m_textDesc.text = $"{_baseDesc}\n\n{remainSeconds} 秒后{actionText}";
                return;
            }

            m_textDesc.text = _baseDesc;
        }

        private void CancelAutoConfirm()
        {
            if (_autoConfirmCts == null)
            {
                return;
            }

            _autoConfirmCts.Cancel();
            _autoConfirmCts.Dispose();
            _autoConfirmCts = null;
        }
    }
}