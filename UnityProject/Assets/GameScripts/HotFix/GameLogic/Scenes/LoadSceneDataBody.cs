using System;

namespace GameLogic
{
    /// <summary>
    /// 场景加载流程的数据载体。
    /// </summary>
    /// <remarks>
    /// 承载一次场景跳转所需的"目标场景名"与"激活后完成回调"。
    /// <para>
    /// 数据流向：<see cref="GameSceneModule"/> 的跳转入口（LoadScene/JumpToMainScene）
    /// 构造本对象 → 通过 <c>GameModule.UI.ShowUI&lt;SwitchUI&gt;(loadData)</c> 作为
    /// <c>userDatas</c> 透传 → <see cref="SwitchUI"/> 在 OnCreate 中以 <c>UserData</c> 取出。
    /// </para>
    /// <para>
    /// 激活完成后由 <see cref="SwitchUI"/> 调用 <see cref="finishCallBack"/>，
    /// 常见用途：关闭多余 UI、启动回放播放、跳转主菜单等。
    /// </para>
    /// </remarks>
    public class LoadSceneDataBody
    {
        /// <summary>
        /// 待加载的场景资源地址（YooAsset location，对应 SceneType 映射出的场景名）。
        /// </summary>
        /// <remarks>由 <see cref="GameSceneModule.GetSceneName"/> 转换得到。</remarks>
        public string sceneName;

        /// <summary>
        /// 场景激活并显示后的完成回调。
        /// </summary>
        /// <remarks>可为空；在 <see cref="SwitchUI"/> 激活场景后于收尾定时器中调用。</remarks>
        public Action finishCallBack;
    }
}
