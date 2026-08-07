using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TEngine.SceneTools
{
    /// <summary>
    /// 场景资源扫描与配置同步工具。
    /// </summary>
    public static class SceneEnumSyncUtil
    {
        /// <summary>
        /// 扫描配置目录，同步场景列表：
        /// <para>① 新增场景追加（枚举名默认=文件名清洗，枚举值=max+1）；</para>
        /// <para>② 已删除/移出目录的场景标记 Active=false（枚举值保留占位）；</para>
        /// <para>③ 改名场景按 GUID 识别，刷新 SceneAsset 引用，枚举名/值不变（资源地址自动跟随）。</para>
        /// </summary>
        public static void SyncScenes(SceneEnumConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[场景枚举] 配置为空");
                return;
            }

            // 优先从 YooAsset 配置读 Scenes Group 收集目录（联动资源打包配置，避免脱节）
            List<string> scanFolders = YooAssetCollectorReader.GetCollectPaths();
            if (scanFolders.Count == 0)
            {
                string fallback = string.IsNullOrEmpty(config.SceneFolder) ? SceneEnumConfig.DefaultSceneFolder : config.SceneFolder;
                scanFolders.Add(fallback);
                Debug.LogWarning($"[场景枚举] 未找到 YooAsset Scenes Group 收集配置，回退使用：{fallback}");
            }

            List<SceneInfo> dirScenes = new List<SceneInfo>();
            foreach (string f in scanFolders)
            {
                dirScenes.AddRange(ScanScenes(f));
            }
            Debug.Log($"[场景枚举] 扫描目录：{string.Join(", ", scanFolders)}，发现 {dirScenes.Count} 个场景");

            List<SceneEnumConfig.SceneEntry> entries = config.Scenes;
            HashSet<string> matchedGuids = new HashSet<string>();

            // 1. 处理已有条目：检测删除/改名，刷新引用
            foreach (SceneEnumConfig.SceneEntry entry in entries)
            {
                if (string.IsNullOrEmpty(entry.SceneGuid) && entry.SceneAsset != null)
                {
                    entry.SceneGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry.SceneAsset));
                }

                if (string.IsNullOrEmpty(entry.SceneGuid))
                {
                    continue;
                }

                matchedGuids.Add(entry.SceneGuid);

                SceneInfo dirMatch = dirScenes.FirstOrDefault(s => s.guid == entry.SceneGuid);
                if (dirMatch == null)
                {
                    if (entry.Active)
                    {
                        entry.Active = false;
                        Debug.Log($"[场景枚举] 场景已删除或移出目录：{entry.EnumName}（GUID={entry.SceneGuid}），标记停用，枚举值 {entry.EnumValue} 保留占位");
                    }
                    continue;
                }

                // 在目录：检测改名（GUID 相同但文件名变化）
                string oldName = entry.SceneAsset != null ? entry.SceneAsset.name : null;
                if (oldName != null && oldName != dirMatch.name)
                {
                    Debug.Log($"[场景枚举] 场景改名：{oldName} -> {dirMatch.name}（枚举 {entry.EnumName} 不变，资源地址将自动跟随新文件名）");
                }

                entry.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(dirMatch.path);

                if (!entry.Active)
                {
                    entry.Active = true;
                    Debug.Log($"[场景枚举] 场景恢复：{entry.EnumName}（{dirMatch.name}）");
                }
            }

            // 2. 追加新场景：目录有但配置无
            int maxEnumValue = entries.Count > 0 ? entries.Max(e => e.EnumValue) : -1;
            HashSet<string> usedEnumNames = new HashSet<string>(entries.Where(e => !string.IsNullOrEmpty(e.EnumName)).Select(e => e.EnumName));

            foreach (SceneInfo dirScene in dirScenes)
            {
                if (matchedGuids.Contains(dirScene.guid))
                {
                    continue;
                }

                maxEnumValue++;
                string enumName = EnsureUnique(CleanEnumName(dirScene.name), usedEnumNames);
                usedEnumNames.Add(enumName);

                entries.Add(new SceneEnumConfig.SceneEntry
                {
                    SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(dirScene.path),
                    SceneGuid = dirScene.guid,
                    EnumName = enumName,
                    DisplayName = dirScene.name,
                    EnumValue = maxEnumValue,
                    Active = true,
                });
                Debug.Log($"[场景枚举] 新增场景：{dirScene.name} -> 枚举 {enumName} = {maxEnumValue}");
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log($"[场景枚举] 同步完成：共 {entries.Count} 条（启用 {entries.Count(e => e.Active)} 条）");
        }

        /// <summary>
        /// 扫描目录下所有 .unity 场景（含子目录）。
        /// </summary>
        public static List<SceneInfo> ScanScenes(string folder)
        {
            List<SceneInfo> result = new List<SceneInfo>();
            if (string.IsNullOrEmpty(folder))
            {
                return result;
            }

            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { folder });
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }
                result.Add(new SceneInfo
                {
                    guid = g,
                    path = path,
                    name = System.IO.Path.GetFileNameWithoutExtension(path),
                });
            }
            return result;
        }

        /// <summary>
        /// 把场景文件名清洗为合法 C# 标识符：非法字符转 _，数字开头加 _。
        /// </summary>
        public static string CleanEnumName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return "Scene";
            }

            StringBuilder sb = new StringBuilder();
            foreach (char ch in rawName)
            {
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            string result = sb.ToString();
            if (result.Length == 0)
            {
                return "Scene";
            }

            if (!char.IsLetter(result[0]) && result[0] != '_')
            {
                result = "_" + result;
            }

            return result;
        }

        private static string EnsureUnique(string name, HashSet<string> used)
        {
            if (!used.Contains(name))
            {
                return name;
            }
            int i = 2;
            while (used.Contains($"{name}_{i}"))
            {
                i++;
            }
            return $"{name}_{i}";
        }

        public class SceneInfo
        {
            public string guid;
            public string path;
            public string name;
        }
    }
}
