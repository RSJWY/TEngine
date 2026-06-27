using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// UIJump 导航栈可视化调试工具。
    /// 运行时在 Game 视图左上角绘制当前导航栈和路由表信息。
    /// </summary>
    /// <remarks>
    /// 使用方式：在热更入口（如 GameApp.StartGameLogic）中调用 UIJumpDebugger.Create() 即可。
    /// 仅 Development Build 或 Editor 下生效，Release 包自动跳过创建。
    /// </remarks>
    public sealed class UIJumpDebugger : MonoBehaviour
    {
        private static UIJumpDebugger _instance;

        private IUIJumpControl _jumpControl;
        private bool _showRouteTable;
        private Vector2 _scrollPos;

        // 样式缓存
        private GUIStyle _boxStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _itemStyle;
        private GUIStyle _topItemStyle;
        private GUIStyle _buttonStyle;
        private bool _stylesInitialized;

        /// <summary>
        /// 创建调试器实例。非 Development Build 时不创建。
        /// </summary>
        public static UIJumpDebugger Create()
        {
            if (!Debug.isDebugBuild && !Application.isEditor)
                return null;

            if (_instance != null)
                return _instance;

            var go = new GameObject("[UIJumpDebugger]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<UIJumpDebugger>();
            return _instance;
        }

        private void Awake()
        {
            _jumpControl = GameModule.UIJumpControl;
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 8, 8)
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan }
            };

            _itemStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            _topItemStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.green }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (_jumpControl == null)
            {
                _jumpControl = GameModule.UIJumpControl;
                if (_jumpControl == null) return;
            }

            InitStyles();

            float panelWidth = 280f;
            float panelX = 10f;
            float panelY = 10f;

            GUILayout.BeginArea(new Rect(panelX, panelY, panelWidth, Screen.height - 20f), _boxStyle);

            // 标题
            GUILayout.Label("UIJump Navigator", _headerStyle);
            GUILayout.Space(4);

            // 导航栈
            IReadOnlyList<Type> stack = _jumpControl.NavigationStack;
            GUILayout.Label($"Navigation Stack ({stack.Count})", _headerStyle);

            if (stack.Count == 0)
            {
                GUILayout.Label("  (empty)", _itemStyle);
            }
            else
            {
                for (int i = 0; i < stack.Count; i++)
                {
                    string prefix = i == 0 ? "► " : "  ";
                    string typeName = stack[i]?.Name ?? "null";
                    GUIStyle style = i == 0 ? _topItemStyle : _itemStyle;
                    GUILayout.Label($"{prefix}[{i}] {typeName}", style);
                }
            }

            GUILayout.Space(8);

            // 操作按钮
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("JumpBack", _buttonStyle))
            {
                _jumpControl.JumpBack();
            }
            if (GUILayout.Button("JumpToMain", _buttonStyle))
            {
                _jumpControl.JumpToMain();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // 路由表折叠
            _showRouteTable = GUILayout.Toggle(_showRouteTable, "Show Route Table", GUI.skin.button);
            if (_showRouteTable)
            {
                IReadOnlyDictionary<UIWindowType, Type> routes = _jumpControl.RouteTable;
                foreach (var kvp in routes)
                {
                    GUILayout.Label($"  {kvp.Key} → {kvp.Value.Name}", _itemStyle);
                }
            }

            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            _instance = null;
        }
    }
}
