#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using TMPro;

namespace GameLogic
{
    public static class UITMPTextDrawEditor
    {
        [MenuItem("GameObject/UI/UITMPText", priority = 31)]
        public static void CreateUITMPText()
        {
            GameObject textObject = ObjectFactory.CreateGameObject("UITMPText", typeof(RectTransform), typeof(UITMPText));
            UITMPText uiText = textObject.GetComponent<UITMPText>();
            UnityEditorUtil.ResetInCanvasFor(uiText.rectTransform);
            // 默认字体走 TMP Settings 默认值，不显式设置
            uiText.text = "UITMPText";
            uiText.color = Color.black;
            uiText.fontSize = 24;
            uiText.raycastTarget = false;
            uiText.rectTransform.sizeDelta = new Vector2(200, 50);
            uiText.alignment = TextAlignmentOptions.Center;
            uiText.rectTransform.localPosition = Vector3.zero;

            GameObject undoTarget = uiText.transform.parent == null ? textObject : uiText.transform.parent.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(undoTarget, string.Empty);
            Undo.SetCurrentGroupName($"Create {textObject.name}");
            Selection.activeGameObject = textObject;
        }
    }
}

#endif
