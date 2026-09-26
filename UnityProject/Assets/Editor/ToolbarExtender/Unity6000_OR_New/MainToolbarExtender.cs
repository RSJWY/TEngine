#if UNITY_6000_3_OR_NEWER

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using TEngine;

[InitializeOnLoad]
public class MainToolbarInitializeOnLoad
{
    static MainToolbarInitializeOnLoad()
    {
        MainToolbarSceneLauncherButton.Init();
        MainToolbarDropdownSceneSelector.Init();
        MainToolbarDropdownPlayMode.Init();
    }
}

public class MainToolbarSceneLauncherButton
{
    private const string PreviousSceneKey = "TEngine_PreviousScenePath"; // 用于存储之前场景路径的键
    private const string IsLauncherBtn = "TEngine_IsLauncher"; // 用于存储之前是否按下launcher

    private static readonly string SceneMain = "main";

    private const string MainScenePath = "Assets/Scenes/main.unity";

    [MainToolbarElement("TEngine/Scene Launcher Button", defaultDockIndex = -10, defaultDockPosition = MainToolbarDockPosition.Middle)]
    private static MainToolbarElement ProjectSettingsButton()
    {
        var onIcon = EditorGUIUtility.IconContent("PlayButton").image as Texture2D;
        var offIcon = EditorGUIUtility.IconContent("StopButton").image as Texture2D;
        var icon = !EditorApplication.isPlaying ? onIcon : offIcon;
        var content = new MainToolbarContent("Launcher", icon, "");
        var launcherBtn = new MainToolbarButton(content, () => { SceneHelper.StartScene(SceneMain); })
        {
            displayed = true
        };
        return launcherBtn;
    }

    [MainToolbarElement("TEngine/Go To Main Scene", defaultDockIndex = -9, defaultDockPosition = MainToolbarDockPosition.Middle)]
    private static MainToolbarElement GoToMainSceneButton()
    {
        var icon = EditorGUIUtility.IconContent("Scene").image as Texture2D;
        var content = new MainToolbarContent("前往主场景", icon, "切换到 Assets/Scenes/main.unity 主启动场景");
        var btn = new MainToolbarButton(content, () => { GoToMainScene(); })
        {
            displayed = true
        };
        return btn;
    }

    private static void GoToMainScene()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.Log("正在退出播放模式，请再次点击以切换到主启动场景。");
            EditorApplication.isPlaying = false;
            return;
        }

        if (!TEngine.EditorSceneTransitionUtility.ConfirmSaveModifiedScenesBeforeSwitch())
            return;

        if (!File.Exists(MainScenePath))
        {
            Debug.LogWarning($"找不到主启动场景文件：{MainScenePath}");
            return;
        }

        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
    }

    public static void Init()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.quitting -= OnEditorQuit;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting += OnEditorQuit;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        ProjectSettingsButton();
        MainToolbar.Refresh("TEngine/Scene Launcher Button");
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            // 从 EditorPrefs 读取之前的场景路径 并恢复之前的场景
            var previousScenePath = EditorPrefs.GetString(PreviousSceneKey, string.Empty);
            if (!string.IsNullOrEmpty(previousScenePath) && EditorPrefs.GetBool(IsLauncherBtn))
            {
                EditorApplication.delayCall += () =>
                {
                    if (TEngine.EditorSceneTransitionUtility.ConfirmSaveModifiedScenesBeforeSwitch())
                    {
                        EditorSceneManager.OpenScene(previousScenePath);
                    }
                };
            }

            EditorPrefs.SetBool(IsLauncherBtn, false);
        }
    }

    private static void OnEditorQuit()
    {
        EditorPrefs.SetString(PreviousSceneKey, "");
        EditorPrefs.SetBool(IsLauncherBtn, false);
    }

    private static class SceneHelper
    {
        private static string m_sceneToOpen;

        public static void StartScene(string sceneName)
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }

            var activeScene = SceneManager.GetActiveScene();

            // 缓存一下当前正在进行编辑的场景文件
            if (activeScene.isLoaded && activeScene.name != sceneName)
            {
                EditorPrefs.SetString(PreviousSceneKey, activeScene.path);
                EditorPrefs.SetBool(IsLauncherBtn, true);
            }

            m_sceneToOpen = sceneName;
            EditorApplication.update += OnUpdate;
        }

        private static void OnUpdate()
        {
            if (string.IsNullOrEmpty(m_sceneToOpen) ||
                EditorApplication.isPlaying || EditorApplication.isPaused ||
                EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EditorApplication.update -= OnUpdate;

            if (TEngine.EditorSceneTransitionUtility.ConfirmSaveModifiedScenesBeforeSwitch())
            {
                string scenePath = TEngine.EditorSceneTransitionUtility.FindScenePathInFolder(
                    m_sceneToOpen,
                    TEngine.EditorSceneTransitionUtility.InitialSceneFolder);

                if (string.IsNullOrEmpty(scenePath))
                {
                    Debug.LogWarning($"找不到 {TEngine.EditorSceneTransitionUtility.InitialSceneFolder} 下的场景文件：{m_sceneToOpen}");
                }
                else
                {
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    EditorApplication.isPlaying = true;
                }
            }

            m_sceneToOpen = null;
        }
    }
}

