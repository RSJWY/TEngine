using System;
using TMPro;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// TMP 文本渐变色扩展（移植自 UITextGradientColorExtend，顶点单位从 UGUI 的 6 顶点流改为 TMP 的每字符 4 顶点）。
    /// 通过 OnPreRenderText 在 TMP 生成网格后、上传 mesh 前修改 meshInfo.colors32。
    /// 注：TMP 原生 enableVertexGradient/colorGradient 仅支持整体四角渐变，本扩展补充逐字符（split）渐变与偏移能力。
    /// </summary>
    [Serializable]
    public class TMPGradientColorExtend
    {
        private const int ONE_CHAR_VERTEX = 4;

        [SerializeField]
        private bool m_isUseGradientColor;
        [SerializeField]
        private Color m_colorTop = Color.white;
        [SerializeField]
        private Color m_colorBottom = Color.white;
        [SerializeField]
        private Color m_colorLeft = Color.white;
        [SerializeField]
        private Color m_colorRight = Color.white;
        [SerializeField, Range(-1f, 1f)]
        private float m_gradientOffsetVertical = 0f;
        [SerializeField, Range(-1f, 1f)]
        private float m_gradientOffsetHorizontal = 0f;
        [SerializeField]
        private bool m_splitTextGradient = false;

        public bool isUseGradientColor
        {
            get => m_isUseGradientColor;
            set
            {
                if (m_isUseGradientColor == value)
                {
                    return;
                }
                m_isUseGradientColor = value;
                Refresh();
            }
        }

        public Color colorTop
        {
            get => m_colorTop;
            set { if (m_colorTop != value) { m_colorTop = value; Refresh(); } }
        }

        public Color colorBottom
        {
            get => m_colorBottom;
            set { if (m_colorBottom != value) { m_colorBottom = value; Refresh(); } }
        }

        public Color colorLeft
        {
            get => m_colorLeft;
            set { if (m_colorLeft != value) { m_colorLeft = value; Refresh(); } }
        }

        public Color colorRight
        {
            get => m_colorRight;
            set { if (m_colorRight != value) { m_colorRight = value; Refresh(); } }
        }

        public float gradientOffsetVertical
        {
            get => m_gradientOffsetVertical;
            set { if (m_gradientOffsetVertical != value) { m_gradientOffsetVertical = value; Refresh(); } }
        }

        public float gradientOffsetHorizontal
        {
            get => m_gradientOffsetHorizontal;
            set { if (m_gradientOffsetHorizontal != value) { m_gradientOffsetHorizontal = value; Refresh(); } }
        }

        public bool splitTextGradient
        {
            get => m_splitTextGradient;
            set
            {
                if (m_splitTextGradient != value)
                {
                    m_splitTextGradient = value;
                    Refresh();
                }
            }
        }

        private TextMeshProUGUI m_text;

        public void Initialize(TextMeshProUGUI text)
        {
            m_text = text;
        }

        public void SetUseGradientColor(bool useGradientColor)
        {
            isUseGradientColor = useGradientColor;
        }

        public void SetGradientColor(Color32 colorTop, Color32 colorBottom, Color32 colorLeft = default, Color32 colorRight = default, float verticalOffset = 0f, float horizontalOffset = 0f, bool splitTextGradient = false)
        {
            SetUseGradientColor(true);
            m_colorTop = colorTop;
            m_colorBottom = colorBottom;
            m_colorLeft = colorLeft;
            m_colorRight = colorRight;
            m_splitTextGradient = splitTextGradient;
            m_gradientOffsetVertical = verticalOffset;
            m_gradientOffsetHorizontal = horizontalOffset;
            Refresh();
        }

        public void Refresh()
        {
            m_text?.SetVerticesDirty();
        }

        #region 顶点修改（OnPreRenderText 调用，红线：零分配、不调 Dirty、不手动上传 mesh）

        public void ModifyMesh(TMP_TextInfo textInfo)
        {
            if (m_text?.IsActive() == false || !m_isUseGradientColor || textInfo == null)
            {
                return;
            }

            int materialCount = textInfo.materialCount;

            if (m_splitTextGradient)
            {
                // 单字渐变模式：每个字符（4 顶点 quad）独立计算边界
                for (int m = 0; m < materialCount; m++)
                {
                    ModifyMeshSplitMode(textInfo.meshInfo[m]);
                }
            }
            else
            {
                // 整体渐变模式：先跨全部 meshInfo 扫描全局边界，再统一应用
                ComputeGlobalBounds(textInfo, materialCount, out float minX, out float minY, out float maxX, out float maxY);
                float invWidth = maxX != minX ? 1f / (maxX - minX) : 0f;
                float invHeight = maxY != minY ? 1f / (maxY - minY) : 0f;

                for (int m = 0; m < materialCount; m++)
                {
                    TMP_MeshInfo meshInfo = textInfo.meshInfo[m];
                    Color32[] colors32 = meshInfo.colors32;
                    Vector3[] vertices = meshInfo.vertices;
                    int vertexCount = meshInfo.vertexCount;
                    for (int i = 0; i + ONE_CHAR_VERTEX <= vertexCount; i += ONE_CHAR_VERTEX)
                    {
                        for (int j = 0; j < ONE_CHAR_VERTEX; j++)
                        {
                            colors32[i + j] = CalculateGradientColor(colors32[i + j], vertices[i + j], minX, minY, invWidth, invHeight);
                        }
                    }
                }
            }
        }

        // 单字渐变模式：逐 4 顶点 quad 求边界并应用
        private void ModifyMeshSplitMode(TMP_MeshInfo meshInfo)
        {
            Color32[] colors32 = meshInfo.colors32;
            Vector3[] vertices = meshInfo.vertices;
            int vertexCount = meshInfo.vertexCount;

            for (int i = 0; i + ONE_CHAR_VERTEX <= vertexCount; i += ONE_CHAR_VERTEX)
            {
                float minX = vertices[i].x;
                float minY = vertices[i].y;
                float maxX = minX;
                float maxY = minY;

                for (int j = 1; j < ONE_CHAR_VERTEX; j++)
                {
                    Vector3 pos = vertices[i + j];
                    if (pos.x < minX) minX = pos.x;
                    else if (pos.x > maxX) maxX = pos.x;
                    if (pos.y < minY) minY = pos.y;
                    else if (pos.y > maxY) maxY = pos.y;
                }

                float invWidth = maxX != minX ? 1f / (maxX - minX) : 0f;
                float invHeight = maxY != minY ? 1f / (maxY - minY) : 0f;

                for (int j = 0; j < ONE_CHAR_VERTEX; j++)
                {
                    colors32[i + j] = CalculateGradientColor(colors32[i + j], vertices[i + j], minX, minY, invWidth, invHeight);
                }
            }
        }

        // 整体模式全局边界（跨全部 meshInfo）
        private void ComputeGlobalBounds(TMP_TextInfo textInfo, int materialCount, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.MaxValue;
            minY = float.MaxValue;
            maxX = float.MinValue;
            maxY = float.MinValue;

            for (int m = 0; m < materialCount; m++)
            {
                TMP_MeshInfo meshInfo = textInfo.meshInfo[m];
                Vector3[] vertices = meshInfo.vertices;
                int vertexCount = meshInfo.vertexCount;
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 pos = vertices[i];
                    if (pos.x < minX) minX = pos.x;
                    if (pos.x > maxX) maxX = pos.x;
                    if (pos.y < minY) minY = pos.y;
                    if (pos.y > maxY) maxY = pos.y;
                }
            }

            if (minX > maxX)
            {
                // 无任何顶点时退化为零区间，避免 NaN
                minX = minY = maxX = maxY = 0f;
            }
        }

        // 计算单个顶点的渐变色（内联优化，移植自 UITextGradientColorExtend）
        // 基准色读 colors32 自身：TMP 顶点色已含 fontColor/富文本色，不要再乘 m_text.color
        private Color CalculateGradientColor(Color32 original, Vector3 pos, float minX, float minY, float invWidth, float invHeight)
        {
            float tY = (pos.y - minY) * invHeight + m_gradientOffsetVertical;
            float tX = (pos.x - minX) * invWidth + m_gradientOffsetHorizontal;

            // 手动双线性插值，避免多次Color.Lerp
            float invTY = 1f - tY;
            float invTX = 1f - tX;

            Color colorVertical;
            colorVertical.r = m_colorBottom.r * invTY + m_colorTop.r * tY;
            colorVertical.g = m_colorBottom.g * invTY + m_colorTop.g * tY;
            colorVertical.b = m_colorBottom.b * invTY + m_colorTop.b * tY;
            colorVertical.a = m_colorBottom.a * invTY + m_colorTop.a * tY;

            Color colorHorizontal;
            colorHorizontal.r = m_colorLeft.r * invTX + m_colorRight.r * tX;
            colorHorizontal.g = m_colorLeft.g * invTX + m_colorRight.g * tX;
            colorHorizontal.b = m_colorLeft.b * invTX + m_colorRight.b * tX;
            colorHorizontal.a = m_colorLeft.a * invTX + m_colorRight.a * tX;

            // 颜色相乘
            Color orig = original;
            Color result;
            result.r = orig.r * colorVertical.r * colorHorizontal.r;
            result.g = orig.g * colorVertical.g * colorHorizontal.g;
            result.b = orig.b * colorVertical.b * colorHorizontal.b;
            result.a = orig.a * colorVertical.a * colorHorizontal.a;

            return result;
        }

        #endregion
    }
}
