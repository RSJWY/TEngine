using System;
using System.Collections;
using UnityEngine;
using UnityEditor;

/// <summary>
/// AssetReferenceDrawer
/// 功能：AssetReference 弱引用字段的 Inspector 绘制——包裹名 + 资源对象拖拽框 + 只读 GUID。
/// 来源：YooAsset 3.0 扩展示例（Extension Sample），适配 GameLogic.AssetReference。
/// 创建时间：2026-07-02
/// </summary>
[CustomPropertyDrawer(typeof(GameLogic.AssetReference), true)]
public class AssetReferenceDrawer : PropertyDrawer
{
    private const float LineSpacing = 2f;

    private Type _assetType;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty packageNameProp = property.FindPropertyRelative("_packageName");
        SerializedProperty assetGUIDProp = property.FindPropertyRelative("_assetGUID");

        Type assetType = GetAssetType();

        EditorGUI.BeginProperty(position, label, property);
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            Rect line = new Rect(position.x, position.y, position.width, lineHeight);

            // 绘制 PackageName
            packageNameProp.stringValue = EditorGUI.TextField(line, "Package Name", packageNameProp.stringValue);

            // 加载当前资源对象
            string assetGUID = assetGUIDProp.stringValue;
            UnityEngine.Object current = null;
            if (string.IsNullOrEmpty(assetGUID) == false)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
                if (string.IsNullOrEmpty(assetPath) == false)
                    current = AssetDatabase.LoadAssetAtPath(assetPath, assetType);
            }

            // 绘制资源对象字段（标签使用声明字段名，如 "Prefab Ref"）
            line.y += lineHeight + LineSpacing;
            UnityEngine.Object newAsset = EditorGUI.ObjectField(line, label, current, assetType, false);
            if (newAsset != current)
            {
                if (newAsset == null)
                {
                    assetGUIDProp.stringValue = "";
                }
                else
                {
                    string newPath = AssetDatabase.GetAssetPath(newAsset);
                    if (string.IsNullOrEmpty(newPath) == false)
                        assetGUIDProp.stringValue = AssetDatabase.AssetPathToGUID(newPath);
                }
            }

            // 绘制 AssetGUID（只读）
            line.y += lineHeight + LineSpacing;
            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.TextField(line, "Asset GUID", assetGUIDProp.stringValue);
            EditorGUI.EndDisabledGroup();
        }
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 3 + LineSpacing * 2;
    }

    /// <summary>
    /// 通过反射获取字段对应子类的 AssetType
    /// </summary>
    private Type GetAssetType()
    {
        if (_assetType != null)
            return _assetType;

        Type fieldType = fieldInfo.FieldType;

        // 兼容数组
        if (fieldType.IsArray)
            fieldType = fieldType.GetElementType();
        // 兼容 List<T>
        else if (fieldType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(fieldType))
            fieldType = fieldType.GetGenericArguments()[0];

        var instance = (GameLogic.AssetReference)Activator.CreateInstance(fieldType);
        _assetType = instance.AssetType;
        return _assetType;
    }
}
