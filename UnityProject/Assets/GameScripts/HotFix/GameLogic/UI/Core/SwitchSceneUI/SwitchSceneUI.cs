using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TEngine;

namespace GameLogic
{
	[Window(UILayer.UI, location : "SwitchSceneUI")]
	public partial class SwitchSceneUI
	{
		/// <summary>每帧：从场景模块读取已平滑的展示进度并刷新进度条与百分比文本。</summary>
		protected override void OnUpdate()
		{
			base.OnUpdate();

			float progress = GameModule.GameScene.DisplayProgress;
			m_img_progress.fillAmount = progress;
			m_tmp_progressText.text = $"{Mathf.RoundToInt(progress * 100)}%";
		}
		#region 事件

		#endregion
	}
}
