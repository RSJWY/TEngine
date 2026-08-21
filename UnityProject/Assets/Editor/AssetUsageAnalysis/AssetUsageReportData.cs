using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AssetUsageReportData
/// 功能描述：
/// 创建时间：2026-08-21 14:17
/// 开发者：Administrator
/// 最后修改：
/// 修改内容：
/// </summary>

/// <summary>
/// AB包资产使用分析结果。
/// 由 AssetUsageAnalysisWindow 根据 YooAsset 构建报告（.report）生成，
/// 保存为 .asset 便于 SVN 提交跟踪。
/// </summary>
public class AssetUsageReportData : ScriptableObject
{
    [Serializable]
    public class AssetEntry
    {
        /// <summary>资源路径（Assets/ 或 Packages/ 开头）</summary>
        public string AssetPath;
        /// <summary>资源GUID</summary>
        public string AssetGUID;
        /// <summary>文件大小（字节，取自磁盘文件）</summary>
        public long FileSize;
        /// <summary>是否为可寻址主资产（仅对已用列表有意义）</summary>
        public bool IsMainAsset;
    }

    [Header("报告来源")]
    public string ReportFilePath;
    public string PackageName;
    public string PackageVersion;
    public string BuildDate;
    public string AnalyzeDate;

    [Header("统计")]
    public int UsedCount;
    public long UsedTotalSize;
    public int UnusedCount;
    public long UnusedTotalSize;

    [Header("已使用资产（打进AB包：主资产 + 依赖资产）")]
    public List<AssetEntry> UsedAssets = new List<AssetEntry>();

    [Header("未使用资产（AssetRaw 下存在但未打进AB包）")]
    public List<AssetEntry> UnusedAssets = new List<AssetEntry>();
}
