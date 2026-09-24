using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 加密密钥配置窗口：集中管理 Bundle/Manifest 加密密钥，烘焙到代码常量。
    /// 菜单：Build/加密密钥配置
    /// </summary>
    public class CryptoKeyConfigWindow : OdinEditorWindow
    {
        private const string MenuPath = "Build/加密密钥配置";
        private const string KeyStorePath = "Assets/TEngine/CryptoKeys/KeyStore.cs";

        [MenuItem(MenuPath, false, 50)]
        public static void ShowWindow()
        {
            var window = GetWindow<CryptoKeyConfigWindow>();
            window.titleContent = new GUIContent("加密密钥配置");
            window.minSize = new Vector2(640, 520);
            window.Show();
        }

        [TabGroup("Pages", "Bundle 密钥")]
        [BoxGroup("Pages/Bundle 密钥/ChaCha20")]
        [InfoBox("Bundle ChaCha20 加密：32 字节 key + 12 字节 nonce。修改后需重新打包全部资源并烘焙密钥到代码。", InfoMessageType.None)]
        [ShowInInspector]
        [InlineEditor(InlineEditorModes.GUIAndHeader, InlineEditorObjectFieldModes.Hidden)]
        [OnInspectorInit(nameof(InitBundleChaCha20))]
        private BundleChaCha20KeyConfig BundleChaCha20;

        [TabGroup("Pages", "Bundle 密钥")]
        [BoxGroup("Pages/Bundle 密钥/XOR")]
        [InfoBox("Bundle XOR 加密：16~128 字节随机 key，按文件位置取模使用。修改后需重新打包全部资源并烘焙密钥到代码。", InfoMessageType.None)]
        [ShowInInspector]
        [InlineEditor(InlineEditorModes.GUIAndHeader, InlineEditorObjectFieldModes.Hidden)]
        [OnInspectorInit(nameof(InitBundleXor))]
        private BundleXorKeyConfig BundleXor;

        [TabGroup("Pages", "Manifest 密钥")]
        [BoxGroup("Pages/Manifest 密钥/ChaCha20")]
        [InfoBox("资源清单 ChaCha20 加密：32 字节 key + 12 字节 nonce。与 Bundle 密钥独立存放。修改后需重新打包全部资源并烘焙密钥到代码。", InfoMessageType.None)]
        [ShowInInspector]
        [InlineEditor(InlineEditorModes.GUIAndHeader, InlineEditorObjectFieldModes.Hidden)]
        [OnInspectorInit(nameof(InitManifestChaCha20))]
        private ManifestChaCha20KeyConfig ManifestChaCha20;

        private void InitBundleChaCha20() => BundleChaCha20 ??= BundleChaCha20KeyConfig.Instance;
        private void InitBundleXor() => BundleXor ??= BundleXorKeyConfig.Instance;
        private void InitManifestChaCha20() => ManifestChaCha20 ??= ManifestChaCha20KeyConfig.Instance;

        [TabGroup("Pages", "烘焙")]
        [BoxGroup("Pages/烘焙/操作")]
        [InfoBox(
            "烘焙将当前 .asset 中的密钥值写入 Assets/TEngine/CryptoKeys/KeyStore.cs 的代码常量。\n" +
            "烘焙后密钥不再随 Resources 打入运行时包，而以代码形式编入 DLL，配合 Obfuz FieldEncrypt 保护。\n\n" +
            "工作流：\n" +
            "1. 在此面板编辑/重新生成密钥\n" +
            "2. 点击「烘焙密钥到代码」\n" +
            "3. 重新打包资源（使新密钥生效于 Bundle/Manifest）\n" +
            "4. 构建玩家（Obfuz 在构建时加密 DLL 中的密钥字段）",
            InfoMessageType.None)]
        [GUIColor(0.45f, 0.85f, 0.45f)]
        [Button("烘焙密钥到代码", ButtonSizes.Large)]
        private void BakeKeys()
        {
            string generatedCode = GenerateKeyStoreCode();
            string existingCode = File.Exists(KeyStorePath) ? File.ReadAllText(KeyStorePath, Encoding.UTF8) : null;

            if (existingCode == generatedCode)
            {
                Debug.Log("[CryptoKeyConfig] 密钥未变更，跳过写入。");
                return;
            }

            File.WriteAllText(KeyStorePath, generatedCode, Encoding.UTF8);
            AssetDatabase.ImportAsset(KeyStorePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            Debug.Log($"[CryptoKeyConfig] 密钥已烘焙到 {KeyStorePath}");
        }

        [TabGroup("Pages", "烘焙")]
        [BoxGroup("Pages/烘焙/状态")]
        [ShowInInspector, ReadOnly]
        [TableList(ShowIndexLabels = false, AlwaysExpanded = true, IsReadOnly = true)]
        private List<BakeStatusItem> BakeStatus => BuildBakeStatus();

        private sealed class BakeStatusItem
        {
            [TableColumnWidth(160, Resizable = true)]
            [LabelText("密钥")]
            public string Name;

            [TableColumnWidth(80, Resizable = false)]
            [LabelText("状态")]
            [GUIColor("$Color")]
            public string State;

            [HideLabel]
            public string Status;

            public Color Color => State == "已烘焙" ? new Color(0.45f, 0.85f, 0.45f)
                : State == "全零" ? new Color(0.9f, 0.4f, 0.35f)
                : new Color(0.95f, 0.7f, 0.25f);
        }

        private List<BakeStatusItem> BuildBakeStatus()
        {
            var report = new List<BakeStatusItem>();
            void Add(string name, byte[] assetValue, byte[] codeValue)
            {
                bool codeIsZero = codeValue != null && IsAllZero(codeValue);
                bool assetIsZero = assetValue != null && IsAllZero(assetValue);
                bool matched = codeValue != null && assetValue != null && BytesEqual(codeValue, assetValue);

                string state = matched ? "已烘焙" : codeIsZero ? "全零" : "不一致";
                string status = $"Asset: {ToHex(assetValue)}\nCode:  {ToHex(codeValue)}";
                report.Add(new BakeStatusItem { Name = name, Status = status, State = state });
            }

            Add("Bundle ChaCha20 Key", BundleChaCha20KeyConfig.Instance.key, KeyStore.BundleChaCha20Key);
            Add("Bundle ChaCha20 Nonce", BundleChaCha20KeyConfig.Instance.nonce, KeyStore.BundleChaCha20Nonce);
            Add("Bundle XOR Key", BundleXorKeyConfig.Instance.key, KeyStore.BundleXorKey);
            Add("Manifest ChaCha20 Key", ManifestChaCha20KeyConfig.Instance.key, KeyStore.ManifestChaCha20Key);
            Add("Manifest ChaCha20 Nonce", ManifestChaCha20KeyConfig.Instance.nonce, KeyStore.ManifestChaCha20Nonce);
            return report;
        }

        private static bool IsAllZero(byte[] array)
        {
            if (array == null) return true;
            foreach (byte b in array) { if (b != 0) return false; }
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) return false; }
            return true;
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "(空)";
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        private static string FormatByteArray(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "new byte[0]";

            var sb = new StringBuilder();
            sb.Append("new byte[]\r\n            {\r\n                ");
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append($"0x{bytes[i]:X2}");
                if (i < bytes.Length - 1)
                    sb.Append(", ");
                if ((i + 1) % 8 == 0 && i < bytes.Length - 1)
                    sb.Append("\r\n                ");
            }
            sb.Append("\r\n            }");
            return sb.ToString();
        }

        private static string GenerateKeyStoreCode()
        {
            var bundle = BundleChaCha20KeyConfig.Instance;
            var xor = BundleXorKeyConfig.Instance;
            var manifest = ManifestChaCha20KeyConfig.Instance;

            var sb = new StringBuilder();
            sb.AppendLine("namespace TEngine");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 运行时密钥存储。密钥值通过 Editor 烘焙脚本从 ScriptableObject 资产写入此处，");
            sb.AppendLine("    /// 以代码常量形式编入 DLL，配合 Obfuz FieldEncrypt 加密字段值。");
            sb.AppendLine("    /// <remarks>");
            sb.AppendLine("    /// 注意：修改密钥后必须执行菜单 Build/加密密钥配置 中的\"烘焙密钥到代码\"，");
            sb.AppendLine("    /// 再重新编译，新的密钥才会生效。");
            sb.AppendLine("    /// </remarks>");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class KeyStore");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>Bundle ChaCha20 密钥（32 字节）。与清单密钥独立存放。</summary>");
            sb.AppendLine($"        public static readonly byte[] BundleChaCha20Key = {FormatByteArray(bundle.key)};");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Bundle ChaCha20 Nonce（12 字节）。</summary>");
            sb.AppendLine($"        public static readonly byte[] BundleChaCha20Nonce = {FormatByteArray(bundle.nonce)};");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Bundle XOR 密钥（16~128 字节）。</summary>");
            sb.AppendLine($"        public static readonly byte[] BundleXorKey = {FormatByteArray(xor.key)};");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>资源清单 ChaCha20 密钥（32 字节）。与 Bundle 密钥独立存放。</summary>");
            sb.AppendLine($"        public static readonly byte[] ManifestChaCha20Key = {FormatByteArray(manifest.key)};");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>资源清单 ChaCha20 Nonce（12 字节）。</summary>");
            sb.AppendLine($"        public static readonly byte[] ManifestChaCha20Nonce = {FormatByteArray(manifest.nonce)};");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
