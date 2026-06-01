using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor
{
    public static class HotUpdatePlayerPrefsTool
    {
        private const string DefaultGameVersionKey = "GAME_VERSION";
        private const string DefaultCodeVersionKey = "CODE_VERSION";

        [MenuItem("TEngine/HotUpdate/Clear Saved Package Versions", false, 20)]
        public static void ClearSavedPackageVersions()
        {
            var keys = GetSavedPackageVersionKeys();
            if (keys.Count == 0)
            {
                EditorUtility.DisplayDialog("Clear Saved Package Versions", "没有找到需要清理的资源包版本记录。", "OK");
                return;
            }

            var keyText = string.Join("\n", keys);
            if (!EditorUtility.DisplayDialog("Clear Saved Package Versions", $"将清理以下 PlayerPrefs 版本记录：\n\n{keyText}\n\n其它 PlayerPrefs 数据不会被删除。", "Clear", "Cancel"))
            {
                return;
            }

            ClearKeys(keys);
        }

        [MenuItem("TEngine/HotUpdate/Clear GAME_VERSION", false, 21)]
        public static void ClearGameVersion()
        {
            var key = GetGameVersionKey();
            if (!EditorUtility.DisplayDialog("Clear GAME_VERSION", $"将清理 PlayerPrefs 版本记录：\n\n{key}", "Clear", "Cancel"))
            {
                return;
            }

            ClearKeys(new HashSet<string> { key });
        }

        [MenuItem("TEngine/HotUpdate/Clear CODE_VERSION", false, 22)]
        public static void ClearCodeVersion()
        {
            var key = GetCodeVersionKey();
            if (!EditorUtility.DisplayDialog("Clear CODE_VERSION", $"将清理 PlayerPrefs 版本记录：\n\n{key}", "Clear", "Cancel"))
            {
                return;
            }

            ClearKeys(new HashSet<string> { key });
        }

        private static HashSet<string> GetSavedPackageVersionKeys()
        {
            var keys = new HashSet<string>();
            var updateSetting = Settings.UpdateSetting;
            if (updateSetting == null)
            {
                keys.Add(DefaultGameVersionKey);
                keys.Add(DefaultCodeVersionKey);
                return keys;
            }

            foreach (var runtimePackage in updateSetting.GetEnabledRuntimePackages())
            {
                if (runtimePackage == null || !runtimePackage.SaveVersion || string.IsNullOrWhiteSpace(runtimePackage.VersionKey))
                {
                    continue;
                }

                keys.Add(runtimePackage.VersionKey.Trim());
            }

            return keys;
        }

        private static string GetGameVersionKey()
        {
            var updateSetting = Settings.UpdateSetting;
            return updateSetting == null ? DefaultGameVersionKey : updateSetting.GetVersionKey(updateSetting.GetDefaultPackageName());
        }

        private static string GetCodeVersionKey()
        {
            var updateSetting = Settings.UpdateSetting;
            return updateSetting == null ? DefaultCodeVersionKey : updateSetting.GetVersionKey(updateSetting.GetAssemblyPackageName());
        }

        private static void ClearKeys(IEnumerable<string> keys)
        {
            var removedKeys = new List<string>();
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var versionKey = key.Trim();
                var existed = Utility.PlayerPrefs.HasKey(versionKey);
                Utility.PlayerPrefs.DeleteKey(versionKey);
                removedKeys.Add($"{versionKey}{(existed ? string.Empty : " (not found)")}");
            }

            Utility.PlayerPrefs.Save();
            Debug.Log($"[HotUpdatePlayerPrefsTool] Cleared package version PlayerPrefs keys: {string.Join(", ", removedKeys)}");
            EditorUtility.DisplayDialog("Clear Saved Package Versions", $"已清理版本记录：\n\n{string.Join("\n", removedKeys)}", "OK");
        }
    }
}
