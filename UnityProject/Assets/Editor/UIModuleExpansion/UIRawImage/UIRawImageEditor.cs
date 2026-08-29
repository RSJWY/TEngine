#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace GameLogic
{
    [CustomEditor(typeof(UIRawImage), true)]
    [CanEditMultipleObjects]
    public class UIRawImageEditor : RawImageEditor
    {
        private bool m_isRawImageMaskPanelOpen = false;
        private bool m_isRawImageRoundedCornersPanelOpen = false;
        private bool m_isRawImageMirrorPanelOpen = false;

        // 不规则图形
        private SerializedProperty m_isUseMaskImage;
        private SerializedProperty m_fillPercent;
        private SerializedProperty m_fill;
        private SerializedProperty m_ringWidth;
        private SerializedProperty m_segements;
        private SerializedProperty m_verticesDistances;
        private SerializedProperty m_isUsePercentVert;
        private SerializedProperty m_rotation;

        // 圆角
        private SerializedProperty m_isUseRoundedCorners;
        private SerializedProperty m_radius;
        private SerializedProperty m_triangleNum;

        // Mirror
        private SerializedProperty m_isUseRawImageMirror;
        private SerializedProperty m_mirrorType;

        protected override void OnEnable()
        {
            base.OnEnable();

            var uiRawImage = (UIRawImage)target;
            uiRawImage.UIRawImageMirrorExtend.Initialize(uiRawImage);

            m_isRawImageMaskPanelOpen = EditorPrefs.GetBool("UIRawImage.m_isImageMaskPanelOpen", m_isRawImageMaskPanelOpen);
            m_isRawImageRoundedCornersPanelOpen = EditorPrefs.GetBool("UIRawImage.m_isImageRoundedCornersPanelOpen", m_isRawImageRoundedCornersPanelOpen);
            m_isRawImageMirrorPanelOpen = EditorPrefs.GetBool("UIRawImage.m_isImageMirrorPanelOpen", m_isRawImageMirrorPanelOpen);

            // 不规则图形
            {
                m_segements = serializedObject.FindProperty("m_uiRawImageMaskExtend.m_segements");
                m_isUseMaskImage = serializedObject.FindProperty("m_uiRawImageMaskExtend.m_isUseMaskImage");
                m_fillPercent = serializedObject.FindProperty("m_uiRawImageMaskExtend.m_fillPercent");
                m_fill = serializedObject.FindProperty("m_uiRawImageMaskExtend.m_fill");
                m_ringWidth = serializedObject.FindProperty("m_uiRawImageMaskExtend.m_ringWidth");
                m_verticesDistances = serializedObject.FindProperty("m_uiRawImageMaskExtend.m_verticesDistances");
                m_isUsePercentVert = serializedObject.FindProperty("m_uiRawImageMaskExtend.m_isUsePercentVert");
                m_rotation = serializedObject.FindProperty("m_uiRawImageMaskExtend.m_rotation");
            }

            // 圆角
            {
                m_isUseRoundedCorners = serializedObject.FindProperty("m_uiRawImageRoundedCornersExtend.m_isUseRoundedCorners");
                m_radius = serializedObject.FindProperty("m_uiRawImageRoundedCornersExtend.m_radius");
                m_triangleNum = serializedObject.FindProperty("m_uiRawImageRoundedCornersExtend.m_triangleNum");
            }

            // Mirror
            {
                m_isUseRawImageMirror = serializedObject.FindProperty("m_uiRawImageMirrorExtend.m_isUseRawImageMirror");
                m_mirrorType = serializedObject.FindProperty("m_uiRawImageMirrorExtend.m_mirrorType");
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            UIRawImageGUI();

            serializedObject.ApplyModifiedProperties();
            base.OnInspectorGUI();
        }

        private void UIRawImageGUI()
        {
            //绘制方法只依赖 SerializedProperty，与组件类型解耦，直接复用 UIImageDrawEditor
            UIImageDrawEditor.DrawImageMaskGUI("不规则图形", ref m_isRawImageMaskPanelOpen, m_isUseMaskImage, m_fillPercent,
                m_fill, m_ringWidth, m_segements, m_isUsePercentVert, m_verticesDistances, m_rotation,
                m_isUseRoundedCorners);
            UIImageDrawEditor.DrawImageRoundedCornersGUI("圆角图形", ref m_isRawImageRoundedCornersPanelOpen,
                m_isUseRoundedCorners, m_radius, m_triangleNum, m_isUseMaskImage);
            UIImageDrawEditor.DrawImageMirrorGUI("图片镜像", ref m_isRawImageMirrorPanelOpen, m_isUseRawImageMirror, m_mirrorType);

            if (GUI.changed)
            {
                EditorPrefs.SetBool("UIRawImage.m_isImageMaskPanelOpen", m_isRawImageMaskPanelOpen);
                EditorPrefs.SetBool("UIRawImage.m_isImageRoundedCornersPanelOpen", m_isRawImageRoundedCornersPanelOpen);
                EditorPrefs.SetBool("UIRawImage.m_isImageMirrorPanelOpen", m_isRawImageMirrorPanelOpen);
            }
        }
    }
}

#endif
