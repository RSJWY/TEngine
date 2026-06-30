using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 场景切换加载页窗口（纯展示，节点由 <c>SwitchUI_Gen.g.cs</c> 代码生成绑定）。
    /// </summary>
    /// <remarks>
    /// <b>职责边界</b>：本窗口仅负责把 <see cref="IGameSceneModule.DisplayProgress"/> 渲染为进度条 fillAmount 与百分比文本。
    /// 三段式进度状态机、场景资源加载（suspendLoad）、场景激活（UnSuspend）、完成回调与关闭时机全部由
    /// <see cref="GameSceneModule"/> 独占管理；本窗口不持有任何加载状态，不主动关闭自身（由模块在收尾完成后调用 <c>CloseUI&lt;SwitchUI&gt;</c>）。
    /// <para>
    /// 经 <c>GameModule.UI.ShowUI&lt;SwitchUI&gt;()</c> 直接打开（场景切换不再走 UIJump 路由，过渡页不参与业务返回导航）。
    /// </para>
    /// </remarks>
    [Window(UILayer.Top, location: "SwitchUI")]
    public partial class SwitchUI
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
