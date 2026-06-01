using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor
{
    public sealed class HotUpdatePlayerPrefsTool : EditorWindow
    {
        private const string DefaultPackageName = "DefaultPackage";
        private const string DefaultCodePackageName = "CodePackage";
        private const string DefaultGameVersionKey = "GAME_VERSION";
        private const string DefaultCodeVersionKey = "CODE_VERSION";

        private readonly List<PackageVersionInfo> _packages = new List<PackageVersionInfo>();
        private readonly Dictionary<string, bool> _selectedRows = new Dictionary<string, bool>();
        private Vector2 _scrollPosition;
        private UpdateSetting _updateSetting;

        [MenuItem("TEngine/HotUpdate/Package Version PlayerPrefs", false, 20)]
        public static void OpenWindow()
        {
            var window = GetWindow<HotUpdatePlayerPrefsTool>("Package Versions");
            window.minSize = new Vector2(760f, 360f);
            window.RefreshPackages();
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(760f, 360f);
            RefreshPackages();
        }

        private void OnFocus()
        {
            RefreshCurrentValues();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("HotUpdate Package Version PlayerPrefs", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("自动读取 UpdateSetting.RuntimePackages 中每个包的 VersionKey，只清理这些版本记录，不会清理其它 PlayerPrefs 数据。", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _updateSetting = (UpdateSetting)EditorGUILayout.ObjectField("UpdateSetting", _updateSetting, typeof(UpdateSetting), false);
                if (EditorGUI.EndChangeCheck())
                {
                    RefreshPackages();
                }

                if (_updateSetting != null && GUILayout.Button("定位", GUILayout.Width(60f)))
                {
                    Selection.activeObject = _updateSetting;
                    EditorGUIUtility.PingObject(_updateSetting);
                }
            }

            DrawToolbar();
            DrawPackageList();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新", GUILayout.Width(70f)))
                {
                    RefreshPackages();
                }

                if (GUILayout.Button("选中有记录", GUILayout.Width(90f)))
                {
                    SelectRowsWithValue();
                }

                if (GUILayout.Button("全选", GUILayout.Width(60f)))
                {
                    SelectAllRows(true);
                }

                if (GUILayout.Button("取消选择", GUILayout.Width(80f)))
                {
                    SelectAllRows(false);
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(GetSelectedKeys().Count == 0))
                {
                    if (GUILayout.Button("清理选中", GUILayout.Width(90f)))
                    {
                        ClearSelectedKeys();
                    }
                }

                using (new EditorGUI.DisabledScope(_packages.Count == 0))
                {
                    if (GUILayout.Button("清理全部", GUILayout.Width(90f)))
                    {
                        ClearAllKeys();
                    }
                }
            }
        }

        private void DrawPackageList()
        {
            if (_packages.Count == 0)
            {
                EditorGUILayout.HelpBox("没有读取到资源包版本记录配置。", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("选择", EditorStyles.boldLabel, GUILayout.Width(40f));
                GUILayout.Label("PackageName", EditorStyles.boldLabel, GUILayout.Width(150f));
                GUILayout.Label("启用", EditorStyles.boldLabel, GUILayout.Width(45f));
                GUILayout.Label("保存", EditorStyles.boldLabel, GUILayout.Width(45f));
                GUILayout.Label("VersionKey", EditorStyles.boldLabel, GUILayout.Width(190f));
                GUILayout.Label("PlayerPrefs 当前值", EditorStyles.boldLabel, GUILayout.MinWidth(180f));
                GUILayout.Label("操作", EditorStyles.boldLabel, GUILayout.Width(60f));
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            foreach (var package in _packages)
            {
                DrawPackageRow(package);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPackageRow(PackageVersionInfo package)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var selected = _selectedRows.ContainsKey(package.RowKey) && _selectedRows[package.RowKey];
                _selectedRows[package.RowKey] = EditorGUILayout.Toggle(selected, GUILayout.Width(40f));

                GUILayout.Label(package.PackageName, GUILayout.Width(150f));
                GUILayout.Label(package.Enable ? "是" : "否", GUILayout.Width(45f));
                GUILayout.Label(package.SaveVersion ? "是" : "否", GUILayout.Width(45f));

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(package.VersionKey, GUILayout.Width(190f));
                    EditorGUILayout.TextField(GetDisplayValue(package), GUILayout.MinWidth(180f));
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(package.VersionKey)))
                {
                    if (GUILayout.Button("清理", GUILayout.Width(60f)))
                    {
                        ConfirmAndClear(new[] { package.VersionKey }, $"Clear {package.PackageName}");
                        RefreshCurrentValues();
                    }
                }
            }
        }

        private void RefreshPackages()
        {
            _updateSetting = _updateSetting == null ? LoadUpdateSetting() : _updateSetting;
            var previousSelections = new Dictionary<string, bool>(_selectedRows);
            _selectedRows.Clear();
            _packages.Clear();

            var packages = GetPackageVersionInfos(_updateSetting);
            for (var i = 0; i < packages.Count; i++)
            {
                var package = packages[i];
                package.RowKey = $"{i}:{package.PackageName}:{package.VersionKey}";
                _packages.Add(package);
                _selectedRows[package.RowKey] = previousSelections.ContainsKey(package.RowKey) && previousSelections[package.RowKey];
            }

            RefreshCurrentValues();
        }

        private void RefreshCurrentValues()
        {
            foreach (var package in _packages)
            {
                if (string.IsNullOrWhiteSpace(package.VersionKey))
                {
                    package.HasValue = false;
                    package.CurrentValue = string.Empty;
                    continue;
                }

                package.HasValue = Utility.PlayerPrefs.HasKey(package.VersionKey);
                package.CurrentValue = package.HasValue ? Utility.PlayerPrefs.GetString(package.VersionKey, string.Empty) : string.Empty;
            }
        }

        private void SelectRowsWithValue()
        {
            foreach (var package in _packages)
            {
                _selectedRows[package.RowKey] = package.HasValue;
            }
        }

        private void SelectAllRows(bool selected)
        {
            foreach (var package in _packages)
            {
                _selectedRows[package.RowKey] = selected && !string.IsNullOrWhiteSpace(package.VersionKey);
            }
        }

        private void ClearSelectedKeys()
        {
            ConfirmAndClear(GetSelectedKeys(), "Clear Selected Package Versions");
            RefreshCurrentValues();
        }

        private void ClearAllKeys()
        {
            ConfirmAndClear(GetAllWindowKeys(), "Clear All Package Versions");
            RefreshCurrentValues();
        }

        private HashSet<string> GetSelectedKeys()
        {
            var keys = new HashSet<string>();
            foreach (var package in _packages)
            {
                if (_selectedRows.ContainsKey(package.RowKey) && _selectedRows[package.RowKey] && !string.IsNullOrWhiteSpace(package.VersionKey))
                {
                    keys.Add(package.VersionKey.Trim());
                }
            }

            return keys;
        }

        private HashSet<string> GetAllWindowKeys()
        {
            var keys = new HashSet<string>();
            foreach (var package in _packages)
            {
                if (!string.IsNullOrWhiteSpace(package.VersionKey))
                {
                    keys.Add(package.VersionKey.Trim());
                }
            }

            return keys;
        }

        private static UpdateSetting LoadUpdateSetting()
        {
            if (Settings.Instance != null)
            {
                return Settings.UpdateSetting;
            }

            var guids = AssetDatabase.FindAssets("t:UpdateSetting");
            if (guids.Length == 0)
            {
                return null;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<UpdateSetting>(path);
        }

        private static List<PackageVersionInfo> GetPackageVersionInfos(UpdateSetting updateSetting)
        {
            var packages = new List<PackageVersionInfo>();
            if (updateSetting == null)
            {
                packages.Add(CreateFallbackPackage(DefaultPackageName, DefaultGameVersionKey));
                packages.Add(CreateFallbackPackage(DefaultCodePackageName, DefaultCodeVersionKey));
                return packages;
            }

            var runtimePackages = updateSetting.RuntimePackages;
            if (runtimePackages == null || runtimePackages.Count == 0)
            {
                runtimePackages = updateSetting.GetEnabledRuntimePackages();
            }

            foreach (var runtimePackage in runtimePackages)
            {
                if (runtimePackage == null)
                {
                    continue;
                }

                var packageName = string.IsNullOrWhiteSpace(runtimePackage.PackageName) ? "<Empty PackageName>" : runtimePackage.PackageName.Trim();
                var versionKey = ResolveVersionKey(updateSetting, runtimePackage);
                packages.Add(new PackageVersionInfo
                {
                    PackageName = packageName,
                    Enable = runtimePackage.Enable,
                    SaveVersion = runtimePackage.SaveVersion,
                    VersionKey = versionKey,
                });
            }

            return packages;
        }

        private static string ResolveVersionKey(UpdateSetting updateSetting, RuntimePackageEntry runtimePackage)
        {
            if (!string.IsNullOrWhiteSpace(runtimePackage.VersionKey))
            {
                return runtimePackage.VersionKey.Trim();
            }

            if (updateSetting == null || string.IsNullOrWhiteSpace(runtimePackage.PackageName))
            {
                return string.Empty;
            }

            return updateSetting.GetVersionKey(runtimePackage.PackageName.Trim());
        }

        private static PackageVersionInfo CreateFallbackPackage(string packageName, string versionKey)
        {
            return new PackageVersionInfo
            {
                PackageName = packageName,
                Enable = true,
                SaveVersion = true,
                VersionKey = versionKey,
            };
        }

        private static void ConfirmAndClear(IEnumerable<string> keys, string title)
        {
            var normalizedKeys = NormalizeKeys(keys);
            if (normalizedKeys.Count == 0)
            {
                EditorUtility.DisplayDialog(title, "没有找到需要清理的资源包版本记录。", "OK");
                return;
            }

            var keyText = string.Join("\n", normalizedKeys);
            if (!EditorUtility.DisplayDialog(title, $"将清理以下 PlayerPrefs 版本记录：\n\n{keyText}\n\n其它 PlayerPrefs 数据不会被删除。", "Clear", "Cancel"))
            {
                return;
            }

            var clearedKeys = ClearKeys(normalizedKeys);
            EditorUtility.DisplayDialog(title, $"已清理版本记录：\n\n{string.Join("\n", clearedKeys)}", "OK");
        }

        private static HashSet<string> NormalizeKeys(IEnumerable<string> keys)
        {
            var normalizedKeys = new HashSet<string>();
            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    normalizedKeys.Add(key.Trim());
                }
            }

            return normalizedKeys;
        }

        private static List<string> ClearKeys(IEnumerable<string> keys)
        {
            var clearedKeys = new List<string>();
            foreach (var key in keys)
            {
                var existed = Utility.PlayerPrefs.HasKey(key);
                var value = existed ? Utility.PlayerPrefs.GetString(key, string.Empty) : string.Empty;
                Utility.PlayerPrefs.DeleteKey(key);
                clearedKeys.Add(existed ? $"{key} = {GetLogValue(value)}" : $"{key} (not found)");
            }

            Utility.PlayerPrefs.Save();
            Debug.Log($"[HotUpdatePlayerPrefsTool] Cleared package version PlayerPrefs keys: {string.Join(", ", clearedKeys)}");
            return clearedKeys;
        }

        private static string GetDisplayValue(PackageVersionInfo package)
        {
            if (!package.HasValue)
            {
                return "<未记录>";
            }

            return string.IsNullOrEmpty(package.CurrentValue) ? "<空字符串>" : package.CurrentValue;
        }

        private static string GetLogValue(string value)
        {
            return string.IsNullOrEmpty(value) ? "<empty>" : value;
        }

        private sealed class PackageVersionInfo
        {
            public string RowKey;
            public string PackageName;
            public bool Enable;
            public bool SaveVersion;
            public string VersionKey;
            public bool HasValue;
            public string CurrentValue;
        }
    }
}
