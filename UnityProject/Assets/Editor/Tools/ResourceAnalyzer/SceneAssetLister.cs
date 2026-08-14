using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;


/// <summary>
/// 场景资产依赖列举器
/// 
/// 功能：
///   1. 分析【当前打开的场景】或【拖入的场景文件】，列出该场景引用的所有外部资产路径。
///   2. 可在"过滤目标"中填入关键字/扩展名/路径片段，仅显示匹配的资产（多个用 ; 或换行分隔）。
///   3. 支持按类型过滤、按路径排序、导出 CSV 报告。
///   4. 区分"场景内对象引用"与"场景文件直接依赖"两个层级。
/// 
/// 菜单：Tools/资源分析器/场景资产依赖列举器
/// </summary>
public class SceneAssetLister : EditorWindow
{
    #region 数据结构
    private class AssetEntry
    {
        public string Path;
        public string Guid;
        public Type AssetType;
        public long SizeBytes;
        public float SizeMB => SizeBytes / (1024f * 1024f);
        public int RefCount;              // 被场景内多少个对象引用
        public List<UnityEngine.Object> ReferencedBy = new List<UnityEngine.Object>(); // 引用此资产的对象（去重）
        public List<string> SceneLevelSources = new List<string>(); // 场景级引用来源（LightmapSettings/RenderSettings/场景文件）
    }

    private class SceneObjEntry
    {
        public GameObject GameObject;
        public List<UnityEngine.Object> Dependencies; // 该对象引用到的目标资产
    }

    private class SceneLevelEntry
    {
        public string Source;   // "LightmapSettings" / "RenderSettings" / "场景文件"
        public List<AssetEntry> Entries = new List<AssetEntry>();
    }
    #endregion

    #region 字段
    private SceneAsset targetSceneAsset;       // 拖入的场景文件
    private bool useActiveScene = true;        // 默认分析当前打开的场景

    private List<AssetEntry> allEntries = new List<AssetEntry>();
    private List<AssetEntry> filteredEntries = new List<AssetEntry>();
    private List<SceneObjEntry> sceneObjEntries = new List<SceneObjEntry>();
    private List<SceneLevelEntry> sceneLevelEntries = new List<SceneLevelEntry>();
    private bool hasSearched;

    private string filterText = "";            // 关键字过滤
    private string[] filterTokens = Array.Empty<string>();
    private bool showOnlyReferenced = true;    // 仅显示被场景内对象实际引用的资产
    private bool showSceneObjects = false;     // 是否展开场景对象 -> 依赖明细
    private bool showEntries = true;

    private Vector2 entryScroll;
    private Vector2 objScroll;
    private Vector2 filterScroll;

    private enum SortMode { Path, Size, RefCount, Type }
    private SortMode sortMode = SortMode.Path;
    private bool sortDesc;

    private readonly List<string> typeFilter = new List<string>(); // 空表示全部
    private bool showTypeFilter;

    private readonly List<string> scanFolders = new List<string>(); // 定向扫描目录，空=全部
    private Vector2 scanFolderScroll;
    private bool showScanFolders = true;

    private bool showSceneLevel;
    private Vector2 sceneLevelScroll;
    #endregion

    #region 入口
    [MenuItem("Tools/资源分析器/场景资产依赖列举器")]
    public static void ShowWindow()
    {
        var window = GetWindow<SceneAssetLister>("场景资产依赖");
        window.minSize = new Vector2(700, 560);
        window.Show();
    }
    #endregion

    #region UI
    private void OnGUI()
    {
        DrawHeader();
        DrawSeparator();
        DrawScanFolders();
        DrawSeparator();
        DrawFilterBar();
        DrawSeparator();
        DrawSummary();
        DrawEntries();
        if (showSceneObjects) DrawSceneObjects();
        if (showSceneLevel) DrawSceneLevel();
    }

