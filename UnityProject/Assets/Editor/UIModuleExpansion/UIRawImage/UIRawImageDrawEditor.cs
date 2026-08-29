#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace GameLogic
{
    public class UIRawImageDrawEditor
    {
        [MenuItem("GameObject/UI/UIRawImage", priority = 33)]
        public static void CreateUIRawImage()
        {
            GameObject rawImageObject = ObjectFactory.CreateGameObject("UIRawImage", typeof(RectTransform), typeof(UIRawImage));
            UIRawImage uiRawImage = rawImageObject.GetComponent<UIRawImage>();
            UnityEditorUtil.ResetInCanvasFor(uiRawImage.rectTransform);
            uiRawImage.rectTransform.localPosition = Vector3.zero;
            uiRawImage.raycastTarget = false;

            GameObject undoTarget = uiRawImage.transform.parent == null ? rawImageObject : uiRawImage.transform.parent.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(undoTarget, string.Empty);
            Undo.SetCurrentGroupName($"Create {rawImageObject.name}");
            Selection.activeGameObject = rawImageObject;
        }
    }
}

#endif