public class MainToolbarDropdownSceneSelector
{
    const string kElementPath = "TEngine/Scene Switcher";

    private static List<(string sceneName, string scenePath)> m_initScenes;
    private static List<(string sceneName, string scenePath)> m_defaultScenes;
    private static List<(string sceneName, string scenePath)> m_configScenes;
    private static List<(string sceneName, string scenePath)> m_otherScenes;

    private static string initScenePath = "Assets/Scenes";
    private static string defaultScenePath = "Assets/AssetRaw/Scenes";

    static string[] scenePaths;

    [MainToolbarElement(kElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 50)]
    public static MainToolbarElement CreateSceneSelectorDropdown()
    {
        string activeSceneName;
        if (Application.isPlaying)
            activeSceneName = SceneManager.GetActiveScene().name;
        else
            activeSceneName = EditorSceneManager.GetActiveScene().name;
        if (activeSceneName.Length == 0)
            activeSceneName = "Untitled";

        var icon = EditorGUIUtility.IconContent("UnityLogo").image as Texture2D;
        var content = new MainToolbarContent(activeSceneName, icon, "Select active scene");
        return new MainToolbarDropdown(content, ShowDropdownMenu);
    }

    public static void Init()
    {
        EditorApplication.projectChanged += UpdateScenes;
        UpdateScenes();
        SceneManager.activeSceneChanged += SceneSwitched;
        EditorSceneManager.activeSceneChangedInEditMode += SceneSwitched;
    }

    static void ShowDropdownMenu(Rect dropDownRect)
    {
        var menu = new GenericMenu();
        AddScenesToMenu(m_initScenes, "初始化场景", menu);
        AddScenesToMenu(m_configScenes, "注册场景", menu);
        AddScenesToMenu(m_defaultScenes, "默认场景", menu);
        AddScenesToMenu(m_otherScenes, "其他场景", menu);
        menu.DropDown(dropDownRect);
    }

    private static void AddScenesToMenu(List<(string sceneName, string scenePath)> scenes, string category, GenericMenu menu)
    {
        if (scenes != null && scenes.Count > 0)
        {
            foreach (var scene in scenes)
            {
                menu.AddItem(new GUIContent($"{category}/{scene.sceneName}"), false, () =>
                {
                    SwitchScene(scene.scenePath);
                });
            }
        }
    }