    private void DrawHeader()
    {
        GUILayout.Space(10);
        GUILayout.Label("选择场景来源:", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        useActiveScene = GUILayout.Toggle(useActiveScene, "分析当前打开的场景", GUILayout.Width(160));
        GUI.enabled = !useActiveScene;
        targetSceneAsset = (SceneAsset)EditorGUILayout.ObjectField(targetSceneAsset, typeof(SceneAsset), false, GUILayout.Width(260));
        GUI.enabled = true;

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("开始分析", GUILayout.Width(100), GUILayout.Height(24)))
        {
            Analyze();
        }
        GUILayout.EndHorizontal();

        if (!useActiveScene && targetSceneAsset == null)
        {
            EditorGUILayout.HelpBox("未选择场景文件，将分析当前打开的场景。", MessageType.Warning);
        }

        // 拖拽区域
        Rect dragRect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
        GUI.Box(dragRect, "也可将场景文件拖拽到此处自动切换为分析该场景", GUI.skin.GetStyle("HelpBox"));
        Event evt = Event.current;
        if (evt.type == EventType.DragUpdated && dragRect.Contains(evt.mousePosition))
        {
            if (DragAndDrop.objectReferences.OfType<SceneAsset>().Any())
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform && dragRect.Contains(evt.mousePosition))
        {
            var scene = DragAndDrop.objectReferences.OfType<SceneAsset>().FirstOrDefault();
            if (scene != null)
            {
                targetSceneAsset = scene;
                useActiveScene = false;
            }
            DragAndDrop.AcceptDrag();
            evt.Use();
        }
    }

