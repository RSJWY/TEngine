// ExitErrorUI.cs
// 作用：授权错误弹窗（零框架依赖，仿 Assets/Launcher 的纯 C# UI 写法，不挂脚本到 prefab）。
// prefab：Resources/UIWindow/ExitErrorUI.prefab（根节点自带 Canvas，可独立显示）。
// 层级：TipBg/m_tmp_TipTitle（标题 TMP）、TipBg/m_tmp_Content（内容 TMP）、TipBg/m_btn_Exit（退出按钮）。

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

        void OnClick_ExitBtn()
        {
            Action cb = _data?.onConfirm ?? Application.Quit;
            Close();
            cb?.Invoke();
        }
    }
}
