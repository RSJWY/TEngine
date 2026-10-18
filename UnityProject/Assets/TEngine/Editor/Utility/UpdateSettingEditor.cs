using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using HybridCLR.Settings;

namespace TEngine.Editor
{
    [CustomEditor(typeof(UpdateSetting), true)]
    public class UpdateSettingEditor : UnityEditor.Editor
    {
#if ENABLE_HYBRIDCLR
        public List<string> HotUpdateAssemblies = new() {};
        public List<string> AOTMetaAssemblies = new() {};
        
        private void OnEnable()
        {
            // 获取当前编辑的 ScriptableObject 实例
            UpdateSetting updateSetting = (UpdateSetting)target;
            if (updateSetting != null)
            {
                HotUpdateAssemblies.AddRange(updateSetting.HotUpdateAssemblies);
                AOTMetaAssemblies.AddRange(updateSetting.AOTMetaAssemblies);
            }
        }

        public override void OnInspectorGUI()
        {
            // 记录对象修改前的状态
            EditorGUI.BeginChangeCheck();

            // 记录 projectName / companyName / version 修改前的值，用于检测是否被改动
            UpdateSetting updateSetting = (UpdateSetting)target;
            string projectNameBefore = updateSetting != null ? updateSetting.GetProjectName() : null;
            string companyNameBefore = updateSetting != null ? updateSetting.GetCompanyName() : null;
            string versionBefore = updateSetting != null ? updateSetting.GetVersion() : null;

            // 绘制默认的 Inspector 界面
            base.OnInspectorGUI();

            // 检测是否有字段被修改
            if (EditorGUI.EndChangeCheck())
            {
                if (updateSetting == null)
                {
                    return;
                }

                // 标记对象为“已修改”，确保修改能被保存
                EditorUtility.SetDirty(updateSetting);

                // projectName / companyName / version 以 UpdateSetting 为数据源，改动时自动同步到 PlayerSettings
                bool isProjectNameChanged = !string.Equals(projectNameBefore, updateSetting.GetProjectName(), StringComparison.Ordinal);
                bool isCompanyNameChanged = !string.Equals(companyNameBefore, updateSetting.GetCompanyName(), StringComparison.Ordinal);
                bool isVersionChanged = !string.Equals(versionBefore, updateSetting.GetVersion(), StringComparison.Ordinal);
                if (isProjectNameChanged || isCompanyNameChanged || isVersionChanged)
                {
                    SyncProjectInfoToPlayerSettings(updateSetting);
                }

                bool isHotChanged = !HotUpdateAssemblies.SequenceEqual(updateSetting.HotUpdateAssemblies);
                bool isAOTChanged = !AOTMetaAssemblies.SequenceEqual(updateSetting.AOTMetaAssemblies);
                if (isHotChanged)
                {
                    HybridCLRSettings.instance.hotUpdateAssemblies = updateSetting.HotUpdateAssemblies.ToArray();
                    for (int i = 0; i < updateSetting.HotUpdateAssemblies.Count; i++)
                    {
                        var assemblyName = updateSetting.HotUpdateAssemblies[i];
                        string assemblyNameWithoutExtension = assemblyName.Substring(0, assemblyName.LastIndexOf('.'));
                        HybridCLRSettings.instance.hotUpdateAssemblies[i] = assemblyNameWithoutExtension;
                    }
                    Debug.Log("HotUpdateAssemblies changed");
                }
                if (isAOTChanged)
                {
                    // HybridCLR v9 起移除 HybridCLRSettings.patchAOTAssemblies，
                    // AOT 补充元数据列表以 UpdateSetting.AOTMetaAssemblies + AOTMetadataManifest 为准。
                    Debug.Log("AOTMetaAssemblies changed");
                }

                if (isAOTChanged || isHotChanged)
                {
                    // 在修改HybridCLRSettings后添加
                    EditorUtility.SetDirty(HybridCLRSettings.instance);
                    HybridCLRSettings.Save();
                    AssetDatabase.SaveAssets();
                }
            }
        }
#endif

        /// <summary>
        /// 把 UpdateSetting 的 projectName / companyName / version 同步到 PlayerSettings（UpdateSetting 为数据源）。
        /// version 对应 ProjectSettings 的 Version（PlayerSettings.bundleVersion）。
        /// </summary>
        public static void SyncProjectInfoToPlayerSettings(UpdateSetting updateSetting)
        {
            if (updateSetting == null)
            {
                return;
            }

            bool changed = false;

            var projectName = updateSetting.GetProjectName();
            if (!string.Equals(PlayerSettings.productName, projectName, StringComparison.Ordinal))
            {
                PlayerSettings.productName = projectName;
                changed = true;
            }

            var companyName = updateSetting.GetCompanyName();
            if (!string.Equals(PlayerSettings.companyName, companyName, StringComparison.Ordinal))
            {
                PlayerSettings.companyName = companyName;
                changed = true;
            }

            var version = updateSetting.GetVersion();
            if (!string.Equals(PlayerSettings.bundleVersion, version, StringComparison.Ordinal))
            {
                PlayerSettings.bundleVersion = version;
                changed = true;
            }

            if (changed)
            {
                Debug.Log($"[UpdateSetting] 已同步到 PlayerSettings：productName={projectName}，companyName={companyName}，bundleVersion={version}");
            }
        }

        public static void ForceUpdateAssemblies()
        {
            UpdateSetting updateSetting = null;
            string[] guids = AssetDatabase.FindAssets("t:UpdateSetting");
            if (guids.Length >= 1)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                updateSetting = AssetDatabase.LoadAssetAtPath<UpdateSetting>(path);
            }

            if (updateSetting == null)
            {
                Log.Error("Can not find UpdateSetting");
                return;
            }
            
            HybridCLRSettings.instance.hotUpdateAssemblies = updateSetting.HotUpdateAssemblies.ToArray();
            for (int i = 0; i < updateSetting.HotUpdateAssemblies.Count; i++)
            {
                var assemblyName = updateSetting.HotUpdateAssemblies[i];
                string assemblyNameWithoutExtension = assemblyName.Substring(0, assemblyName.LastIndexOf('.'));
                HybridCLRSettings.instance.hotUpdateAssemblies[i] = assemblyNameWithoutExtension;
            }
            
            HybridCLRSettings.Save();
            EditorUtility.SetDirty(HybridCLRSettings.instance);
            AssetDatabase.SaveAssets();
            
            Debug.Log("HotUpdateAssemblies changed");
        }
    }
}