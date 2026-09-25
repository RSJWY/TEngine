using System;
using UnityEngine;

/// <summary>
/// AssetReferenceGameObject
/// 功能描述：GameObject（预制体）资源弱引用。
/// 创建时间：2026-07-02
/// 开发者：lzx
/// </summary>

namespace GameLogic
{
    /// <summary>
    /// GameObject 资源弱引用。
    /// </summary>
    [Serializable]
    public class AssetReferenceGameObject : AssetReference
    {
        public override Type AssetType => typeof(GameObject);
    }
}
