using System.IO;
using UnityEngine;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 在构建输出目录额外生成 BuiltinCatalog 的工具。
    /// <para>依赖 YooAsset 3.0.6+ 的友元程序集声明（YooAsset.Custom.Editor），
    /// 可直接访问 internal 的 BuiltinCatalogHelper。</para>
    /// </summary>
    public static class CatalogOutputHelper
    {
        /// <summary>
        /// 在指定目录生成 BuiltinCatalog 文件（BuiltinCatalog.bytes + BuiltinCatalog.json）。
        /// </summary>
        /// <param name="decryptor">清单解密器；清单未加密时传 null。</param>
        /// <param name="packageName">资源包名。</param>
        /// <param name="packageDirectory">构建输出目录（含 .version 与 manifest .bytes）。</param>
        /// <returns>是否生成成功。</returns>
        public static bool GenerateCatalog(IManifestDecryptor decryptor, string packageName, string packageDirectory)
        {
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(packageDirectory))
            {
                Debug.LogError("[CatalogOutputHelper] packageName 或 packageDirectory 为空。");
                return false;
            }

            if (!Directory.Exists(packageDirectory))
            {
                Debug.LogError($"[CatalogOutputHelper] 目录不存在: {packageDirectory}");
                return false;
            }

            bool result = BuiltinCatalogHelper.CreateFile(decryptor, packageName, packageDirectory);
            if (result)
            {
                Debug.Log($"[CatalogOutputHelper] 已在构建输出目录生成 BuiltinCatalog: {packageDirectory}");
            }
            else
            {
                Debug.LogError($"[CatalogOutputHelper] BuiltinCatalog 生成失败: {packageDirectory}");
            }

            return result;
        }
    }
}
