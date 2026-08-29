#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using TMPro.EditorUtilities;

namespace GameLogic
{
    /// <summary>
    /// UITMPText 编辑器：继承 TMP_EditorPanelUI（TMPro.EditorUtilities）保留 TMP 原生完整 Inspector，
    /// 追加 Gradient / Circle / Shadow 三个扩展折叠面板（EditorPrefs 记忆展开态，照 UITextEditor 模式）。
    /// 注意：TMP 3.0.9 的 UGUI 版面板类是 TMP_EditorPanelUI；TMP_EditorPanel 是 3D 版 TextMeshPro 的面板。
    /// </summary>
    [CustomEditor(typeof(UITMPText), true)]
    [CanEditMultipleObjects]
    public class UITMPTextEditor : TMP_EditorPanelUI
    {
        private static bool m_gradientColorPanelOpen = true;
        private static bool m_circlePanelOpen = true;
        private static bool m_shadowPanelOpen = false;

        // 渐变
        private SerializedProperty m_isUseGradientColor;
        private SerializedProperty m_colorTop;
        private SerializedProperty m_colorBottom;
        private SerializedProperty m_colorLeft;
        private SerializedProperty m_colorRight;
        private SerializedProperty m_gradientOffsetVertical;
        private SerializedProperty m_gradientOffsetHorizontal;
        private SerializedProperty m_splitTextGradient;

        // 环形
        private SerializedProperty m_useTextCircle;
        private SerializedProperty m_radius;
        private SerializedProperty m_spaceCoff;
        private SerializedProperty m_angleOffset;

        // 阴影
        private SerializedProperty m_isUseTextShadow;
        private SerializedProperty m_shadowColor;
        private SerializedProperty m_shadowEffectDistance;
        private SerializedProperty m_shadowSoftness;

        protected override void OnEnable()
        {
            base.OnEnable();

            m_gradientColorPanelOpen = EditorPrefs.GetBool("UITMPText.m_gradientColorPanelOpen", m_gradientColorPanelOpen);
            m_circlePanelOpen = EditorPrefs.GetBool("UITMPText.m_circlePanelOpen", m_circlePanelOpen);
            m_shadowPanelOpen = EditorPrefs.GetBool("UITMPText.m_shadowPanelOpen", m_shadowPanelOpen);

            // 渐变
            {
                m_isUseGradientColor = serializedObject.FindProperty("m_tmpGradientColorExtend.m_isUseGradientColor");
                m_colorTop = serializedObject.FindProperty("m_tmpGradientColorExtend.m_colorTop");
                m_colorBottom = serializedObject.FindProperty("m_tmpGradientColorExtend.m_colorBottom");
                m_colorLeft = serializedObject.FindProperty("m_tmpGradientColorExtend.m_colorLeft");
                m_colorRight = serializedObject.FindProperty("m_tmpGradientColorExtend.m_colorRight");
                m_gradientOffsetVertical = serializedObject.FindProperty("m_tmpGradientColorExtend.m_gradientOffsetVertical");
                m_gradientOffsetHorizontal = serializedObject.FindProperty("m_tmpGradientColorExtend.m_gradientOffsetHorizontal");
                m_splitTextGradient = serializedObject.FindProperty("m_tmpGradientColorExtend.m_splitTextGradient");
            }

            // 环形
            {
                m_useTextCircle = serializedObject.FindProperty("m_tmpCircleExtend.m_useTextCircle");
                m_radius = serializedObject.FindProperty("m_tmpCircleExtend.m_radius");
                m_spaceCoff = serializedObject.FindProperty("m_tmpCircleExtend.m_spaceCoff");
                m_angleOffset = serializedObject.FindProperty("m_tmpCircleExtend.m_angleOffset");
            }

            // 阴影
            {
                m_isUseTextShadow = serializedObject.FindProperty("m_tmpShadowExtend.m_isUseTextShadow");
                m_shadowColor = serializedObject.FindProperty("m_tmpShadowExtend.m_shadowColor");
                m_shadowEffectDistance = serializedObject.FindProperty("m_tmpShadowExtend.m_effectDistance");
                m_shadowSoftness = serializedObject.FindProperty("m_tmpShadowExtend.m_shadowSoftness");
            }
        }

