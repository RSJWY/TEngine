using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using TEngine;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor
{
    [CustomEditor(typeof(AOTMetadataManifest), true)]
    public class AOTMetadataManifestEditor : OdinEditor
    {
        private AOTMetadataManifest Manifest => (AOTMetadataManifest)target;

        protected override void OnEnable()
        {
            base.OnEnable();
        }

        public override void OnInspectorGUI()
        {
            DrawToolbar();
            SirenixEditorGUI.DrawThickHorizontalSeparator();
            EditorGUILayout.Space(4);
            base.OnInspectorGUI();
        }

        private void DrawToolbar()
        {
            SirenixEditorGUI.MessageBox(
                "编辑下方列表后，点击「同步到 JSON」生成打包用的 .json.bytes。\n" +
                "运行时（归档管线与非归档管线）统一从 .json.bytes 加载。",
                MessageType.Info);

            EditorGUILayout.Space(2);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("同步到 JSON", EditorStyles.toolbarButton))
                {
                    WriteJsonBytes();
                }

                if (GUILayout.Button("从 HybridCLR 同步", EditorStyles.toolbarButton))
                {
                    BuildDLLCommand.SyncAOTMetadataManifest();
                    GUIUtility.ExitGUI();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("定位资产", EditorStyles.toolbarButton))
                {
                    EditorGUIUtility.PingObject(target);
                }
            }
        }

        private void WriteJsonBytes()
        {
            string assetPath = Settings.UpdateSetting.GetAOTMetadataManifestBytesAssetPath();
            string fullPath = Application.dataPath + "/" + assetPath.Substring("Assets/".Length);
            string dir = System.IO.Path.GetDirectoryName(fullPath);
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(fullPath, Manifest.ToJson());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AOTMetadata] JSON 资产已写入：{assetPath}");
            EditorUtility.DisplayDialog("同步完成", $"JSON 资产已写入：\n{assetPath}", "确定");
        }
    }
}
