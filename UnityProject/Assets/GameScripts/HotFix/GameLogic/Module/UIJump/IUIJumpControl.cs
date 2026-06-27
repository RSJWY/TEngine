using System.Collections;
using System.Collections.Generic;
using TEngine;

using UnityEngine;

namespace GameLogic
{
    public enum UIWindowType
    {
        MenuMain,//主菜单
        PlayBack,//回放
    }


    /// <summary>
    /// UI 业务跳转控制接口。
    /// </summary>
    /// <remarks>
    /// 这一层只负责“界面之间怎么走”：把业务侧的 <see cref="UIWindowType"/> 路由到具体 UIWindow，
    /// 并维护简单的返回栈；窗口加载、层级排序和生命周期仍由 TEngine 的 <c>GameModule.UI</c> 负责。
    /// </remarks>
    public interface IUIJumpControl
    {
        /// <summary>
        /// 跳转到指定业务窗口。
        /// </summary>
        /// <param name="windowType">业务窗口类型。</param>
        /// <param name="userDatas">透传给目标 UIWindow 的用户数据，对应 TEngine ShowUI 的 userDatas。</param>
        /// <returns>路由存在并发起显示返回 true；未注册路由返回 false。</returns>
        bool JumpTo(UIWindowType windowType, params System.Object[] userDatas);

        /// <summary>
        /// 返回导航栈中的上一级窗口。
        /// </summary>
        /// <returns>成功返回上一级返回 true；导航栈为空或已到栈底返回 false。</returns>
        bool JumpBack();

        /// <summary>
        /// 清空导航历史并跳转回主菜单。
        /// </summary>
        void JumpToMain();

        /// <summary>
        /// 清空当前 UI 导航历史。
        /// </summary>
        void ClearHistory();

        /// <summary>
        /// 注册业务窗口类型到具体 UIWindow 类型的路由。
        /// </summary>
        /// <param name="windowType">业务窗口类型。</param>
        /// <param name="uiType">具体 UIWindow 类型，必须能被 GameModule.UI.ShowUI(Type) 打开。</param>
        void RegisterRoute(UIWindowType windowType, System.Type uiType);

        /// <summary>
        /// 退出菜单：关闭当前导航栈中的所有窗口，清空历史，恢复游戏。
        /// </summary>
        void ExitMenu();

        /// <summary>
        /// 当前导航栈快照（栈顶在索引 0）。用于调试可视化。
        /// </summary>
        IReadOnlyList<System.Type> NavigationStack { get; }

        /// <summary>
        /// 当前路由表快照。用于调试可视化。
        /// </summary>
        IReadOnlyDictionary<UIWindowType, System.Type> RouteTable { get; }
    }
}
