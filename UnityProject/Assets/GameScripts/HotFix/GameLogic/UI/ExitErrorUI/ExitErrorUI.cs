using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TEngine;

namespace GameLogic
{
	[Window(UILayer.UI, location : "ExitErrorUI")]
	public partial class ExitErrorUI
	{
		#region 事件

		private partial void OnClick_ExitBtn()
		{
			Application.Quit();
		}

		#endregion
	}
}
