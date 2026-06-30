using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class MaterialOrganizer : EditorWindow
{
    public List<GameObject> targetObjects = new List<GameObject>();
    private string folderName = "OrganizedMaterials";
    private Vector2 scrollPos;

    [MenuItem("Tools/资源整理/整理材质球 - 移动到指定目录")]
    public static void ShowWindow()
    {
        GetWindow<MaterialOrganizer>("整理材质球");
    }

    private void OnGUI()
    {
        GUILayout.Label("将物体拖入下方列表，整理其材质球", EditorStyles.boldLabel);

        // 文件夹名称输入
        folderName = EditorGUILayout.TextField("目标文件夹名 (Assets/下)", folderName);

        EditorGUILayout.Space();

        // 列表显示
        SerializedObject so = new SerializedObject(this);
        SerializedProperty sp = so.FindProperty("targetObjects");

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        EditorGUILayout.PropertyField(sp, true);
        so.ApplyModifiedProperties();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        if (GUILayout.Button("开始整理材质球", GUILayout.Height(40)))
        {
            OrganizeMaterials();
        }
    }

    private void OrganizeMaterials()
    {
        if (targetObjects == null || targetObjects.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先添加目标物体！", "确定");
            return;
        }

        // 1. 创建文件夹
        string rootPath = "Assets/" + folderName;
        if (!AssetDatabase.IsValidFolder(rootPath))
        {
            string guid = AssetDatabase.CreateFolder("Assets", folderName);
            rootPath = AssetDatabase.GUIDToAssetPath(guid);
        }

        HashSet<Material> foundMaterials = new HashSet<Material>();

        // 2. 查找物体身上所有的材质球
        foreach (GameObject obj in targetObjects)
        {
            if (obj == null) continue;

            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat != null)
                    {
                        foundMaterials.Add(mat);
                    }
                }
            }
        }

        // 3. 移动材质球
        int moveCount = 0;
        foreach (Material mat in foundMaterials)
        {
            string oldPath = AssetDatabase.GetAssetPath(mat);

            // 过滤掉：内置材质球(如Default-Diffuse) 和 已经在目标文件夹里的材质球
            if (string.IsNullOrEmpty(oldPath) || oldPath.StartsWith("Library/") || oldPath.StartsWith("Resources/unity_builtin_extra"))
            {
                continue;
            }

            if (oldPath.StartsWith(rootPath))
            {
                continue;
            }

            string fileName = Path.GetFileName(oldPath);
            string newPath = rootPath + "/" + fileName;

            // 处理重名情况
            newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

            string error = AssetDatabase.MoveAsset(oldPath, newPath);

            if (string.IsNullOrEmpty(error))
            {
                moveCount++;
            }
            else
            {
                Debug.LogError($"移动失败: {mat.name}, 错误: {error}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("完成", $"整理完毕！\n共找到 {foundMaterials.Count} 个材质球\n成功移动 {moveCount} 个材质球", "确定");
    }
}