    static void SwitchScene(string scenePath)
    {
        if (Application.isPlaying)
        {
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.Log($"Switching to scene: {sceneName}");
                SceneManager.LoadScene(sceneName);
            }
            else
            {
                Debug.LogError($"Scene '{sceneName}' is not in the Build Settings.");
            }
        }
        else
        {
            if (File.Exists(scenePath))
            {
                if (TEngine.EditorSceneTransitionUtility.ConfirmSaveModifiedScenesBeforeSwitch())
                {
                    Debug.Log($"Switching to scene: {scenePath}");
                    EditorSceneManager.OpenScene(scenePath);
                }
            }
            else
            {
                Debug.LogError($"Scene at path '{scenePath}' does not exist.");
            }
        }
    }

    static void SceneSwitched(Scene oldScene, Scene newScene)
    {
        MainToolbar.Refresh(kElementPath);
    }

    static void UpdateScenes()
    {
        m_initScenes = SceneSwitcher.GetScenesInPath(initScenePath);
        m_defaultScenes = SceneSwitcher.GetScenesInPath(defaultScenePath);
        m_configScenes = SceneEnumConfigSceneSource.GetConfiguredScenes();

        List<(string sceneName, string scenePath)> allScenes = GetScenesInPath();
        m_otherScenes = new List<(string sceneName, string scenePath)>(allScenes);
        m_otherScenes.RemoveAll(scene =>
            m_initScenes.Exists(init => init.scenePath == scene.scenePath) ||
            m_defaultScenes.Exists(abScene => abScene.scenePath == scene.scenePath));
    }

    private static List<(string sceneName, string scenePath)> GetScenesInPath()
    {
        var allScenes = new List<(string sceneName, string scenePath)>();

        // 查找项目中所有场景文件
        string[] guids = AssetDatabase.FindAssets("t:Scene");
        foreach (var guid in guids)
        {
            var scenePath = AssetDatabase.GUIDToAssetPath(guid);
            var sceneName = Path.GetFileNameWithoutExtension(scenePath);
            allScenes.Add((sceneName, scenePath));
        }

        return allScenes;
    }

    private static class SceneSwitcher
    {
        public static List<(string sceneName, string scenePath)> GetScenesInPath(string path)
        {
            var scenes = new List<(string sceneName, string scenePath)>();
            var guids = AssetDatabase.FindAssets("t:Scene", new string[] { path });

            foreach (var guid in guids)
            {
                var scenePath = AssetDatabase.GUIDToAssetPath(guid);
                var sceneName = Path.GetFileNameWithoutExtension(scenePath);
                scenes.Add((sceneName, scenePath));
            }
            return scenes;
        }

        public static bool PromptSaveCurrentScene()
        {
            return TEngine.EditorSceneTransitionUtility.ConfirmSaveModifiedScenesBeforeSwitch();
        }
    }
}

public class MainToolbarDropdownPlayMode
{
    const string kElementPath = "TEngine/Play Mode";
    private const string ResourceModePrefsKey = "EditorPlayMode";
    private const string ResourceModePrefsVersionKey = "TEngine.EditorPlayModePrefsVersion";
    private const int ResourceModePrefsVersion = 1;

    private static readonly string[] _resourceModeNames =
    {
        "EditorMode (编辑器下的模拟模式)",
        "OfflinePlayMode (单机模式)",
        "HostPlayMode (联机运行模式)",
        "WebPlayMode (WebGL运行模式)"
    };

    private static readonly EPlayMode[] _resourceModes =
    {
        EPlayMode.EditorSimulateMode,
        EPlayMode.OfflinePlayMode,
        EPlayMode.HostPlayMode,
        EPlayMode.WebPlayMode
    };

    private static int _resourceModeIndex = 0;
    public static int ResourceModeIndex => _resourceModeIndex;

    private static MainToolbarElement m_btn;

    [MainToolbarElement(kElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 51)]
    public static MainToolbarElement CreateExampleDropdown()
    {
        _resourceModeIndex = GetResourceModeIndex();
        var content = new MainToolbarContent(_resourceModeNames[ResourceModeIndex]);
        m_btn = new MainToolbarDropdown(content, ShowDropdownMenu)
        {
            enabled = !EditorApplication.isPlaying
        };
        return m_btn;
    }

    private static int GetResourceModeIndex()
    {
        MigrateLegacyResourceModePreference();
        int savedMode = EditorPrefs.GetInt(ResourceModePrefsKey, (int)EPlayMode.EditorSimulateMode);
        for (int i = 0; i < _resourceModes.Length; i++)
        {
            if ((int)_resourceModes[i] == savedMode)
                return i;
        }

        return 0;
    }

    private static void MigrateLegacyResourceModePreference()
    {
        if (EditorPrefs.GetInt(ResourceModePrefsVersionKey, 0) >= ResourceModePrefsVersion)
            return;

        int legacyIndex = EditorPrefs.GetInt(ResourceModePrefsKey, 0);
        if (legacyIndex >= 0 && legacyIndex < _resourceModes.Length)
            EditorPrefs.SetInt(ResourceModePrefsKey, (int)_resourceModes[legacyIndex]);

        EditorPrefs.SetInt(ResourceModePrefsVersionKey, ResourceModePrefsVersion);
    }

