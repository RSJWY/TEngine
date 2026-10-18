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
            // 构建模式指示（模式/Obfuz/pdb）已移至左区，见 UnityToolbarExtenderLeft/BuildModeIndicator.cs，
            // 右区只保留场景切换与资源模式，避免窗口变窄时被裁切
            _resourceModeIndex = GetResourceModeIndex();
        }
    }
}

#endif
