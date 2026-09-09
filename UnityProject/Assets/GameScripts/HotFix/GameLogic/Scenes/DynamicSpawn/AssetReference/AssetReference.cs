using System;
using UnityEngine;
using YooAsset;

/// <summary>
/// AssetReference
/// 功能描述：资源弱引用基类——序列化"包裹名 + 资源 GUID"（非对象引用），运行时按需加载、主动释放。
/// 来源：YooAsset 3.0 扩展示例（Extension Sample），按需适配进 GameLogic 命名空间。
/// 创建时间：2026-07-02
/// 开发者：lzx
/// </summary>

namespace GameLogic
{
    /// <summary>
    /// 资源弱引用基类。只保存定位信息（包裹名 + GUID），不持有对象引用，
    /// 因此序列化它的预制体/场景<b>不会</b>与目标资源产生 Bundle 依赖。
    /// </summary>
    /// <remarks>
    /// <para>运行时加载见 <see cref="LoadAssetAsync"/>，不再使用时必须 <see cref="ReleaseAsset"/>。</para>
    /// <para>同一引用重复加载会抛异常，需先释放再重新加载。</para>
    /// </remarks>
    [Serializable]
    public abstract class AssetReference
    {
        [SerializeField]
        protected string _packageName = "DefaultPackage";

        [SerializeField]
        protected string _assetGUID = "";

        [NonSerialized]
        protected AssetHandle _handle;

        /// <summary>
        /// 资源所属的包裹名称
        /// </summary>
        public string PackageName => _packageName;

        /// <summary>
        /// 资源 GUID
        /// </summary>
        public string AssetGUID => _assetGUID;

        /// <summary>
        /// 当前加载句柄（未加载时为 null）
        /// </summary>
        public AssetHandle Handle => _handle;

        /// <summary>
        /// 该引用负责加载的资源类型，由子类指定
        /// </summary>
        public abstract Type AssetType { get; }

        /// <summary>
        /// 检查运行时引用键是否有效（资源已被收集进对应包裹）
        /// </summary>
        public bool RuntimeKeyIsValid()
        {
            if (string.IsNullOrEmpty(_packageName) || string.IsNullOrEmpty(_assetGUID))
                return false;

            var package = YooAssets.GetPackage(_packageName);
            if (package == null)
                return false;

            var assetInfo = package.GetAssetInfoByGuid(_assetGUID, AssetType);
            return assetInfo.IsValid;
        }

        /// <summary>
        /// 异步加载引用的资源
        /// </summary>
        /// <returns>加载操作句柄</returns>
        public AssetHandle LoadAssetAsync()
        {
            if (_handle != null)
                throw new InvalidOperationException($"{GetType().Name} has already been loaded. Release it first.");

            if (string.IsNullOrEmpty(_packageName))
                throw new ArgumentException("Package name is not set.", nameof(_packageName));
            if (string.IsNullOrEmpty(_assetGUID))
                throw new ArgumentException("Asset GUID is not set.", nameof(_assetGUID));

            var package = YooAssets.GetPackage(_packageName);
            var assetInfo = package.GetAssetInfoByGuid(_assetGUID, AssetType);
            _handle = package.LoadAssetAsync(assetInfo);
            return _handle;
        }

        /// <summary>
        /// 释放已加载的资源句柄
        /// </summary>
        public void ReleaseAsset()
        {
            if (_handle == null)
                return;

            _handle.Release();
            _handle = null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器辅助：写回资源 GUID（编辑器工具/迁移代码专用，运行时不要调用）。
        /// </summary>
        public void EditorSetAssetGUID(string guid)
        {
            _assetGUID = guid ?? string.Empty;
        }

        /// <summary>
        /// 编辑器辅助：写回包裹名（迁移代码专用，运行时不要调用）。
        /// </summary>
        public void EditorSetPackageName(string packageName)
        {
            _packageName = string.IsNullOrEmpty(packageName) ? "DefaultPackage" : packageName;
        }
#endif
    }
}