    public static void Init()
    {
        // 监听播放模式变化
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        // EditorApplication.projectChanged += UpdateScenes;
        // SceneManager.activeSceneChanged += SceneSwitched;
        // EditorSceneManager.activeSceneChangedInEditMode += SceneSwitched;
    }
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        CreateExampleDropdown();
        MainToolbar.Refresh(kElementPath);
    }

    static void ShowDropdownMenu(Rect dropDownRect)
    {
        var menu = new GenericMenu();

        for (var index = 0; index < _resourceModeNames.Length; index++)
        {
            int i = index;
            var resourceModeName = _resourceModeNames[index];
            menu.AddItem(new GUIContent(resourceModeName), false, () =>
            {
                _resourceModeIndex = i;
                Debug.Log($"更改编辑器资源运行模式：{_resourceModeNames[_resourceModeIndex]}");
                EditorPrefs.SetInt(ResourceModePrefsKey, (int)_resourceModes[_resourceModeIndex]);
                MainToolbar.Refresh(kElementPath);
            });
        }

        menu.DropDown(dropDownRect);
    }
}

public class MainToolbarBuildModeDropdown
{
    const string kElementPath = "TEngine/Build Mode";

    // 模式状态由 ENABLE_RELEASE 宏表达，切换会触发重编译与域重载，工具栏随之重建，无需手动刷新
    [MainToolbarElement(kElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 52)]
    public static MainToolbarElement CreateBuildModeDropdown()
    {
        bool isRelease = BuildDLLCommand.IsReleaseModeActive;
        bool pdbOn = BuildDLLCommand.IsPdbEnabled;
        string pdbText = isRelease ? "禁用" : (pdbOn ? "开" : "关");
        string label = isRelease ? "模式: release" : "模式: dev";
        string tooltip = isRelease
            ? "当前构建模式：release（发布：不生成/不加载 pdb）"
            : "当前构建模式：dev（开发：pdb 有则加载）";
        if (BuildDLLCommand.IsObfuzInstalled)
        {
            bool obfuzOn = BuildDLLCommand.IsObfuzActiveSafe;
            label += $" | Obfuz: {(obfuzOn ? "开" : "关")}";
            tooltip += $"\nObfuz 混淆：{(obfuzOn ? "开" : "关")}";
        }
        label += $" | pdb: {pdbText}";
        tooltip += $"\npdb 符号：{pdbText}";
        tooltip += "\n点击弹出快捷切换菜单";
        var content = new MainToolbarContent(label, null, tooltip);
        return new MainToolbarDropdown(content, ShowDropdownMenu);
    }

    static void ShowDropdownMenu(Rect dropDownRect)
    {
        bool isRelease = BuildDLLCommand.IsReleaseModeActive;
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("dev 模式（开发，pdb 可用）"), !isRelease, () => BuildDLLCommand.SetReleaseModeConfirm(false));
        menu.AddItem(new GUIContent("release 模式（发布，不含 pdb）"), isRelease, () => BuildDLLCommand.SetReleaseModeConfirm(true));
        if (BuildDLLCommand.IsObfuzInstalled)
        {
            menu.AddSeparator(string.Empty);
            bool obfuzActive = BuildDLLCommand.IsObfuzActiveSafe;
            menu.AddItem(new GUIContent("Obfuz 混淆/开启"), obfuzActive, () => BuildDLLCommand.SetObfuzSafeConfirm(true));
            menu.AddItem(new GUIContent("Obfuz 混淆/关闭"), !obfuzActive, () => BuildDLLCommand.SetObfuzSafeConfirm(false));
        }

        menu.AddSeparator(string.Empty);
        if (isRelease)
        {
            // release 模式下 pdb 开关不生效，不提供切换
            menu.AddDisabledItem(new GUIContent("pdb 符号（release 模式不生效）"));
        }
        else
        {
            bool pdbOn = BuildDLLCommand.IsPdbEnabled;
            menu.AddItem(new GUIContent("pdb 符号/开启"), pdbOn, () => BuildDLLCommand.SetPdbEnabledConfirm(true));
            menu.AddItem(new GUIContent("pdb 符号/关闭"), !pdbOn, () => BuildDLLCommand.SetPdbEnabledConfirm(false));
        }

        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("打开构建模式窗口"), false,
            () => EditorApplication.ExecuteMenuItem("Build/构建模式窗口"));
        menu.DropDown(dropDownRect);
    }
}

#endif