    private void DrawScanFolders()
    {
        showScanFolders = EditorGUILayout.Foldout(showScanFolders,
            $"定向扫描目录 ({scanFolders.Count}) {(scanFolders.Count == 0 ? "- 空=扫描场景全部依赖" : "")}", true, EditorStyles.foldoutHeader);
        if (!showScanFolders) return;

        EditorGUILayout.HelpBox(
            "指定一个或多个文件夹后，只收集并展示这些目录下的依赖资产。\n" +
            "留空则展示场景的全部依赖。支持拖拽文件夹。",
            MessageType.Info);

        scanFolderScroll = GUILayout.BeginScrollView(scanFolderScroll, GUILayout.MaxHeight(90));
        for (int i = 0; i < scanFolders.Count; i++)
        {
            GUILayout.BeginHorizontal("box");
            GUILayout.Label(scanFolders[i], EditorStyles.miniLabel);
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                scanFolders.RemoveAt(i);
                i--;
                ApplyFilter();
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("添加文件夹", GUILayout.Width(90)))
        {
            string abs = EditorUtility.OpenFolderPanel("选择扫描目录", "Assets", "");
            if (!string.IsNullOrEmpty(abs))
            {
                string rel = ToAssetRelative(abs);
                if (!string.IsNullOrEmpty(rel) && !scanFolders.Contains(rel))
                {
                    scanFolders.Add(rel);
                    ApplyFilter();
                }
                else if (string.IsNullOrEmpty(rel))
                {
                    EditorUtility.DisplayDialog("提示", "请选择项目 Assets 目录内的文件夹", "OK");
                }
            }
        }
        if (GUILayout.Button("从选中项载入", GUILayout.Width(110)))
        {
            bool added = false;
            foreach (UnityEngine.Object sel in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(sel);
                if (string.IsNullOrEmpty(p) || !AssetDatabase.IsValidFolder(p)) continue;
                if (!scanFolders.Contains(p)) { scanFolders.Add(p); added = true; }
            }
            if (added) ApplyFilter();
        }
        if (scanFolders.Count > 0 && GUILayout.Button("清空", GUILayout.Width(60)))
        {
            scanFolders.Clear();
            ApplyFilter();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // 拖拽文件夹
        Rect dragRect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
        GUI.Box(dragRect, "将文件夹拖拽到此处添加为扫描目录", GUI.skin.GetStyle("HelpBox"));
        Event evt = Event.current;
        if (evt.type == EventType.DragUpdated && dragRect.Contains(evt.mousePosition))
        {
            if (DragAndDrop.paths != null && DragAndDrop.paths.Any(p => AssetDatabase.IsValidFolder(p)))
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform && dragRect.Contains(evt.mousePosition))
        {
            DragAndDrop.AcceptDrag();
            bool added = false;
            if (DragAndDrop.paths != null)
            {
                foreach (string p in DragAndDrop.paths)
                {
                    if (AssetDatabase.IsValidFolder(p) && !scanFolders.Contains(p))
                    {
                        scanFolders.Add(p);
                        added = true;
                    }
                }
            }
            if (added) ApplyFilter();
            evt.Use();
        }
    }

    private void DrawFilterBar()
    {
        GUILayout.Label("过滤目标 (路径片段 / 扩展名 / 关键字，用 ; 或换行分隔，留空=全部):", EditorStyles.boldLabel);

        filterScroll = GUILayout.BeginScrollView(filterScroll, GUILayout.MaxHeight(60));
        filterText = EditorGUILayout.TextArea(filterText, GUILayout.ExpandHeight(true));
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("应用过滤", GUILayout.Width(80))) ApplyFilter();
        GUILayout.Space(8);

        GUILayout.Label("排序:", GUILayout.Width(36));
        SortMode newMode = (SortMode)EditorGUILayout.EnumPopup(sortMode, GUILayout.Width(90));
        if (newMode != sortMode) { sortMode = newMode; ApplyFilter(); }
        bool newDesc = GUILayout.Toggle(sortDesc, sortDesc ? "降序" : "升序", GUILayout.Width(50));
        if (newDesc != sortDesc) { sortDesc = newDesc; ApplyFilter(); }

        GUILayout.Space(10);
        bool newOnly = GUILayout.Toggle(showOnlyReferenced, "仅被对象引用的", GUILayout.Width(110));
        if (newOnly != showOnlyReferenced) { showOnlyReferenced = newOnly; ApplyFilter(); }

        GUILayout.FlexibleSpace();
        if (filteredEntries.Count > 0 && GUILayout.Button("导出CSV", GUILayout.Width(80)))
        {
            ExportCsv();
        }
        GUILayout.EndHorizontal();

        // 类型过滤
        showTypeFilter = EditorGUILayout.Foldout(showTypeFilter, $"类型过滤 ({(typeFilter.Count == 0 ? "全部" : typeFilter.Count + "项")})", false);
        if (showTypeFilter)
        {
            EditorGUI.indentLevel++;
            GUILayout.Label("常用: .png .tga .jpg .mat .prefab .fbx .wav .mp3 .anim .controller .asset", EditorStyles.miniLabel);
            GUILayout.BeginHorizontal();
            string newType = EditorGUILayout.TextField("", GUILayout.Width(120));
            if (GUILayout.Button("+ 添加", GUILayout.Width(60)) && !string.IsNullOrWhiteSpace(newType))
            {
                string t = newType.Trim().ToLowerInvariant();
                if (!t.StartsWith(".")) t = "." + t;
                if (!typeFilter.Contains(t)) typeFilter.Add(t);
                ApplyFilter();
            }
            GUILayout.EndHorizontal();
            for (int i = 0; i < typeFilter.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(typeFilter[i], EditorStyles.miniLabel);
                if (GUILayout.Button("X", GUILayout.Width(22))) { typeFilter.RemoveAt(i); i--; ApplyFilter(); }
                GUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }
    }

    private void DrawSummary()
    {
        if (!hasSearched) return;
        long totalSize = allEntries.Sum(e => e.SizeBytes);
        long filteredSize = filteredEntries.Sum(e => e.SizeBytes);
        int sceneLevelCount = sceneLevelEntries.Sum(sle => sle.Entries.Count);
        string folderHint = scanFolders.Count > 0
            ? $"  |  扫描目录: {scanFolders.Count} 个"
            : "  |  扫描目录: 全部";
        GUILayout.Label(
            $"全部资产: {allEntries.Count} 个 ({FormatSize(totalSize)})  |  " +
            $"过滤后: {filteredEntries.Count} 个 ({FormatSize(filteredSize)})  |  " +
            $"场景对象: {sceneObjEntries.Count} 个  |  场景级引用: {sceneLevelCount} 个{folderHint}",
            EditorStyles.helpBox);
    }

    private void DrawEntries()
    {
        if (!hasSearched) return;

        showEntries = EditorGUILayout.Foldout(showEntries, $"资产列表 ({filteredEntries.Count})", true, EditorStyles.foldoutHeader);
        if (!showEntries) return;

        if (filteredEntries.Count == 0)
        {
            GUILayout.Label("无匹配资产。", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        entryScroll = GUILayout.BeginScrollView(entryScroll, GUILayout.MaxHeight(360));
        for (int i = 0; i < filteredEntries.Count; i++)
        {
            var e = filteredEntries[i];
            GUILayout.BeginHorizontal("box");

            var icon = AssetDatabase.GetCachedIcon(e.Path);
            if (icon != null) GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));

            string typeName = e.AssetType != null ? e.AssetType.Name : "Unknown";
            GUILayout.Label($"[{typeName}]", GUILayout.Width(90));
            GUILayout.Label(FormatSize(e.SizeBytes), GUILayout.Width(70));

            // 引用计数：有场景级引用时高亮标注来源
            if (e.SceneLevelSources.Count > 0 && e.ReferencedBy.Count == 0)
            {
                // 纯场景级引用（如光照贴图），用黄色标注
                GUI.color = new Color(1f, 0.8f, 0.3f);
                GUILayout.Label($"x{e.RefCount}(场景级)", GUILayout.Width(100));
                GUI.color = Color.white;
            }
            else if (e.SceneLevelSources.Count > 0)
            {
                GUI.color = new Color(1f, 0.8f, 0.3f);
                GUILayout.Label($"x{e.RefCount}(含场景级)", GUILayout.Width(100));
                GUI.color = Color.white;
            }
            else
            {
                GUILayout.Label($"x{e.RefCount}", GUILayout.Width(36));
            }

            if (GUILayout.Button(e.Path, EditorStyles.miniLabel))
            {
                PingAndFrame(e.Path);
            }
            if (GUILayout.Button("定位", GUILayout.Width(44)))
            {
                PingAndFrame(e.Path);
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        showSceneObjects = GUILayout.Toggle(showSceneObjects, "展开场景对象 -> 依赖明细", GUILayout.Width(200));
        if (sceneLevelEntries.Count > 0)
        {
            showSceneLevel = GUILayout.Toggle(showSceneLevel,
                $"展开场景级引用 -> 光照/渲染设置 ({sceneLevelEntries.Count} 组)", GUILayout.Width(260));
        }
        GUILayout.EndHorizontal();
    }

    private void DrawSceneObjects()
    {
        GUILayout.Space(6);
        GUILayout.Label($"场景对象依赖明细 ({sceneObjEntries.Count} 个对象):", EditorStyles.boldLabel);
        objScroll = GUILayout.BeginScrollView(objScroll, GUILayout.MaxHeight(260));
        foreach (var soe in sceneObjEntries)
        {
            if (soe.Dependencies == null || soe.Dependencies.Count == 0) continue;
            GUILayout.BeginHorizontal("box");
            EditorGUILayout.ObjectField(soe.GameObject, typeof(GameObject), true, GUILayout.Width(240));
            GUILayout.Label(GetGameObjectPath(soe.GameObject), EditorStyles.miniLabel);
            GUILayout.Label($"({soe.Dependencies.Count} 个)", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            foreach (var dep in soe.Dependencies)
            {
                if (dep == null) continue;
                string p = AssetDatabase.GetAssetPath(dep);
                GUILayout.BeginHorizontal();
                GUILayout.Space(30);
                EditorGUILayout.ObjectField(dep, typeof(UnityEngine.Object), false, GUILayout.Width(240));
                GUILayout.Label(p, EditorStyles.miniLabel);
                if (GUILayout.Button("定位", GUILayout.Width(44)))
                {
                    PingAndFrame(p);
                }
                GUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }
        GUILayout.EndScrollView();
    }

    private void DrawSceneLevel()
    {
        GUILayout.Space(6);
        GUILayout.Label($"场景级引用明细 (光照贴图/LightingData/反射探针/天空盒等):", EditorStyles.boldLabel);
        sceneLevelScroll = GUILayout.BeginScrollView(sceneLevelScroll, GUILayout.MaxHeight(260));
        foreach (var sle in sceneLevelEntries)
        {
            GUILayout.BeginHorizontal("box");
            GUILayout.Label($"[{sle.Source}]", EditorStyles.boldLabel, GUILayout.Width(160));
            GUILayout.Label($"{sle.Entries.Count} 个资产", EditorStyles.miniBoldLabel);
            GUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            foreach (var e in sle.Entries)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(30);
                var icon = AssetDatabase.GetCachedIcon(e.Path);
                if (icon != null) GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
                EditorGUILayout.ObjectField(
                    AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(e.Path),
                    typeof(UnityEngine.Object), false, GUILayout.Width(240));
                GUILayout.Label(e.Path, EditorStyles.miniLabel);
                if (GUILayout.Button("定位", GUILayout.Width(44)))
                {
                    PingAndFrame(e.Path);
                }
                GUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
            GUILayout.Space(4);
        }
        GUILayout.EndScrollView();
    }

    private void DrawSeparator()
    {
        GUILayout.Space(8);
        Rect rect = EditorGUILayout.GetControlRect(false, 2);
        rect.height = 1;
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
        GUILayout.Space(5);
    }
    #endregion

    #region 分析逻辑
    private void Analyze()
    {
        allEntries.Clear();
        filteredEntries.Clear();
        sceneObjEntries.Clear();
        sceneLevelEntries.Clear();
        hasSearched = true;

        // 确定场景路径
        string scenePath = null;
        Scene activeScene = default;
        bool analyzingActiveScene = useActiveScene || targetSceneAsset == null;

        if (analyzingActiveScene)
        {
            activeScene = SceneManager.GetActiveScene();
            scenePath = activeScene.path;
            if (string.IsNullOrEmpty(scenePath))
            {
                EditorUtility.DisplayDialog("提示", "当前没有打开的有效场景。请先打开场景，或拖入一个场景文件。", "OK");
                return;
            }
        }
        else
        {
            scenePath = AssetDatabase.GetAssetPath(targetSceneAsset);
            if (string.IsNullOrEmpty(scenePath))
            {
                EditorUtility.DisplayDialog("提示", "选择的场景文件无效。", "OK");
                return;
            }
        }

        try
        {
            EditorUtility.DisplayProgressBar("场景资产依赖", "收集场景文件依赖...", 0.1f);

            // 1. 场景文件本身的依赖（静态分析，无需打开场景）
            string[] sceneDeps = AssetDatabase.GetDependencies(scenePath, true);
            var pathToEntry = new Dictionary<string, AssetEntry>(StringComparer.OrdinalIgnoreCase);

            int idx = 0;
            foreach (string dep in sceneDeps)
            {
                idx++;
                if (idx % 50 == 0)
                    EditorUtility.DisplayProgressBar("场景资产依赖", $"解析资产 {idx}/{sceneDeps.Length}", 0.1f + 0.4f * idx / sceneDeps.Length);

                if (string.IsNullOrEmpty(dep) || dep == scenePath) continue;
                if (!dep.StartsWith("Assets/") && !dep.StartsWith("Packages/")) continue;
                if (dep.EndsWith(".cs") || dep.EndsWith(".dll")) continue;

                var entry = GetOrCreateEntry(dep, pathToEntry);
            }

            EditorUtility.DisplayProgressBar("场景资产依赖", "扫描场景内对象...", 0.55f);

            // 2. 场景内对象的引用（需要场景已打开；未打开时此部分跳过）
            if (analyzingActiveScene && activeScene.IsValid())
            {
                var allGos = Resources.FindObjectsOfTypeAll<GameObject>()
                    .Where(go => go.scene == activeScene && go.hideFlags == HideFlags.None)
                    .ToArray();

                int count = 0;
                foreach (GameObject go in allGos)
                {
                    count++;
                    if (count % 50 == 0)
                        EditorUtility.DisplayProgressBar("场景资产依赖", $"扫描对象 {count}/{allGos.Length}", 0.55f + 0.4f * count / allGos.Length);

                    UnityEngine.Object[] deps = EditorUtility.CollectDependencies(new UnityEngine.Object[] { go });
                    var hitTargets = new List<UnityEngine.Object>();
                    var hitPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (UnityEngine.Object d in deps)
                    {
                        if (d == null) continue;
                        string p = AssetDatabase.GetAssetPath(d);
                        if (string.IsNullOrEmpty(p)) continue;
                        if (!pathToEntry.TryGetValue(p, out var entry))
                        {
                            // 可能是 CollectDependencies 发现的、GetDependencies 未列出的资产
                            if (!p.StartsWith("Assets/") && !p.StartsWith("Packages/")) continue;
                            if (p.EndsWith(".cs") || p.EndsWith(".dll")) continue;
                            entry = GetOrCreateEntry(p, pathToEntry);
                        }
                        if (hitPaths.Add(p))
                        {
                            entry.RefCount++;
                            entry.ReferencedBy.Add(go);
                            hitTargets.Add(d);
                        }
                    }

                    if (hitTargets.Count > 0)
                        sceneObjEntries.Add(new SceneObjEntry { GameObject = go, Dependencies = hitTargets });
                }
            }

            // 3. 场景级引用：LightmapSettings / RenderSettings / 场景文件本身直接依赖但无对象引用的资产
            CollectSceneLevelRefs(scenePath, activeScene, analyzingActiveScene, pathToEntry);

            allEntries = pathToEntry.Values.ToList();
            ApplyFilter();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[场景资产依赖] {Path.GetFileName(scenePath)}: 全部 {allEntries.Count} 个资产, {sceneObjEntries.Count} 个对象有引用, {sceneLevelEntries.Count} 组场景级引用。");
    }

    /// <summary>
    /// 收集场景级引用：LightmapSettings（光照贴图/LightingData/反射探针）、RenderSettings（天空盒/雾/环境光）。
    /// 这些资产被场景全局设置引用，不属于任何 GameObject，EditorUtility.CollectDependencies(go) 无法捕获。
    /// </summary>
    private void CollectSceneLevelRefs(string scenePath, Scene activeScene, bool analyzingActiveScene,
        Dictionary<string, AssetEntry> pathToEntry)
    {
        EditorUtility.DisplayProgressBar("场景资产依赖", "收集场景级光照引用...", 0.97f);

        // --- A. 从场景文件 YAML 解析 LightingDataAsset / LightingSettings 的 GUID ---
        // 适用于所有场景（打开/未打开），因为 LightmapSettings.lightingDataAsset 无公开运行时 API。
        var yamlEntry = new SceneLevelEntry { Source = "场景文件(光照)" };
        var yamlPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string line in File.ReadLines(scenePath))
            {
                string trimmed = line.Trim();
                // m_LightingDataAsset: {fileID: 112000000, guid: xxx, type: 2}
                // m_LightingSettings: {fileID: 4890085278179872738, guid: xxx, type: 2}
                if (!trimmed.StartsWith("m_Lighting")) continue;
                string guid = ExtractGuidFromYamlLine(trimmed);
                if (string.IsNullOrEmpty(guid)) continue;
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(p) || (!p.StartsWith("Assets/") && !p.StartsWith("Packages/"))) continue;
                yamlPaths.Add(p);
                // LightingDataAsset 是二进制，但 AssetDatabase.GetDependencies 能穿透其内部引用的 lightmap 贴图
                foreach (string dep in AssetDatabase.GetDependencies(p, true))
                {
                    if (dep != p && !dep.EndsWith(".cs") && !dep.EndsWith(".dll"))
                        yamlPaths.Add(dep);
                }
            }
        }
        catch { }

        foreach (string p in yamlPaths)
        {
            if (string.IsNullOrEmpty(p) || (!p.StartsWith("Assets/") && !p.StartsWith("Packages/"))) continue;
            var entry = GetOrCreateEntry(p, pathToEntry);
            if (entry.SceneLevelSources.Contains(yamlEntry.Source)) continue;
            entry.SceneLevelSources.Add(yamlEntry.Source);
            entry.RefCount++;
            yamlEntry.Entries.Add(entry);
        }
        if (yamlEntry.Entries.Count > 0) sceneLevelEntries.Add(yamlEntry);

        // --- B. 运行时补充：LightmapSettings.lightmaps 数组 + ReflectionProbe.bakedTexture ---
        // 场景文件 YAML 可能未完整记录所有 lightmap 贴图引用，运行时数据更准确。
        if (analyzingActiveScene && activeScene.IsValid())
        {
            var runtimeLmEntry = new SceneLevelEntry { Source = "LightmapSettings(运行时)" };
            var runtimePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            LightmapData[] lightmaps = LightmapSettings.lightmaps;
            if (lightmaps != null)
            {
                foreach (var lmd in lightmaps)
                {
                    if (lmd == null) continue;
                    AddTexturePath(lmd.lightmapDir, runtimePaths);
                    AddTexturePath(lmd.lightmapColor, runtimePaths);
                    AddTexturePath(lmd.shadowMask, runtimePaths);
                }
            }

            // 反射探针烘焙数据（bakedTexture 指向 .exr）
            foreach (var rp in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            {
                if (rp == null) continue;
                if (rp.gameObject == null || rp.gameObject.scene != activeScene) continue;
                AddTexturePath(rp.bakedTexture, runtimePaths);
            }

            foreach (string p in runtimePaths)
            {
                if (string.IsNullOrEmpty(p) || (!p.StartsWith("Assets/") && !p.StartsWith("Packages/"))) continue;
                var entry = GetOrCreateEntry(p, pathToEntry);
                if (entry.SceneLevelSources.Contains(runtimeLmEntry.Source)) continue;
                entry.SceneLevelSources.Add(runtimeLmEntry.Source);
                entry.RefCount++;
                runtimeLmEntry.Entries.Add(entry);
            }
            if (runtimeLmEntry.Entries.Count > 0) sceneLevelEntries.Add(runtimeLmEntry);

            // --- C. RenderSettings（天空盒/环境反射）---
            var rsEntry = new SceneLevelEntry { Source = "RenderSettings" };
            var rsPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddTexturePath(RenderSettings.skybox, rsPaths);
            if (RenderSettings.customReflection != null)
                rsPaths.Add(AssetDatabase.GetAssetPath(RenderSettings.customReflection));

            foreach (string p in rsPaths)
            {
                if (string.IsNullOrEmpty(p) || (!p.StartsWith("Assets/") && !p.StartsWith("Packages/"))) continue;
                var entry = GetOrCreateEntry(p, pathToEntry);
                if (entry.SceneLevelSources.Contains(rsEntry.Source)) continue;
                entry.SceneLevelSources.Add(rsEntry.Source);
                entry.RefCount++;
                rsEntry.Entries.Add(entry);
            }
            if (rsEntry.Entries.Count > 0) sceneLevelEntries.Add(rsEntry);
        }
    }

    private static void AddTexturePath(Texture tex, HashSet<string> set)
    {
        if (tex == null) return;
        string p = AssetDatabase.GetAssetPath(tex);
        if (!string.IsNullOrEmpty(p)) set.Add(p);
    }

    private static void AddTexturePath(Material mat, HashSet<string> set)
    {
        if (mat == null) return;
        string p = AssetDatabase.GetAssetPath(mat);
        if (!string.IsNullOrEmpty(p)) set.Add(p);
    }

    /// <summary>
    /// 从 YAML 行提取 guid 值，如 "m_LightingDataAsset: {fileID: 112000000, guid: abc123, type: 2}" -> "abc123"
    /// </summary>
    private static string ExtractGuidFromYamlLine(string yamlLine)
    {
        const string key = "guid: ";
        int idx = yamlLine.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return null;
        int start = idx + key.Length;
        int end = yamlLine.IndexOfAny(new[] { ',', '}', ' ' }, start);
        if (end < 0) end = yamlLine.Length;
        return yamlLine.Substring(start, end - start);
    }

    private AssetEntry GetOrCreateEntry(string assetPath, Dictionary<string, AssetEntry> dict)
    {
        if (dict.TryGetValue(assetPath, out var e)) return e;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        Type t = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
        long size = 0;
        try
        {
            string full = Path.GetFullPath(assetPath);
            if (File.Exists(full)) size = new FileInfo(full).Length;
        }
        catch { }

        e = new AssetEntry
        {
            Path = assetPath,
            Guid = guid,
            AssetType = t,
            SizeBytes = size,
            RefCount = 0
        };
        dict[assetPath] = e;
        return e;
    }

    private void ApplyFilter()
    {
        // 解析过滤 token
        var tokens = new List<string>();
        if (!string.IsNullOrWhiteSpace(filterText))
        {
            foreach (var raw in filterText.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(t)) tokens.Add(t);
            }
        }
        filterTokens = tokens.ToArray();

        filteredEntries = allEntries.Where(e => MatchFilter(e)).ToList();
        SortFiltered();
        Repaint();
    }

    private bool MatchFilter(AssetEntry e)
    {
        if (showOnlyReferenced && e.RefCount == 0) return false;

        // 定向扫描目录：资产路径必须落在某个扫描目录下
        if (scanFolders.Count > 0)
        {
            string normalized = e.Path.Replace('\\', '/');
            bool inFolder = false;
            foreach (var folder in scanFolders)
            {
                string f = folder.Replace('\\', '/');
                if (normalized.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, f, StringComparison.OrdinalIgnoreCase))
                {
                    inFolder = true;
                    break;
                }
            }
            if (!inFolder) return false;
        }

        string ext = Path.GetExtension(e.Path)?.ToLowerInvariant() ?? "";
        if (typeFilter.Count > 0 && !typeFilter.Contains(ext)) return false;

        if (filterTokens.Length == 0) return true;

        string lower = e.Path.ToLowerInvariant();
        foreach (var tok in filterTokens)
        {
            if (lower.Contains(tok)) return true;
        }
        return false;
    }

    private void SortFiltered()
    {
        Comparison<AssetEntry> cmp;
        switch (sortMode)
        {
            case SortMode.Size:
                cmp = (a, b) => a.SizeBytes.CompareTo(b.SizeBytes); break;
            case SortMode.RefCount:
                cmp = (a, b) => a.RefCount.CompareTo(b.RefCount); break;
            case SortMode.Type:
                cmp = (a, b) => string.Compare(a.AssetType?.Name ?? "", b.AssetType?.Name ?? "", StringComparison.Ordinal); break;
            default:
                cmp = (a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal); break;
        }
        if (sortDesc) cmp = (a, b) => -cmp(a, b);
        filteredEntries.Sort(cmp);
    }
    #endregion

    #region 导出
    private void ExportCsv()
    {
        string path = EditorUtility.SaveFilePanel("导出场景资产依赖", "Assets",
            $"scene_deps_{DateTime.Now:yyyyMMddHHmm}", "csv");
        if (string.IsNullOrEmpty(path)) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Path,Guid,Type,SizeBytes,SizeHuman,RefCount,ReferencedByCount");
        foreach (var e in filteredEntries)
        {
            string typeName = e.AssetType != null ? e.AssetType.Name : "Unknown";
            sb.AppendLine($"\"{e.Path}\",{e.Guid},{typeName},{e.SizeBytes},{FormatSize(e.SizeBytes)},{e.RefCount},{e.ReferencedBy.Count}");
        }
        File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("导出成功", "报告已导出到:\n" + path, "OK");
    }
    #endregion

    #region 辅助
    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
        if (bytes >= 1024) return (bytes / 1024f).ToString("0.0") + " KB";
        return bytes + " B";
    }

    private static string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform current = obj.transform;
        while (current.parent != null)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }
        return path;
    }

    private static string ToAssetRelative(string absoluteOrRelative)
    {
        if (string.IsNullOrEmpty(absoluteOrRelative)) return null;
        string normalized = absoluteOrRelative.Replace('\\', '/');
        if (normalized.StartsWith(Application.dataPath))
        {
            return "Assets" + normalized.Substring(Application.dataPath.Length);
        }
        if (normalized.StartsWith("Assets"))
        {
            return normalized;
        }
        return null;
    }

    /// <summary>
    /// 选中资产、Ping 高亮、聚焦 Project 窗口并 Frame Selected 使其在 Project 视图中可见。
    /// </summary>
    private static void PingAndFrame(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return;
        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (obj == null) return;

        Selection.activeObject = obj;
        EditorGUIUtility.PingObject(obj);

        // 聚焦 Project 窗口并执行 Frame Selected，使资产在 Project 视图中居中可见
        try
        {
            var projectWindow = EditorWindow.GetWindow(System.Type.GetType("UnityEditor.ProjectBrowser,UnityEditor"));
            if (projectWindow != null)
            {
                projectWindow.Focus();
                projectWindow.Repaint();
                EditorApplication.delayCall += () =>
                {
                    try { EditorApplication.ExecuteMenuItem("Edit/Frame Selected"); }
                    catch { }
                };
            }
        }
        catch { }
    }
    #endregion
}
