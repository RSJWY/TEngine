using System;
using UnityEngine;

/// <summary>
/// 特写拍摄角度预设：存储相机相对于目标物体的位姿关系。
/// 可复用到不同大小/位置/朝向的目标上。
/// </summary>
[CreateAssetMenu(menuName = "Tools/特写拍摄角度预设", fileName = "NewCloseupAnglePreset")]
public class CloseupAnglePreset : ScriptableObject
{
    [Header("基本信息")]
    public string presetName = "新预设";

    [Header("相对位姿（目标局部空间）")]
    [Tooltip("相机相对于目标中心的方向（目标局部空间，归一化）")]
    public Vector3 relativeDirection = new Vector3(0f, 0f, -1f);

    [Tooltip("距离 = 包围盒半径 × 此倍率")]
    public float distanceMultiplier = 3f;

    [Tooltip("相机旋转相对于目标旋转的差值")]
    public Quaternion relativeRotation = Quaternion.identity;

    [Header("构图参数快照")]
    public float fieldOfView = 35f;
    public bool isOrthographic;
    public float orthographicSizeMultiplier = 1.15f; // 相对于包围盒半径
    public float padding = 1.15f;
}
