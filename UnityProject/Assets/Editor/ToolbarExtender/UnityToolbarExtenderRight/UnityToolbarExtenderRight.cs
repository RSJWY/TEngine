#if !UNITY_6000_3_OR_NEWER

using UnityEditor;
using UnityToolbarExtender;

namespace TEngine
{
    [InitializeOnLoad]
    public partial class UnityToolbarExtenderRight
    {
        
        static UnityToolbarExtenderRight()
        {
            // 添加自定义按钮到右上工具栏
            ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI_SceneSwitch);
            // 订阅项目变化事件
            EditorApplication.projectChanged += UpdateScenes;
            UpdateScenes();
            ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI_EditorPlayMode);
            ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI_BuildMode);
            // pdb 开关不走宏，切换不触发域重载，需检测变化并主动重绘
            EditorApplication.update += RefreshToolbarOnPdbChanged;
            _resourceModeIndex = GetResourceModeIndex();
        }
    }
}

#endif
