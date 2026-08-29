using System;
using TMPro;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// TMP 环形排字扩展（移植自 UITextCircleExtend，顶点单位从 UGUI 的 6 顶点流改为 TMP 的每字符 4 顶点）。
    /// 通过 OnPreRenderText 直接读写 meshInfo.vertices（Vector3[]）。
    /// 角度公式与 UGUI 版一致：angle = π/2 - centerX * spaceCoff / radius + angleOffset * Deg2Rad，
    /// 绕 Z 旋转贴圆 + verticalOffset = -radius + centerY。
    /// </summary>
    [Serializable]
    public class TMPCircleExtend
    {
        [SerializeField] private bool m_useTextCircle;
        [SerializeField, Range(100, 1000)] private float m_radius = 100;
        [SerializeField, Range(0, 10)] private float m_spaceCoff = 1f;
        [SerializeField, Range(0, 360)] private float m_angleOffset = 0;

        public bool UseTextCircle => m_useTextCircle;

        /// <summary>
        /// 开关环形排字。
        /// </summary>
        public void SetUseTextCircle(bool useTextCircle)
        {
            if (m_useTextCircle == useTextCircle)
            {
                return;
            }
            m_useTextCircle = useTextCircle;
            Refresh();
        }

        private TextMeshProUGUI m_text;

        public void Initialize(TextMeshProUGUI text)
        {
            m_text = text;
        }

        public void Refresh()
        {
            m_text?.SetVerticesDirty();
        }

        #region 顶点修改（OnPreRenderText 调用，红线：零分配、不调 Dirty、不手动上传 mesh）

        public void ModifyMesh(TMP_TextInfo textInfo)
        {
            if (!m_useTextCircle || m_radius <= 0 || textInfo == null)
            {
                return;
            }

            // 预先计算常用值
            float radiusReciprocal = 1f / m_radius;
            float angleOffsetRad = m_angleOffset * Mathf.Deg2Rad;
            float halfPI = Mathf.PI * 0.5f;
            int materialCount = textInfo.materialCount;

            // 多材质（fallback 字体/图混排）时 meshInfo 有多份，必须全部遍历
            for (int m = 0; m < materialCount; m++)
            {
                TMP_MeshInfo meshInfo = textInfo.meshInfo[m];
                Vector3[] vertices = meshInfo.vertices;
                int vertexCount = meshInfo.vertexCount;

                for (int i = 0; i + 4 <= vertexCount; i += 4)
                {
                    // 字符中心取 4 顶点位置的 (min + max) / 2
                    float minX = vertices[i].x;
                    float minY = vertices[i].y;
                    float maxX = minX;
                    float maxY = minY;
                    for (int j = 1; j < 4; j++)
                    {
                        Vector3 p = vertices[i + j];
                        if (p.x < minX) minX = p.x;
                        else if (p.x > maxX) maxX = p.x;
                        if (p.y < minY) minY = p.y;
                        else if (p.y > maxY) maxY = p.y;
                    }
                    float centerX = (minX + maxX) * 0.5f;
                    float centerY = (minY + maxY) * 0.5f;

                    // 预先计算角度
                    float angle = halfPI - (centerX * m_spaceCoff * radiusReciprocal) + angleOffsetRad;
                    float cosAngle = Mathf.Cos(angle);
                    float sinAngle = Mathf.Sin(angle);

                    // 目标位置
                    float targetX = cosAngle * m_radius;
                    float targetY = sinAngle * m_radius;

                    // 旋转角度（绕Z轴）
                    float rotationAngle = angle - halfPI;
                    float cosRot = Mathf.Cos(rotationAngle);
                    float sinRot = Mathf.Sin(rotationAngle);

                    // 垂直偏移
                    float verticalOffset = -m_radius + centerY;

                    // 直接计算变换后的位置，避免创建Matrix4x4
                    for (int j = 0; j < 4; j++)
                    {
                        Vector3 pos = vertices[i + j];

                        // Step 1: 平移到原点 (pos - center)
                        float localX = pos.x - centerX;
                        float localY = pos.y - centerY;

                        // Step 2: 绕Z轴旋转
                        float rotatedX = localX * cosRot - localY * sinRot;
                        float rotatedY = localX * sinRot + localY * cosRot;

                        // Step 3: 平移到目标位置 + 垂直偏移
                        pos.x = rotatedX + targetX;
                        pos.y = rotatedY + targetY + verticalOffset;
                        vertices[i + j] = pos;
                    }
                }
            }
        }

        #endregion
    }
}
