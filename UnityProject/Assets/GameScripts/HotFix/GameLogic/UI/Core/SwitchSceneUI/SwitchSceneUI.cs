using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TEngine;

namespace GameLogic
{
	[Window(UILayer.UI, location : "SwitchSceneUI")]
	public partial class SwitchSceneUI
	{
		private string _cachedTip;

		protected override void OnCreate()
		{
			base.OnCreate();
			_cachedTip = GameModule.GameScene.GetRandomTip();
		}

		/// <summary>每帧：从场景模块读取已平滑的展示进度并刷新进度条与文案。</summary>
		protected override void OnUpdate()
		{
			base.OnUpdate();

			float progress = GameModule.GameScene.DisplayProgress;
			m_img_progress.fillAmount = progress;
			m_tmp_progressValue.text = $"{Mathf.RoundToInt(progress * 100)}%";

			string phaseText = GameModule.GameScene.DisplayPhaseText;
			m_tmp_progressText.text = string.IsNullOrEmpty(_cachedTip)
				? phaseText
				: $"{phaseText}\n<size=60%>{_cachedTip}</size>";
		}
		#region 事件

		#endregion
	}
}