        public override void OnInspectorGUI()
        {
            // 自定义扩展面板绘制在 TMP 原生面板上方
            serializedObject.Update();
            UITMPTextGUI();

            if (GUI.changed)
            {
                EditorPrefs.SetBool("UITMPText.m_gradientColorPanelOpen", m_gradientColorPanelOpen);
                EditorPrefs.SetBool("UITMPText.m_circlePanelOpen", m_circlePanelOpen);
                EditorPrefs.SetBool("UITMPText.m_shadowPanelOpen", m_shadowPanelOpen);
            }

            serializedObject.ApplyModifiedProperties();

            // TMP 原生完整面板（内部已 Update + Apply）
            base.OnInspectorGUI();
        }

        private void UITMPTextGUI()
        {
            DrawGradientColorGUI("字体渐变", ref m_gradientColorPanelOpen);
            DrawCircleGUI("环形字体", ref m_circlePanelOpen);
            DrawShadowGUI("字体阴影", ref m_shadowPanelOpen);
        }

        private void DrawGradientColorGUI(string title, ref bool isPanelOpen)
        {
            if (m_isUseGradientColor == null)
            {
                return;
            }

            UnityEditorUtil.LayoutFrameBox(() =>
            {
                EditorGUILayout.PropertyField(m_isUseGradientColor, new GUIContent("开启字体渐变"));

                if (m_isUseGradientColor.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(m_colorTop, new GUIContent("顶部颜色"));
                    EditorGUILayout.PropertyField(m_colorBottom, new GUIContent("底部颜色"));
                    EditorGUILayout.PropertyField(m_colorLeft, new GUIContent("左侧颜色"));
                    EditorGUILayout.PropertyField(m_colorRight, new GUIContent("右侧颜色"));
                    EditorGUILayout.PropertyField(m_gradientOffsetVertical, new GUIContent("垂直偏移"));
                    EditorGUILayout.PropertyField(m_gradientOffsetHorizontal, new GUIContent("水平偏移"));
                    EditorGUILayout.PropertyField(m_splitTextGradient, new GUIContent("逐字符渐变"));
                    EditorGUI.indentLevel--;
                }
            }, title, ref isPanelOpen, true);
        }

        private void DrawCircleGUI(string title, ref bool isPanelOpen)
        {
            if (m_useTextCircle == null)
            {
                return;
            }

            UnityEditorUtil.LayoutFrameBox(() =>
            {
                EditorGUILayout.PropertyField(m_useTextCircle, new GUIContent("开启环形字体"));

                if (m_useTextCircle.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(m_radius, new GUIContent("半径"));
                    EditorGUILayout.PropertyField(m_spaceCoff, new GUIContent("字符间距"));
                    EditorGUILayout.PropertyField(m_angleOffset, new GUIContent("起始角度偏移"));
                    EditorGUI.indentLevel--;
                }
            }, title, ref isPanelOpen, true);
        }

        private void DrawShadowGUI(string title, ref bool isPanelOpen)
        {
            if (m_isUseTextShadow == null)
            {
                return;
            }

            UnityEditorUtil.LayoutFrameBox(() =>
            {
                EditorGUILayout.PropertyField(m_isUseTextShadow, new GUIContent("开启字体阴影"));

                if (m_isUseTextShadow.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(m_shadowColor, new GUIContent("阴影颜色"));
                    EditorGUILayout.PropertyField(m_shadowEffectDistance, new GUIContent("阴影偏移"));
                    EditorGUILayout.PropertyField(m_shadowSoftness, new GUIContent("阴影柔化"));
                    EditorGUILayout.HelpBox("TMP 版阴影基于 SDF Underlay，仅支持单色；偏移为相对字号的浮点（典型 -1~1），与 UGUI 版像素语义不同。", MessageType.Info);
                    EditorGUI.indentLevel--;
                }
            }, title, ref isPanelOpen, true);
        }
    }
}

#endif
