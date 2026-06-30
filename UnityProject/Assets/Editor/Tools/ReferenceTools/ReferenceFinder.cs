using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System;

public class ReferenceFinder : EditorWindow
{
    private UnityEngine.Object targetObject;
    private List<ReferenceResult> results = new List<ReferenceResult>();
    private Vector2 scrollPos;

    private class ReferenceResult
    {
        public GameObject rootObject;
        public Component component;
        public string fieldName;
    }

    [MenuItem("Tools/引用查找/查脚本字段引用 - 反射扫描当前场景")]
    public static void ShowWindow()
    {
        GetWindow<ReferenceFinder>("脚本字段引用");
    }

    private void OnGUI()
    {
        GUILayout.Label("查找哪个物体被哪些脚本引用了？", EditorStyles.boldLabel);

        targetObject = EditorGUILayout.ObjectField("目标物体/资源", targetObject, typeof(UnityEngine.Object), true);

        if (GUILayout.Button("开始查找", GUILayout.Height(30)))
        {
            FindReferences();
        }

        EditorGUILayout.Space();

        if (results.Count > 0)
        {
            GUILayout.Label($"找到 {results.Count} 处引用:", EditorStyles.helpBox);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            foreach (var res in results)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.textField);

                if (GUILayout.Button("定位", GUILayout.Width(50)))
                {
                    EditorGUIUtility.PingObject(res.rootObject);
                    Selection.activeGameObject = res.rootObject;
                }

                GUILayout.Label($"物体: [{res.rootObject.name}]", GUILayout.Width(150));
                GUILayout.Label($"脚本: {res.component.GetType().Name}", GUILayout.Width(150));
                GUILayout.Label($"变量: {res.fieldName}");

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
        else if (targetObject != null)
        {
            GUILayout.Label("未找到引用。");
        }
    }

    private void FindReferences()
    {
        results.Clear();
        if (targetObject == null) return;

        // 获取已加载场景中的所有组件，包括未激活物体。
        Component[] allComponents = Resources.FindObjectsOfTypeAll<Component>();

        foreach (var comp in allComponents)
        {
            // 跳过空组件、Transform，以及 Project 里的预制体/资源组件。
            if (comp == null || comp is Transform || !comp.gameObject.scene.IsValid()) continue;

            Type type = comp.GetType();
            // 获取所有字段（包括私有和公有）
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                try
                {
                    object value = field.GetValue(comp);

                    // 1. 直接引用检查
                    if (IsReferenceMatch(value, targetObject))
                    {
                        AddResult(comp, field.Name);
                    }
                    // 2. 列表或数组引用检查
                    // 排除掉 Transform，因为它也是 IEnumerable 但我们不需要遍历它的子物体
                    else if (value is IEnumerable enumerable && !(value is string) && !(value is Transform))
                    {
                        foreach (var item in enumerable)
                        {
                            if (IsReferenceMatch(item, targetObject))
                            {
                                AddResult(comp, field.Name + " (List/Array)");
                                break;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // 忽略获取值时抛出的任何异常（如 UnassignedReferenceException）
                    continue;
                }
            }
        }
    }

    private bool IsReferenceMatch(object value, UnityEngine.Object target)
    {
        if (value == null || value.Equals(null)) return false;

        // 如果值是 GameObject
        if (value is GameObject go)
        {
            if (target is GameObject targetGo) return go == targetGo;
            if (target is Component targetComp) return go == targetComp.gameObject;
        }

        // 如果值是 Component
        if (value is Component c)
        {
            if (target is GameObject targetGo) return c.gameObject == targetGo;
            if (target is Component targetComp) return c == targetComp || c.gameObject == targetComp.gameObject;
        }

        // 如果是通用 Unity Object (例如 Material, Texture)
        if (value is UnityEngine.Object obj && target is UnityEngine.Object targetObj)
        {
            return obj == targetObj;
        }

        return false;
    }

    private void AddResult(Component comp, string fieldName)
    {
        results.Add(new ReferenceResult
        {
            rootObject = comp.gameObject,
            component = comp,
            fieldName = fieldName
        });
    }
}
