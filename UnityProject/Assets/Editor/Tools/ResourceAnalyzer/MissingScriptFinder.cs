using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class MissingScriptFinder : EditorWindow
{
    private class MissingScriptInfo
    {
        public GameObject TargetObject;
        public int MissingCount;
    }

    private List<MissingScriptInfo> missingScriptList = new List<MissingScriptInfo>();
    private Vector2 scrollPosition;

    [MenuItem("Tools/资源分析器/查 Missing Script - 当前场景可清理")]
    public static void ShowWindow()
    {
        var window = GetWindow<MissingScriptFinder>("Missing Script");
        window.minSize = new Vector2(450, 400);
        window.Show();
    }

    // 监听场景切换，自动清空脏数据
    private void OnEnable()
    {
        EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
    }

    private void OnDisable()
    {
        EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
    }

    private void OnSceneChanged(Scene current, Scene next)
    {
        missingScriptList.Clear();
        Repaint();
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawSeparator();
        DrawResultList();
    }

    private void DrawHeader()
    {
        GUILayout.Space(10);
        Scene activeScene = SceneManager.GetActiveScene();
        GUILayout.Label($"当前分析场景: {activeScene.name} {(activeScene.isDirty ? "(未保存)" : "")}", EditorStyles.boldLabel);
        
        GUILayout.Space(5);
        GUILayout.BeginHorizontal();
        
        if (GUILayout.Button("扫描场景缺失脚本", GUILayout.Height(30)))
        {
            FindMissingScriptsInScene();
        }

        // 如果找到了缺失脚本，显示一键清理按钮
        GUI.enabled = missingScriptList.Count > 0;
        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f); // 标红按钮提醒
        if (GUILayout.Button("一键清除所有缺失组件", GUILayout.Height(30), GUILayout.Width(150)))
        {
            RemoveAllMissingScripts();
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;
        
        GUILayout.EndHorizontal();

        if (missingScriptList.Count > 0)
        {
            GUILayout.Space(5);
            GUILayout.Label($"警告: 场景中找到了 {missingScriptList.Count} 个带有 Missing Script 的物体！", EditorStyles.helpBox);
        }
    }

    private void DrawSeparator()
    {
        GUILayout.Space(5);
        Rect rect = EditorGUILayout.GetControlRect(false, 2);
        rect.height = 1;
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
        GUILayout.Space(5);
    }

    private void DrawResultList()
    {
        if (missingScriptList.Count == 0)
        {
            GUILayout.Label("点击“扫描”开始检查，当前列表为空或未发现丢失。", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        foreach (var info in missingScriptList)
        {
            // 防空判断，防止物体在扫描后被手动删除
            if (info.TargetObject == null) continue;

            GUILayout.BeginHorizontal("box");
            
            // 显示包含缺失脚本的物体
            EditorGUILayout.ObjectField(info.TargetObject, typeof(GameObject), true, GUILayout.Width(250));
            
            GUIStyle warningStyle = new GUIStyle(EditorStyles.label);
            warningStyle.normal.textColor = Color.yellow;
            GUILayout.Label($"[缺失 {info.MissingCount} 个组件]", warningStyle, GUILayout.Width(120));
            
            GUILayout.Label(GetGameObjectPath(info.TargetObject), EditorStyles.miniLabel);
            
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
        
        // 清理一下可能变成 null 的列表项
        missingScriptList.RemoveAll(info => info.TargetObject == null);
    }

    private void FindMissingScriptsInScene()
    {
        missingScriptList.Clear();
        Scene activeScene = SceneManager.GetActiveScene();

        // 找到场景中所有物体（包括隐藏的）
        GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(go => go != null && go.scene == activeScene).ToArray();

        int count = 0;
        foreach (GameObject go in allGameObjects)
        {
            count++;
            if (count % 50 == 0)
            {
                EditorUtility.DisplayProgressBar("扫描中", $"正在检查物体: {go.name}...", (float)count / allGameObjects.Length);
            }

            // 获取物体上的所有组件
            Component[] components = go.GetComponents<Component>();
            int missingCount = 0;

            // 核心逻辑：Unity 中丢失的脚本组件在数组中会表现为 null
            foreach (Component c in components)
            {
                if (c == null)
                {
                    missingCount++;
                }
            }

            if (missingCount > 0)
            {
                missingScriptList.Add(new MissingScriptInfo
                {
                    TargetObject = go,
                    MissingCount = missingCount
                });
            }
        }

        EditorUtility.ClearProgressBar();
        Repaint();
    }

    private void RemoveAllMissingScripts()
    {
        if (EditorUtility.DisplayDialog("确认清理", "此操作将遍历列表中的所有物体，并删除它们身上已丢失的脚本组件。\n此操作可以撤销(Ctrl+Z)。\n确定要继续吗？", "清理", "取消"))
        {
            int totalRemoved = 0;
            
            // 记录Undo以便撤销
            Undo.SetCurrentGroupName("Remove Missing Scripts");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var info in missingScriptList)
            {
                if (info.TargetObject != null)
                {
                    Undo.RegisterCompleteObjectUndo(info.TargetObject, "Remove Missing Scripts");
                    
                    // 使用 Unity 官方 API 清除丢失的组件
                    int removedCount = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(info.TargetObject);
                    totalRemoved += removedCount;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            // 清理完毕后重新扫描一次更新列表
            FindMissingScriptsInScene();

            // 将场景标记为已修改
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            
            Debug.Log($"<color=green>清理完成！共删除了 {totalRemoved} 个丢失的脚本组件。</color>");
        }
    }

    private string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return "已销毁";
        
        string path = obj.name;
        Transform current = obj.transform;
        while (current.parent != null)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }
        return path;
    }
}
