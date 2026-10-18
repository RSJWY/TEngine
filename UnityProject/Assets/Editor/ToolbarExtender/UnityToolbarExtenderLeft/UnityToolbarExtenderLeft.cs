#if !UNITY_6000_3_OR_NEWER

using UnityEditor;
using UnityToolbarExtender;

namespace TEngine
{
    [InitializeOnLoad]
    public partial class UnityToolbarExtenderLeft
    {
        static UnityToolbarExtenderLeft()
        {
            // 构建模式指示注册在左区最左端（SceneLauncher 的 FlexibleSpace 之前），
            // 让出右区空间给场景切换与资源模式下拉
            ToolbarExtender.LeftToolbarGUI.Add(OnToolbarGUI_BuildMode);
            ToolbarExtender.LeftToolbarGUI.Add(OnToolbarGUI_SceneLauncher);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnEditorQuit;
            // pdb 开关不走宏，切换不触发域重载，需检测变化并主动重绘
            EditorApplication.update += RefreshToolbarOnPdbChanged;
        }
    }
}

#endif