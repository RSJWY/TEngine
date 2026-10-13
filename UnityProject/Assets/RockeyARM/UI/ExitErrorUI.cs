// ExitErrorUI.cs
// 作用：授权错误弹窗（零框架依赖，仿 Assets/Launcher 的纯 C# UI 写法，不挂脚本到 prefab）。
// prefab：Resources/UIWindow/ExitErrorUI.prefab（根节点自带 Canvas，可独立显示）。
// 层级：TipBg/m_tmp_TipTitle（标题 TMP）、TipBg/m_tmp_Content（内容 TMP）、TipBg/m_btn_Exit（退出按钮）。
// 退出语义：点“退出应用”→ 冻结 Time.timeScale →（可选 onConfirm 钩子）→ 编辑器停播放 / 打包 Application.Quit；
//          弹窗自身永不 Close，置顶覆盖整个退出过程。退出动作内置，宿主无需再传 Application.Quit。

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RockeyArm
{
    /// <summary>授权错误弹窗数据。</summary>
    public class ExitPanelData
    {
        public string title;
        public string contenet;
        public string buttonText;
        public Action onConfirm;
    }

    /// <summary>授权错误弹窗：标题 + 内容 + 退出按钮，确认回调默认退出应用。</summary>
    public class ExitErrorUI : RockeyUIBase
    {
        TextMeshProUGUI m_tmp_TipTitle;
        TextMeshProUGUI m_tmp_Content;
        Button m_btn_Exit;

        ExitPanelData _data;

        protected override void ScriptGenerator()
        {
            m_tmp_TipTitle = FindChildComponent<TextMeshProUGUI>("TipBg/m_tmp_TipTitle");
            m_tmp_Content = FindChildComponent<TextMeshProUGUI>("TipBg/m_tmp_Content");
            m_btn_Exit = FindChildComponent<Button>("TipBg/m_btn_Exit");

            if (m_btn_Exit != null)
            {
                m_btn_Exit.onClick.AddListener(OnClick_ExitBtn);
            }
        }

        public override void OnInit(object param)
        {
            base.OnInit(param);
            _data = param as ExitPanelData;

            if (m_tmp_TipTitle != null)
            {
                m_tmp_TipTitle.text = string.IsNullOrEmpty(_data?.title) ? "授权验证失败" : _data.title;
            }

            if (m_tmp_Content != null)
            {
                m_tmp_Content.text = _data?.contenet ?? string.Empty;
            }
        }

        protected override void OnClose()
        {
            if (m_btn_Exit != null)
            {
                m_btn_Exit.onClick.RemoveListener(OnClick_ExitBtn);
            }
        }

        bool _quitRequested;

        void OnClick_ExitBtn()
        {
            if (_quitRequested)
            {
                return;
            }

            _quitRequested = true;

            // 时间完全冻结：退出流程期间画面与逻辑静止，不受看门狗/计时器干扰。
            // 不影响退出本身：Application.Quit 与编辑器停止播放均不依赖 timeScale。
            Time.timeScale = 0f;

            // onConfirm 仅为退出前附加钩子（日志/清理等）；真正的退出动作内置在下面，不再由宿主传 Application.Quit。
            _data?.onConfirm?.Invoke();

            // UI 永不退出：不 Close，弹窗保持置顶覆盖整个退出过程。
#if UNITY_EDITOR
            // 编辑器下 Application.Quit 无效果，直接停止播放模式。
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
