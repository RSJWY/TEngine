using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TEngine;
using UnityEngine;

namespace GameLogic
{

    /// <summary>
    /// UI 业务跳转控制模块。
    /// 功能描述：在 TEngine UIModule 之上增加一层业务路由与导航历史管理，
    /// 将 UIWindowType 映射到具体 UIWindow 类型，并统一处理 JumpTo / JumpBack / JumpToMain。
    /// 创建时间：2026-01-27 22:21
    /// 开发者：z1208453245
    /// 最后修改：2026-06-24
    /// 修改内容：
    ///   1. 使用字典路由表替代 JumpTo if-else 分支，并增加返回栈安全检查。
    ///   2. OnInit 注册 Loading → LoadingUI 路由，打通场景加载页跳转链路。
    ///   3. 移除 Loading 路由：场景切换改由 GameModule.UI.ShowUI<LoadingUI> 直接打开加载页，
    ///      不再经 UIJump，避免过渡页被压入导航栈导致返回栈残留。
    /// </summary>
    public sealed partial class UIJumpControl : Module, IUIJumpControl
    {
        /// <summary>
        /// UI 导航历史栈。
        /// </summary>
        /// <remarks>
        /// 栈顶表示当前业务窗口；JumpBack 时先弹出当前窗口，再显示新的栈顶窗口。
        /// 这里仅记录业务跳转历史，不参与 TEngine UIModule 的窗口层级排序。
        /// </remarks>
        private readonly Stack<Type> _navigationHistory = new Stack<Type>();

        /// <summary>
        /// 窗口路由表：业务窗口类型 → 具体 UIWindow 类型。
        /// </summary>
        /// <remarks>
        /// 新增业务窗口时优先在 OnInit 中调用 RegisterRoute 注册，
        /// JumpTo 只负责查表与执行，避免把路由分支散落到方法体中。
        /// </remarks>
        private readonly Dictionary<UIWindowType, Type> _routeTable = new Dictionary<UIWindowType, Type>();

        /// <summary>
        /// 当前导航栈快照（栈顶在索引 0）。仅用于调试可视化，每次访问生成新列表。
        /// </summary>
        public IReadOnlyList<Type> NavigationStack => _navigationHistory.ToArray();

        /// <summary>
        /// 当前路由表快照。仅用于调试可视化，每次访问生成新字典。
        /// </summary>
        public IReadOnlyDictionary<UIWindowType, Type> RouteTable =>
            new Dictionary<UIWindowType, Type>(_routeTable);

        /// <summary>
        /// 初始化 UI 路由表。
        /// </summary>
        /// <remarks>
        /// UIJumpControl 由 TEngine ModuleSystem 按接口命名约定自动创建，OnInit 会在模块创建时调用。
        /// 这里只注册业务路由，不直接打开窗口。
        /// </remarks>
        public override void OnInit()
        {
            // 新增窗口在此注册一行即可，无需改动 JumpTo 方法体。
            // 注意：加载页 LoadingUI 不在此注册——场景切换已改由 GameModule.UI.ShowUI<LoadingUI> 直接打开，
            // 不再经 UIJump 路由，避免过渡页被压入导航栈造成返回栈残留。
            //RegisterRoute(UIWindowType.MenuMain, typeof(MainMenuUI));
        }

        /// <summary>
        /// 注册业务窗口类型到具体 UIWindow 类型的映射。
        /// </summary>
        /// <param name="windowType">业务窗口类型。</param>
        /// <param name="uiType">具体窗口类型，需继承 UIWindow 并配置 WindowAttribute。</param>
        public void RegisterRoute(UIWindowType windowType, Type uiType)
        {
            _routeTable[windowType] = uiType;
        }

        /// <summary>
        /// 返回上一级业务窗口。
        /// </summary>
        /// <returns>成功显示上一级窗口返回 true；无可返回窗口时返回 false。</returns>
        public bool JumpBack()
        {
            if (_navigationHistory.Count == 0)
            {
                Log.Warning("[UIJump] 导航栈为空，无法返回。");
                return false;
            }

            _navigationHistory.Pop(); // 弹出当前窗口

            if (_navigationHistory.Count == 0)
            {
                Log.Warning("[UIJump] 已到栈底，无上一级界面。");
                return false;
            }

            Type ui = _navigationHistory.Peek();
            GameModule.UI.ShowUI(ui);
            return true;
        }

        /// <summary>
        /// 跳转到指定业务窗口。
        /// </summary>
        /// <param name="windowType">业务窗口类型。</param>
        /// <param name="userDatas">透传给目标 UIWindow 的用户数据。</param>
        /// <returns>路由存在并发起窗口显示返回 true；未注册路由返回 false。</returns>
        public bool JumpTo(UIWindowType windowType, params System.Object[] userDatas)
        {
            if (!_routeTable.TryGetValue(windowType, out Type uiType))
            {
                Log.Warning($"[UIJump] 未注册的窗口类型: {windowType}，请在 UIJumpControl.OnInit 中 RegisterRoute。");
                return false;
            }

            // 防重入：栈顶已是目标窗口时不重复跳转
            if (_navigationHistory.Count > 0 && _navigationHistory.Peek() == uiType)
            {
                Log.Warning($"[UIJump] {windowType} 已在栈顶，跳过重复跳转。");
                return false;
            }

            // 使用 UIModule 的非泛型 ShowUI(Type) 重载，避免 JumpTo 内部继续维护 if-else 泛型分支。
            GameModule.UI.ShowUI(uiType, userDatas);
            _navigationHistory.Push(uiType);
            return true;
        }

        /// <summary>
        /// 清空导航历史并回到主菜单窗口。
        /// </summary>
        public void JumpToMain()
        {
            ClearHistory();

            if (!_routeTable.TryGetValue(UIWindowType.MenuMain, out Type mainType))
            {
                Log.Warning("[UIJump] 主菜单未注册路由，无法跳转。");
                return;
            }

            _navigationHistory.Push(mainType);
            GameModule.UI.ShowUI(mainType);
        }

        /// <summary>
        /// 清空当前 UI 导航历史。
        /// </summary>
        /// <remarks>
        /// 只影响 UIJump 自身维护的返回栈，不会关闭已经打开的 UIWindow。
        /// 如需关闭窗口应继续使用 GameModule.UI 的 CloseUI / CloseAll 等接口。
        /// </remarks>
        public void ClearHistory()
        {
            _navigationHistory.Clear();
        }

        /// <summary>
        /// 退出菜单：关闭导航栈中的所有窗口，清空历史，恢复游戏时间。
        /// </summary>
        public void ExitMenu()
        {
            // 关闭导航栈中记录的所有窗口
            while (_navigationHistory.Count > 0)
            {
                Type uiType = _navigationHistory.Pop();
                GameModule.UI.CloseUI(uiType);
            }

            Time.timeScale = 1f;
        }

        /// <summary>
        /// 模块关闭回调。
        /// </summary>
        /// <remarks>
        /// 当前模块只维护内存中的路由表和导航栈，随 ModuleSystem 释放即可；
        /// 如后续接入事件监听或计时器，应在此处统一清理。
        /// </remarks>
        public override void Shutdown()
        {

        }
    }
}
